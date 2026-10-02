using System.Globalization;

namespace MarkupString.Ansi;

/// <summary>
/// Parses ANSI colour/attribute code strings into <see cref="AnsiMarkup"/>.
///
/// <para>
/// Supports the code syntax of the MUSHCode <c>ansi()</c> function: single-letter codes, hex RGB
/// (<c>#rgb</c> / <c>#rrggbb</c>), xterm palette entries (a bare integer 0–255 or the <c>+xtermN</c>
/// form), RGB triplets (<c>&lt;r g b&gt;</c>), and named colours (<c>+name</c>), which need a resolver
/// from the caller: the colour names are the game's configuration, not the markup layer's.
/// </para>
///
/// <para>
/// The string is read as PennMUSH's <c>define_ansi_data</c> reads it. Spaces separate codes, but a colour
/// may also follow letters directly (<c>hBr</c>, <c>u#ff0000</c>), and <c>/</c> or <c>!</c> turns the
/// colour after it into the background (<c>#ff0000!#0000ff</c>, <c>/&lt;0 0 255&gt;</c>). Within a run of
/// letters the <c>h</c> (highlight) modifier raises a following foreground letter to its bright variant,
/// for the rest of that run only. A following background letter has no bright variant — terminals have no
/// "bright background" SGR distinct from bold text — so <c>h</c> there sets <see cref="AnsiStyle.Bold"/>
/// instead and leaves the background colour at its normal intensity. An <c>h</c> that no palette letter in
/// its run takes up is PennMUSH's hilite on whatever colour the text has, which is SGR 1: <c>h</c>,
/// <c>hu</c> and <c>hd</c> set <see cref="AnsiStyle.Bold"/>. Unrecognised codes and malformed colours are
/// ignored rather than throwing.
/// </para>
/// </summary>
public static class AnsiCodeParser
{
	private const string XtermPrefix = "+xterm";

	/// <summary>
	/// Splits <paramref name="input"/> into codes: a run of letters, or one colour (<c>#…</c>, <c>+…</c>, an
	/// xterm number, or a <c>&lt;…&gt;</c> group, spaces and all). A colour after <c>/</c> or <c>!</c> comes back
	/// with a leading <c>/</c>, the background marker the parser reads.
	/// </summary>
	private static IEnumerable<string> Tokenize(string input)
	{
		var letters = new System.Text.StringBuilder();
		var background = false;
		var i = 0;
		while (i < input.Length)
		{
			var c = input[i];
			if (c == ' ')
			{
				// A space ends a run of letters, and a background marker with nothing after it.
				if (letters.Length > 0) yield return Take(letters, background);
				background = false;
				i++;
				continue;
			}

			if (c is '/' or '!')
			{
				if (letters.Length > 0) yield return Take(letters, background);
				background = true;
				i++;
				continue;
			}

			var number = char.IsAsciiDigit(c) || (c == '-' && i + 1 < input.Length && char.IsAsciiDigit(input[i + 1]));
			if (c is '#' or '+' or '<' || number)
			{
				if (letters.Length > 0)
				{
					yield return Take(letters, background);
					background = false;
				}

				var end = i + 1;
				if (c == '<')
				{
					// No closing '>': only the word it starts is the (malformed, ignored) triplet, and the
					// codes after the next space are read as usual.
					end = input.IndexOf('>', i) is var close and >= 0
						? close + 1
						: input.IndexOf(' ', i) is var space and >= 0 ? space : input.Length;
				}
				else if (number)
				{
					// An xterm palette index: digits only, so u200 is underline and xterm 200. A sign is kept
					// with them, so -1 is the out-of-range number it reads as, not a 1.
					while (end < input.Length && char.IsAsciiDigit(input[end])) end++;
				}
				else
				{
					// A colour name may carry _ or - (a game's own names); hex digits never do.
					while (end < input.Length
						&& (char.IsAsciiLetterOrDigit(input[end]) || (c == '+' && input[end] is '_' or '-')))
					{
						end++;
					}
				}

				yield return (background ? "/" : "") + input[i..end];
				background = false;
				i = end;
				continue;
			}

			letters.Append(c);
			i++;
		}

		if (letters.Length > 0) yield return Take(letters, background);
	}

