using System.Globalization;
using System.Text;
using MarkupString.Decoding;
using MarkupString.Display;
namespace MarkupString.Ansi.Decoding;

/// <summary>Registers the terminal plugins on a <see cref="DecoderRegistry"/>.</summary>
public static class VtDecoding
{
	/// <summary>
	/// The registry with SGR, the C0 controls and line editing, character sets, OSC (links, titles, palette,
	/// clipboard) and the answers to terminal queries.
	/// </summary>
	public static DecoderRegistry WithVt(this DecoderRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);
		return registry
			.With(SgrPlugin.Instance)
			.With(ControlsPlugin.Instance)
			.With(CharsetPlugin.Instance)
			.With(OscPlugin.Instance)
			.With(AnswersPlugin.Instance);
	}

	/// <summary>A registry with nothing but the terminal plugins.</summary>
	public static DecoderRegistry Terminal { get; } = DecoderRegistry.Empty.WithVt();
}

/// <summary>The pen slots the terminal plugins write under, for a plugin that wants to read or replace them.</summary>
public static class VtPens
{
	/// <summary>The SGR style, an <see cref="AnsiMarkup"/>.</summary>
	public const string Style = "sgr";

	/// <summary>The OSC 8 or MSLP link, an <see cref="AnsiMarkup"/> with only a link.</summary>
	public const string Link = "link";

	/// <summary>
	/// The OSC 8 link's <c>id</c>, a <see cref="LinkGroupMarkup"/>, set and cleared with <see cref="Link"/>. A link with
	/// no id, and an MSLP link, has an <see cref="ElementMarkup"/> here instead, which keeps it apart from the next.
	/// </summary>
	public const string LinkGroup = "link-group";
}

/// <summary>SGR (<c>CSI … m</c>): colours and attributes, colon subparameters included.</summary>
public sealed class SgrPlugin : IDecoderPlugin
{
	/// <summary>The one instance.</summary>
	public static SgrPlugin Instance { get; } = new();

	private SgrPlugin()
	{
	}

	/// <inheritdoc/>
	public string Name => "sgr";

	/// <inheritdoc/>
	public void Register(DecoderBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.OnCsi('m', static (in CsiSequence sequence, DecodeContext context) =>
		{
			var state = context.State<VtState>();
			var before = state.Style;
			state.Style = Apply(state.Style, sequence);
			if (before.Underlined && !state.Style.Underlined && state.MslpUnderlined)
			{
				// The underline is an MSLP link's extent.
				context.SetPen(VtPens.Link, null);
				context.SetPen(VtPens.LinkGroup, null);
				state.MslpUnderlined = false;
			}
			else if (!before.Underlined && state.Style.Underlined && context.Pen(VtPens.Link) is AnsiMarkup { Style.LinkKind: LinkKind.Command })
			{
				state.MslpUnderlined = true;
			}

			context.SetPen(VtPens.Style, state.Style.IsNone ? null : new AnsiMarkup(state.Style));
			return true;
		});
	}

	/// <summary><paramref name="style"/> with the parameters of an SGR sequence applied.</summary>
	public static AnsiStyle Apply(AnsiStyle style, in CsiSequence sequence)
	{
		if (sequence.Count == 0) return AnsiStyle.None;

		for (var i = 0; i < sequence.Count; i++)
		{
			var code = sequence.Get(i);
			var colon = i + 1 < sequence.Count && sequence.IsSubparameter(i + 1);
			switch (code)
			{
				case 0: style = AnsiStyle.None; break;
				case 1: style = style with { Bold = true }; break;
				case 2: style = style with { Faint = true }; break;
				case 3: style = style with { Italic = true }; break;
				case 4:
					// 4:0 is no underline; 4:1 to 4:5 are kinds of underline, all drawn as one.
					style = style with { Underlined = !colon || sequence.Get(i + 1) != 0 };
					break;
				case 5 or 6: style = style with { Blink = true }; break;
				case 7: style = style with { Inverted = true }; break;
				case 8: style = style with { Concealed = true }; break;
				case 9: style = style with { StrikeThrough = true }; break;
				case 21: style = style with { Underlined = true }; break;
				case 22: style = style with { Bold = false, Faint = false }; break;
				case 23: style = style with { Italic = false }; break;
				case 24: style = style with { Underlined = false }; break;
				case 25: style = style with { Blink = false }; break;
				case 27: style = style with { Inverted = false }; break;
				case 28: style = style with { Concealed = false }; break;
				case 29: style = style with { StrikeThrough = false }; break;
				case >= 30 and <= 37: style = style with { Foreground = AnsiColor.StandardOf((byte)(code - 30), false) }; break;
				case 38: style = style with { Foreground = Extended(sequence, ref i) ?? style.Foreground }; break;
				case 39: style = style with { Foreground = null }; break;
				case >= 40 and <= 47: style = style with { Background = AnsiColor.StandardOf((byte)(code - 40), false) }; break;
				case 48: style = style with { Background = Extended(sequence, ref i) ?? style.Background }; break;
				case 49: style = style with { Background = null }; break;
				case 53: style = style with { Overlined = true }; break;
				case 55: style = style with { Overlined = false }; break;
				case 58: Extended(sequence, ref i); break;
				case >= 90 and <= 97: style = style with { Foreground = AnsiColor.StandardOf((byte)(code - 90), true) }; break;
				case >= 100 and <= 107: style = style with { Background = AnsiColor.StandardOf((byte)(code - 100), true) }; break;
			}

			// Subparameters belong to the code before them; whatever this code did not read is skipped.
			while (i + 1 < sequence.Count && sequence.IsSubparameter(i + 1)) i++;
		}

		return style;
	}

