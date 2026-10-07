namespace MarkupString;

/// <summary>
/// One row of the cells a picture covers in a laid-out block, over the text a client that draws no
/// pictures shows there instead: a row of the figure's text art, or blanks. <see cref="Layout.Figure"/>
/// lays one out per row for a reader whose client draws pictures (<see cref="Layout.LayoutContext.Pictures"/>).
/// </summary>
/// <remarks>
/// <para>The text under it is exactly <see cref="Columns"/> cells wide, so the block around it lines up
/// whether the picture is drawn or not. A terminal emitter that can draw the picture writes it into
/// those cells in place of the text; every other emitter, and every string operation, sees the text.</para>
/// <para>Only a terminal format draws it, and only when its host gave it the picture's pixels. There is
/// no codec: it exists between a reader's relayout and the render, and is never stored.</para>
/// </remarks>
/// <param name="Image">The picture.</param>
/// <param name="Row">Which row of the picture this is, from 0.</param>
/// <param name="Rows">How many rows the picture covers.</param>
/// <param name="Columns">How many cells wide the picture is.</param>
public sealed record PictureCellsMarkup(ImageMarkup Image, int Row, int Rows, int Columns) : IMarkup;
