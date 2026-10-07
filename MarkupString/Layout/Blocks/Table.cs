using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>
/// Rows under column headings. Columns grow to fit their widest cell; when the table is too wide, the
/// columns that may wrap give way first, then the least important columns are left out, and when
/// even one column will not fit each row becomes a card of labelled values. When it is narrower than
/// its width, the columns that <see cref="TableColumn.Grow"/> share what is left, so the table fills it.
/// </summary>
/// <param name="Columns">The columns, in order.</param>
/// <param name="Rows">The rows, a cell per column; a short row is padded with empty cells. A cell's text takes its column's alignment unless it sets its own.</param>
public sealed record Table(ImmutableArray<TableColumn> Columns, ImmutableArray<ImmutableArray<Block>> Rows) : Block
{
	/// <summary>Cells between two columns, when there is no <see cref="Separator"/>.</summary>
	public int Gap { get; init; } = 2;

	/// <summary>Drawn between two columns on every line instead of the gap.</summary>
	public MarkupText? Separator { get; init; }

	/// <summary>The line under the headings, repeated; empty for none; unset, the theme's (<c>-</c>).</summary>
	public MarkupText? HeaderRule { get; init; }

	/// <summary>
	/// Whether every second row is laid on the theme's <see cref="LayoutTheme.StripeColor"/>, to help the
	/// eye along a wide row. Not when the rows are drawn as cards.
	/// </summary>
	public bool Striped { get; init; }

	private ImmutableArray<ImmutableArray<Block>> AllRows => Rows.IsDefault ? [] : Rows;

	private static readonly Block Blank = new TextBlock(MarkupText.Empty);

