using System.Globalization;
using System.Text;
using System.Text.Json;
using MarkupString.Ansi;
using MarkupString.Decoding;
namespace MarkupString.Mxp.Decoding;

/// <summary>
/// Says what an MXP line tag or a <c>FLAG</c> marks the text as: a room's name, its description, its exits, or
/// whatever a server's own element flags. It changes nothing about how the text looks.
/// </summary>
/// <param name="Flag">The flag, as the server named it, such as <c>RoomName</c>.</param>
public sealed record MxpFlagMarkup(string Flag) : IDataMarkup
{
	/// <summary>A room's name (line tag 10).</summary>
	public const string RoomName = "RoomName";

	/// <summary>A room's description (line tag 11).</summary>
	public const string RoomDescription = "RoomDesc";

	/// <summary>A room's exits (line tag 12).</summary>
	public const string RoomExits = "RoomExit";

	/// <summary>The welcome text (line tag 19).</summary>
	public const string Welcome = "Welcome";
}

/// <summary>Registers MXP decoding on a <see cref="DecoderRegistry"/>.</summary>
public static class MxpDecoding
{
	/// <summary>The name to pass <see cref="MarkupString.Decoding.Decoder.EnableProtocol(string)"/> once telnet has negotiated MXP.</summary>
	public const string Protocol = "mxp";

	/// <summary>The registry with <see cref="MxpPlugin"/>.</summary>
	public static DecoderRegistry WithMxp(this DecoderRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);
		return registry.With(MxpPlugin.Instance);
	}

	/// <summary>The codec that stores <see cref="MxpFlagMarkup"/>, for a registry that serialises decoded lines.</summary>
	public static IMarkupCodec FlagCodec { get; } = new MxpFlagCodec();

	private sealed class MxpFlagCodec : IMarkupCodec
	{
		public string Kind => "mxp-flag";

		public Type MarkupType => typeof(MxpFlagMarkup);

		public void Write(Utf8JsonWriter writer, IMarkup markup) => writer.WriteString("f", ((MxpFlagMarkup)markup).Flag);

		public IMarkup Read(JsonElement element) =>
			new MxpFlagMarkup(element.TryGetProperty("f", out var flag) && flag.ValueKind == JsonValueKind.String ? flag.GetString()! : string.Empty);
	}
}

/// <summary>
/// MXP, the MUD eXtension Protocol, as a text protocol: tags and entities in the text, and the line modes
/// (<c>ESC [ n z</c>) that say which tags a line may use. Turn it on with
/// <see cref="MarkupString.Decoding.Decoder.EnableProtocol(string)"/>(<see cref="MxpDecoding.Protocol"/>) when telnet negotiates it.
/// </summary>
/// <remarks>
/// <para>
/// Styles (<c>B</c>, <c>I</c>, <c>U</c>, <c>S</c>, <c>H</c>, <c>COLOR</c>, <c>FONT</c>) become <see cref="AnsiMarkup"/>; links
/// (<c>A</c>, <c>SEND</c>) a link on an <see cref="AnsiMarkup"/>, with a <see cref="CommandMenuMarkup"/> for a menu or a
/// prompt; <c>IMAGE</c>, <c>SOUND</c>, <c>MUSIC</c>, <c>DEST</c>, <c>EXPIRE</c>, <c>RELOCATE</c>, <c>USER</c>,
/// <c>PASSWORD</c> and <c>VAR</c> the shared vocabulary; and custom elements (<c>&lt;!ELEMENT&gt;</c>) what their
/// definitions say. <c>VERSION</c> and <c>SUPPORT</c> are answered from the decoder's identity.
/// </para>
/// <para>
/// A secure tag on a line that is not secure is dropped, as the protocol says, and counted as unhandled.
/// </para>
/// </remarks>
public sealed class MxpPlugin : IDecoderPlugin
{
	/// <summary>The one instance.</summary>
	public static MxpPlugin Instance { get; } = new();

