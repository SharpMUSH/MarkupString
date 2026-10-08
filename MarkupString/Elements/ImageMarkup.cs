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
/// address when there is none, or a figure's text art. <see cref="MarkupText.Image"/> builds one inline,
/// and a <see cref="Layout.Figure"/> lays one out over rows of cells (<see cref="Row"/>).
/// </summary>
/// <remarks>
/// <para>MXP writes <c>&lt;IMAGE&gt;</c>, Pueblo and HTML <c>&lt;img&gt;</c>, and BBCode <c>[img]</c>, once
/// for the picture (<see cref="StartsPicture"/>); over a figure's rows they keep the rest of its cells blank.
/// A terminal draws a figure's rows in its cells when its host has the picture's pixels. Every other format,
/// and a client that refuses the picture, writes the wrapped text, so a terminal still learns there was a
/// picture and where it is. Put it inside a link to make it one.</para>
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
	ImageAlign? Align = null) : IMarkup
{
	/// <summary>
	/// The row of a laid-out picture this covers, or null for a picture in a line of text. A figure sets it;
	/// see <see cref="PictureRow"/>.
	/// </summary>
	public PictureRow? Row { get; init; }

	/// <summary>
	/// Whether the run <paramref name="context"/> describes is where a format that writes the picture as an
	/// element of its own writes it: the start of an inline picture, or of row 0 of a laid-out one. Every
	/// other run of the picture writes no element, so a description in several runs, or art over several
	/// rows, is still one picture.
	/// </summary>
	public bool StartsPicture(in EmitContext context) => Row is not { Row: > 0 } && context.StartsRegion(this);
}
