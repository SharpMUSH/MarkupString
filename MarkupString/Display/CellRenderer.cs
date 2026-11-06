namespace MarkupString.Display;

/// <summary>
/// One cell of a <see cref="CellLayout"/>: a grapheme and how it looks. A wide grapheme takes two cells, the
/// second a continuation with empty <see cref="Text"/>.
/// </summary>
/// <param name="Text">The grapheme, or empty for the right half of a wide one.</param>
/// <param name="Style">How it looks.</param>
public readonly record struct Cell(string Text, DisplayStyle Style)
{
	/// <summary>Cells the grapheme takes: 1 or 2, or 0 for a continuation.</summary>
	public int Width { get; init; } = 1;

	/// <summary>
	/// For a cell of a picture (<see cref="DisplayStyle.Picture"/> set), its column in the picture, counted in
	/// cells from the picture's left edge; with the picture's <see cref="PictureRow.Row"/> it says which part of
	/// the picture to draw here.
	/// </summary>
	public int PictureColumn { get; init; }

	/// <summary>Whether this is the right half of a wide grapheme.</summary>
	public bool IsContinuation => Width == 0;
}

/// <summary>A point (a bell, a sound) and the cell it stands before.</summary>
/// <param name="Row">The row.</param>
/// <param name="Column">The column.</param>
/// <param name="Markup">The point.</param>
public readonly record struct CellPoint(int Row, int Column, IPointMarkup Markup);

/// <summary>One row of a <see cref="CellLayout"/>.</summary>
public sealed class CellRow
{
	internal CellRow(List<Cell> cells, bool wrapped)
	{
		Cells = cells;
		Wrapped = wrapped;
	}

	/// <summary>The cells, left to right. A row is as long as its text, never padded to the width.</summary>
	public IReadOnlyList<Cell> Cells { get; }

	/// <summary>Whether the next row continues this one because the text was wrapped, not because a line ended.</summary>
	public bool Wrapped { get; }
}

/// <summary>Text laid out on a grid of cells: what a cell renderer draws and hit tests against.</summary>
public sealed class CellLayout
{
	internal CellLayout(List<CellRow> rows, List<CellPoint> points, int columns)
	{
		Rows = rows;
		Points = points;
		Columns = columns;
	}

	/// <summary>The rows, top to bottom.</summary>
	public IReadOnlyList<CellRow> Rows { get; }

	/// <summary>The points, in order.</summary>
	public IReadOnlyList<CellPoint> Points { get; }

	/// <summary>The width it was laid out to, or 0 for unwrapped.</summary>
	public int Columns { get; }

	/// <summary>The cell at <paramref name="row"/> and <paramref name="column"/>, or null past the text.</summary>
	public Cell? At(int row, int column)
	{
		if (row < 0 || row >= Rows.Count || column < 0) return null;
		var cells = Rows[row].Cells;
		return column < cells.Count ? cells[column] : null;
	}

	/// <summary>
	/// What clicking the cell at <paramref name="row"/> and <paramref name="column"/> does, or null. The right half
	/// of a wide grapheme answers for the whole of it.
	/// </summary>
	public Interaction? InteractionAt(int row, int column)
	{
		if (At(row, column) is not { } cell) return null;
		if (cell.IsContinuation && column > 0) cell = Rows[row].Cells[column - 1];
		return cell.Style.Interaction;
	}
}

/// <summary>
/// Lays text out on a grid of cells, for an output that draws cells itself: a Skia window, a terminal emulator's
/// buffer. Graphemes are measured with <see cref="DisplayWidth"/>; tabs advance to the next stop.
/// </summary>
public static class CellRenderer
{
	/// <summary>Lays <paramref name="text"/> out.</summary>
	/// <param name="text">The text; a line feed in it starts a new row.</param>
	/// <param name="columns">The width to wrap at, or 0 not to wrap.</param>
	/// <param name="registry">The styles.</param>
	/// <param name="options">The output.</param>
	/// <param name="wordWrap">Wrap at the last space that fits, rather than at the last cell.</param>
	public static CellLayout Layout(MarkupText text, int columns, IStyleResolver registry, DisplayOptions options, bool wordWrap = false)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentOutOfRangeException.ThrowIfNegative(columns);