	/// <summary>The tags this decoder reads, as <c>&lt;SUPPORTS&gt;</c> answers.</summary>
	public static IReadOnlyList<string> Supported { get; } =
	[
		"B", "BOLD", "STRONG", "I", "ITALIC", "EM", "U", "UNDERLINE", "S", "STRIKEOUT", "H", "HIGH", "C", "COLOR", "FONT",
		"BR", "SBR", "P", "NOBR", "A", "SEND", "EXPIRE", "VERSION", "SUPPORT", "IMAGE", "SOUND", "MUSIC", "FRAME", "DEST",
		"RELOCATE", "USER", "PASSWORD", "VAR", "ELEMENT", "ENTITY", "ATTLIST", "TAG",
	];

	private MxpPlugin()
	{
	}

	/// <inheritdoc/>
	public string Name => "mxp";

	/// <inheritdoc/>
	public void Register(DecoderBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder
			.TextProtocol(MxpDecoding.Protocol, static () => new MxpReader())
			.OnCsi('z', static (in CsiSequence sequence, DecodeContext context) =>
			{
				// A line mode means nothing until MXP is on, and is dropped.
				if (context.IsActive(MxpDecoding.Protocol)) MxpReader.LineMode(sequence.Get(0), context);
				return true;
			});
	}
}

internal enum MxpMode
{
	Open,
	Secure,
	Locked,
}

internal sealed record MxpElementDefinition(string Name, string Definition, IReadOnlyList<TagArgument> Attributes, string? Flag, bool Open, bool Empty);

internal sealed class MxpOpenElement(string name, IMarkup? first, bool openMode, bool lineTag)
{
	public string Name { get; } = name;

	public IMarkup? First { get; } = first;

	public bool OpenMode { get; } = openMode;

	public bool LineTag { get; } = lineTag;
}

internal sealed class MxpState
{
	public MxpMode Default { get; set; } = MxpMode.Open;

	public MxpMode Line { get; set; } = MxpMode.Open;

	public bool TemporarySecure { get; set; }

	public bool InBreak { get; set; }

	public List<MxpOpenElement> Open { get; } = [];

	public Dictionary<string, MxpElementDefinition> Elements { get; } = new(StringComparer.OrdinalIgnoreCase);

	public Dictionary<string, string> Entities { get; } = new(StringComparer.OrdinalIgnoreCase);

	public Dictionary<int, string> LineTags { get; } = [];

	public Dictionary<string, string?> FrameTitles { get; } = new(StringComparer.OrdinalIgnoreCase);

	public ParsedTag? Capture { get; set; }

	public StringBuilder CaptureRaw { get; } = new();

	public StringBuilder CaptureText { get; } = new();

	public int CaptureDepth { get; set; }

	public List<(ImageMarkup Image, int Rows, int Columns, int Column, int Next)> Overlays { get; } = [];
}

internal sealed class MxpReader : ITextProtocol, ITagSink
{
	private const int MaxCapture = 4096;

	private static readonly HashSet<string> OpenTags = new(StringComparer.OrdinalIgnoreCase)
	{
		"B", "BOLD", "STRONG", "I", "ITALIC", "EM", "U", "UNDERLINE", "S", "STRIKEOUT", "STRIKE", "H", "HIGH", "C", "COLOR",
		"FONT", "NOBR", "P", "BR", "SBR", "SMALL", "TT",
	};

	private readonly TagScanner _scanner = new();
	private DecodeContext? _context;

	private DecodeContext Context => _context!;

	private MxpState State => Context.State<MxpState>();

	public bool ReadsMarkup
	{
		get
		{
			var state = State;
			return state.TemporarySecure || state.Line != MxpMode.Locked;
		}
	}

	private bool Secure
	{
		get
		{
			var state = State;
			return state.TemporarySecure || state.Line == MxpMode.Secure;
		}
	}

	public void Feed(ReadOnlySpan<char> text, DecodeContext context)
	{
		_context = context;
		_scanner.Feed(text, this);
	}

	public void EndLine(DecodeContext context)
	{
		_context = context;
		var state = State;
		_scanner.Flush(this);
		if (state.Capture is not null) FinishCapture();
		DrawOverlays();
		if (state.InBreak) return;

		// Elements opened on an open line, and line tags, end with it.
		var index = state.Open.FindIndex(element => element.OpenMode || element.LineTag);
		if (index >= 0) CloseFrom(index);
		state.Line = state.Default;
		state.TemporarySecure = false;
	}