	/// <summary>Reads <c>38;5;n</c>, <c>38;2;r;g;b</c> or their colon forms, leaving <paramref name="i"/> on the last parameter read.</summary>
	private static AnsiColor? Extended(in CsiSequence sequence, ref int i)
	{
		if (i + 1 >= sequence.Count) return null;
		var colon = sequence.IsSubparameter(i + 1);
		var kind = sequence.Get(i + 1);
		if (kind == 5)
		{
			if (i + 2 >= sequence.Count) return null;
			var index = sequence.Get(i + 2);
			i += 2;
			return index is >= 0 and <= 255 ? AnsiColor.XtermOf((byte)index) : null;
		}

		if (kind != 2) return null;

		var first = i + 2;
		if (colon)
		{
			// 38:2:<colour space>:r:g:b, or 38:2:r:g:b from writers that leave the colour space out.
			var count = 0;
			while (first + count < sequence.Count && sequence.IsSubparameter(first + count)) count++;
			if (count >= 4) first++;
			else if (count < 3) return null;
		}
		else if (first + 2 >= sequence.Count)
		{
			// Too few components: they are still the colour's, not codes of their own (38;2;40 does not
			// set a black background).
			i = sequence.Count - 1;
			return null;
		}

		var r = sequence.Get(first);
		var g = sequence.Get(first + 1);
		var b = sequence.Get(first + 2);
		i = first + 2;
		return r is >= 0 and <= 255 && g is >= 0 and <= 255 && b is >= 0 and <= 255
			? new AnsiColor.Rgb((byte)r, (byte)g, (byte)b)
			: null;
	}
}

/// <summary>
/// The C0 controls and line editing: bell, carriage return, backspace, tab, form feed, vertical tab, <c>CSI K</c>,
/// <c>CSI J</c>, <c>CSI G</c>, <c>CSI C</c>, <c>CSI D</c>, and saving and restoring the cursor's column
/// (<c>ESC 7</c>, <c>ESC 8</c>). Cursor up (<c>CSI A</c>), index and reverse index (<c>ESC D</c>, <c>ESC M</c>)
/// are taken and ignored.
/// </summary>
public sealed class ControlsPlugin : IDecoderPlugin
{
	/// <summary>Columns between tab stops.</summary>
	public const int TabStop = 8;

	/// <summary>The one instance.</summary>
	public static ControlsPlugin Instance { get; } = new();

	private ControlsPlugin()
	{
	}

	/// <inheritdoc/>
	public string Name => "controls";

