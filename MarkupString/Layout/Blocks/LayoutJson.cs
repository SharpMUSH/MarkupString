using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace MarkupString.Layout;

/// <summary>
/// The JSON form of a <see cref="LayoutMarkup"/>. Core's own, like the shared vocabulary, because the
/// text inside the tree carries markup of every kind and has to be written with the registry the
/// whole text is.
/// </summary>
internal static class LayoutJson
{
	public const string Kind = "layout";

	public static void Write(Utf8JsonWriter writer, LayoutMarkup layout, MarkupRegistry? registry)
	{
		writer.WriteNumber("w", layout.Width);
		if (layout.Fluid) writer.WriteBoolean("fl", true);
		writer.WriteNumber("l", layout.Length);
		writer.WriteString("h", layout.Hash.ToString("x16", CultureInfo.InvariantCulture));
		writer.WriteString("id", layout.Id.ToString("x16", CultureInfo.InvariantCulture));
		writer.WritePropertyName("n");
		WriteNode(writer, layout.Root, registry);
	}

	public static IMarkup Read(JsonElement element, MarkupRegistry? registry)
	{
		var hash = Hex(element, "h");
		var root = element.TryGetProperty("n", out var node) ? ReadNode(node, registry) : Empty;
		return new LayoutMarkup(root, Int(element, "w") ?? 0, Bool(element, "fl"), Int(element, "l") ?? -1, hash, Hex(element, "id"));
	}

	private static ulong Hex(JsonElement element, string name) =>
		String(element, name) is { } hex && ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
			? value
			: 0UL;

	private static readonly LayoutNode Empty = new StackNode([]);

