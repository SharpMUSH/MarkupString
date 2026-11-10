using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>A title set into a line or a frame's edge: against its left end, its right end, or in its middle.</summary>
/// <param name="Text">The title.</param>
/// <param name="Side"><see cref="Alignment.Left"/>, <see cref="Alignment.Right"/>, or the middle for any other value.</param>
public sealed record EdgeTitle(MarkupText Text, Alignment Side = Alignment.Center)
{
	/// <summary>
	/// How important it is, as a <see cref="TableColumn.Priority"/> is: when the titles do not fit, the highest number is
	/// left out first. Unset, it follows the side: 1 on the left, 2 on the right, 3 in the middle.
	/// </summary>
	public int? Priority { get; init; }
}

/// <summary>A line across the width, with titles set into it.</summary>
/// <param name="Title">The title, or none.</param>
/// <remarks>
/// <see cref="Titles"/> add more, each at its own side: one left, one in the middle and one right, or two on the right.
/// Titles on one side sit in the order given, a cell of the line apart. When they do not all fit, the middle ones go
/// first, then the right, then the left, and on a side the one farthest from its end goes first.
/// </remarks>
public sealed record Rule(MarkupText? Title = null) : Block
{
	/// <inheritdoc/>
	public override Block MapText(Func<MarkupText, MarkupText> map) =>
		this with { Title = Title is null ? null : map(Title), Titles = BlockText.Map(Titles, map) };

	/// <summary>Whose horizontal edge and title brackets the line is drawn with; unset, the frame's around it or the theme's.</summary>
	public BorderStyle? Border { get; init; }

	/// <summary>Where the title sits along the line.</summary>
	public Alignment TitleAlignment { get; init; } = Alignment.Center;

	/// <summary>More titles, after <see cref="Title"/>, each at its own side.</summary>
	public ImmutableArray<EdgeTitle> Titles { get; init; } = [];

	/// <summary><see cref="Title"/> at <see cref="TitleAlignment"/>, then <see cref="Titles"/>; the empty ones left out.</summary>
	internal IReadOnlyList<EdgeTitle> EdgeTitles => BlockText.Titles(Title, TitleAlignment, Titles);

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		var style = context.Paint(context.Border(Border));
		lines.Add(BlockText.Edge(MarkupText.Empty, style.Top, MarkupText.Empty, BlockText.Painted(context, EdgeTitles), style, width));
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (BlockText.Read(EdgeTitles) is { } titles) lines.Add(titles);
	}
}

/// <summary>A frame around <paramref name="Body"/>, its title set into the top edge.</summary>
/// <param name="Body">What is framed. A <see cref="Stack"/>'s <see cref="Rule"/> children become dividers that meet the sides.</param>
/// <remarks>Usually made with <see cref="BlockExtensions.Bordered"/>.</remarks>
public sealed record Frame(Block Body) : Block
{
	/// <inheritdoc/>
	public override Block MapText(Func<MarkupText, MarkupText> map) =>
		this with
		{
			Body = Body.MapText(map),
			Title = Title is null ? null : map(Title),
			Titles = BlockText.Map(Titles, map),
			BottomTitles = BlockText.Map(BottomTitles, map),
		};

	/// <summary>The characters the frame is drawn with; unset, the theme's.</summary>
	public BorderStyle? Border { get; init; }

	/// <summary>The title in the top edge, or none.</summary>
	public MarkupText? Title { get; init; }

	/// <summary>Where the title sits along the edge.</summary>
	public Alignment TitleAlignment { get; init; } = Alignment.Center;

	/// <summary>More titles in the top edge, after <see cref="Title"/>, each at its own side, as a <see cref="Rule"/>'s.</summary>
	public ImmutableArray<EdgeTitle> Titles { get; init; } = [];

	/// <summary>Titles in the bottom edge, each at its own side, as a <see cref="Rule"/>'s: a page count, a footnote.</summary>
	public ImmutableArray<EdgeTitle> BottomTitles { get; init; } = [];

	/// <summary>Spaces between each side and the body.</summary>
	public int Padding { get; init; } = 1;

	private Block[] Parts => Body is Stack { Children.IsDefault: false } stack ? [.. stack.Children] : [Body];

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		var style = context.Paint(context.Border(Border));
		var none = style.Name == BorderStyle.None.Name;
		var padding = BlockText.Blank(Padding);
		var inner = Math.Max(1, width - style.Left.DisplayWidth - style.Right.DisplayWidth - padding.DisplayWidth * 2);

		// Each edge drawn (the top, a divider, the bottom), where it is, its fill, and the columns where a line
		// between panes meets it from above and from below; the junctions are set into it once the body is drawn.
		var edges = new List<(int Line, MarkupText Fill, HashSet<int> Up, HashSet<int> Down)>();
		var above = new HashSet<int>();
		var lastWasEdge = false;
		void AddEdge(MarkupText line, MarkupText fill)
		{
			edges.Add((lines.Count, fill, above, []));
			lines.Add(line);
			above = [];
			lastWasEdge = true;
		}

		var top = BlockText.Titles(Title, TitleAlignment, Titles);
		var bottom = BlockText.Titles(null, Alignment.Center, BottomTitles);
		if (!none || top.Count > 0)
			AddEdge(BlockText.Edge(style.TopLeft, style.Top, style.TopRight, BlockText.Painted(context, top), style, width), style.Top);