	/// <summary>The cell in <paramref name="column"/> of <paramref name="row"/>, empty past the row's end.</summary>
	private static Block Cell(ImmutableArray<Block> row, int column) => !row.IsDefault && column < row.Length ? row[column] : Blank;

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Columns.IsDefaultOrEmpty) return;
		// Measured as drawn: an ASCII reader's separator may be the wider " | " stand-in.
		var divider = Separator is { } drawn ? context.Paint(theme => theme.SeparatorColor, context.Glyph(drawn, " | ")) : BlockText.Blank(Gap);
		if (Widths(context, width, divider.DisplayWidth) is not { } widths)
		{
			DrawCards(context, width, lines);
			return;
		}

		var shown = Enumerable.Range(0, Columns.Length).Where(c => widths[c] > 0).ToArray();
		var tableWidth = shown.Sum(c => widths[c]) + divider.DisplayWidth * (shown.Length - 1);

		MarkupText Join(IEnumerable<MarkupText> cells) => BlockText.Fit(MarkupText.Join(divider, cells), width);

		lines.Add(Join(shown.Select(c => BlockText.Fit(context.Paint(theme => theme.HeadingColor, Columns[c].Header).FormatColumn(BlockText.Column(widths[c], Columns[c].Alignment))[0], widths[c]))));
		var rule = HeaderRule ?? context.Theme.Piece(theme => theme.HeaderRule);
		if (rule.Length > 0) lines.Add(BlockText.Fit(context.Paint(theme => theme.HeaderRuleColor, BlockText.Run(context.Glyph(rule, "-"), tableWidth)), width));

		var cellLines = new List<MarkupText>[Columns.Length];
		var index = 0;
		foreach (var row in AllRows)
		{
			var first = lines.Count;
			var height = 1;
			foreach (var c in shown)
			{
				cellLines[c] = (context with { TextAlignment = Columns[c].Alignment }).Lines(Cell(row, c), widths[c]);
				if (!Columns[c].Wrap && cellLines[c].Count > 1) cellLines[c].RemoveRange(1, cellLines[c].Count - 1);
				height = Math.Max(height, cellLines[c].Count);
			}
			for (var line = 0; line < height; line++)
				lines.Add(Join(shown.Select(c => line < cellLines[c].Count ? BlockText.Fit(cellLines[c][line], widths[c]) : BlockText.Blank(widths[c]))));
			if (Striped) context.Stripe(lines, first, index);
			index++;
		}
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (!Columns.IsDefaultOrEmpty) DrawCards(context, width, lines);
	}

	/// <summary>Each row as a card of labelled values, the way a phone shows a table too wide for it.</summary>
	private void DrawCards(LayoutContext context, int width, IList<MarkupText> lines)
	{
		// A card's values all start under their labels, whatever side their column kept them to.
		var cards = context with { TextAlignment = Alignment.Left };
		var rows = AllRows;
		for (var r = 0; r < rows.Length; r++)
		{
			if (r > 0 && !context.Linear) lines.Add(MarkupText.Empty);
			var row = rows[r];
			cards.Draw(new Fields([.. Enumerable.Range(0, Columns.Length).Select(c => new Field(Columns[c].Header, Cell(row, c)))]), width, lines);
		}
	}

	/// <summary>
	/// The width each column is drawn at in <paramref name="width"/> cells, zero for one left out; empty
	/// when the rows are drawn as cards instead. A format that lays the table out itself, such as HTML,
	/// reads its proportions here.
	/// </summary>
	public ImmutableArray<int> ColumnWidths(LayoutContext context, int width)
	{
		if (Columns.IsDefaultOrEmpty) return [];
		var divider = Separator is { } drawn ? context.Glyph(drawn, " | ").DisplayWidth : Gap;
		return Widths(context, width, divider) is { } widths ? [.. widths] : [];
	}

	/// <summary>
	/// <paramref name="spare"/> cells shared among the shown columns that grow, by their
	/// <see cref="TableColumn.Grow"/> shares; the cells a share rounds away go to the first of them.
	/// None grows past its <see cref="TableColumn.Max"/>.
	/// </summary>
	private int[] Spread(int[] widths, List<int> shown, int spare)
	{
		var growing = shown.Where(c => Columns[c].Grow > 0).ToList();
		var shares = growing.Sum(c => Columns[c].Grow);
		if (spare <= 0 || shares == 0) return widths;
		var added = growing.ToDictionary(c => c, c => spare * Columns[c].Grow / shares);
		added[growing[0]] += spare - added.Values.Sum();
		foreach (var c in growing)
			widths[c] = Columns[c].Max > 0 ? Math.Min(widths[c] + added[c], Math.Max(widths[c], Columns[c].Max)) : widths[c] + added[c];
		return widths;
	}

	/// <summary>
	/// The width of each column, zero for one left out, or null when not even one column fits. Each
	/// column asks for its widest cell; too wide, the columns that wrap give way, widest first, down to
	/// their least widths, and then the least important column is left out. A column that does not wrap
	/// never gives way: it is shown whole or not at all.
	/// </summary>
	private int[]? Widths(LayoutContext context, int width, int gap)
	{
		var natural = new int[Columns.Length];
		for (var c = 0; c < Columns.Length; c++)
		{
			var widest = Columns[c].Header.DisplayWidth;
			foreach (var row in AllRows) widest = Math.Max(widest, Cell(row, c).Measure(context, width).Natural);
			if (Columns[c].Max > 0) widest = Math.Min(widest, Columns[c].Max);
			natural[c] = Math.Max(Math.Max(1, Columns[c].Min), widest);
		}

		var active = Enumerable.Range(0, Columns.Length).ToList();
		while (active.Count > 0)
		{
			var widths = new int[Columns.Length];
			foreach (var c in active) widths[c] = natural[c];
			var overflow = active.Sum(c => widths[c]) + gap * (active.Count - 1) - width;
			while (overflow > 0)
			{
				var widest = active.Where(c => Columns[c].Wrap && widths[c] > Math.Max(1, Columns[c].Min))
					.OrderByDescending(c => widths[c]).ThenByDescending(c => c).FirstOrDefault(-1);
				if (widest < 0) break;
				widths[widest]--;
				overflow--;
			}
			if (overflow <= 0) return Spread(widths, active, -overflow);

			// Leave out the least important column, the rightmost of those tied.
			if (active.Count == 1) return null;
			active.Remove(active.OrderByDescending(c => Columns[c].Priority).ThenByDescending(c => c).First());
		}
		return null;
	}
}

/// <summary>One column of a <see cref="Table"/>.</summary>
/// <param name="Header">The heading.</param>
public sealed record TableColumn(MarkupText Header)
{
	/// <summary>Where the heading and each cell's text sit.</summary>
	public Alignment Alignment { get; init; } = Alignment.Left;

	/// <summary>The fewest cells the column is drawn in before it is left out.</summary>
	public int Min { get; init; } = 1;

	/// <summary>The most cells it grows to, or zero for no limit.</summary>
	public int Max { get; init; }

	/// <summary>How important it is: when the table is too wide the highest number is left out first. One is never left out while another column could be.</summary>
	public int Priority { get; init; } = 1;

	/// <summary>Whether its cells may wrap onto more lines; a column that may not is shown whole or left out.</summary>
	public bool Wrap { get; init; } = true;

	/// <summary>
	/// Its share of the width the table has left over once every column fits, or zero for none. A
	/// table with a column that grows fills its width; one without stays as wide as its cells.
	/// </summary>
	public int Grow { get; init; }
}