	private static void WriteNode(Utf8JsonWriter writer, LayoutNode node, MarkupRegistry? registry)
	{
		writer.WriteStartObject();
		switch (node)
		{
			case TextNode text:
				writer.WriteString("t", "text");
				WriteText(writer, "c", text.Content, registry);
				if (text.Alignment != Alignment.Left) writer.WriteString("a", Name(text.Alignment));
				break;
			case StackNode stack:
				writer.WriteString("t", "stack");
				writer.WriteStartArray("ch");
				foreach (var child in stack.Children) WriteNode(writer, child, registry);
				writer.WriteEndArray();
				break;
			case BoxNode box:
				writer.WriteString("t", "box");
				writer.WritePropertyName("b");
				WriteNode(writer, box.Body, registry);
				WriteBorder(writer, box.Border, registry);
				if (box.Title is { } boxTitle) WriteText(writer, "ti", boxTitle, registry);
				if (box.TitleAlignment != Alignment.Center) writer.WriteString("ta", Name(box.TitleAlignment));
				if (box.Padding != 1) writer.WriteNumber("p", box.Padding);
				break;
			case RuleNode rule:
				writer.WriteString("t", "rule");
				WriteBorder(writer, rule.Border, registry);
				if (rule.Title is { } ruleTitle) WriteText(writer, "ti", ruleTitle, registry);
				if (rule.TitleAlignment != Alignment.Center) writer.WriteString("ta", Name(rule.TitleAlignment));
				break;
			case FlexNode flex:
				writer.WriteString("t", "flex");
				writer.WriteStartArray("it");
				foreach (var item in flex.Items)
				{
					writer.WriteStartObject();
					writer.WritePropertyName("c");
					WriteNode(writer, item.Content, registry);
					if (item.Basis.Kind != BlockSizeKind.Auto) writer.WriteString("b", item.Basis.ToString());
					if (item.Min != 1) writer.WriteNumber("m", item.Min);
					if (item.Grow != 0) writer.WriteNumber("g", item.Grow);
					writer.WriteEndObject();
				}
				writer.WriteEndArray();
				var options = flex.Options;
				if (options.Gap != FlexOptions.Default.Gap) writer.WriteNumber("g", options.Gap);
				if (options.Separator is { } separator) WriteText(writer, "s", separator, registry);
				if (options.Justify != FlexJustify.Start) writer.WriteString("j", options.Justify.ToString().ToLowerInvariant());
				if (options.Align != FlexAlign.Start) writer.WriteString("a", options.Align.ToString().ToLowerInvariant());
				if (options.Vertical) writer.WriteBoolean("v", true);
				break;
			case FigureNode figure:
				writer.WriteString("t", "figure");
				writer.WriteString("s", figure.Image.Source);
				if (figure.Image.Description is { } description) writer.WriteString("d", description);
				if (figure.Image.Width is { } width) writer.WriteNumber("w", width);
				if (figure.Image.Height is { } height) writer.WriteNumber("h", height);
				if (figure.Art.Length > 0) WriteText(writer, "art", figure.Art, registry);
				if (figure.Float != FigureFloat.None) writer.WriteString("f", figure.Float.ToString().ToLowerInvariant());
				if (figure.Beside is { } beside)
				{
					writer.WritePropertyName("bd");
					WriteNode(writer, beside, registry);
				}
				if (figure.Gap != 2) writer.WriteNumber("g", figure.Gap);
				break;
			case FieldsNode fields:
				writer.WriteString("t", "fields");
				writer.WriteStartArray("f");
				foreach (var field in fields.Fields)
				{
					writer.WriteStartObject();
					WriteText(writer, "k", field.Label, registry);
					writer.WritePropertyName("v");
					WriteNode(writer, field.Value, registry);
					writer.WriteEndObject();
				}
				writer.WriteEndArray();
				var settings = fields.Options;
				if (settings.LabelAlignment != Alignment.Left) writer.WriteString("a", Name(settings.LabelAlignment));
				if (!Same(settings.Separator, FieldsOptions.Default.Separator)) WriteText(writer, "s", settings.Separator, registry);
				if (settings.Leader is { } leader) WriteText(writer, "ld", leader, registry);
				if (settings.Columns != 1) writer.WriteNumber("c", settings.Columns);
				if (settings.Gap != FieldsOptions.Default.Gap) writer.WriteNumber("g", settings.Gap);
				break;
			case TreeNode tree:
				writer.WriteString("t", "tree");
				WriteTreeItems(writer, "it", tree.Items, registry);
				writer.WriteStartObject("gd");
				writer.WriteString("n", tree.Guide.Name);
				var preset = TreeGuide.Preset(tree.Guide.Name) ?? TreeGuide.None;
				foreach (var (key, get) in GuidePieces)
					if (!Same(get(tree.Guide), get(preset))) WriteText(writer, key, get(tree.Guide), registry);
				writer.WriteEndObject();
				break;
			case GaugeNode gauge:
				writer.WriteString("t", "gauge");
				writer.WriteNumber("v", gauge.Value);
				writer.WriteNumber("m", gauge.Maximum);
				if (gauge.Label is { } gaugeLabel) WriteText(writer, "k", gaugeLabel, registry);
				var gaugeOptions = gauge.Options;
				var gaugeDefault = GaugeOptions.Default;
				if (!Same(gaugeOptions.Filled, gaugeDefault.Filled)) WriteText(writer, "f", gaugeOptions.Filled, registry);
				if (!Same(gaugeOptions.Empty, gaugeDefault.Empty)) WriteText(writer, "e", gaugeOptions.Empty, registry);
				if (!Same(gaugeOptions.Open, gaugeDefault.Open)) WriteText(writer, "o", gaugeOptions.Open, registry);
				if (!Same(gaugeOptions.Close, gaugeDefault.Close)) WriteText(writer, "c", gaugeOptions.Close, registry);
				if (gaugeOptions.Show != GaugeShow.Percent) writer.WriteString("sh", gaugeOptions.Show.ToString().ToLowerInvariant());
				if (gaugeOptions.BarWidth != 0) writer.WriteNumber("bw", gaugeOptions.BarWidth);
				break;
			case BulletsNode bullets:
				writer.WriteString("t", "bullets");
				writer.WriteStartArray("it");
				if (!bullets.Items.IsDefault)
					foreach (var item in bullets.Items) WriteNode(writer, item, registry);
				writer.WriteEndArray();
				if (bullets.Options.Style != BulletStyle.Bullet) writer.WriteString("st", bullets.Options.Style.ToString().ToLowerInvariant());
				if (bullets.Options.Marker is { } marker) WriteText(writer, "mk", marker, registry);
				if (bullets.Options.Start != 1) writer.WriteNumber("s", bullets.Options.Start);
				break;
			case GridNode grid:
				writer.WriteString("t", "grid");
				writer.WriteStartArray("it");
				if (!grid.Items.IsDefault)
					foreach (var item in grid.Items) MarkupTextSerializer.Write(writer, item, registry);
				writer.WriteEndArray();
				if (grid.Gap != 2) writer.WriteNumber("g", grid.Gap);
				if (grid.Across) writer.WriteBoolean("ac", true);
				break;
			case TableNode table:
				writer.WriteString("t", "table");
				writer.WriteStartArray("cols");
				if (!table.Columns.IsDefault)
				{
					foreach (var column in table.Columns)
					{
						writer.WriteStartObject();
						WriteText(writer, "h", column.Header, registry);
						if (column.Alignment != Alignment.Left) writer.WriteString("a", Name(column.Alignment));
						if (column.Min != 1) writer.WriteNumber("mn", column.Min);
						if (column.Max != 0) writer.WriteNumber("mx", column.Max);
						if (column.Priority != 1) writer.WriteNumber("p", column.Priority);
						if (!column.Wrap) writer.WriteBoolean("nw", true);
						writer.WriteEndObject();
					}
				}
				writer.WriteEndArray();
				writer.WriteStartArray("rows");
				if (!table.Rows.IsDefault)
				{
					foreach (var row in table.Rows)
					{
						writer.WriteStartArray();
						if (!row.IsDefault)
							foreach (var cell in row) WriteNode(writer, cell, registry);
						writer.WriteEndArray();
					}
				}
				writer.WriteEndArray();
				if (table.Options.Gap != TableOptions.Default.Gap) writer.WriteNumber("g", table.Options.Gap);
				if (table.Options.Separator is { } tableSeparator) WriteText(writer, "s", tableSeparator, registry);
				if (!Same(table.Options.HeaderRule, TableOptions.Default.HeaderRule)) WriteText(writer, "hr", table.Options.HeaderRule, registry);
				break;
			default:
				writer.WriteString("t", "stack");
				break;
		}
		writer.WriteEndObject();
	}