	/// <inheritdoc/>
	public void Register(DecoderBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder
			.OnControl('\a', static (_, context) =>
			{
				context.Point(BellMarkup.Instance);
				return true;
			})
			.OnControl('\r', static (_, context) =>
			{
				context.CarriageReturn();
				return true;
			})
			.OnControl('\b', static (_, context) =>
			{
				context.Backspace();
				return true;
			})
			.OnControl('\t', static (_, context) =>
			{
				context.MoveToColumn((context.Column / TabStop + 1) * TabStop);
				return true;
			})
			.OnControl('\f', static (_, context) =>
			{
				context.Point(ClearScreenMarkup.Instance);
				return true;
			})
			.OnControl('\v', static (_, context) =>
			{
				context.EndLine();
				return true;
			})
			.OnCsi('K', static (in CsiSequence sequence, DecodeContext context) =>
			{
				switch (sequence.Get(0))
				{
					case 0: context.EraseToEndOfLine(); break;
					case 1: context.EraseToStartOfLine(); break;
					case 2: context.EraseLine(); break;
					default: return false;
				}

				return true;
			})
			.OnCsi('J', static (in CsiSequence sequence, DecodeContext context) =>
			{
				// A scrolling pane has no screen below the cursor to erase; only a whole clear means anything.
				if (sequence.Get(0) is 2 or 3) context.Point(ClearScreenMarkup.Instance);
				return true;
			})
			.OnCsi('G', static (in CsiSequence sequence, DecodeContext context) =>
			{
				context.MoveToColumn(sequence.Get(0, 1) - 1);
				return true;
			})
			.OnCsi('`', static (in CsiSequence sequence, DecodeContext context) =>
			{
				context.MoveToColumn(sequence.Get(0, 1) - 1);
				return true;
			})
			.OnCsi('C', static (in CsiSequence sequence, DecodeContext context) =>
			{
				context.MoveToColumn(context.Column + Math.Max(1, sequence.Get(0, 1)));
				return true;
			})
			.OnCsi('D', static (in CsiSequence sequence, DecodeContext context) =>
			{
				context.Backspace(Math.Max(1, sequence.Get(0, 1)));
				return true;
			})
			// A scrolling pane cannot go back up a line, nor down one without ending it: cursor up, index and
			// reverse index are taken and do nothing. A picture drawn over the cells after them is placed by the
			// pictures plugin instead.
			.OnCsi('A', static (in CsiSequence _, DecodeContext _) => true)
			.OnEsc('D', static (in EscSequence _, DecodeContext _) => true)
			.OnEsc('M', static (in EscSequence _, DecodeContext _) => true)
			.OnEsc('7', static (in EscSequence _, DecodeContext context) =>
			{
				context.State<VtState>().SavedColumn = context.Column;
				return true;
			})
			.OnEsc('8', static (in EscSequence _, DecodeContext context) =>
			{
				var state = context.State<VtState>();
				if (state.SavedColumn is { } column) context.MoveToColumn(column);
				state.SavedColumn = null;
				return true;
			});
	}
}

/// <summary>The DEC special graphics set (<c>ESC ( 0</c>), and shifting between G0 and G1 with SO and SI.</summary>
public sealed class CharsetPlugin : IDecoderPlugin
{
	// 0x5F to 0x7E in the DEC special graphics set.
	private const string Graphics = " ◆▒␉␌␍␊°±␤␋┘┐┌└┼⎺⎻─⎼⎽├┤┴┬│≤≥π≠£·";

	/// <summary>The one instance.</summary>
	public static CharsetPlugin Instance { get; } = new();

	private CharsetPlugin()
	{
	}

	/// <inheritdoc/>
	public string Name => "charsets";

	/// <inheritdoc/>
	public void Register(DecoderBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder
			.OnEsc('0', static (in EscSequence sequence, DecodeContext context) => Designate(sequence, context, true), "(")
			.OnEsc('B', static (in EscSequence sequence, DecodeContext context) => Designate(sequence, context, false), "(")
			.OnEsc('0', static (in EscSequence sequence, DecodeContext context) => Designate(sequence, context, true), ")")
			.OnEsc('B', static (in EscSequence sequence, DecodeContext context) => Designate(sequence, context, false), ")")
			.OnControl('\u000e', static (_, context) =>
			{
				context.State<VtState>().ShiftedOut = true;
				return true;
			})
			.OnControl('\u000f', static (_, context) =>
			{
				context.State<VtState>().ShiftedOut = false;
				return true;
			})
			.OnPrint(static (text, context) =>
			{
				var state = context.State<VtState>();
				if (!(state.ShiftedOut ? state.G1Graphics : state.G0Graphics)) return false;

				Span<char> mapped = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
				for (var i = 0; i < text.Length; i++)
				{
					var c = text[i];
					mapped[i] = c is >= '_' and <= '~' ? Graphics[c - 0x5f] : c;
				}

				context.Write(mapped);
				return true;
			});
	}

