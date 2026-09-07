namespace MarkupString.Layout;

/// <summary>
/// One shaped line of a column: its text, the cell offset the text starts at, the cell width
/// available to it, and whether it ends a paragraph.
/// </summary>
/// <remarks>
/// <c>Start</c> carries an indent rather than baking leading spaces into <c>Text</c>, so the
/// column's fill shows through it and a right-aligned indented line still reaches the edge.
/// <c>Width</c> is per line because an indent may widen the column from a given line on.
/// <c>EndsParagraph</c> is true when a hard break or the end of the text follows;
/// <see cref="Alignment.Paragraph"/> needs it and no caller can recover it afterwards.
/// </remarks>
public readonly record struct TextLine(MarkupText Text, int Start, int Width, bool EndsParagraph);
