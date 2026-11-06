using System.Collections.Frozen;

namespace MarkupString.Decoding;

/// <summary>Claims a control sequence. Returns false to let an older handler for the same sequence try.</summary>
public delegate bool CsiHandler(in CsiSequence sequence, DecodeContext context);

/// <summary>Claims an escape sequence. Returns false to let an older handler try.</summary>
public delegate bool EscHandler(in EscSequence sequence, DecodeContext context);

/// <summary>Claims an operating system command. <paramref name="payload"/> is what follows its number and <c>;</c>.</summary>
public delegate bool OscHandler(ReadOnlySpan<char> payload, DecodeContext context);

/// <summary>Claims a device control string.</summary>
public delegate bool DcsHandler(in CsiSequence header, ReadOnlySpan<char> data, DecodeContext context);

/// <summary>Claims an application program command.</summary>
public delegate bool ApcHandler(ReadOnlySpan<char> payload, DecodeContext context);

/// <summary>Claims a C0 control character.</summary>
public delegate bool ControlHandler(char control, DecodeContext context);

/// <summary>
/// Sees printable text before it is written, and returns true when it wrote the text itself (with
/// <see cref="DecodeContext.Write(ReadOnlySpan{char})"/>), as a filter that translates a character set does.
/// </summary>
public delegate bool PrintFilter(ReadOnlySpan<char> text, DecodeContext context);

/// <summary>
/// A protocol carried in the text itself, such as MXP or Pueblo: while it is on, every piece of printable text
/// goes to it, and it writes what is literal text with <see cref="DecodeContext.Print(ReadOnlySpan{char})"/>.
/// One instance serves one decoder.
/// </summary>
public interface ITextProtocol
{
	/// <summary>Reads printable text.</summary>
	void Feed(ReadOnlySpan<char> text, DecodeContext context);

	/// <summary>The line is ending: finish or abandon whatever is half read, and end what lasts only a line.</summary>
	void EndLine(DecodeContext context);

	/// <summary>The protocol was turned off, or the decoder reset: forget everything.</summary>
	void Reset(DecodeContext context);
}

/// <summary>A protocol that adds handlers to a <see cref="DecoderRegistry"/>.</summary>
public interface IDecoderPlugin
{
	/// <summary>The plugin's name, such as <c>vt</c> or <c>mxp</c>.</summary>
	string Name { get; }

	/// <summary>Adds this plugin's handlers.</summary>
	void Register(DecoderBuilder builder);
}

/// <summary>Collects a plugin's handlers. Handlers added later are tried first.</summary>
public sealed class DecoderBuilder
{
	internal readonly Dictionary<CsiKey, List<CsiHandler>> Csi = [];
	internal readonly Dictionary<CsiKey, List<DcsHandler>> Dcs = [];
	internal readonly Dictionary<CsiKey, List<EscHandler>> Esc = [];
	internal readonly Dictionary<int, List<OscHandler>> Osc = [];
	internal readonly Dictionary<char, List<ControlHandler>> Controls = [];
	internal readonly List<ApcHandler> Apc = [];
	internal readonly List<PrintFilter> PrintFilters = [];
	internal readonly Dictionary<string, Func<ITextProtocol>> TextProtocols = new(StringComparer.OrdinalIgnoreCase);
	internal readonly List<Action<DecodeContext>> LineEndHandlers = [];

	internal DecoderBuilder()
	{
	}

	/// <summary>Handles the control sequence ending in <paramref name="final"/>, with that prefix and those intermediates.</summary>
	public DecoderBuilder OnCsi(char final, CsiHandler handler, char prefix = '\0', string intermediates = "")
	{
		ArgumentNullException.ThrowIfNull(handler);
		Add(Csi, new CsiKey(prefix, intermediates ?? string.Empty, final), handler);
		return this;
	}

	/// <summary>Handles the device control string whose header ends in <paramref name="final"/>.</summary>
	public DecoderBuilder OnDcs(char final, DcsHandler handler, char prefix = '\0', string intermediates = "")
	{
		ArgumentNullException.ThrowIfNull(handler);
		Add(Dcs, new CsiKey(prefix, intermediates ?? string.Empty, final), handler);
		return this;
	}

	/// <summary>Handles the escape sequence ending in <paramref name="final"/> after those intermediates.</summary>
	public DecoderBuilder OnEsc(char final, EscHandler handler, string intermediates = "")
	{
		ArgumentNullException.ThrowIfNull(handler);
		Add(Esc, new CsiKey('\0', intermediates ?? string.Empty, final), handler);
		return this;
	}

	/// <summary>Handles operating system command <paramref name="command"/>.</summary>
	public DecoderBuilder OnOsc(int command, OscHandler handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentOutOfRangeException.ThrowIfNegative(command);
		Add(Osc, command, handler);
		return this;
	}

