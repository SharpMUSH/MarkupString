namespace MarkupString;

/// <summary>
/// Which row of a laid-out picture a run is, and how big the picture is in cells. A <see cref="Layout.Figure"/>
/// sets it (<see cref="ImageMarkup.Row"/>) on every row it draws its picture in, over the text a client that
/// shows no picture reads there: a row of the figure's text art, its description, or blanks.
/// </summary>
/// <remarks>
/// The text under a row is exactly <see cref="Columns"/> cells wide, so the block around the picture lines
/// up whichever way a format writes it: a terminal that draws pictures in its cells draws the row there, a
/// format with a picture element of its own writes it once, on row 0, and keeps the cells blank, and every
/// other format writes the text.
/// </remarks>
/// <param name="Row">Which row of the picture this is, from 0.</param>
/// <param name="Rows">How many rows the picture covers.</param>
/// <param name="Columns">How many cells wide the picture is.</param>
public readonly record struct PictureRow(int Row, int Rows, int Columns)
{
	/// <summary>
	/// Whether these cells hold the picture's description rather than cells laid out for it: the figure had no
	/// art, and nothing gave the picture a size in cells. A format that sizes its picture element to the cells
	/// it covers (MXP's <c>W</c> and <c>H</c>) leaves these alone, since a picture drawn in a line of its
	/// description would be a strip.
	/// </summary>
	public bool IsDescription { get; init; }
}
