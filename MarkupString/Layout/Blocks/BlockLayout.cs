using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>How <see cref="BlockLayout"/> draws a tree as text.</summary>
public sealed record BlockRenderOptions
{
	/// <summary>Box drawing in whatever characters the borders name, every part shown.</summary>
	public static BlockRenderOptions Default { get; } = new();

	/// <summary>Borders in plain ASCII, for a client that cannot show anything else (<see cref="BorderStyle.ToAscii"/>).</summary>
	public bool AsciiOnly { get; init; }

	/// <summary>
	/// The content alone in reading order, for a screen reader: no borders, fill or columns, each
	/// title on a line of its own, each item after the one before it, a picture as its description.
	/// </summary>
	public bool Linear { get; init; }
}

/// <summary>
/// Lays a <see cref="LayoutNode"/> tree out as lines of text at a given width, and wraps the result in
/// the <see cref="LayoutMarkup"/> a richer format draws instead.
/// </summary>
public static class BlockLayout
{
	/// <summary>
	/// Lays <paramref name="root"/> out at <paramref name="width"/> and returns the lines, joined by
	/// newlines, under a <see cref="LayoutMarkup"/> that carries the tree.
	/// </summary>
	/// <param name="root">The tree.</param>
	/// <param name="width">The width in display cells.</param>
	/// <param name="fluid">Whether a reader may lay it out again at a width of its own (<see cref="Relayout"/>).</param>
	/// <param name="options">How to draw it; <see cref="BlockRenderOptions.Default"/> when null.</param>
	public static MarkupText Build(LayoutNode root, int width, bool fluid = false, BlockRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(root);
		var text = MarkupText.Join(MarkupText.NewLine, Lines(root, width, options));
		return text.Length == 0 ? text : MarkupText.Wrap(new LayoutMarkup(root, width, fluid, text.Text), text);
	}

	/// <summary>Lays <paramref name="node"/> out at <paramref name="width"/> display cells, one entry per line.</summary>
	public static IReadOnlyList<MarkupText> Lines(LayoutNode node, int width, BlockRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(node);
		var lines = new List<MarkupText>();
		Draw(node, Math.Max(1, width), options ?? BlockRenderOptions.Default, lines);
		return lines;
	}

	/// <summary>
	/// The tree <paramref name="content"/> was laid out from when the whole of it is one intact block,
	/// so a layout built from it nests that block rather than quoting its text; otherwise a
	/// <see cref="TextNode"/> holding it.
	/// </summary>
	public static LayoutNode AsNode(MarkupText content, Alignment alignment = Alignment.Left)
	{
		ArgumentNullException.ThrowIfNull(content);
		foreach (var region in BlockRegions.Find(content))
			if (region.Start == 0 && region.End == content.Length && region.Markup is LayoutMarkup layout && region.Intact)
				return layout.Root;
		return new TextNode(content, alignment);
	}

	/// <summary>
	/// <paramref name="content"/> as a sequence of nodes: each intact block standing on lines of its own
	/// as the tree it was laid out from, and the text between blocks as <see cref="TextNode"/>s, the line
	/// ending that separates text from a block dropped. How a builder that takes a body nests the
	/// blocks inside it — a box whose body holds columns, then a rule, then text.
	/// </summary>
	public static IReadOnlyList<LayoutNode> Nodes(MarkupText content, Alignment alignment = Alignment.Left)
	{
		ArgumentNullException.ThrowIfNull(content);
		var nodes = new List<LayoutNode>();
		var position = 0;
		foreach (var region in BlockRegions.Find(content))
		{
			if (region.Markup is not LayoutMarkup layout || !region.Intact || !region.Standalone) continue;
			AddText(content, position, region.Start, alignment, nodes);
			nodes.Add(layout.Root);
			position = region.End;
		}
		AddText(content, position, content.Length, alignment, nodes);
		return nodes;
	}

	private static void AddText(MarkupText content, int start, int end, Alignment alignment, List<LayoutNode> nodes)
	{
		var text = content.Text;
		if (start > 0 && start < end && text[start] == '\r') start++;
		if (start > 0 && start < end && text[start] == '\n') start++;
		if (end < text.Length && end > start && text[end - 1] == '\n') end--;
		if (end < text.Length && end > start && text[end - 1] == '\r') end--;
		if (end <= start) return;
		nodes.Add(new TextNode(content.Substring(start, end - start), alignment));
	}

	/// <summary>
	/// <paramref name="text"/> with every intact block laid out again under <paramref name="options"/>:
	/// a <see cref="LayoutMarkup.Fluid"/> block at <paramref name="width"/>, any other at its own width.
	/// A block that was cut, edited or shares a line with other text is left as it is.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <param name="width">The reader's width, or zero or less to keep every block's own.</param>
	/// <param name="options">How to draw the blocks.</param>
	public static MarkupText Relayout(MarkupText text, int width, BlockRenderOptions options)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(options);

