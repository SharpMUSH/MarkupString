namespace MarkupString.Layout;

/// <summary>The line arithmetic the built-in blocks share.</summary>
internal static class BlockText
{
	/// <summary><paramref name="line"/> made exactly <paramref name="width"/> cells wide.</summary>
	public static MarkupText Fit(MarkupText line, int width) =>
		line.DisplayWidth == width ? line : line.Pad(MarkupText.Space, width, PadType.Right, TruncationType.Truncate);

	/// <summary><paramref name="width"/> cells of the pattern <paramref name="fill"/>.</summary>
	public static MarkupText Run(MarkupText fill, int width) =>
		width <= 0 ? MarkupText.Empty : MarkupText.Empty.FormatColumn(new ColumnFormat { Width = width, Fill = fill })[0];

	/// <summary><paramref name="width"/> spaces.</summary>
	public static MarkupText Blank(int width) => MarkupText.Space.Repeat(Math.Max(0, width));

	/// <summary>A word-wrapping column.</summary>
	public static ColumnFormat Column(int width, Alignment alignment) =>
		new() { Width = width, Wrap = WrapMode.Word, Alignment = alignment };

	/// <summary>Adds every one of <paramref name="items"/>.</summary>
	public static void AddRange(this IList<MarkupText> lines, IEnumerable<MarkupText> items)
	{
		foreach (var item in items) lines.Add(item);
	}

	/// <summary>A line across <paramref name="width"/>: two ends, a fill between, a title set into it.</summary>
	public static MarkupText Edge(
		MarkupText left, MarkupText fill, MarkupText right, MarkupText? title, Alignment alignment, BorderStyle style, int width)
	{
		var inner = Math.Max(0, width - left.DisplayWidth - right.DisplayWidth);
		var middle = title is { Length: > 0 }
			? MarkupText.Concat([style.TitleOpen, title, style.TitleClose])
			: MarkupText.Empty;
		fill = fill.Length == 0 ? MarkupText.Space : fill;
		var aside = alignment is Alignment.Left or Alignment.Right;
		if (middle.Length > 0 && middle.DisplayWidth + (aside ? 2 : 1) <= inner)
		{
			// The odd cell goes before a centred title, as a MUSH header has it: "+====< Title >===+".
			// A title to one side stays a cell in from the corner: "+=< Title >======+".
			var before = alignment switch
			{
				Alignment.Left => 1,
				Alignment.Right => inner - middle.DisplayWidth - 1,
				_ => (inner - middle.DisplayWidth + 1) / 2,
			};
			// The fill is one pattern along the whole edge, so it reads on unbroken past the title.
			var edge = Run(fill, inner);
			var cut = DisplayWidth.IndexAtWidth(edge.Text, before);
			var resume = DisplayWidth.IndexAtWidth(edge.Text, before + middle.DisplayWidth);
			return MarkupText.Concat([left, edge.Substring(0, cut), middle, edge.Substring(resume), right]);
		}
		var format = new ColumnFormat { Width = inner, Alignment = alignment, Fill = fill };
		return MarkupText.Concat([left, middle.FormatColumn(format)[0], right]);
	}

	/// <summary>A number as a person writes it: <c>6</c>, <c>2.5</c>.</summary>
	public static string Figure(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
