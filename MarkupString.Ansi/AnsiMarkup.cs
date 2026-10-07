namespace MarkupString.Ansi;

/// <summary>
/// A markup layer carrying terminal formatting. Value-equal through <see cref="Style"/>, so two
/// identically styled spans coalesce.
/// </summary>
public sealed record AnsiMarkup(AnsiStyle Style) : IColorMarkup, IAnsiStyleSource
{
	/// <inheritdoc/>
	/// <remarks>A palette colour is resolved to its usual RGB value; the terminal's default has none.</remarks>
	public RgbColor? Foreground => Style.Foreground?.ToRgb() is { } rgb ? new RgbColor(rgb.R, rgb.G, rgb.B) : null;

	/// <inheritdoc/>
	/// <remarks>A palette colour is resolved to its usual RGB value; the terminal's default has none.</remarks>
	public RgbColor? Background => Style.Background?.ToRgb() is { } rgb ? new RgbColor(rgb.R, rgb.G, rgb.B) : null;

	/// <inheritdoc/>
	/// <remarks>
	/// Everything else about the style is kept, and the colour this layer had is what a client with only
	/// the sixteen standard colours is sent (<see cref="AnsiStyle.StandardForeground"/>).
	/// </remarks>
	public IColorMarkup WithForeground(RgbColor color) =>
		this with
		{
			Style = Style with
			{
				Foreground = new AnsiColor.Rgb(color.R, color.G, color.B),
				StandardForeground = Style.StandardForeground ?? Style.Foreground,
			}
		};

	/// <inheritdoc/>
	/// <remarks>This layer is terminal formatting and nothing else, so it folds in every format.</remarks>
	public bool TryGetAnsiStyle(MarkupFormat format, out AnsiStyle style)
	{
		style = Style;
		return true;
	}

	/// <summary>
	/// Builds a markup from individual attributes. A <see langword="null"/> colour means the span
	/// does not set that colour.
	/// </summary>
	public static AnsiMarkup Create(
		AnsiColor? foreground = null,
		AnsiColor? background = null,
		string? linkText = null,
		string? linkUrl = null,
		LinkKind linkKind = LinkKind.Url,
		bool blink = false,
		bool bold = false,
		bool clear = false,
		bool faint = false,
		bool inverted = false,
		bool italic = false,
		bool overlined = false,
		bool underlined = false,
		bool strikeThrough = false) =>
		new(new AnsiStyle
		{
			Foreground = foreground,
			Background = background,
			LinkText = linkText,
			LinkUrl = linkUrl,
			LinkKind = linkKind,
			Blink = blink,
			Bold = bold,
			Clear = clear,
			Faint = faint,
			Inverted = inverted,
			Italic = italic,
			Overlined = overlined,
			Underlined = underlined,
			StrikeThrough = strikeThrough
		});
}
