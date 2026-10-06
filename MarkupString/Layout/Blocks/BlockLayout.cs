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
			case FieldsNode fields:
				DrawFields(fields, width, options, lines);
				break;
			case TreeNode tree:
				DrawTree(tree, width, options, lines);
				break;
			case GaugeNode gauge:
				DrawGauge(gauge, width, options, lines);
				break;
			case BulletsNode bullets:
				DrawBullets(bullets, width, options, lines);
				break;
			case GridNode grid:
				DrawGrid(grid, width, options, lines);
				break;
			case TableNode table:
				DrawTable(table, width, options, lines);
				break;
		}
	}

	/// <summary>
	/// <paramref name="piece"/> for this client: as it is, or for an ASCII-only one its box-drawing
	/// characters translated, or <paramref name="fallback"/> when it holds anything else.
	/// </summary>
	private static MarkupText Shown(MarkupText piece, string fallback, BlockRenderOptions options) =>
		!options.AsciiOnly ? piece : BorderStyle.AsciiText(piece) ?? MarkupText.Plain(fallback);

	private static string Figure(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

	private static void DrawGauge(GaugeNode gauge, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		var settings = gauge.Options;
		var ratio = gauge.Maximum > 0 && double.IsFinite(gauge.Value) ? gauge.Value / gauge.Maximum : 0;
		var figures = settings.Show switch
		{
			GaugeShow.Value => $"{Figure(gauge.Value)}/{Figure(gauge.Maximum)}",
			GaugeShow.Percent => Figure(Math.Round(ratio * 100)) + "%",
			_ => string.Empty,
		};

		if (options.Linear)
		{
			var spoken = $"{Figure(gauge.Value)} of {Figure(gauge.Maximum)}" + (settings.Show == GaugeShow.Percent ? $" ({figures})" : string.Empty);
			lines.Add(gauge.Label is { Length: > 0 } named ? MarkupText.Concat([named, MarkupText.Plain(": " + spoken)]) : MarkupText.Plain(spoken));
			return;
		}

		var open = Shown(settings.Open, "[", options);
		var close = Shown(settings.Close, "]", options);
		var filled = Shown(settings.Filled, "#", options);
		var empty = Shown(settings.Empty, "-", options);
		var label = gauge.Label is { Length: > 0 } text ? MarkupText.Concat([text, MarkupText.Space]) : MarkupText.Empty;
		var after = figures.Length > 0 ? MarkupText.Plain(" " + figures) : MarkupText.Empty;

		var room = width - label.DisplayWidth - open.DisplayWidth - close.DisplayWidth - after.DisplayWidth;
		var bar = settings.BarWidth > 0 ? Math.Min(settings.BarWidth, Math.Max(1, room)) : Math.Max(1, room);
		var full = (int)Math.Round(bar * Math.Clamp(ratio, 0, 1));
		var fill = settings.Gradient is { IsEmpty: false } gradient
			? settings.Shade == GaugeShade.Value
				? gradient.Paint(Run(filled, full), Math.Clamp(ratio, 0, 1))
				: Shaded(Run(filled, full), gradient, bar)
			: Run(filled, full);
		lines.Add(Fit(MarkupText.Concat([label, open, fill, Run(empty, bar - full), close, after]), width));
	}

	private static void DrawBullets(BulletsNode bullets, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		var items = bullets.Items;
		if (items.IsDefaultOrEmpty) return;
		var settings = bullets.Options;
		var markers = Enumerable.Range(0, items.Length).Select(i => Marker(settings, i, options)).ToArray();
		var markerWidth = markers.Max(marker => marker.DisplayWidth);
		var numbered = settings.Style is BulletStyle.Number or BulletStyle.Alpha or BulletStyle.Roman;
		var gutter = markerWidth + (markerWidth > 0 ? 1 : 2);
		var hang = MarkupText.Space.Repeat(gutter);

		var drawn = new List<MarkupText>();
		for (var i = 0; i < items.Length; i++)
		{
			drawn.Clear();
			Draw(items[i], Math.Max(1, width - gutter), options, drawn);
			if (drawn.Count == 0) drawn.Add(MarkupText.Empty);
			var marker = markers[i].Pad(MarkupText.Space, markerWidth, numbered ? PadType.Left : PadType.Right, TruncationType.Truncate);
			for (var row = 0; row < drawn.Count; row++)
			{
				var lead = row == 0 ? MarkupText.Concat([marker, MarkupText.Space.Repeat(gutter - markerWidth)]) : hang;
				lines.Add(Fit(MarkupText.Concat([lead, drawn[row]]), width));
			}
		}
	}

	private static MarkupText Marker(BulletOptions settings, int index, BlockRenderOptions options)
	{
		var number = settings.Start + index;
		return settings.Style switch
		{
			BulletStyle.Bullet => options.AsciiOnly ? MarkupText.Plain("*") : MarkupText.Plain("•"),
			BulletStyle.Dash => MarkupText.Plain("-"),
			BulletStyle.Star => MarkupText.Plain("*"),
			BulletStyle.Number => MarkupText.Plain(number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "."),
			BulletStyle.Alpha => MarkupText.Plain(Letters(number) + "."),
			BulletStyle.Roman => MarkupText.Plain(Roman(number) + "."),
			BulletStyle.Custom when settings.Marker is { } marker => Shown(marker, "*", options),
			_ => MarkupText.Empty,
		};
	}

	/// <summary><c>a</c> to <c>z</c>, then <c>aa</c>, <c>ab</c>, ... as a spreadsheet names columns.</summary>
	internal static string Letters(int number)
	{
		if (number < 1) return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
		var letters = string.Empty;
		for (; number > 0; number = (number - 1) / 26) letters = (char)('a' + (number - 1) % 26) + letters;
		return letters;
	}

	/// <summary>Lower-case Roman numerals, for 1 to 3999; other numbers as digits.</summary>
	internal static string Roman(int number)
	{
		if (number is < 1 or > 3999) return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
		(int Value, string Numeral)[] table =
			[(1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")];
		var numeral = new System.Text.StringBuilder();
		foreach (var (value, text) in table)
			for (; number >= value; number -= value) numeral.Append(text);
		return numeral.ToString();
	}

	private static void DrawGrid(GridNode grid, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		var items = grid.Items;
		if (items.IsDefaultOrEmpty) return;
		if (options.Linear)
		{
			lines.AddRange(items);
			return;
		}

		var gap = Math.Max(0, grid.Gap);
		var cell = Math.Min(width, Math.Max(1, items.Max(item => item.DisplayWidth)));
		var columns = Math.Max(1, (width + gap) / (cell + gap));
		var rows = (items.Length + columns - 1) / columns;
		columns = (items.Length + rows - 1) / rows;
		var spacer = MarkupText.Space.Repeat(gap);
		var parts = new List<MarkupText>(columns * 2);
		for (var row = 0; row < rows; row++)
		{
			parts.Clear();
			for (var column = 0; column < columns; column++)
			{
				var index = grid.Across ? row * columns + column : column * rows + row;
				if (index >= items.Length) continue;
				if (column > 0) parts.Add(spacer);
				parts.Add(Fit(items[index], cell));
			}
			lines.Add(Fit(MarkupText.Concat(parts.ToArray().AsSpan()), width));
		}
	}

	private static void DrawTable(TableNode table, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		var columns = table.Columns;
		if (columns.IsDefaultOrEmpty) return;
		var rows = table.Rows.IsDefault ? [] : table.Rows;
		var settings = table.Options;

		LayoutNode Cell(ImmutableArray<LayoutNode> row, int column) =>
			!row.IsDefault && column < row.Length
				? row[column] is TextNode text ? text with { Alignment = columns[column].Alignment } : row[column]
				: new TextNode(MarkupText.Empty, columns[column].Alignment);

		var widths = options.Linear ? null : TableWidths(table, width, Cell);
		if (widths is null)
		{
			// Each row as a card of labelled values, the way a phone shows a table too wide for it.
			for (var r = 0; r < rows.Length; r++)
			{
				if (r > 0 && !options.Linear) lines.Add(MarkupText.Empty);
				// A card's values all start under their labels, whatever side their column kept them to.
				var fields = Enumerable.Range(0, columns.Length).Select(c => new Field(columns[c].Header,
					Cell(rows[r], c) is TextNode text ? text with { Alignment = Alignment.Left } : Cell(rows[r], c)));
				DrawFields(new FieldsNode([.. fields], FieldsOptions.Default), width, options, lines);
			}
			return;
		}

		var shown = Enumerable.Range(0, columns.Length).Where(c => widths[c] > 0).ToArray();
		var divider = settings.Separator is { } drawn ? Shown(drawn, " | ", options) : MarkupText.Space.Repeat(Math.Max(0, settings.Gap));
		var tableWidth = shown.Sum(c => widths[c]) + divider.DisplayWidth * (shown.Length - 1);

		MarkupText Join(IEnumerable<MarkupText> cells) =>
			Fit(MarkupText.Join(divider, cells), width);

		lines.Add(Join(shown.Select(c => Fit(columns[c].Header.FormatColumn(Column(widths[c], columns[c].Alignment))[0], widths[c]))));
		if (settings.HeaderRule is { Length: > 0 } rule) lines.Add(Fit(Run(Shown(rule, "-", options), tableWidth), width));

		var cellLines = new List<MarkupText>[columns.Length];
		foreach (var row in rows)
		{
			var height = 1;
			foreach (var c in shown)
			{
				cellLines[c] = [];
				Draw(Cell(row, c), widths[c], options, cellLines[c]);
				if (!columns[c].Wrap && cellLines[c].Count > 1) cellLines[c].RemoveRange(1, cellLines[c].Count - 1);
				height = Math.Max(height, cellLines[c].Count);
			}
			for (var line = 0; line < height; line++)
				lines.Add(Join(shown.Select(c => line < cellLines[c].Count ? Fit(cellLines[c][line], widths[c]) : MarkupText.Space.Repeat(widths[c]))));
		}
	}

	/// <summary>
	/// The width of each column, zero for one left out, or null when not even one column fits. Each
	/// column asks for its widest cell; too wide, the columns that wrap give way, widest first, down to
	/// their least widths, and then the least important column is left out. A column that does not wrap
	/// never gives way: it is shown whole or not at all.
	/// </summary>
	private static int[]? TableWidths(TableNode table, int width, Func<ImmutableArray<LayoutNode>, int, LayoutNode> cell)
	{
		var columns = table.Columns;
		var rows = table.Rows.IsDefault ? [] : table.Rows;
		var gap = table.Options.Separator?.DisplayWidth ?? Math.Max(0, table.Options.Gap);

		var natural = new int[columns.Length];
		for (var c = 0; c < columns.Length; c++)
		{
			var widest = columns[c].Header.DisplayWidth;
			foreach (var row in rows)
				foreach (var line in Lines(cell(row, c) is TextNode text ? text with { Alignment = Alignment.Left } : cell(row, c), Math.Max(width, 1)))
					widest = Math.Max(widest, line.Trim(TrimType.TrimEnd).DisplayWidth);
			if (columns[c].Max > 0) widest = Math.Min(widest, columns[c].Max);
			natural[c] = Math.Max(Math.Max(1, columns[c].Min), widest);
		}

		var active = Enumerable.Range(0, columns.Length).ToList();
		while (active.Count > 0)
		{
			var widths = new int[columns.Length];
			foreach (var c in active) widths[c] = natural[c];
			var overflow = active.Sum(c => widths[c]) + gap * (active.Count - 1) - width;
			while (overflow > 0)
			{
				var widest = active.Where(c => columns[c].Wrap && widths[c] > Math.Max(1, columns[c].Min))
					.OrderByDescending(c => widths[c]).ThenByDescending(c => c).FirstOrDefault(-1);
				if (widest < 0) break;
				widths[widest]--;
				overflow--;
			}
			if (overflow <= 0) return widths;

			// Leave out the least important column, the rightmost of those tied.
			var least = active.OrderByDescending(c => columns[c].Priority).ThenByDescending(c => c).First();
			if (active.Count == 1) return null;
			active.Remove(least);
		}
		return null;
	}

	/// <summary>The narrowest a value column may be before each label goes over its value instead.</summary>
	private const int MinFieldValue = 10;

	private static void DrawFields(FieldsNode fields, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		var all = fields.Fields;
		if (all.IsDefaultOrEmpty) return;
		var settings = fields.Options;

		if (settings.Columns > 1 && all.Length > 1 && !options.Linear)
		{
			// Dealt down each column first, the way a reader expects to go on reading.
			var count = Math.Min(settings.Columns, all.Length);
			var per = (all.Length + count - 1) / count;
			var single = settings with { Columns = 1 };
			var items = all.Chunk(per)
				.Select(chunk => new FlexItem(new FieldsNode([.. chunk], single), BlockSize.Auto, MinWidth(chunk, single)))
				.ToArray();
			DrawFlex(new FlexNode([.. items], FlexOptions.Default with { Gap = Math.Max(0, settings.Gap) }), width, options, lines);
			return;
		}

		var separator = settings.Separator;
		if (options.Linear)
		{
			var value = new List<MarkupText>();
			foreach (var field in all)
			{
				value.Clear();
				Draw(field.Value, width, options, value);
				var first = value.Count > 0 ? value[0] : MarkupText.Empty;
				lines.Add(field.Label.Length == 0 ? first : MarkupText.Concat([field.Label, separator, first]));
				for (var i = 1; i < value.Count; i++) lines.Add(value[i]);
			}
			return;
		}

		var labelWidth = Math.Min(all.Max(field => field.Label.DisplayWidth), Math.Max(1, width / 2));
		var valueWidth = width - labelWidth - separator.DisplayWidth;
		var drawn = new List<MarkupText>();
		if (valueWidth < Math.Min(MinFieldValue, width))
		{
			// Too narrow to sit side by side: each label on its own line, its value indented under it.
			var indent = MarkupText.Space.Repeat(Math.Min(2, width - 1));
			foreach (var field in all)
			{
				if (field.Label.Length > 0)
					lines.AddRange(MarkupText.Concat([field.Label, separator.Trim(TrimType.TrimEnd)]).FormatColumn(Column(width, Alignment.Left)));
				drawn.Clear();
				Draw(field.Value, width - indent.DisplayWidth, options, drawn);
				foreach (var line in drawn) lines.Add(MarkupText.Concat([indent, line]));
			}
			return;
		}

		// Without a leader the separator rides on the label, "Sex:" then the gap to the value; with
		// one, the leader fills from the label to the separator, "Sex.....: ".
		var alignment = settings.LabelAlignment == Alignment.Right ? Alignment.Right : Alignment.Left;
		var head = separator.Trim(TrimType.TrimEnd);
		var leader = settings.Leader is { Length: > 0 } pattern ? pattern : null;
		var labelColumn = labelWidth + separator.DisplayWidth;
		var blankLabel = MarkupText.Space.Repeat(labelColumn);
		foreach (var field in all)
		{
			var label = new List<MarkupText>();
			if (field.Label.Length > 0 && leader is not null)
			{
				var rows = field.Label.FormatColumn(Column(labelWidth, alignment) with { Fill = leader });
				for (var i = 0; i < rows.Length; i++)
					label.Add(MarkupText.Concat([Fit(rows[i], labelWidth), i == 0 ? separator : MarkupText.Space.Repeat(separator.DisplayWidth)]));
			}
			else if (field.Label.Length > 0)
			{
				var rows = MarkupText.Concat([field.Label, head]).FormatColumn(Column(labelWidth + head.DisplayWidth, alignment));
				foreach (var row in rows) label.Add(Fit(row, labelColumn));
			}

			drawn.Clear();
			Draw(field.Value, valueWidth, options, drawn);
			var height = Math.Max(Math.Max(label.Count, drawn.Count), 1);
			for (var row = 0; row < height; row++)
			{
				lines.Add(MarkupText.Concat([
					row < label.Count ? label[row] : blankLabel,
					row < drawn.Count ? Fit(drawn[row], valueWidth) : MarkupText.Space.Repeat(valueWidth)]));
			}
		}
	}

	/// <summary>The fewest cells a column of <paramref name="fields"/> needs to keep its labels beside its values.</summary>
	private static int MinWidth(IEnumerable<Field> fields, FieldsOptions options) =>
		fields.Max(field => field.Label.DisplayWidth) + options.Separator.DisplayWidth + MinFieldValue;

	private static void DrawTree(TreeNode tree, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		if (tree.Items.IsDefaultOrEmpty) return;
		var guide = options.AsciiOnly ? tree.Guide.ToAscii() : tree.Guide;
		foreach (var item in tree.Items) DrawTreeItem(item, MarkupText.Empty, null, guide, width, options, lines);
	}

	/// <summary>
	/// One item at <paramref name="prefix"/>: its first line after the branch or last guide (none at the
	/// top level), the rest after the guide that carries its level on, then its children one level in.
	/// </summary>
	private static void DrawTreeItem(TreeItem item, MarkupText prefix, bool? last, TreeGuide guide, int width, BlockRenderOptions options, List<MarkupText> lines)
	{
		if (options.Linear)
		{
			// A reader hears the levels as indentation, not as line drawing.
			var depth = prefix.DisplayWidth + (last is null ? 0 : 2);
			var spoken = MarkupText.Space.Repeat(depth);
			var heard = new List<MarkupText>();
			Draw(item.Content, Math.Max(1, width - depth), options, heard);
			foreach (var line in heard) lines.Add(MarkupText.Concat([spoken, line]));
			if (!item.Children.IsDefaultOrEmpty)
				for (var i = 0; i < item.Children.Length; i++)
					DrawTreeItem(item.Children[i], MarkupText.Space.Repeat(depth), i == item.Children.Length - 1, guide, width, options, lines);
			return;
		}

		var head = last switch { null => MarkupText.Empty, true => guide.Last, false => guide.Branch };
		var carry = last switch { null => MarkupText.Empty, true => guide.Blank, false => guide.Pipe };
		var hasChildren = !item.Children.IsDefaultOrEmpty;
		var lead = MarkupText.Concat([prefix, head]);
		var content = new List<MarkupText>();
		Draw(item.Content, Math.Max(1, width - lead.DisplayWidth), options, content);
		for (var i = 0; i < content.Count; i++)
		{
			var line = MarkupText.Concat([i == 0 ? lead : MarkupText.Concat([prefix, carry]), content[i]]);
			lines.Add(Fit(line, width));
		}

		if (!hasChildren) return;
		var inner = MarkupText.Concat([prefix, carry]);
		for (var i = 0; i < item.Children.Length; i++)
			DrawTreeItem(item.Children[i], inner, i == item.Children.Length - 1, guide, width, options, lines);
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

	/// <summary>Each character of <paramref name="text"/> in the colour at its cell's place along a bar <paramref name="bar"/> cells wide.</summary>
	private static MarkupText Shaded(MarkupText text, ColorGradient gradient, int bar)
	{
		var cell = 0;
		var pieces = new List<MarkupText>();
		foreach (var character in text.EnumerateGraphemes())
		{
			pieces.Add(gradient.Paint(character, bar > 1 ? cell / (double)(bar - 1) : 0));
			cell += Math.Max(1, character.DisplayWidth);
		}
		return MarkupText.Concat(pieces);
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
