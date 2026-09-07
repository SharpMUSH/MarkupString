namespace MarkupString.Layout;

/// <summary>
/// The drawn columns of a layout, together with what each one does on the rows where it, or a
/// neighbour, has run out of text.
/// </summary>
/// <remarks>
/// The two servers do different things with an exhausted column, and both are here. PennMUSH
/// <em>merges</em>: the empty column's cells are absorbed by a neighbour, which widens in place
/// and goes on wrapping into the extra room — the same thing the shaper already does for a
/// column that widens from a given line, so a merge is expressed as exactly that. RhostMUSH
/// <em>shifts</em>: a neighbour's line is drawn at the empty column's position and the
/// neighbour's own position is left blank, the text moving rather than the column growing.
/// </remarks>
internal sealed class Plan
{
	private readonly MarkupText[]?[] _blocks;
	private readonly ColumnFormat?[] _formats;
	/// <summary>From this row on, draw nothing: a merged neighbour covers these cells.</summary>
	private readonly int[] _silentFrom;
	/// <summary>From this row on, draw <see cref="_source"/>'s line instead of this column's.</summary>
	private readonly int[] _borrowFrom;
	private readonly int[] _source;
	/// <summary>From this row on, this column's line has been drawn elsewhere.</summary>
	private readonly int[] _lentFrom;
	/// <summary>Whether a lent column still occupies its cells, blank, or vacates them entirely.</summary>
	private readonly bool[] _lentBlank;
	/// <summary>The row from which each column has nothing more to say.</summary>
	private readonly int[] _exhausted;

	internal int RowCount { get; }

	private Plan(
		MarkupText[]?[] blocks,
		ColumnFormat?[] formats,
		int[] silentFrom,
		int[] borrowFrom,
		int[] source,
		int[] lentFrom,
		bool[] lentBlank,
		int[] exhausted,
		int rowCount)
	{
		_blocks = blocks;
		_formats = formats;
		_silentFrom = silentFrom;
		_borrowFrom = borrowFrom;
		_source = source;
		_lentFrom = lentFrom;
		_lentBlank = lentBlank;
		_exhausted = exhausted;
		RowCount = rowCount;
	}

	internal static Plan Build(ReadOnlySpan<LayoutCell> cells)
	{
		var count = cells.Length;
		var formats = new ColumnFormat?[count];
		var exhausted = new int[count];
		for (var i = 0; i < count; i++)
			if (cells[i] is LayoutColumn column)
			{
				formats[i] = column.Format;
				exhausted[i] = Exhausted(column);
			}

		var silentFrom = Filled(count);
		var borrowFrom = Filled(count);
		var lentFrom = Filled(count);
		var source = new int[count];
		var lentBlank = new bool[count];
		Array.Fill(source, -1);

		// Every widening has to be known before anything is drawn, so this pass only records.
		for (var i = 0; i < count; i++)
		{
			if (cells[i] is not LayoutColumn column || column.Format.WhenEmpty == WhenEmpty.None) continue;

			var toLeft = column.Format.WhenEmpty is WhenEmpty.GiveSpaceToLeft or WhenEmpty.PushLeftColumnRight;
			var neighbour = Neighbour(cells, i, toLeft);
			if (neighbour < 0) continue;

			var from = exhausted[i];
			switch (column.Format.WhenEmpty)
			{
				case WhenEmpty.GiveSpaceToLeft:
					formats[neighbour] = Widen(formats[neighbour]!, column.Format.Width, from);
					silentFrom[i] = from;
					break;
				case WhenEmpty.GiveSpaceToRight:
					formats[neighbour] = Widen(formats[neighbour]!, column.Format.Width, from);
					borrowFrom[i] = from;
					source[i] = neighbour;
					lentFrom[neighbour] = from;
					break;
				case WhenEmpty.PullRightColumnLeft:
				case WhenEmpty.PushLeftColumnRight:
					borrowFrom[i] = from;
					source[i] = neighbour;
					lentFrom[neighbour] = from;
					lentBlank[neighbour] = true;
					break;
			}
		}

		var blocks = new MarkupText[]?[count];
		for (var i = 0; i < count; i++)
			if (cells[i] is LayoutColumn column)
				blocks[i] = column.Content.FormatColumn(formats[i]!);

		var driven = 0;
		var any = 0;
		for (var i = 0; i < count; i++)
		{
			if (cells[i] is not LayoutColumn column) continue;
			any = Math.Max(any, blocks[i]!.Length);
			if (!column.Format.Repeat) driven = Math.Max(driven, blocks[i]!.Length);
		}

		return new Plan(
			blocks, formats, silentFrom, borrowFrom, source, lentFrom, lentBlank, exhausted,
			driven > 0 ? driven : any);
	}

	/// <summary>
	/// What column <paramref name="index"/> draws on <paramref name="row"/>, or null when it
	/// draws nothing at all because a merged neighbour covers its cells.
	/// </summary>
	internal MarkupText? LineOf(int index, LayoutColumn column, int row)
	{
		if (row >= _silentFrom[index]) return null;
		if (row >= _lentFrom[index]) return _lentBlank[index] ? ColumnRenderer.Blank(column.Format) : null;
		if (row >= _borrowFrom[index]) return Line(_source[index], row) ?? Blank(_source[index], column);
		return Line(index, row) ?? ColumnRenderer.Blank(column.Format);
	}

	/// <summary>True when every column has run out of text by <paramref name="row"/>.</summary>
	internal bool AllExhaustedAt(ReadOnlySpan<LayoutCell> cells, int row)
	{
		for (var i = 0; i < cells.Length; i++)
			if (cells[i] is LayoutColumn && row < _exhausted[i]) return false;
		return true;
	}

	private MarkupText? Line(int index, int row)
	{
		if (index < 0) return null;
		var block = _blocks[index];
		if (block is null || block.Length == 0) return null;
		var format = _formats[index]!;
		if (format.Repeat) return block[row % block.Length];
		return row < block.Length ? block[row] : null;
	}

	/// <summary>A blank of the borrowed column's width, so a shifted row keeps the layout square.</summary>
	private MarkupText Blank(int source, LayoutColumn fallback) =>
		ColumnRenderer.Blank(source >= 0 && _formats[source] is { } format ? format : fallback.Format);

	private static int[] Filled(int count)
	{
		var values = new int[count];
		Array.Fill(values, int.MaxValue);
		return values;
	}

	/// <summary>
	/// The neighbour widened by <paramref name="cells"/> from <paramref name="fromLine"/>. A
	/// column that already carries an indent keeps it: composing the two has no obvious meaning.
	/// </summary>
	private static ColumnFormat Widen(ColumnFormat format, int cells, int fromLine) =>
		format.Indent.Amount > 0 || format.Indent.Widen > 0
			? format
			: format with { Indent = new Indent(0, fromLine, cells) };

	/// <summary>The nearest column on the given side, stepping over separators.</summary>
	private static int Neighbour(ReadOnlySpan<LayoutCell> cells, int index, bool toLeft)
	{
		var step = toLeft ? -1 : 1;
		for (var i = index + step; i >= 0 && i < cells.Length; i += step)
			if (cells[i] is LayoutColumn) return i;
		return -1;
	}

	/// <summary>
	/// The row from which a column has nothing more to say: the first of the run of blank lines
	/// that ends its block. A blank line in the middle does not count, because the column has not
	/// run out — it is merely quiet for a line.
	/// </summary>
	private static int Exhausted(LayoutColumn column)
	{
		var lines = column.Content.Shape(column.Format);
		var last = lines.Length;
		while (last > 0 && lines[last - 1].Text.Length == 0) last--;
		return last;
	}
}