	public void Reset(DecodeContext context)
	{
		_context = context;
		_scanner.Reset();
		var state = State;
		if (state.Open.Count > 0) CloseFrom(0);
		state.Capture = null;
		state.Overlays.Clear();
		state.Line = state.Default = MxpMode.Open;
		state.TemporarySecure = false;
	}

	internal static void LineMode(int mode, DecodeContext context)
	{
		var state = context.State<MxpState>();
		switch (mode)
		{
			case 0: state.Line = MxpMode.Open; break;
			case 1: state.Line = MxpMode.Secure; break;
			case 2: state.Line = MxpMode.Locked; break;
			case 3:
				state.Line = state.Default = MxpMode.Open;
				var first = state.Open.FirstOrDefault(element => element.First is not null)?.First;
				if (first is not null) context.Close(first);
				state.Open.Clear();
				break;
			case 4: state.TemporarySecure = true; break;
			case 5: state.Line = state.Default = MxpMode.Open; break;
			case 6: state.Line = state.Default = MxpMode.Secure; break;
			case 7: state.Line = state.Default = MxpMode.Locked; break;
			case >= 10 and <= 99:
				state.Line = MxpMode.Secure;
				if (state.LineTags.TryGetValue(mode, out var element) && state.Elements.TryGetValue(element, out var definition))
				{
					// The line is the element; the reader opens it and the line's end closes it.
					var reader = new MxpReader { _context = context };
					reader.OpenCustom(definition, new ParsedTag(definition.Name, false, []), lineTag: true);
				}
				else if (mode switch { 10 => MxpFlagMarkup.RoomName, 11 => MxpFlagMarkup.RoomDescription, 12 => MxpFlagMarkup.RoomExits, 19 => MxpFlagMarkup.Welcome, _ => null } is { } flag)
				{
					var markup = new MxpFlagMarkup(flag);
					if (context.Open(markup)) state.Open.Add(new MxpOpenElement(flag, markup, openMode: false, lineTag: true));
				}

				break;
		}
	}

	// ── ITagSink ────────────────────────────────────────────────────────────────

	public void Text(ReadOnlySpan<char> text)
	{
		var state = State;
		if (state.Capture is not null)
		{
			Captured(text, text);
			return;
		}

		Context.Print(text);
	}

	public void Entity(ReadOnlySpan<char> name)
	{
		var decoded = TagSyntax.Entity(name, LookupEntity);
		var state = State;
		if (state.Capture is not null)
		{
			Captured($"&{name};", decoded ?? $"&{name};");
			return;
		}

		Context.Print(decoded ?? $"&{name};");
	}

	public void Tag(ReadOnlySpan<char> body)
	{
		var state = State;
		var secure = Secure;
		state.TemporarySecure = false;

		if (state.Capture is { } capture)
		{
			var inner = TagSyntax.Parse(body);
			if (inner is not null && inner.Name.Equals(capture.Name, StringComparison.OrdinalIgnoreCase))
			{
				if (inner.Closing && state.CaptureDepth == 0)
				{
					FinishCapture();
					return;
				}

				state.CaptureDepth += inner.Closing ? -1 : 1;
			}

			Captured($"<{body}>", []);
			return;
		}

		if (!body.IsEmpty && body[0] == '!')
		{
			if (secure) Definition(body[1..]);
			else Context.Diagnostics.UnhandledSequences++;
			return;
		}

		var tag = TagSyntax.Parse(body, LookupEntity);
		if (tag is null) return;

		if (tag.Closing)
		{
			Close(tag.Name);
			return;
		}

		var custom = state.Elements.TryGetValue(tag.Name, out var definition) ? definition : null;
		var open = custom?.Open ?? OpenTags.Contains(tag.Name);
		if (!secure && !open)
		{
			Context.Diagnostics.UnhandledSequences++;
			return;
		}

		if (custom is not null)
		{
			OpenCustom(custom, tag, lineTag: false);
			return;
		}

		if (!Element(tag, openMode: !secure)) Context.Diagnostics.UnhandledSequences++;
	}

	// ── Elements ────────────────────────────────────────────────────────────────

