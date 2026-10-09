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
	/// <remarks>
	/// <c>ansi()</c> is among the most called functions in a game, so the codes are read in place: the only
	/// allocations are the markup returned and, for a named colour, the name handed to
	/// <paramref name="namedColor"/>.
	/// </remarks>
	public static AnsiMarkup Parse(string codes, Func<string, AnsiColor?> namedColor)
	{
		ArgumentNullException.ThrowIfNull(namedColor);
		ArgumentNullException.ThrowIfNull(codes);

		var reader = new Reader(namedColor);
		var input = codes.AsSpan();

		// Splits the input into codes: a run of letters, or one colour (#…, +…, an xterm number, or a <…>
		// group, spaces and all). A colour after / or ! is the background.
		var lettersStart = -1;
		var background = false;
		var i = 0;
		while (i < input.Length)
		{
			var c = input[i];
			if (c == ' ')
			{
				// A space ends a run of letters, and a background marker with nothing after it.
				if (lettersStart >= 0) reader.Letters(input[lettersStart..i]);
				lettersStart = -1;
				background = false;
				i++;
				continue;
			}

			if (c is '/' or '!')
			{
				if (lettersStart >= 0) reader.Letters(input[lettersStart..i]);
				lettersStart = -1;
				background = true;
				i++;
				continue;
			}

			var number = char.IsAsciiDigit(c) || (c == '-' && i + 1 < input.Length && char.IsAsciiDigit(input[i + 1]));
			if (c is '#' or '+' or '<' || number)
			{
				if (lettersStart >= 0)
				{
					reader.Letters(input[lettersStart..i]);
					lettersStart = -1;
					background = false;
				}

				var end = i + 1;
				if (c == '<')
				{
					// No closing '>': only the word it starts is the (malformed, ignored) triplet, and the
					// codes after the next space are read as usual.
					end = input[i..].IndexOf('>') is var close and >= 0
						? i + close + 1
						: input[i..].IndexOf(' ') is var space and >= 0 ? i + space : input.Length;
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

				reader.Colour(input[i..end], background);
				background = false;
				i = end;
				continue;
			}

			if (lettersStart < 0) lettersStart = i;
			i++;
		}

		if (lettersStart >= 0) reader.Letters(input[lettersStart..]);

		return reader.ToMarkup();
	}

	/// <summary>The attributes read so far, one code at a time.</summary>
	private ref struct Reader(Func<string, AnsiColor?> namedColor)
	{
		private AnsiColor? _foreground;
		private AnsiColor? _background;
		private bool _blink;
		private bool _bold;
		private bool _clear;
		private bool _inverted;
		private bool _underlined;
		// PennMUSH's offbits: F, H, I and U turn the attribute off here and in what this span encloses.
		private bool _blinkOff;
		private bool _boldOff;
		private bool _invertedOff;
		private bool _underlinedOff;

		/// <summary>One colour: <c>#…</c>, <c>&lt;…&gt;</c>, <c>+name</c>, <c>+xtermN</c> or a bare number.</summary>
		public void Colour(ReadOnlySpan<char> code, bool isBackground)
		{
			if (code.StartsWith('#'))
			{
				if (AnsiColor.TryParseHex(code, out var hex)) Set(hex, isBackground);
				return;
			}

			if (code.StartsWith('<') && code.EndsWith('>'))
			{
				if (TryParseTriplet(code[1..^1], out var triplet)) Set(triplet, isBackground);
				return;
			}

			var isXtermPrefixed = code.StartsWith(XtermPrefix);
			if (code.StartsWith('+') && !isXtermPrefixed)
			{
				// Always consumed here: falling through would read the name's letters as colour codes.
				if (namedColor(code[1..].ToString()) is { } named) Set(named, isBackground);
				return;
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
					Set(AnsiColor.XtermOf((byte)index), isBackground);
				}

				return;
			}

			// An unterminated <… group: its letters are read as letters.
			Letters(code);
		}

		private void Set(AnsiColor colour, bool isBackground)
		{
			if (isBackground) _background = colour;
			else _foreground = colour;
		}

		/// <summary>A run of single-letter codes. 'h' is a per-run modifier, so it resets on every run.</summary>
		public void Letters(ReadOnlySpan<char> code)
		{
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
					case 'h': highlight = true; _boldOff = false; break;
					case 'H':
						highlight = false;
						_bold = false;
						_boldOff = true;
						// The hilite already given to a palette letter goes too: hrH is plain red.
						if (_foreground is AnsiColor.Standard { Bright: true } bright)
						{
							_foreground = AnsiColor.StandardOf(bright.Index, false);
						}
						break;
					case 'i': _inverted = true; _invertedOff = false; break;
					case 'I': _inverted = false; _invertedOff = true; break;
					case 'f': _blink = true; _blinkOff = false; break;
					case 'F': _blink = false; _blinkOff = true; break;
					case 'u': _underlined = true; _underlinedOff = false; break;
					case 'U': _underlined = false; _underlinedOff = true; break;
					case 'n':
						_clear = true;
						_foreground = null;
						_background = null;
						_blink = false;
						_bold = false;
						_inverted = false;
						_underlined = false;
						highlight = false;
						// Clear already discards everything around this span; nothing is left to turn off.
						_blinkOff = false;
						_boldOff = false;
						_invertedOff = false;
						_underlinedOff = false;
						break;
					case 'd': _foreground = AnsiColor.Default.Instance; break;
					case 'D': _background = AnsiColor.Default.Instance; break;
					case 'x': _foreground = AnsiColor.StandardOf(0, highlight); break;
					case 'r': _foreground = AnsiColor.StandardOf(1, highlight); break;
					case 'g': _foreground = AnsiColor.StandardOf(2, highlight); break;
					case 'y': _foreground = AnsiColor.StandardOf(3, highlight); break;
					case 'b': _foreground = AnsiColor.StandardOf(4, highlight); break;
					case 'm': _foreground = AnsiColor.StandardOf(5, highlight); break;
					case 'c': _foreground = AnsiColor.StandardOf(6, highlight); break;
					case 'w': _foreground = AnsiColor.StandardOf(7, highlight); break;
					// 'h' before a background letter is SGR 1 (bold), not a bright background —
					// terminals have no "bright background" attribute distinct from bold text.
					case 'X': _background = AnsiColor.StandardOf(0, false); _bold |= highlight; break;
					case 'R': _background = AnsiColor.StandardOf(1, false); _bold |= highlight; break;
					case 'G': _background = AnsiColor.StandardOf(2, false); _bold |= highlight; break;
					case 'Y': _background = AnsiColor.StandardOf(3, false); _bold |= highlight; break;
					case 'B': _background = AnsiColor.StandardOf(4, false); _bold |= highlight; break;
					case 'M': _background = AnsiColor.StandardOf(5, false); _bold |= highlight; break;
					case 'C': _background = AnsiColor.StandardOf(6, false); _bold |= highlight; break;
					case 'W': _background = AnsiColor.StandardOf(7, false); _bold |= highlight; break;
				}
			}

			// PennMUSH's hilite with no palette letter to brighten: SGR 1 on whatever colour the text has.
			_bold |= highlight && !highlightUsed;
		}

		public readonly AnsiMarkup ToMarkup() => new(new AnsiStyle
		{
			Foreground = _foreground,
			Background = _background,
			Blink = _blink,
			Bold = _bold,
			Clear = _clear,
			Inverted = _inverted,
			Underlined = _underlined,
			BlinkOff = _blinkOff,
			BoldOff = _boldOff,
			InvertedOff = _invertedOff,
			UnderlinedOff = _underlinedOff,
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