		var builder = new Builder(columns, Math.Max(1, options.TabWidth), wordWrap);
		var styles = new Dictionary<MarkupSet, DisplayStyle>(ReferenceEqualityComparer.Instance);
		var fold = options.Capabilities.Unicode == UnicodeLevel.Ascii;
		var narrow = options.Capabilities.Unicode != UnicodeLevel.Full;
		var source = text.Text;
		var at = 0;

		foreach (var run in text.Runs)
		{
			if (run.Start > at) builder.Write(Fold(source[at..run.Start], fold), DisplayStyle.Plain, narrow);

			var point = SpanRenderer.FindPoint(run.Markups);
			if (point is not null)
			{
				if (SpanRenderer.Shows(point, options.Capabilities)) builder.Point(point);
			}
			else
			{
				if (!styles.TryGetValue(run.Markups, out var style))
				{
					styles[run.Markups] = style = registry.Resolve(run.Markups, options);
				}

				builder.Write(Fold(source.Substring(run.Start, run.Length), fold), style, narrow);
			}

			at = run.End;
		}

		if (at < source.Length) builder.Write(Fold(source[at..], fold), DisplayStyle.Plain, narrow);
		return builder.Finish();
	}

	private static string Fold(string text, bool fold) => fold ? AsciiFold.Default.Fold(text) : text;

	private sealed class Builder(int columns, int tabWidth, bool wordWrap)
	{
		private readonly List<CellRow> _rows = [];
		private readonly List<CellPoint> _points = [];
		private List<Cell> _row = [];

		public void Point(IPointMarkup point) => _points.Add(new CellPoint(_rows.Count, _row.Count, point));

		public void Write(string text, DisplayStyle style, bool narrow)
		{
			foreach (var range in Graphemes.Enumerate(text))
			{
				var grapheme = text[range];
				switch (grapheme)
				{
					case "\n":
					case "\r\n":
						EndRow(wrapped: false);
						continue;
					case "\t":
						var stop = (_row.Count / tabWidth + 1) * tabWidth;
						if (columns > 0) stop = Math.Min(stop, columns);
						if (stop <= _row.Count) stop = _row.Count + 1;
						while (_row.Count < stop) Put(new Cell(" ", style));
						continue;
				}

				var width = DisplayWidth.Of(grapheme);
				if (width <= 0)
				{
					// A control character, or a mark with nothing before it on the row: it takes no cell.
					if (_row.Count > 0 && !char.IsControl(grapheme[0]) && _row[^1] is { IsContinuation: false } last)
					{
						_row[^1] = last with { Text = last.Text + grapheme };
					}

					continue;
				}

				if (width > 2) width = 2;
				if (narrow && width == 2)
				{
					grapheme = "?";
					width = 1;
				}

				var pictureColumn = 0;
				if (style.Picture is { } picture)
				{
					pictureColumn = _row.Count > 0 && _row[^1].Style.Picture is { } previous && ReferenceEquals(previous, picture)
						? _row[^1].PictureColumn + 1
						: picture.Row?.FirstColumn ?? 0;
				}

				if (columns > 0 && _row.Count + width > columns) Wrap(width);
				Put(new Cell(grapheme, style) { Width = width, PictureColumn = pictureColumn });
				if (width == 2) _row.Add(new Cell(string.Empty, style) { Width = 0, PictureColumn = pictureColumn + 1 });
			}
		}

		public CellLayout Finish()
		{
			if (_row.Count > 0 || _rows.Count == 0 || _points.Exists(point => point.Row == _rows.Count)) EndRow(wrapped: false);
			return new CellLayout(_rows, _points, columns);
		}

		private void Put(Cell cell)
		{
			if (columns > 0 && _row.Count >= columns) Wrap(cell.Width);
			_row.Add(cell);
		}

		private void Wrap(int width)
		{
			if (wordWrap)
			{
				var space = _row.FindLastIndex(cell => cell.Text == " " && cell.Style.Picture is null);
				if (space > 0)
				{
					var carried = _row.GetRange(space + 1, _row.Count - space - 1);
					_row.RemoveRange(space + 1, _row.Count - space - 1);
					EndRow(wrapped: true);
					_row.AddRange(carried);
					return;
				}
			}

			EndRow(wrapped: true);
		}

		private void EndRow(bool wrapped)
		{
			_rows.Add(new CellRow(_row, wrapped));
			_row = [];
		}
	}
}
