namespace MarkupString;

/// <summary>How an image sits against the text around it.</summary>
public enum ImageAlign
{
	/// <summary>Floated to the left, text flowing round it.</summary>
	Left,

	/// <summary>Floated to the right, text flowing round it.</summary>
	Right,

	/// <summary>In the line, its top level with the text's.</summary>
	Top,

	/// <summary>In the line, its middle level with the text's.</summary>
	Middle,

	/// <summary>In the line, sitting on the text's baseline.</summary>
	Bottom,
}

/// <summary>
/// A picture. It wraps the text a client with no pictures shows instead — its description, or the
/// address when there is none. <see cref="MarkupText.Image"/> builds one.
/// </summary>
/// <remarks>
/// MXP writes <c>&lt;IMAGE&gt;</c>, Pueblo and HTML <c>&lt;img&gt;</c>, and BBCode <c>[img]</c>. Every
/// other format writes the wrapped text, so a terminal still learns there was a picture and where it is.
/// Put it inside a link to make it one.
/// </remarks>
/// <param name="Source">The picture: a file name the client resolves against the game's own image directory, or an absolute address.</param>
/// <param name="Description">What the picture shows, for a reader who cannot see it.</param>
/// <param name="Width">Its width in pixels, or the picture's own when null.</param>
/// <param name="Height">Its height in pixels, or the picture's own when null.</param>
/// <param name="Align">How it sits against the text, or the client's default when null.</param>
public sealed record ImageMarkup(
	string Source,
	string? Description = null,
	int? Width = null,
	int? Height = null,
	ImageAlign? Align = null) : IMarkup;
