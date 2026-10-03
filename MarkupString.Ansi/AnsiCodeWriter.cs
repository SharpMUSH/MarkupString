using System.Globalization;
using System.Text;

namespace MarkupString.Ansi;

/// <summary>
/// Writes an <see cref="AnsiStyle"/> as the MUSHCode <c>ansi()</c> codes that produce it, the inverse of
/// <see cref="AnsiCodeParser"/>: <c>AnsiCodeParser.Parse(AnsiCodeWriter.Write(style))</c> renders as
/// <c>style</c> does.
/// </summary>
/// <remarks>
/// The codes come in the order PennMUSH's <c>write_ansi_letters</c> writes them: the attribute letters
/// <c>f h i u</c>, the ones turning an attribute off <c>F H I U</c>, a palette background letter, the
/// foreground (a letter, <c>#rrggbb</c> or <c>+xtermN</c>), and a background that is not a letter after
/// <c>!</c>. So <c>hBr</c> and <c>#ff0000!#0000ff</c>. A bright palette foreground is its letter after
/// <c>h</c>; bold on a normal palette foreground is <c>h</c> after its letter, <c>Brh</c>, since an <c>h</c> before
/// it would brighten it; a bright palette background has no letter and is written as its xterm entry,
/// <c>!+xterm8</c> to <c>!+xterm15</c>. Attributes <c>ansi()</c> has no code for (faint, italic,
/// overline, strike-through) and links are not written.
/// </remarks>
public static class AnsiCodeWriter
{
	private const string ForegroundLetters = "xrgybmcw";
	private const string BackgroundLetters = "XRGYBMCW";

	/// <summary>The codes for <paramref name="style"/>; empty when it sets nothing <c>ansi()</c> can express.</summary>
	public static string Write(in AnsiStyle style)
	{
		var codes = new StringBuilder();

		// n first: it discards what surrounds the span, and the codes after it are what the span sets.
		if (style.Clear) codes.Append('n');
		// h brightens a palette letter after it in the same run, so bold on a normal palette foreground is written
		// after the letter (rh), where it brightens nothing and is read as bold.
		var boldAfterLetter = style.Bold && style.Foreground is AnsiColor.Standard { Bright: false };
		if (style.Blink) codes.Append('f');
		if ((style.Bold && !boldAfterLetter) || style.Foreground is AnsiColor.Standard { Bright: true }) codes.Append('h');
		if (style.Inverted) codes.Append('i');
		if (style.Underlined) codes.Append('u');
		if (style.BlinkOff) codes.Append('F');
		if (style.BoldOff) codes.Append('H');
		if (style.InvertedOff) codes.Append('I');
		if (style.UnderlinedOff) codes.Append('U');

		var background = WriteColor(style.Background, background: true);
		if (background.Length == 1) codes.Append(background);
		codes.Append(WriteColor(style.Foreground, background: false));
		if (boldAfterLetter) codes.Append('h');
		if (background.Length > 1) codes.Append('!').Append(background);

		return codes.ToString();
	}

	/// <summary>
	/// One colour as <c>ansi()</c> reads it: a palette letter (lower case for a foreground, upper case for a
	/// background), <c>d</c>/<c>D</c> for the terminal default, <c>#rrggbb</c>, or <c>+xtermN</c>. A bright
	/// palette foreground is its letter, the brightness being the <c>h</c> <see cref="Write"/> puts before
	/// it; a bright palette background is its xterm entry. Empty for <see langword="null"/>.
	/// </summary>
	public static string WriteColor(AnsiColor? color, bool background) => color switch
	{
		null => string.Empty,
		AnsiColor.Default => background ? "D" : "d",
		AnsiColor.Standard { Bright: true } bright when background
			=> string.Create(CultureInfo.InvariantCulture, $"+xterm{bright.Index + 8}"),
		AnsiColor.Standard standard => (background ? BackgroundLetters : ForegroundLetters)[standard.Index].ToString(),
		AnsiColor.Xterm xterm => string.Create(CultureInfo.InvariantCulture, $"+xterm{xterm.Index}"),
		AnsiColor.Rgb rgb => rgb.ToHex(),
		_ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
	};
}
