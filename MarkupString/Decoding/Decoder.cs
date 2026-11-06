namespace MarkupString.Decoding;

/// <summary>
/// Turns one connection's output into <see cref="DisplayLine"/>s. Make one per connection from a
/// <see cref="DecoderRegistry"/>; it holds the connection's state (the current style, open elements, half-read
/// sequences) and is not safe to use from two threads at once.
/// </summary>
/// <remarks>
/// <para>
/// A line ends at a line feed in the text, or when the host says so with <see cref="EndLine"/> (a telnet layer
/// that strips line terminators) or <see cref="EndPrompt"/> (telnet GA or EOR).
/// </para>
/// <para>Things that are not text — a title, an answer to send back — are raised through <see cref="EventRaised"/>.</para>
/// </remarks>
public sealed class Decoder
{
	private readonly VtTokenizer _tokenizer;
	private readonly Sink _sink;
	private readonly List<DisplayLine> _completed = [];

	internal Decoder(DecoderRegistry registry, DecoderOptions options)
	{
		Registry = registry;
		Options = options;
		Identity = options.Identity;
		Diagnostics = new DecodeDiagnostics(DecodeMetrics.For(options.Meters));
		_tokenizer = new VtTokenizer(options.Limits);
		Context = new DecodeContext(this);
		_sink = new Sink(this);
	}

	/// <summary>The registry this decoder was made from.</summary>
	public DecoderRegistry Registry { get; }

	/// <summary>The options it was made with.</summary>
	public DecoderOptions Options { get; }

	/// <summary>Who the client is. Set it again when the pane is resized or the player changes a setting.</summary>
	public ClientIdentity Identity { get; set; }

	/// <summary>Counts of what was dropped, for a diagnostics view.</summary>
	public DecodeDiagnostics Diagnostics { get; }

	/// <summary>Raised for each event, as it is found.</summary>
	public event Action<DecodeEvent>? EventRaised;

	/// <summary>The line read so far, not yet ended.</summary>
	public MarkupText PendingLine => Context.Line.Peek();

	/// <summary>Whether anything has been read into the current line.</summary>
	public bool HasPendingLine => !Context.Line.IsEmpty;

	/// <summary>The text protocol now reading the text, such as <c>mxp</c>, or null.</summary>
	public string? ActiveProtocol => Context.ActiveProtocolName;

	internal DecodeContext Context { get; }

	/// <summary>This decoder's instance of a plugin's state, as <see cref="DecodeContext.State{T}"/> gives it to the plugin.</summary>
	public T State<T>() where T : class, new() => Context.State<T>();

	/// <summary>Reads <paramref name="text"/> and returns the lines it completed.</summary>
	public IReadOnlyList<DisplayLine> Feed(ReadOnlySpan<char> text)
	{
		_tokenizer.Feed(text, _sink);
		return TakeCompleted();
	}

	/// <summary>Reads <paramref name="text"/> and returns the lines it completed.</summary>
	public IReadOnlyList<DisplayLine> Feed(string? text) => Feed((text ?? string.Empty).AsSpan());

	/// <summary>Ends the current line, as a line terminator the host removed would have, and returns it.</summary>
	public DisplayLine EndLine()
	{
		_tokenizer.Interrupt(_sink);
		Context.EndLine();
		var lines = TakeCompleted();
		return lines[^1];
	}

	/// <summary>
	/// Ends the current line as a prompt (telnet GA or EOR) and returns it, or returns null when nothing was read
	/// into it.
	/// </summary>
	public DisplayLine? EndPrompt()
	{
		_tokenizer.Interrupt(_sink);
		if (Context.Line.IsEmpty) return null;
		Context.EndLine(LineKind.Prompt);
		var lines = TakeCompleted();
		return lines[^1];
	}

	/// <summary>Turns on a text protocol the registry offers, such as <c>mxp</c> after telnet negotiates it.</summary>
	/// <returns>False when the registry offers no protocol of that name.</returns>
	public bool EnableProtocol(string name) => Context.EnableProtocol(name);

	/// <summary>Turns a text protocol off.</summary>
	public void DisableProtocol(string name) => Context.DisableProtocol(name);

	/// <summary>Forgets everything: style, open elements, half-read sequences and the pending line.</summary>
	public void Reset()
	{
		_tokenizer.Reset();
		Context.ResetAll();
		_completed.Clear();
	}

	/// <summary>Decodes a whole text at once: its lines, the last one included when it has no line break.</summary>
	public static IReadOnlyList<DisplayLine> DecodeAll(string? text, DecoderRegistry registry, DecoderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(registry);
		var decoder = registry.CreateDecoder(options);
		var lines = new List<DisplayLine>(decoder.Feed(text));
		if (decoder.HasPendingLine) lines.Add(decoder.EndLine());
		return lines;
	}

	internal void Complete(DisplayLine line) => _completed.Add(line);

	internal void Raise(DecodeEvent e) => EventRaised?.Invoke(e);

	private IReadOnlyList<DisplayLine> TakeCompleted()
	{
		if (_completed.Count == 0) return [];
		var lines = _completed.ToArray();
		_completed.Clear();
		return lines;
	}

	/// <summary>Dispatches what the tokenizer finds to the registry's handlers.</summary>
	private sealed class Sink(Decoder decoder) : IVtTokenSink
	{
		private DecodeContext Context => decoder.Context;
		private DecoderRegistry Registry => decoder.Registry;

