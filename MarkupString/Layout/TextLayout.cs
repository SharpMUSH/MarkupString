namespace MarkupString.Layout;

/// <summary>
/// Assembles drawn columns into rows: the layout engine behind column functions such as
/// PennMUSH's <c>align()</c> and RhostMUSH's <c>printf()</c>.
/// </summary>
public static class TextLayout
{
	/// <summary>The rows of the layout, each exactly as wide as the cells that make it up.</summary>
	/// <remarks>
	/// The row count comes from the tallest column that does not repeat; a repeating column
	/// cycles its lines rather than extending the layout. A column that has run out of lines
	/// contributes its fill, so the rows below it stay aligned.
	/// </remarks>
	public static MarkupText[] Rows(ReadOnlySpan<LayoutCell> cells, LayoutOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (cells.Length == 0) return [];

		var blocks = Draw(cells);
		var rowCount = RowCount(cells, blocks);
		if (rowCount == 0) return [];

		var rows = new MarkupText[rowCount];
		var parts = new List<MarkupText>(cells.Length);
		for (var row = 0; row < rowCount; row++)
		{
			parts.Clear();
			for (var i = 0; i < cells.Length; i++)
			{
				switch (cells[i])
				{
					case LayoutColumn column:
						parts.Add(LineOf(blocks[i]!, column.Format, row));
						break;
					case LayoutSeparator separator when !Suppressed(cells, i):
						parts.Add(row == 0 || separator.Rows == SeparatorRows.EveryRow
							? separator.Text
							: MarkupText.Space.Repeat(separator.Text.DisplayWidth));
						break;
				}
			}
			rows[row] = MarkupText.Concat(parts.ToArray().AsSpan());
		}
		return rows;
	}

	/// <summary>The layout as one value, its rows joined by <see cref="LayoutOptions.RowSeparator"/>.</summary>
	public static MarkupText Render(ReadOnlySpan<LayoutCell> cells, LayoutOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var rows = Rows(cells, options);
		if (rows.Length == 0) return MarkupText.Empty;
		if (rows.Length == 1) return rows[0];

		var parts = new MarkupText[rows.Length * 2 - 1];
		for (var i = 0; i < rows.Length; i++)
		{
			if (i > 0) parts[i * 2 - 1] = options.RowSeparator;
			parts[i * 2] = rows[i];
		}
		return MarkupText.Concat(parts.AsSpan());
	}

	private static MarkupText[]?[] Draw(ReadOnlySpan<LayoutCell> cells)
	{
		var blocks = new MarkupText[]?[cells.Length];
		for (var i = 0; i < cells.Length; i++)
			if (cells[i] is LayoutColumn column)
				blocks[i] = column.Content.FormatColumn(column.Format);
		return blocks;
	}

	/// <summary>
	/// How many rows the layout has: the tallest non-repeating column, or the tallest of all of
	/// them when every column repeats and none of them can decide when to stop.
	/// </summary>
	private static int RowCount(ReadOnlySpan<LayoutCell> cells, MarkupText[]?[] blocks)
	{
		var driven = 0;
		var any = 0;
		for (var i = 0; i < cells.Length; i++)
		{
			if (cells[i] is not LayoutColumn column) continue;
			var height = blocks[i]!.Length;
			any = Math.Max(any, height);
			if (!column.Format.Repeat) driven = Math.Max(driven, height);
		}
		return driven > 0 ? driven : any;
	}

	private static MarkupText LineOf(MarkupText[] block, ColumnFormat format, int row)
	{
		if (block.Length == 0) return ColumnRenderer.Blank(format);
		if (format.Repeat) return block[row % block.Length];
		return row < block.Length ? block[row] : ColumnRenderer.Blank(format);
	}

	/// <summary>True when the column before this separator asked for no separator after it.</summary>
	private static bool Suppressed(ReadOnlySpan<LayoutCell> cells, int index) =>
		index > 0 && cells[index - 1] is LayoutColumn previous && previous.Format.NoSeparatorAfter;
}
