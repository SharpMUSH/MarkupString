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
	/// <summary>The column a lent line was drawn at, whose width this one takes in exchange.</summary>
	private readonly int[] _lentTo;
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
		int[] lentTo,
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
		_lentTo = lentTo;
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

		// A column that receives a merge is wider from the merge row on than its Width says, so it
		// cannot correctly pass "its" cells on to a further neighbour — it would hand over the base
		// width and the chain would lose the difference. Merges into such a column win; the merge
		// out of it is abandoned. Deciding it from the intended targets, before anything is
		// applied, keeps the outcome independent of the order these are walked in.
		var receivesMerge = new bool[count];
		for (var i = 0; i < count; i++)
		{
			if (cells[i] is not LayoutColumn giver) continue;
			if (giver.Format.WhenEmpty is not (WhenEmpty.GiveSpaceToLeft or WhenEmpty.GiveSpaceToRight)) continue;
			var target = Neighbour(cells, i, giver.Format.WhenEmpty == WhenEmpty.GiveSpaceToLeft);
			if (target >= 0) receivesMerge[target] = true;
		}

		var silentFrom = Filled(count);
		var borrowFrom = Filled(count);
		var lentFrom = Filled(count);
		var source = new int[count];
		var lentBlank = new bool[count];
		var lentTo = new int[count];
		Array.Fill(source, -1);
		Array.Fill(lentTo, -1);

		// Every widening has to be known before anything is drawn, so this pass only records.
		for (var i = 0; i < count; i++)
		{
			if (cells[i] is not LayoutColumn column || column.Format.WhenEmpty == WhenEmpty.None) continue;

			var toLeft = column.Format.WhenEmpty is WhenEmpty.GiveSpaceToLeft or WhenEmpty.PushLeftColumnRight;
			var neighbour = Neighbour(cells, i, toLeft);
			if (neighbour < 0) continue;

			var from = exhausted[i];
			var merging = column.Format.WhenEmpty is WhenEmpty.GiveSpaceToLeft or WhenEmpty.GiveSpaceToRight;
			if (merging && receivesMerge[i]) continue;

			switch (column.Format.WhenEmpty)
			{
				case WhenEmpty.GiveSpaceToLeft:
					// A merge that cannot widen its neighbour is abandoned outright. Going silent
					// anyway would hand the cells to nobody and leave every merged row short.
					if (!TryWiden(formats[neighbour]!, column.Format.Width, from, out var toLeft2)) break;
					formats[neighbour] = toLeft2;
					silentFrom[i] = from;
					break;
				case WhenEmpty.GiveSpaceToRight:
					if (!TryWiden(formats[neighbour]!, column.Format.Width, from, out var toRight)) break;
					formats[neighbour] = toRight;
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
					// The two slots trade places, widths included, so the row stays square when
					// the columns are not the same width.
					lentTo[neighbour] = i;
					break;
			}
		}

		var blocks = new MarkupText[]?[count];
		for (var i = 0; i < count; i++)
			if (cells[i] is LayoutColumn column)
			{
				blocks[i] = column.Content.FormatColumn(formats[i]!);
				// Re-measured against the format actually drawn: a widened column fits more text
				// per line and so may run out earlier than its own format suggested.
				exhausted[i] = Exhausted(column.Content, formats[i]!);
			}

		var driven = 0;
		var any = 0;
		for (var i = 0; i < count; i++)
		{
			if (cells[i] is not LayoutColumn column) continue;
			any = Math.Max(any, blocks[i]!.Length);
			if (!column.Format.Repeat) driven = Math.Max(driven, blocks[i]!.Length);
		}

		return new Plan(
			blocks, formats, silentFrom, borrowFrom, source, lentFrom, lentBlank, lentTo, exhausted,
			driven > 0 ? driven : any);
	}

	/// <summary>
	/// What column <paramref name="index"/> draws on <paramref name="row"/>, or null when it
	/// draws nothing at all because a merged neighbour covers its cells.
	/// </summary>
	internal MarkupText? LineOf(int index, LayoutColumn column, int row)
	{
		if (row >= _silentFrom[index]) return null;
		if (row >= _lentFrom[index])
			return _lentBlank[index] ? Blank(index, row, _lentTo[index]) : null;
		if (row >= _borrowFrom[index]) return Line(_source[index], row) ?? Blank(_source[index], row);
		return Line(index, row) ?? Blank(index, row);
	}

	/// <summary>True when every column has run out of text by <paramref name="row"/>.</summary>
	internal bool AllExhaustedAt(ReadOnlySpan<LayoutCell> cells, int row)
	{
		for (var i = 0; i < cells.Length; i++)
		{
			if (cells[i] is not LayoutColumn column) continue;
			// A repeating column cycles its lines for as long as the layout runs, so it is never
			// out of text — its own height says nothing about whether this row is blank.
			if (column.Format.Repeat ? _exhausted[i] > 0 : row < _exhausted[i]) return false;
		}
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

	/// <summary>
	/// A blank of the width column <paramref name="index"/> occupies on <paramref name="row"/>,
	/// so a row below an exhausted or shifted column stays as wide as the ones above it.
	/// </summary>
	private MarkupText Blank(int index, int row, int widthOf = -1)
	{
		if (index < 0) return MarkupText.Empty;
		var format = _formats[index]!;
		// A shifted column keeps its own fill and markup but takes the width of the slot its line
		// went to, so the pair of them still covers the cells they covered before.
		var measure = widthOf >= 0 && _formats[widthOf] is { } other ? other : format;
		return ColumnRenderer.Blank(format, WidthAt(measure, row));
	}

	/// <summary>
	/// The cells a column occupies on a row. A merged column is wider from the merge row on than
	/// its own width says, the extra living in the indent the merge gave it.
	/// </summary>
	private static int WidthAt(ColumnFormat format, int row) =>
		row >= format.Indent.FromLine ? format.Width + Math.Max(0, format.Indent.Widen) : format.Width;

	private static int[] Filled(int count)
	{
		var values = new int[count];
		Array.Fill(values, int.MaxValue);
		return values;
	}

	/// <summary>
	/// The neighbour widened by <paramref name="cells"/> from <paramref name="fromLine"/>, or
	/// false when it cannot be: a column that already carries an indent has one width from one
	/// line, and a single <see cref="Indent"/> cannot also express a different width from a
	/// different line. The caller abandons the merge rather than applying half of it.
	/// </summary>
	private static bool TryWiden(ColumnFormat format, int cells, int fromLine, out ColumnFormat widened)
	{
		if (format.Indent.Amount > 0 || format.Indent.Widen > 0)
		{
			widened = format;
			return false;
		}
		widened = format with { Indent = new Indent(0, fromLine, cells) };
		return true;
	}

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
	private static int Exhausted(LayoutColumn column) => Exhausted(column.Content, column.Format);

	private static int Exhausted(MarkupText content, ColumnFormat format)
	{
		var lines = content.Shape(format);
		var last = lines.Length;
		while (last > 0 && lines[last - 1].Text.Length == 0) last--;
		return last;
	}
}
