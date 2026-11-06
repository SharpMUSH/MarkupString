using System.Globalization;
using MarkupString.Ansi;
using MarkupString.Decoding;
namespace MarkupString.Pueblo.Decoding;

/// <summary>Registers Pueblo decoding on a <see cref="DecoderRegistry"/>.</summary>
public static class PuebloDecoding
{
	/// <summary>The name to pass <see cref="MarkupString.Decoding.Decoder.EnableProtocol(string)"/> once the server has said it speaks Pueblo.</summary>
	public const string Protocol = "pueblo";

	/// <summary>The registry with <see cref="PuebloPlugin"/>.</summary>
	public static DecoderRegistry WithPueblo(this DecoderRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);
		return registry.With(PuebloPlugin.Instance);
	}

	/// <summary>
	/// Whether <paramref name="line"/> is a server's Pueblo greeting (<c>This world is Pueblo 1.10 Enhanced.</c>), after
	/// which a client answers <c>PUEBLOCLIENT</c> and turns the protocol on.
	/// </summary>
	public static bool IsGreeting(string line) =>
		line is not null && line.TrimStart().StartsWith("This world is Pueblo ", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Pueblo as a text protocol: plain MUD text until the server sends <c>&lt;/xch_mudtext&gt;</c> (or
/// <c>&lt;img xch_mode=html&gt;</c>), then HTML, in which line breaks are <c>&lt;br&gt;</c> and a bare line feed is
/// whitespace, until <c>&lt;xch_mudtext&gt;</c>. ANSI inside it is read as ever.
/// </summary>
/// <remarks>
/// Styles (<c>b</c>, <c>i</c>, <c>u</c>, <c>s</c>, <c>font color</c>) become <see cref="AnsiMarkup"/>; links
/// (<c>a href</c>, <c>a xch_cmd</c>) a link on one; and <c>img</c>, <c>img xch_sound</c>, <c>xch_pane</c>,
/// <c>xch_page</c>, <c>xch_prefetch</c>, <c>pre</c> and <c>xch_mudtext</c> the shared vocabulary.
/// </remarks>
public sealed class PuebloPlugin : IDecoderPlugin
{
	/// <summary>The one instance.</summary>
	public static PuebloPlugin Instance { get; } = new();

	private PuebloPlugin()
	{
	}

	/// <inheritdoc/>
	public string Name => "pueblo";

	/// <inheritdoc/>
	public void Register(DecoderBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder
			.TextProtocol(PuebloDecoding.Protocol, static () => new PuebloReader())
			.OnControl('\n', static (_, context) =>
			{
				// In HTML a line feed is whitespace; <br> ends the line.
				if (!context.IsActive(PuebloDecoding.Protocol)) return false;
				var state = context.State<PuebloState>();
				return state.Html && state.Preformatted == 0;
			})
			.OnControl('\r', static (_, context) =>
			{
				if (!context.IsActive(PuebloDecoding.Protocol)) return false;
				var state = context.State<PuebloState>();
				return state.Html && state.Preformatted == 0;
			});
	}
}

internal sealed class PuebloState
{
	public bool Html { get; set; }

	public int Preformatted { get; set; }

	public List<(string Name, IMarkup? First)> Open { get; } = [];

	public IMarkup? Pane { get; set; }
}

internal sealed class PuebloReader : ITextProtocol, ITagSink
{
	private readonly TagScanner _scanner = new();
	private DecodeContext? _context;

	private DecodeContext Context => _context!;

	private PuebloState State => Context.State<PuebloState>();

	public bool ReadsMarkup => true;

	public void Feed(ReadOnlySpan<char> text, DecodeContext context)
	{
		_context = context;
		_scanner.Feed(text, this);
	}

	public void EndLine(DecodeContext context)
	{
		_context = context;
		_scanner.Flush(this);
	}

	public void Reset(DecodeContext context)
	{
		_context = context;
		_scanner.Reset();
		var state = State;
		// Close the earliest of what this protocol opened, which closes the rest.
		var ours = state.Open.Select(element => element.First).Append(state.Pane).OfType<IMarkup>().ToHashSet(ReferenceEqualityComparer.Instance);
		var first = context.Elements.FirstOrDefault(ours.Contains);
		if (first is not null) context.Close(first);
		state.Open.Clear();
		state.Pane = null;
		state.Html = false;
		state.Preformatted = 0;
	}

	public void Text(ReadOnlySpan<char> text) => Context.Print(text);

	public void Entity(ReadOnlySpan<char> name)
	{
		var decoded = State.Html ? TagSyntax.Entity(name) : null;
		Context.Print(decoded ?? $"&{name};");
	}

	public void Tag(ReadOnlySpan<char> body)
	{
		var state = State;
		var tag = TagSyntax.Parse(body, static name => null);
		var name = tag?.Name.ToLowerInvariant();

		if (name == "xch_mudtext")
		{
			if (tag!.Closing)
			{
				state.Html = true;
				Close("xch_mudtext");
			}
			else
			{
				state.Html = false;
				OpenElement("xch_mudtext", PreformattedMarkup.Instance);
			}

			return;
		}

		if (name == "img" && tag!.Get("xch_mode") is { } mode)
		{
			state.Html = mode.Equals("html", StringComparison.OrdinalIgnoreCase) || mode.Equals("purehtml", StringComparison.OrdinalIgnoreCase);
			return;
		}

		if (!state.Html || tag is null)
		{
			// MUD text: what looks like a tag is text.
			Context.Print($"<{body}>");
			return;
		}

		if (tag.Closing)
		{
			if (name == "pre" && state.Preformatted > 0) state.Preformatted--;
			Close(name!);
			return;
		}

		if (!Element(tag, name!)) Context.Diagnostics.UnhandledSequences++;
	}

	private bool Element(ParsedTag tag, string name)
	{
		var context = Context;
		switch (name)
		{
			case "b" or "strong":
				OpenStyle(name, new AnsiStyle { Bold = true });
				return true;
			case "i" or "em":
				OpenStyle(name, new AnsiStyle { Italic = true });
				return true;
			case "u":
				OpenStyle(name, new AnsiStyle { Underlined = true });
				return true;
			case "s" or "strike" or "del":
				OpenStyle(name, new AnsiStyle { StrikeThrough = true });
				return true;
			case "font":
				OpenStyle(name, new AnsiStyle { Foreground = Color(tag.Get("color")), Background = Color(tag.Get("bgcolor")) });
				return true;
			case "a":
				{
					AnsiStyle? link = null;
					var hint = tag.Get("xch_hint");
					if (tag.Get("xch_cmd") is { Length: > 0 } command)
					{
						link = new AnsiStyle { LinkUrl = command, LinkKind = LinkKind.Command, LinkText = hint };
					}
					else if (tag.Get("href") is { } href && Context.Options.Links.IsAllowed(href))
					{
						link = new AnsiStyle { LinkUrl = href, LinkKind = LinkKind.Url, LinkText = hint };
					}

					OpenElement(name, link is { } style ? new AnsiMarkup(style) : null);
					return true;
				}
			case "br":
				context.EndLine();
				return true;
			case "p" or "hr":
				if (!context.LineIsEmpty) context.EndLine();
				return true;
			case "pre":
				State.Preformatted++;
				OpenElement(name, PreformattedMarkup.Instance);
				return true;
			case "img":
				Image(tag);
				return true;
			case "xch_page":
				if (string.Equals(tag.Get("clear"), "text", StringComparison.OrdinalIgnoreCase)) context.Point(ClearScreenMarkup.Instance);
				return true;
			case "xch_prefetch":
				if (tag.Get("href") is { Length: > 0 } source) context.Point(new PrefetchMarkup(source));
				return true;
			case "xch_pane":
				Pane(tag);
				return true;
			case "html" or "head" or "body" or "title" or "tt" or "small" or "big" or "center" or "span" or "div" or "nobr":
				return true;
			default:
				return false;
		}
	}

	private void Image(ParsedTag tag)
	{
		var context = Context;
		if (tag.Get("xch_sound") is { } action)
		{
			var device = tag.Get("xch_device");
			var channel = device is null ? (SoundChannel?)null
				: device.Equals("midi", StringComparison.OrdinalIgnoreCase) ? SoundChannel.Music : SoundChannel.Effects;
			if (action.Equals("stop", StringComparison.OrdinalIgnoreCase))
			{
				context.Point(new SoundStopMarkup(channel));
				return;
			}

			if (tag.Get("href") is not { Length: > 0 } href) return;
			int? volume = int.TryParse(tag.Get("xch_volume"), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 100) : null;
			var loops = action.Equals("loop", StringComparison.OrdinalIgnoreCase);
			context.Point(new SoundMarkup(href, channel ?? SoundChannel.Effects, volume, loops ? SoundMarkup.Forever : null));
			return;
		}

		if (tag.Get("src") is not { Length: > 0 } src) return;
		var alt = tag.Get("alt");
		int? width = int.TryParse(tag.Get("width"), NumberStyles.None, CultureInfo.InvariantCulture, out var w) && w > 0 ? w : null;
		int? height = int.TryParse(tag.Get("height"), NumberStyles.None, CultureInfo.InvariantCulture, out var h) && h > 0 ? h : null;
		var align = Enum.TryParse<ImageAlign>(tag.Get("align"), ignoreCase: true, out var parsed) ? parsed : (ImageAlign?)null;
		var image = new ImageMarkup(src, string.IsNullOrEmpty(alt) ? null : alt, width, height, align);
		context.Write(string.IsNullOrEmpty(alt) ? " " : alt, image);
	}

	private void Pane(ParsedTag tag)
	{
		if (!string.Equals(tag.Get("action"), "redirect", StringComparison.OrdinalIgnoreCase)) return;
		var state = State;
		if (state.Pane is { } current)
		{
			Context.Close(current);
			state.Pane = null;
		}

		var name = tag.Get("name");
		if (string.IsNullOrEmpty(name) || name.StartsWith('_')) return;
		var pane = new PaneMarkup(name, tag.Get("panetitle"));
		if (Context.Open(pane)) state.Pane = pane;
	}

	private void OpenStyle(string name, AnsiStyle style) => OpenElement(name, style.IsNone ? null : new AnsiMarkup(style));

	private void OpenElement(string name, IMarkup? markup)
	{
		if (markup is not null && !Context.Open(markup)) markup = null;
		State.Open.Add((name, markup));
	}

	private void Close(string name)
	{
		var open = State.Open;
		var index = open.FindLastIndex(element => element.Name == name);
		if (index < 0) return;
		for (var i = index; i < open.Count; i++)
		{
			if (open[i].First is { } first)
			{
				Context.Close(first);
				break;
			}
		}

		open.RemoveRange(index, open.Count - index);
	}

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
}