	/// <summary>Handles application program commands. Every handler sees each one until one claims it.</summary>
	public DecoderBuilder OnApc(ApcHandler handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		Apc.Insert(0, handler);
		return this;
	}

	/// <summary>Handles the C0 control <paramref name="control"/>.</summary>
	public DecoderBuilder OnControl(char control, ControlHandler handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		Add(Controls, control, handler);
		return this;
	}

	/// <summary>Sees printable text before it is written.</summary>
	public DecoderBuilder OnPrint(PrintFilter filter)
	{
		ArgumentNullException.ThrowIfNull(filter);
		PrintFilters.Insert(0, filter);
		return this;
	}

	/// <summary>Is told whenever a line ends, after a text protocol has been.</summary>
	public DecoderBuilder OnLineEnd(Action<DecodeContext> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		LineEndHandlers.Add(handler);
		return this;
	}

	/// <summary>Offers a text protocol by name; a decoder makes one instance when it is first turned on.</summary>
	public DecoderBuilder TextProtocol(string name, Func<ITextProtocol> create)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(create);
		TextProtocols[name] = create;
		return this;
	}

	private static void Add<TKey, T>(Dictionary<TKey, List<T>> table, TKey key, T handler) where TKey : notnull
	{
		if (!table.TryGetValue(key, out var list)) table[key] = list = [];
		list.Insert(0, handler);
	}
}

internal readonly record struct CsiKey(char Prefix, string Intermediates, char Final);

/// <summary>
/// The protocols a decoder reads, built once and shared. Each connection makes its own <see cref="Decoder"/> from
/// it, which holds that connection's state.
/// </summary>
public sealed class DecoderRegistry
{
	private readonly IDecoderPlugin[] _plugins;

	private DecoderRegistry(IDecoderPlugin[] plugins)
	{
		_plugins = plugins;
		var builder = new DecoderBuilder();
		foreach (var plugin in plugins) plugin.Register(builder);

		Csi = builder.Csi.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
		Dcs = builder.Dcs.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
		Esc = builder.Esc.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
		Osc = builder.Osc.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
		Controls = builder.Controls.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
		Apc = [.. builder.Apc];
		PrintFilters = [.. builder.PrintFilters];
		TextProtocols = builder.TextProtocols.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
		LineEndHandlers = [.. builder.LineEndHandlers];
	}

	/// <summary>A registry that reads nothing: every sequence is dropped and text passes through.</summary>
	public static DecoderRegistry Empty { get; } = new([]);

	/// <summary>The plugins, in the order they were added.</summary>
	public IReadOnlyList<IDecoderPlugin> Plugins => _plugins;

	internal FrozenDictionary<CsiKey, CsiHandler[]> Csi { get; }
	internal FrozenDictionary<CsiKey, DcsHandler[]> Dcs { get; }
	internal FrozenDictionary<CsiKey, EscHandler[]> Esc { get; }
	internal FrozenDictionary<int, OscHandler[]> Osc { get; }
	internal FrozenDictionary<char, ControlHandler[]> Controls { get; }
	internal ApcHandler[] Apc { get; }
	internal PrintFilter[] PrintFilters { get; }
	internal FrozenDictionary<string, Func<ITextProtocol>> TextProtocols { get; }
	internal Action<DecodeContext>[] LineEndHandlers { get; }

	/// <summary>A registry with <paramref name="plugin"/> added. A plugin of the same name is replaced.</summary>
	public DecoderRegistry With(IDecoderPlugin plugin)
	{
		ArgumentNullException.ThrowIfNull(plugin);
		var plugins = _plugins.Where(existing => !string.Equals(existing.Name, plugin.Name, StringComparison.OrdinalIgnoreCase)).Append(plugin);
		return new DecoderRegistry([.. plugins]);
	}

	/// <summary>Whether a plugin of that name is installed.</summary>
	public bool Has(string name) => _plugins.Any(plugin => string.Equals(plugin.Name, name, StringComparison.OrdinalIgnoreCase));

	/// <summary>Whether a text protocol of that name is offered.</summary>
	public bool HasTextProtocol(string name) => TextProtocols.ContainsKey(name);

	/// <summary>A decoder for one connection.</summary>
	public Decoder CreateDecoder(DecoderOptions? options = null) => new(this, options ?? new DecoderOptions());
}

/// <summary>Settings for one decoder.</summary>
public sealed record DecoderOptions
{
	/// <summary>How far a control sequence may grow.</summary>
	public VtLimits Limits { get; init; } = VtLimits.Default;

	/// <summary>Who the client is, for answering a server that asks.</summary>
	public ClientIdentity Identity { get; init; } = new();

	/// <summary>The most elements a text protocol may have open at once.</summary>
	public int MaxOpenElements { get; init; } = 64;

	/// <summary>The longest line, in characters; a longer line is ended there and the rest starts a new one.</summary>
	public int MaxLineLength { get; init; } = 64 * 1024;
}