	private static LayoutNode ReadNode(JsonElement element, MarkupRegistry? registry)
	{
		if (element.ValueKind != JsonValueKind.Object) return Empty;
		switch (String(element, "t"))
		{
			case "text":
				return new TextNode(Text(element, "c", registry) ?? MarkupText.Empty, Align(element, "a", Alignment.Left));
			case "stack":
				return new StackNode(Nodes(element, "ch", registry));
			case "box":
				return new BoxNode(
					element.TryGetProperty("b", out var body) ? ReadNode(body, registry) : Empty,
					ReadBorder(element, registry),
					Text(element, "ti", registry),
					Align(element, "ta", Alignment.Center),
					Int(element, "p") ?? 1);
			case "rule":
				return new RuleNode(Text(element, "ti", registry), ReadBorder(element, registry), Align(element, "ta", Alignment.Center));
			case "flex":
				var items = ImmutableArray.CreateBuilder<FlexItem>();
				if (element.TryGetProperty("it", out var list) && list.ValueKind == JsonValueKind.Array)
				{
					foreach (var item in list.EnumerateArray())
					{
						if (item.ValueKind != JsonValueKind.Object) continue;
						items.Add(new FlexItem(
							item.TryGetProperty("c", out var content) ? ReadNode(content, registry) : Empty,
							BlockSize.TryParse(String(item, "b"), out var basis) ? basis : BlockSize.Auto,
							Int(item, "m") ?? 1,
							Int(item, "g") ?? 0));
					}
				}
				return new FlexNode(items.ToImmutable(), new FlexOptions
				{
					Gap = Int(element, "g") ?? FlexOptions.Default.Gap,
					Separator = Text(element, "s", registry),
					Justify = Enum.TryParse<FlexJustify>(String(element, "j"), ignoreCase: true, out var justify) ? justify : FlexJustify.Start,
					Align = Enum.TryParse<FlexAlign>(String(element, "a"), ignoreCase: true, out var align) ? align : FlexAlign.Start,
					Vertical = Bool(element, "v"),
				});
			case "figure":
				return new FigureNode(
					new ImageMarkup(String(element, "s") ?? string.Empty, String(element, "d"), Int(element, "w"), Int(element, "h")),
					Text(element, "art", registry) ?? MarkupText.Empty,
					Enum.TryParse<FigureFloat>(String(element, "f"), ignoreCase: true, out var side) ? side : FigureFloat.None,
					element.TryGetProperty("bd", out var beside) ? ReadNode(beside, registry) : null,
					Int(element, "g") ?? 2);
			case "fields":
				var fields = ImmutableArray.CreateBuilder<Field>();
				if (element.TryGetProperty("f", out var pairs) && pairs.ValueKind == JsonValueKind.Array)
				{
					foreach (var pair in pairs.EnumerateArray())
					{
						if (pair.ValueKind != JsonValueKind.Object) continue;
						fields.Add(new Field(
							Text(pair, "k", registry) ?? MarkupText.Empty,
							pair.TryGetProperty("v", out var value) ? ReadNode(value, registry) : Empty));
					}
				}
				return new FieldsNode(fields.ToImmutable(), new FieldsOptions
				{
					LabelAlignment = Align(element, "a", Alignment.Left),
					Separator = Text(element, "s", registry) ?? FieldsOptions.Default.Separator,
					Leader = Text(element, "ld", registry),
					Columns = Math.Clamp(Int(element, "c") ?? 1, 1, 64),
					Gap = Int(element, "g") ?? FieldsOptions.Default.Gap,
				});
			case "tree":
				return new TreeNode(ReadTreeItems(element, "it", registry, 0), ReadGuide(element, registry));
			case "gauge":
				var gaugeDefault = GaugeOptions.Default;
				return new GaugeNode(
					Double(element, "v") ?? 0,
					Double(element, "m") ?? 0,
					Text(element, "k", registry),
					new GaugeOptions
					{
						Filled = Text(element, "f", registry) ?? gaugeDefault.Filled,
						Empty = Text(element, "e", registry) ?? gaugeDefault.Empty,
						Open = Text(element, "o", registry) ?? gaugeDefault.Open,
						Close = Text(element, "c", registry) ?? gaugeDefault.Close,
						Show = Enum.TryParse<GaugeShow>(String(element, "sh"), ignoreCase: true, out var show) ? show : GaugeShow.Percent,
						BarWidth = Math.Clamp(Int(element, "bw") ?? 0, 0, 4096),
					});
			case "bullets":
				return new BulletsNode(Nodes(element, "it", registry), new BulletOptions
				{
					Style = Enum.TryParse<BulletStyle>(String(element, "st"), ignoreCase: true, out var style) ? style : BulletStyle.Bullet,
					Marker = Text(element, "mk", registry),
					Start = Int(element, "s") ?? 1,
				});
			case "grid":
				var gridItems = ImmutableArray.CreateBuilder<MarkupText>();
				if (element.TryGetProperty("it", out var gridList) && gridList.ValueKind == JsonValueKind.Array)
					foreach (var item in gridList.EnumerateArray())
						if (item.ValueKind == JsonValueKind.Object) gridItems.Add(MarkupTextSerializer.Read(item, registry));
				return new GridNode(gridItems.ToImmutable(), Math.Clamp(Int(element, "g") ?? 2, 0, 64), Bool(element, "ac"));
			case "table":
				var columns = ImmutableArray.CreateBuilder<TableColumn>();
				if (element.TryGetProperty("cols", out var columnList) && columnList.ValueKind == JsonValueKind.Array)
				{
					foreach (var column in columnList.EnumerateArray())
					{
						if (column.ValueKind != JsonValueKind.Object) continue;
						columns.Add(new TableColumn(
							Text(column, "h", registry) ?? MarkupText.Empty,
							Align(column, "a", Alignment.Left),
							Int(column, "mn") ?? 1,
							Int(column, "mx") ?? 0,
							Int(column, "p") ?? 1,
							!Bool(column, "nw")));
					}
				}
				var rows = ImmutableArray.CreateBuilder<ImmutableArray<LayoutNode>>();
				if (element.TryGetProperty("rows", out var rowList) && rowList.ValueKind == JsonValueKind.Array)
				{
					foreach (var row in rowList.EnumerateArray())
					{
						if (row.ValueKind != JsonValueKind.Array) continue;
						var cells = ImmutableArray.CreateBuilder<LayoutNode>();
						foreach (var cell in row.EnumerateArray()) cells.Add(ReadNode(cell, registry));
						rows.Add(cells.ToImmutable());
					}
				}
				return new TableNode(columns.ToImmutable(), rows.ToImmutable(), new TableOptions
				{
					Gap = Int(element, "g") ?? TableOptions.Default.Gap,
					Separator = Text(element, "s", registry),
					HeaderRule = Text(element, "hr", registry) ?? TableOptions.Default.HeaderRule,
				});
			default:
				return Empty;
		}
	}