	private bool Element(ParsedTag tag, bool openMode)
	{
		var context = Context;
		switch (tag.Name.ToUpperInvariant())
		{
			case "B" or "BOLD" or "STRONG" or "H" or "HIGH":
				OpenStyle(tag.Name, new AnsiStyle { Bold = true }, openMode);
				return true;
			case "I" or "ITALIC" or "EM":
				OpenStyle(tag.Name, new AnsiStyle { Italic = true }, openMode);
				return true;
			case "U" or "UNDERLINE":
				OpenStyle(tag.Name, new AnsiStyle { Underlined = true }, openMode);
				return true;
			case "S" or "STRIKEOUT" or "STRIKE":
				OpenStyle(tag.Name, new AnsiStyle { StrikeThrough = true }, openMode);
				return true;
			case "C" or "COLOR":
				OpenStyle(tag.Name, new AnsiStyle { Foreground = Color(tag.Get("FORE", 0)), Background = Color(tag.Get("BACK", 1)) }, openMode);
				return true;
			case "FONT":
				OpenStyle(tag.Name, new AnsiStyle { Foreground = Color(tag.Get("COLOR")), Background = Color(tag.Get("BACK")) }, openMode);
				return true;
			case "SMALL" or "TT" or "P" or "NOBR":
				return true;
			case "BR":
				State.InBreak = true;
				context.EndLine();
				State.InBreak = false;
				return true;
			case "SBR":
				context.Print(" ");
				return true;
			case "A":
				{
					var href = tag.Get("HREF", 0);
					IMarkup? link = href is not null && Context.Options.Links.IsAllowed(href)
						? new AnsiMarkup(new AnsiStyle { LinkUrl = href, LinkKind = LinkKind.Url, LinkText = tag.Get("HINT", 1) })
						: null;
					OpenElement(tag.Name, link is null ? [] : [link], openMode);
					return true;
				}
			case "SEND":
				{
					var href = tag.Get("HREF", 0);
					if (href is null || href.Contains("&text;", StringComparison.OrdinalIgnoreCase))
					{
						StartCapture(tag);
						return true;
					}

					OpenSend(tag, href, openMode);
					return true;
				}
			case "VAR" or "V":
				StartCapture(tag);
				return true;
			case "EXPIRE":
				context.Point(new ExpireLinksMarkup(tag.Get("NAME", 0)));
				return true;
			case "VERSION":
				if ((context.Identity.Answers & ClientAnswers.Mxp) != 0)
				{
					context.Reply(string.Create(CultureInfo.InvariantCulture,
						$"\e[1z<VERSION MXP=1.0 CLIENT={Quote(context.Identity.Name)} VERSION={Quote(context.Identity.Version)}>\n"));
				}

				return true;
			case "SUPPORT":
				if ((context.Identity.Answers & ClientAnswers.Mxp) != 0) context.Reply(Supports(tag));
				return true;
			case "IMAGE":
				Image(tag);
				return true;
			case "SOUND" or "MUSIC":
				Sound(tag);
				return true;
			case "FRAME":
				{
					var name = tag.Get("NAME", 0);
					if (name is not null) State.FrameTitles[name] = tag.Get("TITLE");
					return true;
				}
			case "DEST":
				{
					var name = tag.Get("NAME", 0);
					if (string.IsNullOrEmpty(name)) return false;
					State.FrameTitles.TryGetValue(name, out var title);
					OpenElement(tag.Name, [new PaneMarkup(name, title)], openMode);
					return true;
				}
			case "RELOCATE":
				{
					var host = tag.Get("HOST", 0);
					if (host is null || !int.TryParse(tag.Get("PORT", 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port)) return false;
					context.Point(new RelocateMarkup(host, port, tag.Has("QUIET")));
					return true;
				}
			case "USER":
				context.Point(new LoginPromptMarkup(LoginField.User));
				return true;
			case "PASSWORD":
				context.Point(new LoginPromptMarkup(LoginField.Password));
				return true;
			case "GAUGE" or "STAT":
				// A gauge stands for a variable the host already has from VAR and ENTITY; there is no text to show.
				return true;
			default:
				return false;
		}
	}

	private void OpenStyle(string name, AnsiStyle style, bool openMode) =>
		OpenElement(name, style.IsNone ? [] : [new AnsiMarkup(style)], openMode);

	private void OpenElement(string name, IMarkup[] markups, bool openMode, bool lineTag = false)
	{
		IMarkup? first = null;
		foreach (var markup in markups)
		{
			if (!Context.Open(markup)) break;
			first ??= markup;
		}

		State.Open.Add(new MxpOpenElement(name, first, openMode, lineTag));
	}

	private void Close(string name)
	{
		var state = State;
		var index = state.Open.FindLastIndex(element => element.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		if (index >= 0) CloseFrom(index);
	}

	private void CloseFrom(int index)
	{
		var state = State;
		for (var i = index; i < state.Open.Count; i++)
		{
			if (state.Open[i].First is { } first)
			{
				Context.Close(first);
				break;
			}
		}

		state.Open.RemoveRange(index, state.Open.Count - index);
	}

	private void OpenSend(ParsedTag tag, string href, bool openMode)
	{
		var commands = href.Split('|');
		var hints = tag.Get("HINT", 1)?.Split('|') ?? [];
		var prompt = tag.Has("PROMPT");

		// With one hint more than commands, the first is the link's own and the rest label the menu.
		string? hint;
		string[] labels;
		if (hints.Length == commands.Length + 1)
		{
			hint = hints[0];
			labels = hints[1..];
		}
		else
		{
			hint = hints.Length > 0 ? hints[0] : null;
			labels = hints.Length == commands.Length ? hints : commands;
		}

		var link = new AnsiMarkup(new AnsiStyle { LinkUrl = commands[0], LinkKind = LinkKind.Command, LinkText = hint });
		if (commands.Length == 1 && !prompt)
		{
			OpenElement(tag.Name, [link], openMode);
			return;
		}

		var choices = new CommandChoice[commands.Length];
		for (var i = 0; i < commands.Length; i++) choices[i] = new CommandChoice(labels[i], commands[i]);
		OpenElement(tag.Name, [new CommandMenuMarkup(choices, hint, prompt), link], openMode);
	}

	private void OpenCustom(MxpElementDefinition definition, ParsedTag tag, bool lineTag)
	{
		var state = State;
		var before = Context.Elements.Count;
		var opened = state.Open.Count;

		if (definition.Flag is { } flag) Context.Open(new MxpFlagMarkup(flag));

		// The definition's tags, with the element's attributes put in for &name;, run as secure tags.
		var expanded = Expand(definition, tag);
		var replay = new TagScanner();
		state.TemporarySecure = false;
		var saved = state.Line;
		state.Line = MxpMode.Secure;
		replay.Feed(expanded, this);
		state.Line = saved;

		// What the definition opened is one element now, closed together.
		if (state.Open.Count > opened) state.Open.RemoveRange(opened, state.Open.Count - opened);
		var first = Context.Elements.Count > before ? Context.Elements[before] : null;
		if (definition.Empty)
		{
			if (first is not null) Context.Close(first);
			return;
		}

		state.Open.Add(new MxpOpenElement(definition.Name, first, openMode: !Secure && !lineTag && definition.Open, lineTag));
	}

	private static string Expand(MxpElementDefinition definition, ParsedTag tag)
	{
		var text = definition.Definition;
		var position = 0;
		foreach (var attribute in definition.Attributes)
		{
			var name = attribute.Name ?? attribute.Value;
			var value = tag.Get(name, position++) ?? (attribute.Name is null ? string.Empty : attribute.Value);
			text = text.Replace($"&{name};", value, StringComparison.OrdinalIgnoreCase);
		}

		return text;
	}

	private void Definition(ReadOnlySpan<char> body)
	{
		var tag = TagSyntax.Parse(body);
		if (tag is null) return;
		var state = State;
		var name = tag.Get("NAME", 0);
		if (name is null) return;

		switch (tag.Name.ToUpperInvariant())
		{
			case "ELEMENT" or "EL":
				{
					if (tag.Has("DELETE"))
					{
						state.Elements.Remove(name);
						return;
					}

					var attributes = TagSyntax.Parse("x " + (tag.Get("ATT") ?? string.Empty))?.Arguments ?? [];
					var definition = new MxpElementDefinition(name, tag.Get("DEFINITION", 1) ?? string.Empty, attributes, tag.Get("FLAG"), tag.Has("OPEN"), tag.Has("EMPTY"));
					state.Elements[name] = definition;
					if (int.TryParse(tag.Get("TAG"), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 20 and <= 99)
					{
						state.LineTags[number] = name;
					}

					return;
				}
			case "ATTLIST" or "AT":
				{
					if (!state.Elements.TryGetValue(name, out var definition)) return;
					var attributes = TagSyntax.Parse("x " + (tag.Get("ATT", 1) ?? string.Empty))?.Arguments ?? [];
					state.Elements[name] = definition with { Attributes = attributes };
					return;
				}
			case "TAG":
				{
					var number = tag.Get("INDEX", 0);
					var element = tag.Get("ELEMENT", 1) ?? tag.Get("NAME");
					if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && element is not null) state.LineTags[index] = element;
					return;
				}
			case "ENTITY" or "EN":
				{
					if (tag.Has("DELETE") || tag.Has("REMOVE"))
					{
						state.Entities.Remove(name);
						Context.Raise(new VariableEvent(name, null));
						return;
					}

					var value = tag.Get("VALUE", 1) ?? string.Empty;
					if (tag.Has("ADD") && state.Entities.TryGetValue(name, out var existing)) value = existing + "|" + value;
					state.Entities[name] = value;
					if (!tag.Has("PRIVATE")) Context.Raise(new VariableEvent(name, value));
					return;
				}
		}
	}

	// ── Capturing the text of SEND and VAR ──────────────────────────────────────

	private void StartCapture(ParsedTag tag)
	{
		var state = State;
		state.Capture = tag;
		state.CaptureDepth = 0;
		state.CaptureRaw.Clear();
		state.CaptureText.Clear();
	}

	private void Captured(ReadOnlySpan<char> raw, ReadOnlySpan<char> text)
	{
		var state = State;
		state.CaptureRaw.Append(raw);
		state.CaptureText.Append(text);
		if (state.CaptureRaw.Length > MaxCapture) FinishCapture();
	}

	private void FinishCapture()
	{
		var state = State;
		var tag = state.Capture!;
		var raw = state.CaptureRaw.ToString();
		var text = state.CaptureText.ToString();
		state.Capture = null;

		var name = tag.Name.ToUpperInvariant();
		if (name == "SEND")
		{
			var href = tag.Get("HREF", 0)?.Replace("&text;", text, StringComparison.OrdinalIgnoreCase) ?? text;
			var hint = tag.Get("HINT", 1)?.Replace("&text;", text, StringComparison.OrdinalIgnoreCase);
			var arguments = tag.Arguments.Where(argument => argument.Name is not null && !argument.Name.Equals("HREF", StringComparison.OrdinalIgnoreCase)
				&& !argument.Name.Equals("HINT", StringComparison.OrdinalIgnoreCase)).ToList();
			if (hint is not null) arguments.Add(new TagArgument("HINT", hint));
			if (tag.Has("PROMPT")) arguments.Add(new TagArgument(null, "PROMPT"));
			if (href.Length > 0) OpenSend(tag with { Arguments = arguments }, href, openMode: false);
			else OpenElement(tag.Name, [], openMode: false);
		}
		else
		{
			var variable = tag.Get("NAME", 0);
			if (!string.IsNullOrEmpty(variable))
			{
				state.Entities[variable] = text;
				if (!tag.Has("PRIVATE")) Context.Raise(new VariableEvent(variable, text));
				OpenElement(tag.Name, [new VariableMarkup(variable)], openMode: false);
			}
			else
			{
				OpenElement(tag.Name, [], openMode: false);
			}
		}

		var saved = state.Line;
		state.Line = MxpMode.Secure;
		new TagScanner().Feed(raw, this);
		state.Line = saved;
		Close(tag.Name);
	}

	// ── Pictures and sounds ─────────────────────────────────────────────────────

	private void Image(ParsedTag tag)
	{
		var file = tag.Get("FNAME", 0);
		if (string.IsNullOrEmpty(file)) return;
		var source = Address(tag.Get("URL"), file);
		var (width, widthCells) = Size(tag.Get("W"));
		var (height, heightCells) = Size(tag.Get("H"));
		var align = Enum.TryParse<ImageAlign>(tag.Get("ALIGN"), ignoreCase: true, out var parsed) ? parsed : (ImageAlign?)null;
		var image = new ImageMarkup(source, null, width, height, align);

		if (widthCells > 0 && heightCells > 0)
		{
			// Sized in cells: drawn over the cells the server leaves for it on this line and the ones after.
			State.Overlays.Add((image, heightCells, widthCells, Context.Column, 0));
			return;
		}

		Context.Write(file, image);
	}

	private static (int? Pixels, int Cells) Size(string? value)
	{
		if (string.IsNullOrEmpty(value)) return (null, 0);
		if (value.EndsWith('c') && int.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var cells)) return (null, cells);
		var digits = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
		return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var pixels) && pixels > 0 ? (pixels, 0) : (null, 0);
	}

