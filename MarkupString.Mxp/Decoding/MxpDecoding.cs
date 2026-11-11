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

/// <summary>How a client reads MXP, for <see cref="MxpDecoding.WithMxp(DecoderRegistry, MxpOptions)"/>.</summary>
public sealed record MxpOptions
{
	/// <summary>The defaults: refused tags are dropped, and <c>&lt;SUPPORT&gt;</c> is answered with <see cref="MxpPlugin.Supported"/>.</summary>
	public static MxpOptions Default { get; } = new();

	/// <summary>
	/// Show a tag a line may not use as the text it was, character for character, instead of dropping it. A player
	/// who types <c>&lt;SEND&gt;</c> into a channel then shows as having typed it, and nothing they wrote goes
	/// missing. A tag refused because it was secure on an open line, a definition there, and the close of a secure
	/// tag that nothing opened are all shown.
	/// </summary>
	public bool ShowRefusedTags { get; init; }

	/// <summary>
	/// What <c>&lt;SUPPORT&gt;</c> is answered with: each tag the client acts on, lower case, with the attributes it
	/// acts on (none for a tag whose attributes it does not list). Null answers with <see cref="MxpPlugin.Supported"/>,
	/// every tag this package reads. A client that reads a tag and does nothing with it should leave it out, since a
	/// server takes the answer as a promise.
	/// </summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>>? Supports { get; init; }

	/// <summary>
	/// The answer to a <c>&lt;SUPPORT&gt;</c> that asked about <paramref name="asked"/>: every supported tag when it
	/// asked about nothing; otherwise each entry it named, as supported or not. <c>tag.attr</c> is supported when the
	/// tag's attributes list it, and <c>tag.*</c> is answered with each attribute listed for the tag, or as unsupported
	/// for a tag that is not.
	/// </summary>
	public (IReadOnlyList<string> Supported, IReadOnlyList<string> Unsupported) Answer(IReadOnlyList<string> asked)
	{
		var entries = Entries(asked);
		return (entries.Where(e => e.Supported).Select(e => e.Name).ToList(), entries.Where(e => !e.Supported).Select(e => e.Name).ToList());
	}

	/// <summary>The answer entry by entry, in the order asked. With no <see cref="Supports"/>, an attribute is answered as its tag is.</summary>
	internal List<(string Name, bool Supported)> Entries(IReadOnlyList<string> asked)
	{
		ArgumentNullException.ThrowIfNull(asked);
		var tags = Supports ?? MxpPlugin.Supported.ToDictionary(name => name.ToLowerInvariant(), _ => (IReadOnlyList<string>)[], StringComparer.OrdinalIgnoreCase);
		var entries = new List<(string, bool)>();
		if (asked.Count == 0)
		{
			foreach (var name in tags.Keys) entries.Add((name, true));
			return entries;
		}

		foreach (var entry in asked)
		{
			var name = entry.Trim('"', '\'');
			if (name.Length == 0) continue;
			var dot = name.IndexOf('.', StringComparison.Ordinal);
			var tag = dot < 0 ? name : name[..dot];
			var known = Lookup(tags, tag, out var attributes);
			if (dot < 0 || Supports is null)
			{
				entries.Add((name, known));
				continue;
			}

			var attribute = name[(dot + 1)..];
			if (attribute == "*")
			{
				if (known) entries.AddRange(attributes.Select(a => ($"{tag}.{a}", true)));
				else entries.Add((name, false));
			}
			else
			{
				entries.Add((name, attributes.Contains(attribute, StringComparer.OrdinalIgnoreCase)));
			}
		}

		return entries;
	}

	private static bool Lookup(IReadOnlyDictionary<string, IReadOnlyList<string>> tags, string tag, out IReadOnlyList<string> attributes)
	{
		foreach (var (name, list) in tags)
		{
			if (name.Equals(tag, StringComparison.OrdinalIgnoreCase))
			{
				attributes = list;
				return true;
			}
		}

		attributes = [];
		return false;
	}
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