		var edits = new List<(int Start, int Length, MarkupText Replacement)>();
		foreach (var region in BlockRegions.Find(text))
		{
			if (region.Markup is not LayoutMarkup layout || !region.Intact || !region.Standalone) continue;
			var target = layout.Fluid && width > 0 ? width : layout.Width;
			if (target == layout.Width && options == BlockRenderOptions.Default) continue;
			edits.Add((region.Start, region.End - region.Start, Build(layout.Root, target, layout.Fluid, options)));
		}

		for (var i = edits.Count - 1; i >= 0; i--)
			text = text.Replace(edits[i].Start, edits[i].Length, edits[i].Replacement);
		return text;
	}

	private static void Draw(LayoutNode node, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		switch (node)
		{
			case TextNode text:
				DrawText(text, width, options, lines);
				break;
			case StackNode stack:
				foreach (var child in stack.Children) Draw(child, width, options, lines);
				break;
			case RuleNode rule:
				DrawRule(rule, width, options, lines);
				break;
			case BoxNode box:
				DrawBox(box, width, options, lines);
				break;
			case FlexNode flex:
				DrawFlex(flex, width, options, lines);
				break;
			case FigureNode figure:
				DrawFigure(figure, width, options, lines);
				break;
		}
	}

	private static void DrawText(TextNode text, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		if (options.Linear)
		{
			lines.AddRange(text.Content.WrapLines(width));
			return;
		}
		lines.AddRange(text.Content.FormatColumn(Column(width, text.Alignment)));
	}

	private static void DrawRule(RuleNode rule, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		if (options.Linear)
		{
			if (rule.Title is { Length: > 0 } title) lines.Add(title);
			return;
		}
		var style = Style(rule.Border, options);
		lines.Add(Edge(MarkupText.Empty, style.Top, MarkupText.Empty, rule.Title, rule.TitleAlignment, style, width));
	}

	private static void DrawBox(BoxNode box, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		var children = box.Body is StackNode stack ? stack.Children : [box.Body];
		if (options.Linear)
		{
			if (box.Title is { Length: > 0 } title) lines.Add(title);
			foreach (var child in children) Draw(child, width, options, lines);
			return;
		}

		var style = Style(box.Border, options);
		var none = style.Name == BorderStyle.None.Name;
		var padding = MarkupText.Space.Repeat(Math.Max(0, box.Padding));
		var inner = Math.Max(1, width - style.Left.DisplayWidth - style.Right.DisplayWidth - padding.DisplayWidth * 2);

		if (!none || box.Title is { Length: > 0 })
			lines.Add(Edge(style.TopLeft, style.Top, style.TopRight, box.Title, box.TitleAlignment, style, width));

		var body = new List<MarkupText>();
		foreach (var child in children)
		{
			if (child is RuleNode rule)
			{
				if (none && rule.Title is not { Length: > 0 }) continue;
				var ruleStyle = Style(rule.Border, options);
				lines.Add(Edge(style.TeeLeft, ruleStyle.Top, style.TeeRight, rule.Title, rule.TitleAlignment, ruleStyle, width));
				continue;
			}

			body.Clear();
			Draw(child, inner, options, body);
			foreach (var line in body)
				lines.Add(MarkupText.Concat([style.Left, padding, Fit(line, inner), padding, style.Right]));
		}

		if (!none) lines.Add(Edge(style.BottomLeft, style.Bottom, style.BottomRight, null, Alignment.Center, style, width));
	}

	private static void DrawFlex(FlexNode flex, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		var items = flex.Items;
		if (items.IsDefaultOrEmpty) return;

		var widths = flex.Options.Vertical || options.Linear ? null : Share(flex, width);
		if (widths is null)
		{
			foreach (var item in items) Draw(item.Content, width, options, lines);
			return;
		}

		var columns = new List<MarkupText>[items.Length];
		var height = 0;
		for (var i = 0; i < items.Length; i++)
		{
			columns[i] = [];
			Draw(items[i].Content, widths[i], options, columns[i]);
			height = Math.Max(height, columns[i].Count);
		}

		var gap = flex.Options.Separator is { } separator ? separator.DisplayWidth : Math.Max(0, flex.Options.Gap);
		var spare = width - widths.Sum() - gap * (items.Length - 1);
		var (lead, between) = flex.Options.Justify switch
		{
			FlexJustify.End => (spare, 0),
			FlexJustify.Center => (spare / 2, 0),
			FlexJustify.Between when items.Length > 1 => (0, spare / (items.Length - 1)),
			_ => (0, 0),
		};

		var divider = flex.Options.Separator is not { } drawn ? MarkupText.Space.Repeat(gap)
			: options.AsciiOnly ? BorderStyle.AsciiText(drawn) ?? drawn
			: drawn;
		var parts = new List<MarkupText>(items.Length * 2 + 2);
		for (var row = 0; row < height; row++)
		{
			parts.Clear();
			if (lead > 0) parts.Add(MarkupText.Space.Repeat(lead));
			for (var i = 0; i < items.Length; i++)
			{
				if (i > 0)
				{
					if (between > 0) parts.Add(MarkupText.Space.Repeat(between));
					parts.Add(divider);
				}
				var offset = flex.Options.Align switch
				{
					FlexAlign.Center => (height - columns[i].Count) / 2,
					FlexAlign.End => height - columns[i].Count,
					_ => 0,
				};
				var index = row - offset;
				parts.Add(index >= 0 && index < columns[i].Count
					? Fit(columns[i][index], widths[i])
					: MarkupText.Space.Repeat(widths[i]));
			}
			lines.Add(Fit(MarkupText.Concat(parts.ToArray().AsSpan()), width));
		}
	}

	/// <summary>The width of each item, or null when they cannot sit side by side and must stack.</summary>
	private static int[]? Share(FlexNode flex, int width)
	{
		var items = flex.Items;
		var gap = flex.Options.Separator is { } separator ? separator.DisplayWidth : Math.Max(0, flex.Options.Gap);
		var available = width - gap * (items.Length - 1);
		if (available < items.Length) return null;

		var widths = new int[items.Length];
		var weights = new int[items.Length];
		var fixedSum = 0;
		for (var i = 0; i < items.Length; i++)
		{
			if (items[i].Basis.Resolve(available) is { } cells)
			{
				widths[i] = cells;
				fixedSum += cells;
				weights[i] = Math.Max(0, items[i].Grow);
			}
			else
			{
				weights[i] = Math.Max(1, items[i].Grow);
			}
		}

		var remaining = available - fixedSum;
		if (remaining < 0) return null;

		var autos = items.Select((item, i) => (item, i)).Where(p => p.item.Basis.Kind == BlockSizeKind.Auto).Select(p => p.i).ToArray();
		var growers = autos.Length > 0 ? autos : Enumerable.Range(0, items.Length).Where(i => weights[i] > 0).ToArray();
		var total = growers.Sum(i => weights[i]);
		if (total > 0 && remaining > 0)
		{
			var given = 0;
			foreach (var i in growers)
			{
				var share = remaining * weights[i] / total;
				widths[i] += share;
				given += share;
			}
			for (var k = 0; given < remaining; k = (k + 1) % growers.Length, given++) widths[growers[k]]++;
		}

		for (var i = 0; i < items.Length; i++)
			if (widths[i] < Math.Max(1, items[i].Min)) return null;
		return widths;
	}

	private static void DrawFigure(FigureNode figure, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		var description = figure.Image.Description is { Length: > 0 } d ? d : figure.Image.Source;
		if (options.Linear || figure.Art.Length == 0)
		{
			var stand = MarkupText.Plain(options.Linear ? $"Image: {description}" : $"[{description}]");
			if (options.Linear) lines.Add(stand);
			else lines.AddRange(stand.FormatColumn(Column(width, Alignment.Left)));
			if (figure.Beside is { } after) Draw(after, width, options, lines);
			return;
		}

		var art = figure.Art.Split("\n").Select(line => line.Text.EndsWith('\r') ? line.Substring(0, line.Length - 1) : line).ToArray();
		var artWidth = Math.Min(width, art.Max(line => line.DisplayWidth));
		var narrow = width - artWidth - Math.Max(0, figure.Gap);

		if (figure.Float == FigureFloat.None || figure.Beside is null || narrow < 8)
		{
			foreach (var line in art) lines.Add(Fit(line, width));
			if (figure.Beside is { } below) Draw(below, width, options, lines);
			return;
		}

		var gap = MarkupText.Space.Repeat(Math.Max(0, figure.Gap));
		var blankArt = MarkupText.Space.Repeat(artWidth);
		List<MarkupText> beside;
		if (figure.Beside is TextNode text)
		{
			// The text wraps at the narrow width beside the art and widens once it is past it.
			var flow = Column(narrow, text.Alignment) with { Indent = new Indent(0, art.Length, artWidth + gap.DisplayWidth) };
			beside = [.. text.Content.FormatColumn(flow)];
		}
		else
		{
			beside = [];
			Draw(figure.Beside, narrow, options, beside);
		}

		var rows = Math.Max(art.Length, beside.Count);
		for (var row = 0; row < rows; row++)
		{
			if (row >= art.Length && figure.Beside is TextNode)
			{
				lines.Add(Fit(beside[row], width));
				continue;
			}
			var picture = row < art.Length ? Fit(art[row], artWidth) : blankArt;
			var words = row < beside.Count ? Fit(beside[row], narrow) : MarkupText.Space.Repeat(narrow);
			lines.Add(figure.Float == FigureFloat.Left
				? MarkupText.Concat([picture, gap, words])
				: MarkupText.Concat([words, gap, picture]));
		}
	}

	/// <summary>A line across <paramref name="width"/>: two ends, a fill between, a title set into it.</summary>
	private static MarkupText Edge(
		MarkupText left, MarkupText fill, MarkupText right, MarkupText? title, Alignment alignment, BorderStyle style, int width)
	{
		var inner = Math.Max(0, width - left.DisplayWidth - right.DisplayWidth);
		var middle = title is { Length: > 0 }
			? MarkupText.Concat([style.TitleOpen, title, style.TitleClose])
			: MarkupText.Empty;
		fill = fill.Length == 0 ? MarkupText.Space : fill;
		if (alignment == Alignment.Center && middle.Length > 0 && middle.DisplayWidth < inner)
		{
			// The odd cell goes before the title, as a MUSH header has it: "+====< Title >===+".
			var before = (inner - middle.DisplayWidth + 1) / 2;
			// The fill is one pattern along the whole edge, so it reads on unbroken past the title.
			var edge = Run(fill, inner);
			var cut = DisplayWidth.IndexAtWidth(edge.Text, before);
			var resume = DisplayWidth.IndexAtWidth(edge.Text, before + middle.DisplayWidth);
			return MarkupText.Concat([left, edge.Substring(0, cut), middle, edge.Substring(resume), right]);
		}
		var format = new ColumnFormat { Width = inner, Alignment = alignment, Fill = fill };
		return MarkupText.Concat([left, middle.FormatColumn(format)[0], right]);
	}

	/// <summary><paramref name="width"/> cells of the pattern <paramref name="fill"/>.</summary>
	private static MarkupText Run(MarkupText fill, int width) =>
		width <= 0 ? MarkupText.Empty : MarkupText.Empty.FormatColumn(new ColumnFormat { Width = width, Fill = fill })[0];

	private static BorderStyle Style(BorderStyle style, BlockRenderOptions options) =>
		options.AsciiOnly ? style.ToAscii() : style;

	private static ColumnFormat Column(int width, Alignment alignment) =>
		new() { Width = width, Wrap = WrapMode.Word, Alignment = alignment };

	/// <summary><paramref name="line"/> made exactly <paramref name="width"/> cells wide.</summary>
	private static MarkupText Fit(MarkupText line, int width) =>
		line.DisplayWidth == width ? line : line.Pad(MarkupText.Space, width, PadType.Right, TruncationType.Truncate);
}