	/// <summary>The letters gathered so far, with the background marker if one came before them.</summary>
	private static string Take(System.Text.StringBuilder letters, bool background)
	{
		var token = (background ? "/" : "") + letters;
		letters.Clear();
		return token;
	}

	/// <summary>
	/// Parses a string of ANSI codes into the markup they describe. Named colours (<c>+name</c>) are ignored;
	/// <see cref="Parse(string, Func{string, AnsiColor?})"/> resolves them.
	/// </summary>
	/// <param name="codes">
	/// Codes in <c>ansi()</c> syntax: single-letter codes (<c>r</c>, <c>hg</c>, <c>/R</c>, …), hex RGB,
	/// xterm integers or RGB triplets.
	/// </param>
	public static AnsiMarkup Parse(string codes) => Parse(codes, static _ => null);

	/// <summary>
	/// Parses a string of ANSI codes into the markup they describe, resolving named colours with
	/// <paramref name="namedColor"/>.
	/// </summary>
	/// <param name="codes">
	/// Codes in <c>ansi()</c> syntax: single-letter codes (<c>r</c>, <c>hg</c>, <c>/R</c>, …), hex RGB,
	/// xterm integers, RGB triplets or named colours.
	/// </param>
	/// <param name="namedColor">
	/// Resolves a <c>+name</c> colour (the name without its <c>+</c>) to the colour it stands for, or
	/// <see langword="null"/> when there is no such colour, which is then ignored.
	/// </param>
	public static AnsiMarkup Parse(string codes, Func<string, AnsiColor?> namedColor)
	{
		ArgumentNullException.ThrowIfNull(namedColor);
		ArgumentNullException.ThrowIfNull(codes);

		AnsiColor? foreground = null;
		AnsiColor? background = null;
		var blink = false;
		var bold = false;
		var clear = false;
		var inverted = false;
		var underlined = false;
		// PennMUSH's offbits: F, H, I and U turn the attribute off here and in what this span encloses.
		var blinkOff = false;
		var boldOff = false;
		var invertedOff = false;
		var underlinedOff = false;

		foreach (var token in Tokenize(codes))
		{
			var code = token.AsSpan();
			var isBackground = false;

			if (code.StartsWith("/"))
			{
				isBackground = true;
				code = code[1..];
			}

			if (code.StartsWith("#"))
			{
				if (AnsiColor.TryParseHex(code, out var hex))
				{
					if (isBackground) background = hex;
					else foreground = hex;
				}

				continue;
			}

			if (code.StartsWith("<") && code.EndsWith(">"))
			{
				if (TryParseTriplet(code[1..^1], out var triplet))
				{
					if (isBackground) background = triplet;
					else foreground = triplet;
				}

				continue;
			}

			var isXtermPrefixed = code.StartsWith(XtermPrefix);
			if (code.StartsWith("+") && !isXtermPrefixed)
			{
				// Always consumed here: falling through would read the name's letters as colour codes.
				if (namedColor(code[1..].ToString()) is { } named)
				{
					if (isBackground) background = named;
					else foreground = named;
				}

				continue;
			}

			if (isXtermPrefixed || int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
			{
				// A "+xterm…" token is always consumed here, even when its index is malformed or out
				// of range. Falling through to the letter branch would read the x/r/m of the literal
				// prefix as colour codes and paint "+xterm256" magenta.
				var digits = isXtermPrefixed ? code[XtermPrefix.Length..] : code;
				if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
					&& index is >= 0 and < 256)
				{
					if (isBackground) background = new AnsiColor.Xterm((byte)index);
					else foreground = new AnsiColor.Xterm((byte)index);
				}

				continue;
			}

			// Single-letter codes. 'h' is a per-token modifier, so it resets on every token.
			var highlight = false;
			// Whether a palette letter has taken the highlight up; one that none has is bold.
			var highlightUsed = false;
			foreach (var chr in code)
			{
				if (highlight && chr is 'x' or 'r' or 'g' or 'y' or 'b' or 'm' or 'c' or 'w'
					or 'X' or 'R' or 'G' or 'Y' or 'B' or 'M' or 'C' or 'W')
				{
					highlightUsed = true;
				}

				switch (chr)
				{
					// Each attribute and its capital are one bit: the later of the two wins.
					case 'h': highlight = true; boldOff = false; break;
					case 'H':
						highlight = false;
						bold = false;
						boldOff = true;
						// The hilite already given to a palette letter goes too: hrH is plain red.
						if (foreground is AnsiColor.Standard { Bright: true } bright)
						{
							foreground = new AnsiColor.Standard(bright.Index, false);
						}
						break;
					case 'i': inverted = true; invertedOff = false; break;
					case 'I': inverted = false; invertedOff = true; break;
					case 'f': blink = true; blinkOff = false; break;
					case 'F': blink = false; blinkOff = true; break;
					case 'u': underlined = true; underlinedOff = false; break;
					case 'U': underlined = false; underlinedOff = true; break;
					case 'n':
						clear = true;
						foreground = null;
						background = null;
						blink = false;
						bold = false;
						inverted = false;
						underlined = false;
						highlight = false;
						// Clear already discards everything around this span; nothing is left to turn off.
						blinkOff = false;
						boldOff = false;
						invertedOff = false;
						underlinedOff = false;
						break;
					case 'd': foreground = AnsiColor.Default.Instance; break;
					case 'D': background = AnsiColor.Default.Instance; break;
					case 'x': foreground = new AnsiColor.Standard(0, highlight); break;
					case 'r': foreground = new AnsiColor.Standard(1, highlight); break;
					case 'g': foreground = new AnsiColor.Standard(2, highlight); break;
					case 'y': foreground = new AnsiColor.Standard(3, highlight); break;
					case 'b': foreground = new AnsiColor.Standard(4, highlight); break;
					case 'm': foreground = new AnsiColor.Standard(5, highlight); break;
					case 'c': foreground = new AnsiColor.Standard(6, highlight); break;
					case 'w': foreground = new AnsiColor.Standard(7, highlight); break;
					// 'h' before a background letter is SGR 1 (bold), not a bright background —
					// terminals have no "bright background" attribute distinct from bold text.
					case 'X': background = new AnsiColor.Standard(0, false); bold |= highlight; break;
					case 'R': background = new AnsiColor.Standard(1, false); bold |= highlight; break;
					case 'G': background = new AnsiColor.Standard(2, false); bold |= highlight; break;
					case 'Y': background = new AnsiColor.Standard(3, false); bold |= highlight; break;
					case 'B': background = new AnsiColor.Standard(4, false); bold |= highlight; break;
					case 'M': background = new AnsiColor.Standard(5, false); bold |= highlight; break;
					case 'C': background = new AnsiColor.Standard(6, false); bold |= highlight; break;
					case 'W': background = new AnsiColor.Standard(7, false); bold |= highlight; break;
				}
			}

			// PennMUSH's hilite with no palette letter to brighten: SGR 1 on whatever colour the text has.
			bold |= highlight && !highlightUsed;
		}

		return new AnsiMarkup(new AnsiStyle
		{
			Foreground = foreground,
			Background = background,
			Blink = blink,
			Bold = bold,
			Clear = clear,
			Inverted = inverted,
			Underlined = underlined,
			BlinkOff = blinkOff,
			BoldOff = boldOff,
			InvertedOff = invertedOff,
			UnderlinedOff = underlinedOff,
		});
	}

	private static bool TryParseTriplet(ReadOnlySpan<char> inner, out AnsiColor.Rgb rgb)
	{
		rgb = new AnsiColor.Rgb(0, 0, 0);

		Span<byte> channels = stackalloc byte[3];
		var count = 0;

		foreach (var range in inner.Split(' '))
		{
			var part = inner[range];
			if (part.IsEmpty)
			{
				continue;
			}

			if (count == 3 || !byte.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
			{
				return false;
			}

			channels[count++] = value;
		}

		if (count != 3)
		{
			return false;
		}

		rgb = new AnsiColor.Rgb(channels[0], channels[1], channels[2]);
		return true;
	}
}
