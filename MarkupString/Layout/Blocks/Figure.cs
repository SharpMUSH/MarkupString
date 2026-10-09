using System.Collections.Immutable;
namespace MarkupString.Layout;

/// <summary>
/// A picture, the text art a terminal shows instead of it, and the content that flows round it.
/// </summary>
/// <param name="Image">The picture: its address and description.</param>
/// <param name="Art">Text art standing in for the picture where pictures are not shown; its lines keep their own spacing. Empty for none.</param>
public sealed record Figure(ImageMarkup Image, MarkupText Art) : Block
{
	/// <inheritdoc/>
	public override Block MapText(Func<MarkupText, MarkupText> map) =>
		this with { Art = map(Art), Beside = Beside?.MapText(map) };

	/// <summary>Which side the picture sits on, with <see cref="Beside"/> flowing round it.</summary>
	public FigureFloat Float { get; init; }

	/// <summary>The content that flows round the picture, or none. Text flows round the art and widens once past it.</summary>
	public Block? Beside { get; init; }

	/// <summary>Cells between the art and the content beside it.</summary>
	public int Gap { get; init; } = 2;

	private string Description => Image.Description is { Length: > 0 } description ? description : Image.Source;

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		var art = Art.Length == 0
			? null
			: Art.Split("\n").Select(line => line.Text.EndsWith('\r') ? line.Substring(0, line.Length - 1) : line).ToArray();

		// Art of line breaks alone has no cells to hold a picture, so it is no art.
		if (art is not null && art.Max(line => line.DisplayWidth) == 0) art = null;

		// The picture is on its rows whoever reads them: a terminal that has its pixels draws it in their cells,
		// MXP or Pueblo writes it once, and everything else reads what the rows hold.
		if (Image.Source.Length > 0)
		{
			if (context.Pictures is { } pictures
				&& pictures(Image, Math.Min(width, art?.Max(line => line.DisplayWidth) ?? width)) is { Columns: > 0, Rows: > 0 } cells)
				art = PictureRows(art, cells, width);
			else if (art is not null)
				art = PictureRows(art, default, width);
		}

		if (art is null)
		{
			var described = MarkupText.Plain($"[{Description}]").FormatColumn(BlockText.Column(width, context.TextAlignment));
			lines.AddRange(Image.Source.Length > 0 ? Marked(described, described.Max(line => line.DisplayWidth), description: true) : described);
			if (Beside is { } after) context.Draw(after, width, lines);
			return;
		}

		var artWidth = Math.Min(width, art.Max(line => line.DisplayWidth));
		var narrow = width - artWidth - Math.Max(0, Gap);

		if (Float == FigureFloat.None || Beside is null || narrow < 8)
		{
			// The art moves as one piece, so its lines keep their spacing against each other.
			var indent = BlockText.Blank(context.TextAlignment switch
			{
				Alignment.Center => (width - artWidth) / 2,
				Alignment.Right => width - artWidth,
				_ => 0,
			});
			foreach (var line in art) lines.Add(BlockText.Fit(MarkupText.Concat([indent, BlockText.Fit(line, artWidth)]), width));
			if (Beside is { } below) context.Draw(below, width, lines);
			return;
		}

		var gap = BlockText.Blank(Gap);
		var blankArt = BlockText.Blank(artWidth);
		List<MarkupText> beside;
		if (Beside is TextBlock text)
		{
			// The text wraps at the narrow width beside the art and widens once it is past it.
			var flow = BlockText.Column(narrow, text.Alignment ?? context.TextAlignment) with { Indent = new Indent(0, art.Length, artWidth + gap.DisplayWidth) };
			beside = [.. text.Content.FormatColumn(flow)];
		}
		else
		{
			beside = context.Lines(Beside, narrow);
		}

		var rows = Math.Max(art.Length, beside.Count);
		for (var row = 0; row < rows; row++)
		{
			if (row >= art.Length && Beside is TextBlock)
			{
				lines.Add(BlockText.Fit(beside[row], width));
				continue;
			}
			var picture = row < art.Length ? BlockText.Fit(art[row], artWidth) : blankArt;
			var words = row < beside.Count ? BlockText.Fit(beside[row], narrow) : BlockText.Blank(narrow);
			lines.Add(Float == FigureFloat.Left
				? MarkupText.Concat([picture, gap, words])
				: MarkupText.Concat([words, gap, picture]));
		}
	}

	/// <summary>
	/// The rows of cells the picture is drawn in, each marked with the picture and its <see cref="PictureRow"/>
	/// over what a client the picture does not reach shows there: the art, which keeps its own size, or, with no
	/// art, blank cells of <paramref name="cells"/> with the description on the middle row.
	/// </summary>
	private MarkupText[]? PictureRows(MarkupText[]? art, PictureCells cells, int width)
	{
		var columns = Math.Min(width, art?.Max(line => line.DisplayWidth) ?? cells.Columns);
		var rows = art?.Length ?? cells.Rows;
		if (columns <= 0) return art;
		var under = art is not null
			? art.Select(line => BlockText.Fit(line, columns)).ToArray()
			: Enumerable.Range(0, rows).Select(row => row == rows / 2
				? BlockText.Fit(MarkupText.Plain($"[{Description}]"), columns)
				: BlockText.Blank(columns)).ToArray();

		return Marked(under, columns);
	}

	/// <summary>
	/// <paramref name="rows"/>, each <paramref name="columns"/> cells wide, as the rows of the picture: its
	/// <paramref name="description"/> standing in for it, or cells laid out for it.
	/// </summary>
	private MarkupText[] Marked(MarkupText[] rows, int columns, bool description = false)
	{
		var marked = new MarkupText[rows.Length];
		for (var row = 0; row < rows.Length; row++)
			marked[row] = MarkupText.Wrap(Image with { Row = new PictureRow(row, rows.Length, columns) { IsDescription = description } },
				Unpictured(rows[row]));
		return marked;
	}

	/// <summary>
	/// <paramref name="text"/> without the pictures it carries. Art is often the picture's own placeholder,
	/// already marked as the picture inline; under the figure's mark that would be a second picture in the
	/// same cells, written twice by a format with an element for it and pushing aside what is beside it.
	/// </summary>
	private static MarkupText Unpictured(MarkupText text)
	{
		if (!text.Runs.Any(run => run.Markups.Any(markup => markup is ImageMarkup))) return text;

		var runs = ImmutableArray.CreateBuilder<Run>(text.Runs.Length);
		foreach (var run in text.Runs)
		{
			var kept = run.Markups.Where(markup => markup is not ImageMarkup).ToArray();
			if (kept.Length > 0) runs.Add(run with { Markups = MarkupSet.Of(kept) });
		}
		return new MarkupText(text.Text, runs.ToImmutable());
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		lines.Add(MarkupText.Plain($"Image: {Description}"));
		if (Beside is { } after) context.Draw(after, width, lines);
	}
}

/// <summary>Which side a figure sits on.</summary>
public enum FigureFloat
{
	/// <summary>On a line of its own, with what is beside it underneath.</summary>
	None,

	/// <summary>On the left, with what is beside it flowing down its right.</summary>
	Left,

	/// <summary>On the right, with what is beside it flowing down its left.</summary>
	Right,
}
