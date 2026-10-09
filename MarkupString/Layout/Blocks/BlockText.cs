using System.Collections.Immutable;

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

	/// <summary><paramref name="title"/> at <paramref name="alignment"/>, then <paramref name="more"/>, leaving out the empty ones.</summary>
	public static IReadOnlyList<EdgeTitle> Titles(MarkupText? title, Alignment alignment, ImmutableArray<EdgeTitle> more)
	{
		var titles = new List<EdgeTitle>();
		if (title is { Length: > 0 }) titles.Add(new EdgeTitle(title, alignment));
		foreach (var extra in more.IsDefault ? [] : more)
			if (extra is { Text.Length: > 0 }) titles.Add(extra);
		return titles;
	}

	/// <summary><paramref name="titles"/> in the theme's title colour.</summary>
	public static IReadOnlyList<EdgeTitle> Painted(LayoutContext context, IReadOnlyList<EdgeTitle> titles) =>
		[.. titles.Select(title => title with { Text = context.Paint(theme => theme.TitleColor, title.Text) })];

	/// <summary>The titles as they are read, left to right, a comma between; null when there are none.</summary>
	public static MarkupText? Read(IReadOnlyList<EdgeTitle> titles) =>
		titles.Count == 0
			? null
			: MarkupText.Join(MarkupText.Plain(", "), Ordered(titles).Select(title => title.Text));

	/// <summary>Left, middle, right; in the order given within each.</summary>
	private static IEnumerable<EdgeTitle> Ordered(IEnumerable<EdgeTitle> titles) => titles.OrderBy(title => Side(title.Side) switch
	{
		Alignment.Left => 0,
		Alignment.Center => 1,
		_ => 2,
	});

	private static Alignment Side(Alignment side) => side is Alignment.Left or Alignment.Right ? side : Alignment.Center;

	/// <summary>A line across <paramref name="width"/>: two ends, a fill between, <paramref name="titles"/> set into it.</summary>
	/// <remarks>
	/// Titles on one side sit in order, a cell of the fill apart, the first on the left a cell in from the end and the
	/// last on the right likewise; the middle ones are centred, moved over only to keep a cell clear of the others.
	/// Those that do not fit are left out by <see cref="EdgeTitle.Priority"/>, highest first; among equals the middle
	/// goes first, then the right, then the left, on each side the one farthest from its end first. One alone that does
	/// not fit is cut, as a single title always was.
	/// </remarks>
	public static MarkupText Edge(
		MarkupText left, MarkupText fill, MarkupText right, IReadOnlyList<EdgeTitle> titles, BorderStyle style, int width)
	{
		var inner = Math.Max(0, width - left.DisplayWidth - right.DisplayWidth);
		fill = fill.Length == 0 ? MarkupText.Space : fill;
		var set = titles
			.Where(title => title.Text.Length > 0)
			.Select(title => (Side: Side(title.Side), Text: MarkupText.Concat([style.TitleOpen, title.Text, style.TitleClose]), Priority: Priority(title)))
			.ToList();
		while (set.Count > 1 && Needed(set) > inner) set.RemoveAt(Dropped(set));
		if (set.Count < 2) return Edge(left, fill, right, set.Count == 0 ? null : set[0].Text, set.Count == 0 ? Alignment.Center : set[0].Side, inner);

		var placed = new List<(int Start, MarkupText Text)>();
		var lefts = set.Where(title => title.Side == Alignment.Left).Select(title => title.Text).ToList();
		var middles = set.Where(title => title.Side == Alignment.Center).Select(title => title.Text).ToList();
		var rights = set.Where(title => title.Side == Alignment.Right).Select(title => title.Text).ToList();

		var from = 1;
		foreach (var text in lefts)
		{
			placed.Add((from, text));
			from += text.DisplayWidth + 1;
		}
		var rightStart = inner - 1 - Span(rights);
		var at = rightStart;
		foreach (var text in rights)
		{
			placed.Add((at, text));
			at += text.DisplayWidth + 1;
		}
		if (middles.Count > 0)
		{
			var span = Span(middles);
			var low = lefts.Count > 0 ? from : 0;
			var high = (rights.Count > 0 ? rightStart - 1 : inner) - span;
			var start = Math.Clamp((inner - span + 1) / 2, low, Math.Max(low, high));
			foreach (var text in middles)
			{
				placed.Add((start, text));
				start += text.DisplayWidth + 1;
			}
		}

		// The fill is one pattern along the whole edge, so it reads on unbroken between the titles.
		var edge = Run(fill, inner);
		var parts = new List<MarkupText> { left };
		var cell = 0;
		foreach (var (start, text) in placed.OrderBy(title => title.Start))
		{
			parts.Add(edge.Substring(DisplayWidth.IndexAtWidth(edge.Text, cell), DisplayWidth.IndexAtWidth(edge.Text, start) - DisplayWidth.IndexAtWidth(edge.Text, cell)));
			parts.Add(text);
			cell = start + text.DisplayWidth;
		}
		parts.Add(edge.Substring(DisplayWidth.IndexAtWidth(edge.Text, cell)));
		parts.Add(right);
		return MarkupText.Concat(parts);
	}

	/// <summary>Cells <paramref name="texts"/> take in a row, a cell apart.</summary>
	private static int Span(List<MarkupText> texts) => texts.Count == 0 ? 0 : texts.Sum(text => text.DisplayWidth) + texts.Count - 1;

	/// <summary>The fewest cells the titles fit in: each side a cell in from its end, a cell of fill between neighbours.</summary>
	private static int Needed(List<(Alignment Side, MarkupText Text, int Priority)> set)
	{
		var groups = new[] { Alignment.Left, Alignment.Center, Alignment.Right }
			.Select(side => set.Where(title => title.Side == side).Select(title => title.Text).ToList())
			.Where(group => group.Count > 0)
			.ToList();
		var ends = (set.Any(title => title.Side == Alignment.Left) ? 1 : 0) + (set.Any(title => title.Side == Alignment.Right) ? 1 : 0);
		// Middle titles alone still keep a cell of fill, as a lone centred title does.
		if (ends == 0) ends = 1;
		return groups.Sum(Span) + groups.Count - 1 + ends;
	}

	/// <summary>A title's priority: its own, else 1 on the left, 2 on the right, 3 in the middle.</summary>
	private static int Priority(EdgeTitle title) => title.Priority ?? Side(title.Side) switch
	{
		Alignment.Left => 1,
		Alignment.Right => 2,
		_ => 3,
	};

	/// <summary>
	/// The title to leave out next: the highest priority; among equals the middle, then the right, then the left, and
	/// on a side the one farthest from its end (the last in the middle or on the left, the first on the right).
	/// </summary>
	private static int Dropped(List<(Alignment Side, MarkupText Text, int Priority)> set)
	{
		var highest = set.Max(title => title.Priority);
		foreach (var side in new[] { Alignment.Center, Alignment.Right, Alignment.Left })
		{
			var index = side == Alignment.Right
				? set.FindIndex(title => title.Priority == highest && title.Side == side)
				: set.FindLastIndex(title => title.Priority == highest && title.Side == side);
			if (index >= 0) return index;
		}
		return set.Count - 1;
	}

	/// <summary>A line of <paramref name="inner"/> cells between two ends, one title set into it.</summary>
	private static MarkupText Edge(MarkupText left, MarkupText fill, MarkupText right, MarkupText? middle, Alignment alignment, int inner)
	{
		middle ??= MarkupText.Empty;
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