	private static bool Designate(in EscSequence sequence, DecodeContext context, bool graphics)
	{
		var state = context.State<VtState>();
		if (sequence.Intermediates[0] == '(') state.G0Graphics = graphics;
		else state.G1Graphics = graphics;
		return true;
	}
}

/// <summary>
/// Operating system commands: OSC 8 links, MSLP command links (OSC 68), titles (0, 1, 2), palette changes (4, 10,
/// 11, 12, 104, 110, 111, 112) and the clipboard (52).
/// </summary>
public sealed class OscPlugin : IDecoderPlugin
{
	/// <summary>The longest OSC 8 address taken, as kitty and VTE cap it.</summary>
	public const int MaxUriLength = 2083;

	/// <summary>The <c>id</c> among OSC 8's params (<c>key=value</c> pairs split by <c>:</c>), or null.</summary>
	private static string? LinkId(ReadOnlySpan<char> parameters)
	{
		foreach (var range in parameters.Split(':'))
		{
			var pair = parameters[range];
			if (pair.StartsWith("id=", StringComparison.Ordinal) && pair.Length > 3 && pair.Length <= 256) return pair[3..].ToString();
		}

		return null;
	}

	/// <summary>The one instance.</summary>
	public static OscPlugin Instance { get; } = new();

	private OscPlugin()
	{
	}

	/// <inheritdoc/>
	public string Name => "osc";