	/// <summary>The registry with an <see cref="MxpPlugin"/> that reads as <paramref name="options"/> say.</summary>
	public static DecoderRegistry WithMxp(this DecoderRegistry registry, MxpOptions options)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(options);
		return registry.With(new MxpPlugin(options));
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
	/// <summary>The plugin with <see cref="MxpOptions.Default"/>.</summary>
	public static MxpPlugin Instance { get; } = new(MxpOptions.Default);

	/// <summary>The tags this decoder reads, as <c>&lt;SUPPORTS&gt;</c> answers.</summary>
	public static IReadOnlyList<string> Supported { get; } =
	[
		"B", "BOLD", "STRONG", "I", "ITALIC", "EM", "U", "UNDERLINE", "S", "STRIKEOUT", "H", "HIGH", "C", "COLOR", "FONT",
		"BR", "SBR", "P", "NOBR", "A", "SEND", "EXPIRE", "VERSION", "SUPPORT", "IMAGE", "SOUND", "MUSIC", "FRAME", "DEST",
		"RELOCATE", "USER", "PASSWORD", "VAR", "GAUGE", "STAT", "ELEMENT", "ENTITY", "ATTLIST", "TAG",
	];

	/// <summary>The most elements one connection may define with <c>&lt;!ELEMENT&gt;</c>.</summary>
	public const int MaxElements = 256;

	/// <summary>The most entities one connection may define with <c>&lt;!ENTITY&gt;</c>.</summary>
	public const int MaxEntities = 512;

	/// <summary>The longest element definition or entity value kept; a longer one is refused.</summary>
	public const int MaxDefinitionLength = 1024;

	/// <summary>How deep an element's definition may open other elements.</summary>
	public const int MaxExpansionDepth = 4;

	/// <summary>How many element expansions one line may cost.</summary>
	public const int MaxExpansionsPerLine = 64;

	/// <summary>A plugin that reads as <paramref name="options"/> say.</summary>
	public MxpPlugin(MxpOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		Options = options;
	}

	/// <summary>How this plugin reads.</summary>
	public MxpOptions Options { get; }

	/// <inheritdoc/>
	public string Name => "mxp";