		public void Print(ReadOnlySpan<char> text) => Context.Feed(text);

		public void Execute(char control)
		{
			if (Registry.Controls.TryGetValue(control, out var handlers))
			{
				foreach (var handler in handlers)
				{
					if (handler(control, Context)) return;
				}
			}

			// A line feed always ends a line, whatever handles the rest of C0.
			if (control == '\n')
			{
				Context.EndLine();
				return;
			}

			decoder.Diagnostics.UnhandledControls++;
		}

		public void Csi(in CsiSequence sequence)
		{
			if (Registry.Csi.TryGetValue(new CsiKey(sequence.Prefix, sequence.Intermediates.ToString(), sequence.Final), out var handlers))
			{
				foreach (var handler in handlers)
				{
					if (handler(sequence, Context)) return;
				}
			}

			decoder.Diagnostics.UnhandledSequences++;
		}

		public void Esc(in EscSequence sequence)
		{
			if (Registry.Esc.TryGetValue(new CsiKey('\0', sequence.Intermediates.ToString(), sequence.Final), out var handlers))
			{
				foreach (var handler in handlers)
				{
					if (handler(sequence, Context)) return;
				}
			}

			decoder.Diagnostics.UnhandledSequences++;
		}

		public void Osc(ReadOnlySpan<char> payload)
		{
			var separator = payload.IndexOf(';');
			var number = separator < 0 ? payload : payload[..separator];
			var rest = separator < 0 ? [] : payload[(separator + 1)..];
			if (int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var command)
				&& Registry.Osc.TryGetValue(command, out var handlers))
			{
				foreach (var handler in handlers)
				{
					if (handler(rest, Context)) return;
				}
			}

			decoder.Diagnostics.UnhandledSequences++;
		}

		public void Dcs(in CsiSequence header, ReadOnlySpan<char> data)
		{
			if (Registry.Dcs.TryGetValue(new CsiKey(header.Prefix, header.Intermediates.ToString(), header.Final), out var handlers))
			{
				foreach (var handler in handlers)
				{
					if (handler(header, data, Context)) return;
				}
			}

			decoder.Diagnostics.UnhandledSequences++;
		}

		public void Apc(ReadOnlySpan<char> payload)
		{
			foreach (var handler in Registry.Apc)
			{
				if (handler(payload, Context)) return;
			}

			decoder.Diagnostics.UnhandledSequences++;
		}

		public void Abandoned(VtSequenceKind kind) => decoder.Diagnostics.AbandonedSequences++;
	}
}

/// <summary>
/// Counts of what a decoder dropped. Each is also reported on the <c>MarkupString.Decoding</c> meter, as the
/// counter <c>markupstring.decoding.dropped</c> tagged with its <c>kind</c>.
/// </summary>
public sealed class DecodeDiagnostics
{
	private readonly DecodeMetrics? _metrics;
	private int _abandoned, _unhandled, _controls, _limits, _pictures;

	/// <summary>Counts that are not reported anywhere.</summary>
	public DecodeDiagnostics()
	{
	}

	internal DecodeDiagnostics(DecodeMetrics metrics) => _metrics = metrics;

	/// <summary>Sequences that were malformed, too long, or cut off by a line ending.</summary>
	public int AbandonedSequences
	{
		get => _abandoned;
		set => _abandoned = Report(_abandoned, value, "abandoned-sequence");
	}

	/// <summary>Well-formed sequences no handler claimed.</summary>
	public int UnhandledSequences
	{
		get => _unhandled;
		set => _unhandled = Report(_unhandled, value, "unhandled-sequence");
	}

	/// <summary>Control characters no handler claimed.</summary>
	public int UnhandledControls
	{
		get => _controls;
		set => _controls = Report(_controls, value, "unhandled-control");
	}

	/// <summary>Things dropped because a limit was reached: an element stack, a line length, a picture size.</summary>
	public int LimitsReached
	{
		get => _limits;
		set => _limits = Report(_limits, value, "limit");
	}

	/// <summary>Pictures that could not be decoded.</summary>
	public int PicturesFailed
	{
		get => _pictures;
		set => _pictures = Report(_pictures, value, "picture");
	}

	private int Report(int before, int after, string kind)
	{
		if (after > before) _metrics?.Dropped(after - before, kind);
		return after;
	}
}

/// <summary>The decoding meter, one for each meter factory.</summary>
internal sealed class DecodeMetrics
{
	public const string MeterName = "MarkupString.Decoding";

	private static readonly DecodeMetrics Shared = new(new System.Diagnostics.Metrics.Meter(MeterName));
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Diagnostics.Metrics.IMeterFactory, DecodeMetrics> ByFactory = [];

	private readonly System.Diagnostics.Metrics.Counter<long> _dropped;

	private DecodeMetrics(System.Diagnostics.Metrics.Meter meter) =>
		_dropped = meter.CreateCounter<long>("markupstring.decoding.dropped", "{item}", "What decoders dropped, by kind.");

	public static DecodeMetrics For(System.Diagnostics.Metrics.IMeterFactory? factory) =>
		factory is null ? Shared : ByFactory.GetValue(factory, static f => new DecodeMetrics(f.Create(new System.Diagnostics.Metrics.MeterOptions(MeterName))));

	public void Dropped(int count, string kind) => _dropped.Add(count, new KeyValuePair<string, object?>("kind", kind));
}