		var body = new List<MarkupText>();
		foreach (var child in Parts)
		{
			if (child is Rule rule)
			{
				var titles = rule.EdgeTitles;
				if (none && titles.Count == 0) continue;
				var ruleStyle = context.Paint(context.Border(rule.Border ?? Border));
				AddEdge(BlockText.Edge(style.TeeLeft, ruleStyle.Top, style.TeeRight, BlockText.Painted(context, titles), ruleStyle, width), ruleStyle.Top);
				continue;
			}

			body.Clear();
			var seams = !none && child is Flex { Join: true, Vertical: false } row ? Panes(context, row, style, width, body) : null;
			if (seams is null)
			{
				context.Draw(child, inner, body);
				for (var i = 0; i < body.Count; i++)
					body[i] = MarkupText.Concat([style.Left, padding, BlockText.Fit(body[i], inner), padding, style.Right]);
			}
			if (body.Count == 0) continue;
			if (lastWasEdge && seams is not null) edges[^1].Down.UnionWith(seams);
			foreach (var line in body) lines.Add(line);
			above = seams is null ? [] : [.. seams];
			lastWasEdge = false;
		}

		if (!none || bottom.Count > 0)
			AddEdge(BlockText.Edge(style.BottomLeft, style.Bottom, style.BottomRight, BlockText.Painted(context, bottom), style, width), style.Bottom);

		foreach (var (line, fill, up, down) in edges)
		{
			var text = lines[line];
			foreach (var column in up.Union(down).Order())
			{
				var glyph = up.Contains(column) && down.Contains(column) ? style.Cross : down.Contains(column) ? style.TeeTop : style.TeeBottom;
				text = Junction(text, column, glyph, fill);
			}
			lines[line] = text;
		}
	}

	/// <summary>
	/// <paramref name="row"/> drawn as panes across the frame, from side to side: a line of the frame's side
	/// between each two, and each pane's own <see cref="Rule"/>s as dividers meeting the lines and sides. The
	/// columns of the lines between panes, or null when the panes do not fit side by side.
	/// </summary>
	private List<int>? Panes(LayoutContext context, Flex row, BorderStyle style, int width, List<MarkupText> lines)
	{
		var seam = style.Left.DisplayWidth;
		var span = width - style.Left.DisplayWidth - style.Right.DisplayWidth;
		if (row.Items.IsDefaultOrEmpty || seam < 1 || row.Share(span, seam) is not { } widths) return null;
		widths[^1] += span - widths.Sum() - seam * (widths.Length - 1);

		var padding = BlockText.Blank(Padding);
		var panes = new List<(MarkupText Text, bool Divider)>[widths.Length];
		for (var i = 0; i < widths.Length; i++)
		{
			panes[i] = [];
			var content = row.Items[i] is Sized sized ? sized.Content : row.Items[i];
			var inner = Math.Max(1, widths[i] - Padding * 2);
			foreach (var part in content is Stack { Children.IsDefault: false } stack ? [.. stack.Children] : new[] { content })
			{
				if (part is Rule rule)
				{
					var ruleStyle = context.Paint(context.Border(rule.Border ?? Border));
					panes[i].Add((BlockText.Edge(MarkupText.Empty, ruleStyle.Top, MarkupText.Empty, BlockText.Painted(context, rule.EdgeTitles), ruleStyle, widths[i]), true));
					continue;
				}
				foreach (var line in context.Lines(part, inner))
					panes[i].Add((BlockText.Fit(MarkupText.Concat([padding, BlockText.Fit(line, inner), padding]), widths[i]), false));
			}
		}

		var height = panes.Max(pane => pane.Count);
		var offsets = panes.Select(pane => row.Align switch
		{
			FlexAlign.Center => (height - pane.Count) / 2,
			FlexAlign.End => height - pane.Count,
			_ => 0,
		}).ToArray();
		bool Divides(int pane, int line) => line - offsets[pane] is var at && at >= 0 && at < panes[pane].Count && panes[pane][at].Divider;

		var parts = new List<MarkupText>(widths.Length * 2 + 1);
		for (var line = 0; line < height; line++)
		{
			parts.Clear();
			parts.Add(Divides(0, line) ? style.TeeLeft : style.Left);
			for (var i = 0; i < widths.Length; i++)
			{
				if (i > 0)
				{
					var (left, right) = (Divides(i - 1, line), Divides(i, line));
					parts.Add(left && right ? style.Cross : left ? style.TeeRight : right ? style.TeeLeft : style.Left);
				}
				var at = line - offsets[i];
				parts.Add(at >= 0 && at < panes[i].Count ? panes[i][at].Text : BlockText.Blank(widths[i]));
			}
			parts.Add(Divides(widths.Length - 1, line) ? style.TeeRight : style.Right);
			lines.Add(MarkupText.Concat(parts.ToArray().AsSpan()));
		}

		var seams = new List<int>(widths.Length - 1);
		var column = style.Left.DisplayWidth;
		for (var i = 0; i < widths.Length - 1; i++)
		{
			column += widths[i];
			seams.Add(column);
			column += seam;
		}
		return seams;
	}

	/// <summary>
	/// <paramref name="line"/> with <paramref name="glyph"/> in place of the cell at <paramref name="column"/>, when
	/// that cell is part of the edge's <paramref name="fill"/> and not of a title set into it.
	/// </summary>
	private static MarkupText Junction(MarkupText line, int column, MarkupText glyph, MarkupText fill)
	{
		if (glyph.Length == 0 || glyph.DisplayWidth != 1) return line;
		var index = DisplayWidth.IndexAtWidth(line.Text, column);
		if (index >= line.Length || DisplayWidth.IndexAtWidth(line.Text, column + 1) != index + 1) return line;
		return fill.Text.Contains(line.Text[index]) ? line.Replace(index, 1, glyph) : line;
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (BlockText.Read(BlockText.Titles(Title, TitleAlignment, Titles)) is { } top) lines.Add(top);
		foreach (var child in Parts) context.Draw(child, width, lines);
		if (BlockText.Read(BlockText.Titles(null, Alignment.Center, BottomTitles)) is { } bottom) lines.Add(bottom);
	}
}