	/// <summary>How deep a tree read back may nest, so a hostile payload cannot exhaust the stack.</summary>
	private const int MaxTreeDepth = 64;

	private static void WriteTreeItems(Utf8JsonWriter writer, string name, ImmutableArray<TreeItem> items, MarkupRegistry? registry)
	{
		writer.WriteStartArray(name);
		if (!items.IsDefault)
		{
			foreach (var item in items)
			{
				writer.WriteStartObject();
				writer.WritePropertyName("c");
				WriteNode(writer, item.Content, registry);
				if (!item.Children.IsDefaultOrEmpty) WriteTreeItems(writer, "ch", item.Children, registry);
				writer.WriteEndObject();
			}
		}
		writer.WriteEndArray();
	}

	private static ImmutableArray<TreeItem> ReadTreeItems(JsonElement element, string name, MarkupRegistry? registry, int depth)
	{
		if (depth > MaxTreeDepth || !element.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array) return [];
		var items = ImmutableArray.CreateBuilder<TreeItem>();
		foreach (var item in list.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object) continue;
			items.Add(new TreeItem(
				item.TryGetProperty("c", out var content) ? ReadNode(content, registry) : Empty,
				ReadTreeItems(item, "ch", registry, depth + 1)));
		}
		return items.ToImmutable();
	}

	private static readonly (string Key, Func<TreeGuide, MarkupText> Get)[] GuidePieces =
	[
		("b", g => g.Branch), ("l", g => g.Last), ("p", g => g.Pipe), ("e", g => g.Blank),
	];

	private static TreeGuide ReadGuide(JsonElement element, MarkupRegistry? registry)
	{
		if (!element.TryGetProperty("gd", out var guide) || guide.ValueKind != JsonValueKind.Object) return TreeGuide.Line;
		var style = TreeGuide.Preset(String(guide, "n") ?? string.Empty) ?? TreeGuide.None;
		MarkupText Piece(string key, MarkupText fallback) => Text(guide, key, registry) ?? fallback;
		return style with
		{
			Name = String(guide, "n") ?? style.Name,
			Branch = Piece("b", style.Branch),
			Last = Piece("l", style.Last),
			Pipe = Piece("p", style.Pipe),
			Blank = Piece("e", style.Blank),
		};
	}

	private static readonly (string Key, Func<BorderStyle, MarkupText> Get)[] Pieces =
	[
		("tl", b => b.TopLeft), ("t", b => b.Top), ("tr", b => b.TopRight), ("l", b => b.Left), ("r", b => b.Right),
		("bl", b => b.BottomLeft), ("b", b => b.Bottom), ("br", b => b.BottomRight), ("el", b => b.TeeLeft),
		("er", b => b.TeeRight), ("o", b => b.TitleOpen), ("c", b => b.TitleClose),
	];

	/// <summary>A border as its preset's name and the pieces that differ from that preset.</summary>
	private static void WriteBorder(Utf8JsonWriter writer, BorderStyle border, MarkupRegistry? registry)
	{
		writer.WriteStartObject("bs");
		writer.WriteString("n", border.Name);
		var preset = BorderStyle.Preset(border.Name) ?? BorderStyle.None;
		foreach (var (key, get) in Pieces)
			if (!Same(get(border), get(preset))) WriteText(writer, key, get(border), registry);
		writer.WriteEndObject();
	}

	private static BorderStyle ReadBorder(JsonElement element, MarkupRegistry? registry)
	{
		if (!element.TryGetProperty("bs", out var border) || border.ValueKind != JsonValueKind.Object) return BorderStyle.None;
		var style = BorderStyle.Preset(String(border, "n") ?? string.Empty) ?? BorderStyle.None;
		MarkupText Piece(string key, MarkupText fallback) => Text(border, key, registry) ?? fallback;
		return style with
		{
			TopLeft = Piece("tl", style.TopLeft),
			Top = Piece("t", style.Top),
			TopRight = Piece("tr", style.TopRight),
			Left = Piece("l", style.Left),
			Right = Piece("r", style.Right),
			BottomLeft = Piece("bl", style.BottomLeft),
			Bottom = Piece("b", style.Bottom),
			BottomRight = Piece("br", style.BottomRight),
			TeeLeft = Piece("el", style.TeeLeft),
			TeeRight = Piece("er", style.TeeRight),
			TitleOpen = Piece("o", style.TitleOpen),
			TitleClose = Piece("c", style.TitleClose),
		};
	}

	private static ImmutableArray<LayoutNode> Nodes(JsonElement element, string name, MarkupRegistry? registry)
	{
		if (!element.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array) return [];
		var nodes = ImmutableArray.CreateBuilder<LayoutNode>();
		foreach (var child in list.EnumerateArray()) nodes.Add(ReadNode(child, registry));
		return nodes.ToImmutable();
	}

	private static void WriteText(Utf8JsonWriter writer, string name, MarkupText text, MarkupRegistry? registry)
	{
		writer.WritePropertyName(name);
		MarkupTextSerializer.Write(writer, text, registry);
	}

	private static MarkupText? Text(JsonElement element, string name, MarkupRegistry? registry) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
			? MarkupTextSerializer.Read(value, registry)
			: null;

	private static string Name(Alignment alignment) => alignment.ToString().ToLowerInvariant();

	private static Alignment Align(JsonElement element, string name, Alignment fallback) =>
		Enum.TryParse<Alignment>(String(element, name), ignoreCase: true, out var alignment) ? alignment : fallback;

	private static string? String(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static int? Int(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
			? number
			: null;

	/// <summary>
	/// Whether <paramref name="piece"/> is the default it would be read back as: the same characters with
	/// no markup. <see cref="MarkupText.Equals(MarkupText)"/> compares plain text only, so a coloured piece
	/// would otherwise be taken for the default and its colour lost.
	/// </summary>
	private static bool Same(MarkupText piece, MarkupText fallback) =>
		ReferenceEquals(piece, fallback)
		|| (piece.Text == fallback.Text && piece.Runs.IsDefaultOrEmpty && fallback.Runs.IsDefaultOrEmpty);

	private static double? Double(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
			? number
			: null;

	private static bool Bool(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