/// <summary>Finds the stretches of a text each block layer covers.</summary>
internal static class BlockRegions
{
	internal readonly record struct Region(int Start, int End, IBlockMarkup Markup, bool Intact, bool Standalone);

	/// <summary>
	/// Every block region in <paramref name="text"/>, outermost layers first where two start together,
	/// in order. A region is the run of adjacent runs that carry an equal layer.
	/// </summary>
	internal static IEnumerable<Region> Find(MarkupText text)
	{
		var runs = text.Runs;
		for (var i = 0; i < runs.Length; i++)
		{
			if (Outermost(runs[i].Markups) is not { } block) continue;
			if (i > 0 && runs[i - 1].End == runs[i].Start && Contains(runs[i - 1].Markups, block)) continue;
			var last = Extent(runs, i, block);
			yield return Describe(text, runs[i].Start, runs[last].End, block);
			i = last;
		}
	}

	/// <summary>The index of the last run of the region <paramref name="block"/> starts at run <paramref name="first"/>.</summary>
	internal static int Extent(ImmutableArray<Run> runs, int first, IMarkup block)
	{
		var last = first;
		while (last + 1 < runs.Length && runs[last + 1].Start == runs[last].End && Contains(runs[last + 1].Markups, block)) last++;
		return last;
	}

	internal static Region Describe(MarkupText text, int start, int end, IBlockMarkup block)
	{
		var content = text.Text.AsSpan();
		var standalone = (start == 0 || content[start - 1] == '\n')
			&& (end == content.Length || content[end] is '\n' or '\r');
		return new Region(start, end, block, block.Covers(content[start..end]), standalone);
	}

	internal static bool Contains(MarkupSet markups, IMarkup markup)
	{
		foreach (var item in markups)
			if (Equals(item, markup)) return true;
		return false;
	}

	private static IBlockMarkup? Outermost(MarkupSet markups)
	{
		for (var i = markups.Count - 1; i >= 0; i--)
			if (markups[i] is IBlockMarkup block) return block;
		return null;
	}
}
