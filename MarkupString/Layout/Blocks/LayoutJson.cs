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
		writer.WritePropertyName("n");
		WriteNode(writer, layout.Root, registry);
	}

	public static IMarkup Read(JsonElement element, MarkupRegistry? registry)
	{
		var hash = String(element, "h") is { } hex && ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: 0UL;
		var root = element.TryGetProperty("n", out var node) ? ReadNode(node, registry) : Empty;
		return new LayoutMarkup(root, Int(element, "w") ?? 0, Bool(element, "fl"), Int(element, "l") ?? -1, hash);
	}

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
			default:
				return Empty;
		}
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
			if (!get(border).Equals(get(preset))) WriteText(writer, key, get(border), registry);
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

	private static bool Bool(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