	/// <inheritdoc/>
	public void Register(DecoderBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder
			.OnOsc(8, static (payload, context) =>
			{
				// 8 ; params ; uri — an empty uri ends the link.
				var separator = payload.IndexOf(';');
				if (separator < 0) return false;
				var uri = payload[(separator + 1)..];
				if (uri.IsEmpty || uri.Length > MaxUriLength)
				{
					context.SetPen(VtPens.Link, null);
					context.SetPen(VtPens.LinkGroup, null);
					return true;
				}

				var target = uri.ToString();
				var safe = context.Options.Links.IsAllowed(target);
				var id = safe ? LinkId(payload[..separator]) : null;

				// With no id, each link is its own (an ElementMarkup), so two to one place side by side stay two. The
				// same link opened again before it was ended, as a program does around a colour change, is still one.
				IMarkup? group = id is not null ? new LinkGroupMarkup(id)
					: !safe ? null
					: context.Pen(VtPens.Link) is AnsiMarkup { Style: { LinkKind: LinkKind.Url } open } && open.LinkUrl == target
						&& context.Pen(VtPens.LinkGroup) is ElementMarkup current ? current
					: context.NextElement();
				context.SetPen(VtPens.Link, safe ? new AnsiMarkup(new AnsiStyle { LinkUrl = target, LinkKind = LinkKind.Url }) : null);
				context.SetPen(VtPens.LinkGroup, group);
				return true;
			})
			.OnOsc(68, static (payload, context) =>
			{
				// 68 ; 1 ; SEND ; command, before the underlined text that is the link.
				var parts = payload.ToString().Split(';', 3);
				if (parts is not ["1", var type, var command] || command.Length == 0) return false;
				if (!type.Equals("SEND", StringComparison.OrdinalIgnoreCase)) return false;
				context.SetPen(VtPens.Link, new AnsiMarkup(new AnsiStyle { LinkUrl = command, LinkKind = LinkKind.Command }));
				context.SetPen(VtPens.LinkGroup, context.NextElement());
				context.State<VtState>().MslpUnderlined = false;
				return true;
			})
			.OnLineEnd(static context =>
			{
				// An MSLP link is on one line; an OSC 8 link lasts until it is ended.
				if (context.Pen(VtPens.Link) is AnsiMarkup { Style.LinkKind: LinkKind.Command })
				{
					context.SetPen(VtPens.Link, null);
					context.SetPen(VtPens.LinkGroup, null);
					context.State<VtState>().MslpUnderlined = false;
				}
			})
			.OnOsc(0, static (payload, context) => Title(payload, context, icon: true))
			.OnOsc(1, static (payload, context) => Title(payload, context, icon: true))
			.OnOsc(2, static (payload, context) => Title(payload, context, icon: false))
			.OnOsc(4, static (payload, context) =>
			{
				var parts = payload.ToString().Split(';');
				for (var i = 0; i + 1 < parts.Length; i += 2)
				{
					if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index > 255) continue;
					if (ParseColor(parts[i + 1]) is { } color) context.Raise(new PaletteEvent(PaletteSlot.Index, index, color));
				}

				return true;
			})
			.OnOsc(104, static (payload, context) =>
			{
				if (payload.IsEmpty)
				{
					context.Raise(new PaletteEvent(PaletteSlot.Index, -1, null));
					return true;
				}

				foreach (var part in payload.ToString().Split(';'))
				{
					if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index <= 255)
					{
						context.Raise(new PaletteEvent(PaletteSlot.Index, index, null));
					}
				}

				return true;
			})
			.OnOsc(10, static (payload, context) => Dynamic(payload, context, 10))
			.OnOsc(11, static (payload, context) => Dynamic(payload, context, 11))
			.OnOsc(12, static (payload, context) => Dynamic(payload, context, 12))
			.OnOsc(110, static (_, context) => Reset(context, PaletteSlot.Foreground))
			.OnOsc(111, static (_, context) => Reset(context, PaletteSlot.Background))
			.OnOsc(112, static (_, context) => Reset(context, PaletteSlot.Cursor))
			.OnOsc(52, static (payload, context) =>
			{
				var separator = payload.IndexOf(';');
				if (separator < 0) return false;
				var selection = payload[..separator].ToString();
				var data = payload[(separator + 1)..];
				if (data is "?")
				{
					context.Raise(new ClipboardEvent(selection, null));
					return true;
				}

				var bytes = new byte[data.Length / 4 * 3 + 3];
				if (!Convert.TryFromBase64Chars(data, bytes, out var written)) return true;
				context.Raise(new ClipboardEvent(selection, Encoding.UTF8.GetString(bytes, 0, written)));
				return true;
			});
	}

	/// <summary>
	/// An X11 colour specification as OSC 4, 10 and 11 take it: <c>rgb:r/g/b</c> with one to four hex digits a
	/// channel, or <c>#rgb</c>, <c>#rrggbb</c>, <c>#rrrgggbbb</c>, <c>#rrrrggggbbbb</c>. Null when it is none of those.
	/// </summary>
	public static RgbColor? ParseColor(ReadOnlySpan<char> spec)
	{
		if (spec.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase))
		{
			var rest = spec[4..];
			Span<Range> channels = stackalloc Range[4];
			if (rest.Split(channels, '/') != 3) return null;
			var r = Channel(rest[channels[0]]);
			var g = Channel(rest[channels[1]]);
			var b = Channel(rest[channels[2]]);
			return r is { } rr && g is { } gg && b is { } bb ? new RgbColor(rr, gg, bb) : null;
		}

		if (spec.Length > 1 && spec[0] == '#' && (spec.Length - 1) % 3 == 0 && spec.Length <= 13)
		{
			var size = (spec.Length - 1) / 3;
			var r = Channel(spec.Slice(1, size));
			var g = Channel(spec.Slice(1 + size, size));
			var b = Channel(spec.Slice(1 + size * 2, size));
			return r is { } rr && g is { } gg && b is { } bb ? new RgbColor(rr, gg, bb) : null;
		}

		return null;
	}

	/// <summary><paramref name="color"/> as an X11 <c>rgb:rrrr/gggg/bbbb</c> specification, as xterm answers.</summary>
	public static string FormatColor(RgbColor color) =>
		string.Create(CultureInfo.InvariantCulture, $"rgb:{color.R:x2}{color.R:x2}/{color.G:x2}{color.G:x2}/{color.B:x2}{color.B:x2}");

	private static byte? Channel(ReadOnlySpan<char> hex)
	{
		if (hex.Length is < 1 or > 4) return null;
		if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return null;
		var max = (1 << (hex.Length * 4)) - 1;
		return (byte)Math.Round(value * 255.0 / max);
	}

	private static bool Title(ReadOnlySpan<char> payload, DecodeContext context, bool icon)
	{
		var title = new StringBuilder(payload.Length);
		foreach (var c in payload)
		{
			if (!char.IsControl(c)) title.Append(c);
		}

		context.Raise(new TitleEvent(title.ToString(), icon));
		return true;
	}

	private static bool Dynamic(ReadOnlySpan<char> payload, DecodeContext context, int command)
	{
		// OSC 10 ; a ; b sets 10 to a and 11 to b, and so on.
		var parts = payload.ToString().Split(';');
		for (var i = 0; i < parts.Length; i++)
		{
			var slot = (command + i) switch
			{
				10 => PaletteSlot.Foreground,
				11 => PaletteSlot.Background,
				12 => PaletteSlot.Cursor,
				_ => (PaletteSlot?)null,
			};
			if (slot is not { } which) break;

			if (parts[i] == "?")
			{
				AnswersPlugin.AnswerColor(context, which);
			}
			else if (ParseColor(parts[i]) is { } color)
			{
				context.Raise(new PaletteEvent(which, 0, color));
			}
		}

		return true;
	}

	private static bool Reset(DecodeContext context, PaletteSlot slot)
	{
		context.Raise(new PaletteEvent(slot, 0, null));
		return true;
	}
}