	/// <inheritdoc/>
	public void Register(DecoderBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder
			.TextProtocol(MxpDecoding.Protocol, () => new MxpReader(Options))
			.OnCsi('z', static (in CsiSequence sequence, DecodeContext context) =>
			{
				// A line mode means nothing until MXP is on, and is dropped.
				// ESC [ z with no number is not a line mode: it moves nothing.
				if (context.IsActive(MxpDecoding.Protocol)) MxpReader.LineMode(sequence.Get(0, -1), context);
				return true;
			})
			.OnControl('\t', static (_, context) =>
			{
				// A tab inside a tag is part of the tag, not a move of the cursor.
				if (!context.IsActive(MxpDecoding.Protocol) || context.State<MxpState>().Reader is not { InMarkup: true } reader) return false;
				reader.Feed("\t", context);
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

	public List<(ImageMarkup Image, ElementMarkup Element, int Rows, int Columns, int Column, int Next, MarkupText Inline)> Overlays { get; } = [];

	public int ExpansionDepth { get; set; }

	public MxpReader? Reader { get; set; }

	public bool Replaying { get; set; }

	public int ExpansionsThisLine { get; set; }

	public string? StyleVersion { get; set; }

	/// <summary>The frame a FRAME redirect sent lines to, so turning MXP off sends them back to the main window.</summary>
	public string? Redirect { get; set; }
}

internal sealed class MxpReader(MxpOptions options) : ITextProtocol, ITagSink, ISequenceObserver
{
	private const int MaxCapture = 4096;

	/// <summary>The most cells a picture sized in cells may cover either way; a larger size is ignored.</summary>
	private const int MaxImageCells = 1024;

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

	public bool InMarkup => _scanner.InMarkup;

	public void Feed(ReadOnlySpan<char> text, DecodeContext context)
	{
		_context = context;
		State.Reader = this;
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
		state.ExpansionsThisLine = 0;
	}

	public void Reset(DecodeContext context)
	{
		_context = context;
		_scanner.Reset();
		var state = State;
		if (state.Open.Count > 0) CloseFrom(0);
		state.Capture = null;
		state.Overlays.Clear();
		if (state.Redirect is { } redirect && string.Equals(context.Destination, redirect, StringComparison.OrdinalIgnoreCase))
		{
			context.Destination = null;
		}

		state.Redirect = null;
		state.Line = state.Default = MxpMode.Open;
		state.TemporarySecure = false;
	}

	internal static void LineMode(int mode, DecodeContext context)
	{
		var state = context.State<MxpState>();
		switch (mode)
		{
			case 0: state.Line = MxpMode.Open; break;
			case 1: LeaveOpen(context); state.Line = MxpMode.Secure; break;
			case 2: LeaveOpen(context); state.Line = MxpMode.Locked; break;
			case 3:
				state.Line = state.Default = MxpMode.Open;
				var first = state.Open.FirstOrDefault(element => element.First is not null)?.First;
				if (first is not null) context.Close(first);
				state.Open.Clear();
				break;
			case 4: state.TemporarySecure = true; break;
			case 5: state.Line = state.Default = MxpMode.Open; break;
			case 6: LeaveOpen(context); state.Line = state.Default = MxpMode.Secure; break;
			case 7: LeaveOpen(context); state.Line = state.Default = MxpMode.Locked; break;
			case >= 10 and <= 99:
				if (state.LineTags.TryGetValue(mode, out var element) && state.Elements.TryGetValue(element, out var definition))
				{
					// The line is the element; the reader opens it and the line's end closes it.
					LeaveOpen(context);
					state.Line = MxpMode.Secure;
					var reader = new MxpReader(MxpOptions.Default) { _context = context };
					reader.OpenCustom(definition, new ParsedTag(definition.Name, false, []), lineTag: true, trusted: true);
				}
				else if (mode switch { 10 => MxpFlagMarkup.RoomName, 11 => MxpFlagMarkup.RoomDescription, 12 => MxpFlagMarkup.RoomExits, 19 => MxpFlagMarkup.Welcome, _ => null } is { } flag)
				{
					LeaveOpen(context);
					state.Line = MxpMode.Secure;
					var flagged = context.NextElement();
					if (context.Open(flagged))
					{
						context.Open(new MxpFlagMarkup(flag));
						state.Open.Add(new MxpOpenElement(flag, flagged, openMode: false, lineTag: true));
					}
				}

				// A number with no meaning moves nothing, in either direction.
				break;
		}
	}

	/// <summary>Leaving open mode mid-line ends what the open part of the line opened, as the line's end would.</summary>
	private static void LeaveOpen(DecodeContext context)
	{
		var state = context.State<MxpState>();
		if (state.Line != MxpMode.Open) return;
		var index = state.Open.FindIndex(element => element.OpenMode);
		if (index < 0) return;
		for (var i = index; i < state.Open.Count; i++)
		{
			if (state.Open[i].First is { } first)
			{
				context.Close(first);
				break;
			}
		}

		state.Open.RemoveRange(index, state.Open.Count - index);
	}

	// ── ISequenceObserver ────────────────────────────────────────────────────────

	/// <summary>
	/// A sequence between <c>ESC [ 4 z</c> and the tag it is for ends it: the one tag it makes secure must come
	/// straight after. <c>ESC [ 4 z</c> itself is a sequence too, and arms it again as it is handled.
	/// </summary>
	public void SequenceSeen(DecodeContext context) => context.State<MxpState>().TemporarySecure = false;

	// ── ITagSink ────────────────────────────────────────────────────────────────

	public void Text(ReadOnlySpan<char> text)
	{
		var state = State;

		// Text between ESC [ 4 z and a tag ends it, locked text too, so the tag after the server's words is not the
		// one it was for.
		if (!text.IsEmpty) state.TemporarySecure = false;
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
		state.TemporarySecure = false;
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
			else Refuse(body);
			return;
		}

		var tag = TagSyntax.Parse(body, LookupEntity);
		if (tag is null) return;

		var custom = state.Elements.TryGetValue(tag.Name, out var definition) ? definition : null;
		var open = custom?.Open ?? OpenTags.Contains(tag.Name);

		if (tag.Closing)
		{
			// The close of a secure tag nothing opened is refused like its opener; one that closes what is open is not.
			if (!secure && !open && state.Open.FindLastIndex(element => element.Name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase)) < 0)
			{
				Refuse(body);
				return;
			}

			Close(tag.Name);
			return;
		}

		if (!secure && !open)
		{
			Refuse(body);
			return;
		}

		if (custom is not null)
		{
			OpenCustom(custom, tag, lineTag: false, trusted: secure);
			return;
		}

		if (!Element(tag, openMode: !secure)) Context.Diagnostics.UnhandledSequences++;
	}

	/// <summary>A tag this line may not use: counted, and shown as the text it was when the options say so.</summary>
	private void Refuse(ReadOnlySpan<char> body)
	{
		Context.Diagnostics.UnhandledSequences++;
		if (!options.ShowRefusedTags || State.Replaying) return;
		Context.Print("<");
		Context.Print(body);
		Context.Print(">");
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
					OpenElement(tag.Name, link is null ? [] : [Context.NextElement(), link], openMode, lineTag: true);
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
				if (tag.Get("STYLE", 0) is { } style)
				{
					// <VERSION n> from the server sets the style-sheet version the client reports back.
					State.StyleVersion = style.Length <= 32 ? style : style[..32];
					return true;
				}

				if ((context.Identity.Answers & ClientAnswers.Mxp) != 0)
				{
					var styleField = State.StyleVersion is { } set ? $" STYLE={Quote(set)}" : "";
					context.Reply(string.Create(CultureInfo.InvariantCulture,
						$"\e[1z<VERSION MXP=1.0{styleField} CLIENT={Quote(context.Identity.Name)} VERSION={Quote(context.Identity.Version)}>\n"));
				}

				return true;
			case "SUPPORT":
				if ((context.Identity.Answers & ClientAnswers.Mxp) != 0) context.Reply(Supports(tag, options));
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
					if (name is null) return true;
					var action = tag.Get("ACTION")?.ToUpperInvariant() ?? (tag.Has("REDIRECT") ? "REDIRECT" : "OPEN");
					var main = name.Equals("_top", StringComparison.OrdinalIgnoreCase) || name.Equals("_previous", StringComparison.OrdinalIgnoreCase);
					switch (action)
					{
						case "REDIRECT":
							// Every line from here goes to the frame, until a redirect back to the main window.
							context.Destination = main ? null : name;
							State.Redirect = context.Destination;
							break;
						case "CLOSE":
							State.FrameTitles.Remove(name);
							if (string.Equals(context.Destination, name, StringComparison.OrdinalIgnoreCase)) context.Destination = null;
							break;
						default:
							if (!main) State.FrameTitles[name] = tag.Get("TITLE");
							break;
					}

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
				{
					// A gauge stands for a variable the host already has from VAR and ENTITY; there is no text to show,
					// only how the host is asked to show it.
					var variable = tag.Get("ENTITY", 0);
					if (string.IsNullOrEmpty(variable)) return false;
					context.Raise(new GaugeEvent(variable, tag.Get("MAX", 1), tag.Get("CAPTION", 2), tag.Get("COLOR"),
						Status: tag.Name.Equals("STAT", StringComparison.OrdinalIgnoreCase)));
					return true;
				}
			default:
				return false;
		}
	}

