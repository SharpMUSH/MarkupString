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

		var plan = Plan.Build(cells);
		if (plan.RowCount == 0) return [];

		var rows = new MarkupText[plan.RowCount];
		var parts = new List<MarkupText>(cells.Length);
		for (var row = 0; row < plan.RowCount; row++)
		{
			parts.Clear();
			for (var i = 0; i < cells.Length; i++)
			{
				switch (cells[i])
				{
					case LayoutColumn column:
						var line = plan.LineOf(i, column, row);
						if (line is not null) parts.Add(line);
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
		return Suppress(cells, plan, rows);
	}

	/// <summary>
	/// Drops a final row on which every column is blank, when every column asked for that. Both
	/// halves matter: RhostMUSH suppresses the row only when the option is on every field.
	/// </summary>
	private static MarkupText[] Suppress(ReadOnlySpan<LayoutCell> cells, Plan plan, MarkupText[] rows)
	{
		if (rows.Length == 0) return rows;

		var columns = 0;
		foreach (var cell in cells)
		{
			if (cell is not LayoutColumn column) continue;
			if (!column.Format.SuppressBlankLast) return rows;
			columns++;
		}
		if (columns == 0 || !plan.AllExhaustedAt(cells, rows.Length - 1)) return rows;

		return rows[..^1];
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

	/// <summary>True when the column before this separator asked for no separator after it.</summary>
	private static bool Suppressed(ReadOnlySpan<LayoutCell> cells, int index) =>
		index > 0 && cells[index - 1] is LayoutColumn previous && previous.Format.NoSeparatorAfter;
}