/// <summary>
/// Answers what a server may ask a terminal, from the decoder's <see cref="ClientIdentity"/>: device attributes,
/// XTVERSION, the pane's size, the cursor's position and the default colours. Each is answered only when
/// <see cref="ClientIdentity.Answers"/> allows it.
/// </summary>
public sealed class AnswersPlugin : IDecoderPlugin
{
	/// <summary>The one instance.</summary>
	public static AnswersPlugin Instance { get; } = new();

	private AnswersPlugin()
	{
	}

	/// <inheritdoc/>
	public string Name => "answers";

	/// <inheritdoc/>
	public void Register(DecoderBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder
			.OnCsi('c', static (in CsiSequence sequence, DecodeContext context) =>
			{
				if (sequence.Get(0) != 0) return false;
				if (!Allowed(context, ClientAnswers.Identity)) return true;
				// A VT220 with colour, and sixel when the client draws it.
				var sixel = (context.Identity.Output.Pictures & PictureMethods.Sixel) != 0;
				context.Reply(sixel ? "\e[?62;4;22c" : "\e[?62;22c");
				return true;
			})
			.OnCsi('q', static (in CsiSequence sequence, DecodeContext context) =>
			{
				if (sequence.Get(0) != 0) return false;
				if (Allowed(context, ClientAnswers.Identity)) context.Reply($"\eP>|{context.Identity.Name} {context.Identity.Version}\e\\");
				return true;
			}, prefix: '>')
			.OnCsi('t', static (in CsiSequence sequence, DecodeContext context) =>
			{
				var identity = context.Identity;
				var reply = sequence.Get(0) switch
				{
					14 => Format($"\e[4;{identity.Rows * identity.CellHeight};{identity.Columns * identity.CellWidth}t"),
					16 => Format($"\e[6;{identity.CellHeight};{identity.CellWidth}t"),
					18 => Format($"\e[8;{identity.Rows};{identity.Columns}t"),
					_ => null,
				};
				if (reply is null) return false;
				if (Allowed(context, ClientAnswers.Size)) context.Reply(reply);
				return true;
			})
			.OnCsi('n', static (in CsiSequence sequence, DecodeContext context) =>
			{
				switch (sequence.Get(0))
				{
					case 5:
						if (Allowed(context, ClientAnswers.Identity)) context.Reply("\e[0n");
						return true;
					case 6:
						// The row is always the last: a scrolling pane writes at its bottom.
						if (Allowed(context, ClientAnswers.CursorPosition)) context.Reply(Format($"\e[{context.Identity.Rows};{context.Column + 1}R"));
						return true;
					default:
						return false;
				}
			});
	}

	internal static void AnswerColor(DecodeContext context, PaletteSlot slot)
	{
		if (!Allowed(context, ClientAnswers.Colors)) return;
		var color = slot switch
		{
			PaletteSlot.Foreground => context.Identity.Foreground,
			PaletteSlot.Background => context.Identity.Background,
			_ => null,
		};
		if (color is not { } known) return;
		var number = slot == PaletteSlot.Foreground ? 10 : 11;
		context.Reply(Format($"\e]{number};{OscPlugin.FormatColor(known)}\e\\"));
	}

	private static bool Allowed(DecodeContext context, ClientAnswers answer) => (context.Identity.Answers & answer) != 0;

	private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The terminal plugins' state for one decoder.</summary>
internal sealed class VtState
{
	public AnsiStyle Style { get; set; }

	public bool MslpUnderlined { get; set; }

	public bool G0Graphics { get; set; }

	public bool G1Graphics { get; set; }

	public bool ShiftedOut { get; set; }

	public int? SavedColumn { get; set; }
}
