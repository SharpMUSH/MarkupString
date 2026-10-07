using MarkupString.Layout;

namespace MarkupString.Ansi;

/// <summary>Theme colours as terminal colour.</summary>
public static class AnsiTheme
{
	/// <summary>
	/// <paramref name="color"/> as an ANSI layer: its exact colour, with its standard colour kept for a
	/// client that has only the sixteen (<see cref="AnsiStyle.StandardForeground"/>); or the standard colour
	/// alone, which each client shows in its own palette. As a background, the exact colour alone, which a
	/// sixteen-colour client is sent the nearest of.
	/// </summary>
	/// <param name="color">The colour.</param>
	/// <param name="paint">As text, bold text or a background.</param>
	public static AnsiMarkup Paint(ThemeColor color, ThemePaint paint = ThemePaint.Text)
	{
		AnsiColor? standard = color.Slot is { } slot ? new AnsiColor.Standard((byte)(slot % 8), slot >= 8) : null;
		AnsiColor? exact = color.Rgb is { } rgb ? new AnsiColor.Rgb(rgb.R, rgb.G, rgb.B) : null;
		if (paint == ThemePaint.Background) return new AnsiMarkup(new AnsiStyle { Background = exact ?? standard });
		return new AnsiMarkup(new AnsiStyle
		{
			Foreground = exact ?? standard,
			StandardForeground = exact is not null ? standard : null,
			Bold = paint == ThemePaint.Bold,
		});
	}

	/// <summary><paramref name="palette"/> as a layout's part colours, in ANSI (<see cref="ThemePalette.ToTheme"/>).</summary>
	public static LayoutTheme ToLayoutTheme(this ThemePalette palette)
	{
		ArgumentNullException.ThrowIfNull(palette);
		return palette.ToTheme(Paint);
	}
}
