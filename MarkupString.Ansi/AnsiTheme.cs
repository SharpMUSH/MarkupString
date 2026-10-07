using MarkupString.Layout;

namespace MarkupString.Ansi;

/// <summary>Theme colours as terminal colour.</summary>
public static class AnsiTheme
{
	/// <summary>
	/// <paramref name="color"/> as an ANSI layer: its exact colour, with its standard colour kept for a
	/// client that has only the sixteen (<see cref="AnsiStyle.StandardForeground"/>); or the standard colour
	/// alone, which each client shows in its own palette.
	/// </summary>
	/// <param name="color">The colour.</param>
	/// <param name="bold">Whether the text is bold as well.</param>
	public static AnsiMarkup Paint(ThemeColor color, bool bold = false)
	{
		AnsiColor? standard = color.Slot is { } slot ? new AnsiColor.Standard((byte)(slot % 8), slot >= 8) : null;
		return new AnsiMarkup(new AnsiStyle
		{
			Foreground = color.Rgb is { } rgb ? new AnsiColor.Rgb(rgb.R, rgb.G, rgb.B) : standard,
			StandardForeground = color.Rgb is not null ? standard : null,
			Bold = bold,
		});
	}

	/// <summary><paramref name="palette"/> as a layout's part colours, in ANSI (<see cref="ThemePalette.ToTheme"/>).</summary>
	public static LayoutTheme ToLayoutTheme(this ThemePalette palette)
	{
		ArgumentNullException.ThrowIfNull(palette);
		return palette.ToTheme((color, bold) => Paint(color, bold));
	}
}