	private void DrawOverlays()
	{
		var overlays = State.Overlays;
		if (overlays.Count == 0) return;
		var context = Context;
		var cursor = context.Column;
		for (var i = 0; i < overlays.Count; i++)
		{
			var (image, rows, columns, column, next) = overlays[i];
			context.MoveToColumn(column);
			context.Write(new string(' ', columns), image with { Row = new PictureRow(next, rows, columns) });
			overlays[i] = (image, rows, columns, column, next + 1);
		}

		overlays.RemoveAll(overlay => overlay.Next >= overlay.Rows);
		context.MoveToColumn(cursor);
	}

	private void Sound(ParsedTag tag)
	{
		var music = tag.Name.Equals("MUSIC", StringComparison.OrdinalIgnoreCase);
		var channel = music ? SoundChannel.Music : SoundChannel.Effects;
		var file = tag.Get("FNAME", 0);
		if (string.IsNullOrEmpty(file)) return;
		if (file.Equals("Off", StringComparison.OrdinalIgnoreCase))
		{
			Context.Point(new SoundStopMarkup(channel));
			return;
		}

		int? volume = int.TryParse(tag.Get("V"), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 100) : null;
		int? repeats = int.TryParse(tag.Get("L"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)
			? l < 0 ? SoundMarkup.Forever : l
			: null;
		var continues = music && tag.Get("C") == "1";
		Context.Point(new SoundMarkup(Address(tag.Get("U"), file), channel, volume, repeats, continues));
	}

	private static string Address(string? directory, string file)
	{
		if (string.IsNullOrEmpty(directory) || Uri.TryCreate(file, UriKind.Absolute, out _)) return file;
		return directory.EndsWith('/') ? directory + file : directory + "/" + file;
	}

	// ── Helpers ─────────────────────────────────────────────────────────────────

	private string? LookupEntity(string name) => State.Entities.TryGetValue(name, out var value) ? value : null;

	private static AnsiColor? Color(string? name)
	{
		if (string.IsNullOrWhiteSpace(name)) return null;
		name = name.Trim();
		if (name[0] == '#' && name.Length == 7
			&& int.TryParse(name.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
		{
			return new AnsiColor.Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
		}

		var known = System.Drawing.Color.FromName(name);
		return known.IsKnownColor && !known.IsSystemColor ? new AnsiColor.Rgb(known.R, known.G, known.B) : null;
	}

	private static string Quote(string value) => "\"" + value.Replace("\"", "'", StringComparison.Ordinal) + "\"";

	private static string Supports(ParsedTag tag)
	{
		var supported = new HashSet<string>(MxpPlugin.Supported, StringComparer.OrdinalIgnoreCase);
		var answer = new StringBuilder("\e[1z<SUPPORTS");
		var asked = tag.Arguments.Where(argument => argument.Name is null).Select(argument => argument.Value).ToList();
		if (asked.Count == 0)
		{
			foreach (var name in MxpPlugin.Supported) answer.Append(" +").Append(name.ToLowerInvariant());
		}
		else
		{
			foreach (var item in asked)
			{
				// "send.prompt" asks about an attribute; the element's answer stands for it.
				var element = item.Split('.')[0];
				answer.Append(supported.Contains(element) ? " +" : " -").Append(item.ToLowerInvariant());
			}
		}

		return answer.Append(">\n").ToString();
	}
}