	private void OpenStyle(string name, AnsiStyle style, bool openMode) =>
		OpenElement(name, style.IsNone ? [] : [new AnsiMarkup(style)], openMode);

	// lineTag: the element ends with the line, as a line tag's does. A link does, wherever it was opened, so a SEND the
	// server left open does not make every line after it a command.
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

		// One command has the whole hint. With several, one hint more than commands is the link's own and then the
		// menu's labels, and as many hints as commands are the labels alone.
		string? hint;
		string[] labels;
		if (commands.Length == 1)
		{
			hint = tag.Get("HINT", 1);
			labels = commands;
		}
		else if (hints.Length == commands.Length + 1)
		{
			hint = hints[0];
			labels = hints[1..];
		}
		else if (hints.Length == commands.Length)
		{
			hint = null;
			labels = hints;
		}
		else
		{
			hint = hints.Length > 0 ? hints[0] : null;
			labels = commands;
		}

		var link = new AnsiMarkup(new AnsiStyle { LinkUrl = commands[0], LinkKind = LinkKind.Command, LinkText = hint });
		if (commands.Length == 1 && !prompt)
		{
			OpenElement(tag.Name, [Context.NextElement(), link], openMode, lineTag: true);
			return;
		}

		var choices = new CommandChoice[commands.Length];
		for (var i = 0; i < commands.Length; i++) choices[i] = new CommandChoice(labels[i], commands[i]);
		OpenElement(tag.Name, [Context.NextElement(), new CommandMenuMarkup(choices, hint, prompt), link], openMode, lineTag: true);
	}

	/// <summary>
	/// Opens a custom element. Its definition's tags run with the trust the element was used with: anything, used on a
	/// secure line; only open tags, used by a player on an open line, where whatever else the definition holds is
	/// dropped (it is the server's text, not the player's, so it is not shown).
	/// </summary>
	private void OpenCustom(MxpElementDefinition definition, ParsedTag tag, bool lineTag, bool trusted)
	{
		var state = State;
		if (state.ExpansionDepth >= MxpPlugin.MaxExpansionDepth || state.ExpansionsThisLine >= MxpPlugin.MaxExpansionsPerLine)
		{
			// An element that opens itself, or a line of thousands of them, is a bomb rather than markup.
			Context.Diagnostics.LimitsReached++;
			return;
		}

		state.ExpansionsThisLine++;
		var before = Context.Elements.Count;
		var opened = state.Open.Count;

		if (definition.Flag is { } flag && Context.Open(Context.NextElement())) Context.Open(new MxpFlagMarkup(flag));

		// The definition's tags, with the element's attributes put in for &name;, run as secure tags.
		var expanded = Expand(definition, tag);
		var replay = new TagScanner();
		state.TemporarySecure = false;
		var saved = state.Line;
		var replaying = state.Replaying;
		state.Line = trusted ? MxpMode.Secure : MxpMode.Open;
		state.Replaying = true;
		state.ExpansionDepth++;
		try
		{
			replay.Feed(expanded, this);
		}
		finally
		{
			// Restored even when a handler throws, or open-line text after it would be read as secure.
			state.Line = saved;
			state.ExpansionDepth--;
			state.Replaying = replaying;
		}

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

			// A value cannot close the tag it is put into and open another.
			value = value.Replace("<", string.Empty, StringComparison.Ordinal).Replace(">", string.Empty, StringComparison.Ordinal);
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

					// The definition is the second positional value, unless that is one of the element's keywords.
					var second = tag.Get("DEFINITION", 1);
					var definitionText = tag.Get("DEFINITION") ?? (second is not null && !ElementKeywords.Contains(second) ? second : null) ?? string.Empty;
					if (IsBuiltIn(name) || definitionText.Length > MxpPlugin.MaxDefinitionLength
						|| (!state.Elements.ContainsKey(name) && state.Elements.Count >= MxpPlugin.MaxElements))
					{
						Context.Diagnostics.LimitsReached++;
						return;
					}

					var attributes = TagSyntax.Parse("x " + (tag.Get("ATT") ?? string.Empty))?.Arguments ?? [];
					var definition = new MxpElementDefinition(name, definitionText, attributes, tag.Get("FLAG"), tag.Has("OPEN"), tag.Has("EMPTY"));
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
					if (tag.Has("DELETE"))
					{
						state.Entities.Remove(name);
						Context.Raise(new VariableEvent(name, null));
						return;
					}

					var value = tag.Get("VALUE", 1) ?? string.Empty;
					state.Entities.TryGetValue(name, out var existing);
					if (tag.Has("REMOVE"))
					{
						// REMOVE takes the value out of the list the entity holds; the entity stays.
						if (existing is null) return;
						value = string.Join('|', existing.Split('|').Where(item => !item.Equals(value, StringComparison.Ordinal)));
					}
					else if (tag.Has("ADD") && existing is not null)
					{
						value = existing + "|" + value;
					}

					if (!CanStoreEntity(name, value)) return;

					state.Entities[name] = value;
					if (!tag.Has("PRIVATE")) Context.Raise(new VariableEvent(name, value));
					return;
				}
		}
	}

	private static readonly HashSet<string> ElementKeywords = new(["OPEN", "EMPTY", "DELETE"], StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> BuiltIns = new(MxpPlugin.Supported.Concat(["STRIKE", "SMALL", "TT", "GAUGE", "STAT", "V"]), StringComparer.OrdinalIgnoreCase);

	private static bool IsBuiltIn(string name) => BuiltIns.Contains(name) || OpenTags.Contains(name);

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

	/// <summary>
	/// Whether <paramref name="name"/> may be set to <paramref name="value"/>: &amp;lt; and the other built-in entities
	/// keep their meaning, and the table stays bounded in size and in each value's length.
	/// </summary>
	private bool CanStoreEntity(string name, string value)
	{
		var state = State;
		if (TagSyntax.Entity(name, static _ => null) is null && value.Length <= MxpPlugin.MaxDefinitionLength
			&& (state.Entities.ContainsKey(name) || state.Entities.Count < MxpPlugin.MaxEntities))
		{
			return true;
		}

		Context.Diagnostics.LimitsReached++;
		return false;
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
				if (CanStoreEntity(variable, text))
				{
					state.Entities[variable] = text;
					if (!tag.Has("PRIVATE")) Context.Raise(new VariableEvent(variable, text));
				}

				OpenElement(tag.Name, [Context.NextElement(), new VariableMarkup(variable)], openMode: false);
			}
			else
			{
				OpenElement(tag.Name, [], openMode: false);
			}
		}

		var saved = state.Line;
		state.Line = MxpMode.Secure;
		try
		{
			new TagScanner().Feed(raw, this);
		}
		finally
		{
			state.Line = saved;
		}

		Close(tag.Name);
	}

	// ── Pictures and sounds ─────────────────────────────────────────────────────

	private void Image(ParsedTag tag)
	{
		var file = tag.Get("FNAME", 0);
		if (string.IsNullOrEmpty(file)) return;
		var source = Address(tag.Get("URL"), tag.Get("T"), file);
		var (width, widthCells) = Size(tag.Get("W"));
		var (height, heightCells) = Size(tag.Get("H"));
		var align = Enum.TryParse<ImageAlign>(tag.Get("ALIGN"), ignoreCase: true, out var parsed) ? parsed : (ImageAlign?)null;
		var image = new ImageMarkup(source, null, width, height, align);

		// Each picture is its own, so two of one file side by side stay two.
		var element = Context.NextElement();
		if (widthCells > 0 && heightCells > 0)
		{
			// Sized in cells: drawn over the cells the server leaves for it on this line and the ones after. If it left
			// none, the picture goes in the line where its tag was, as one sized in pixels does.
			var set = Context.CurrentMarkup;
			var layers = new IMarkup[(set?.Count ?? 0) + 2];
			layers[0] = image with { Width = widthCells * Context.Identity.CellWidth, Height = heightCells * Context.Identity.CellHeight };
			layers[1] = element;
			for (var i = 2; i < layers.Length; i++) layers[i] = set![i - 2];
			State.Overlays.Add((image, element, heightCells, widthCells, Context.Column, 0, MarkupText.Wrap(MarkupSet.Of(layers.AsSpan()), file)));
			return;
		}

		// Sized in cells one way only, the other way is the client's cell size in pixels.
		width ??= widthCells > 0 ? widthCells * Context.Identity.CellWidth : null;
		height ??= heightCells > 0 ? heightCells * Context.Identity.CellHeight : null;
		image = image with { Width = width, Height = height };

		WriteIn(element, file, image);
	}

	/// <summary>Writes <paramref name="text"/> under <paramref name="inner"/> inside <paramref name="element"/>.</summary>
	private void WriteIn(ElementMarkup element, string text, IMarkup inner) => WriteIn(Context, element, text, inner);

	private static void WriteIn(DecodeContext context, ElementMarkup element, string text, IMarkup inner)
	{
		if (!context.Open(element))
		{
			context.Write(text, inner);
			return;
		}

		context.Write(text, inner);
		context.Close(element);
	}

	private static (int? Pixels, int Cells) Size(string? value)
	{
		if (string.IsNullOrEmpty(value)) return (null, 0);
		if (value.EndsWith('c') && int.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var cells))
		{
			return (null, cells <= MaxImageCells ? cells : 0);
		}

		var digits = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
		return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var pixels) && pixels > 0 ? (pixels, 0) : (null, 0);
	}

	private void DrawOverlays()
	{
		var overlays = State.Overlays;
		if (overlays.Count == 0) return;
		var context = Context;
		var cursor = context.Column;
		List<(int Column, int Width)>? inserted = null;
		for (var i = 0; i < overlays.Count; i++)
		{
			var (image, element, rows, columns, column, next, inline) = overlays[i];

			// A picture put inline earlier on this line moved the cells after it to the right.
			var at = column;
			if (inserted is not null)
			{
				foreach (var (insertedAt, width) in inserted)
				{
					if (insertedAt <= column) at += width;
				}
			}

			if (next == 0 && !Blank(context.PendingLine, at, columns))
			{
				// The server left no room for it here: the picture goes where its tag was instead of over its text.
				context.Insert(at, inline);
				if (cursor >= at) cursor += inline.DisplayWidth;
				(inserted ??= []).Add((column, inline.DisplayWidth));
				overlays[i] = (image, element, rows, columns, column, rows, inline);
				continue;
			}

			// A later row drawn over text would hide it; that row of the picture is left out instead.
			if (next == 0 || Blank(context.PendingLine, at, columns))
			{
				context.MoveToColumn(at);
				WriteIn(context, element, new string(' ', columns), image with { Row = new PictureRow(next, rows, columns) });
			}

			overlays[i] = (image, element, rows, columns, column, next + 1, inline);
		}

		overlays.RemoveAll(overlay => overlay.Next >= overlay.Rows);
		context.MoveToColumn(cursor);
	}

	/// <summary>Whether the line has <paramref name="columns"/> blank cells from <paramref name="column"/>.</summary>
	private static bool Blank(MarkupText line, int column, int columns)
	{
		if (line.DisplayWidth < column + columns) return false;
		var start = DisplayWidth.IndexAtWidth(line.Text, column);
		var end = DisplayWidth.IndexAtWidth(line.Text, column + columns);
		foreach (var c in line.Text.AsSpan(start, end - start))
		{
			if (c is not (' ' or '\u00a0')) return false;
		}

		return true;
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
		int? priority = !music && int.TryParse(tag.Get("P"), NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? Math.Clamp(p, 0, 100) : null;
		var type = tag.Get("T") is { Length: > 0 } t ? t : null;
		Context.Point(new SoundMarkup(Address(tag.Get("U"), type, file), channel, volume, repeats, continues)
		{
			Priority = priority,
			Type = type,
		});
	}

	/// <summary>
	/// Where a picture or sound is: the file under the base address, in the type's directory when there is one
	/// ("the classname is appended to the URL, along with the name of the file itself"), or the file as it is
	/// when there is no base or the file is already absolute.
	/// </summary>
	private static string Address(string? directory, string? type, string file)
	{
		if (string.IsNullOrEmpty(directory) || Uri.TryCreate(file, UriKind.Absolute, out _)) return file;
		var address = directory.EndsWith('/') ? directory : directory + "/";
		var kind = type?.Trim().Trim('/');
		if (!string.IsNullOrEmpty(kind)) address += kind + "/";
		return address + file;
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

	private static string Supports(ParsedTag tag, MxpOptions options)
	{
		var asked = tag.Arguments.Where(argument => argument.Name is null).Select(argument => argument.Value).ToList();
		var answer = new StringBuilder("\e[1z<SUPPORTS");
		foreach (var (name, supported) in options.Entries(asked)) answer.Append(supported ? " +" : " -").Append(name.ToLowerInvariant());
		return answer.Append(">\n").ToString();
	}
}
