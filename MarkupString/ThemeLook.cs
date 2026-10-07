using System.Text;
using System.Text.Json;
using MarkupString.Layout;

namespace MarkupString;

/// <summary>
/// The shapes a theme draws with, beside its colours: the border and the ornaments round a title, tree
/// guides, the bullet, a gauge's pieces, the separator after a label and the rule under table headings.
/// Each is <see langword="null"/> until set, and an unset one is the layout's own. A reader whose client
/// has only ASCII gets the ASCII form of a border or guide, and the usual ASCII piece for anything else.
/// </summary>
public sealed record ThemeLook
{
	/// <summary>A border preset's name (<see cref="BorderStyle.Presets"/>).</summary>
	public string? Border { get; init; }

	/// <summary>The top-left corner, one column wide, in place of the border's own.</summary>
	public string? TopLeft { get; init; }

	/// <summary>The top-right corner.</summary>
	public string? TopRight { get; init; }

	/// <summary>The bottom-left corner.</summary>
	public string? BottomLeft { get; init; }

	/// <summary>The bottom-right corner.</summary>
	public string? BottomRight { get; init; }

	/// <summary>The top and bottom edges and a rule's line, a pattern repeated along them: <c>"━╸"</c>.</summary>
	public string? Edge { get; init; }

	/// <summary>The left and right sides, one column wide.</summary>
	public string? Side { get; init; }

	/// <summary>Before a title set into a box's edge or a rule, joining it to the line: <c>"╡ ❖ "</c>.</summary>
	public string? TitleOpen { get; init; }

	/// <summary>After the title: <c>" ❖ ╞"</c>.</summary>
	public string? TitleClose { get; init; }

	/// <summary>A tree guide preset's name (<see cref="TreeGuide.Presets"/>).</summary>
	public string? Guide { get; init; }

	/// <summary>A bulleted list's marker.</summary>
	public string? Bullet { get; init; }

	/// <summary>Before a gauge's bar.</summary>
	public string? GaugeOpen { get; init; }

	/// <summary>A gauge's filled part, repeated.</summary>
	public string? GaugeFilled { get; init; }

	/// <summary>A gauge's empty part, repeated.</summary>
	public string? GaugeEmpty { get; init; }

	/// <summary>After a gauge's bar.</summary>
	public string? GaugeClose { get; init; }

	/// <summary>Between a label and its value.</summary>
	public string? Separator { get; init; }

	/// <summary>The line under a table's headings, repeated.</summary>
	public string? Rule { get; init; }

	/// <summary>This look over <paramref name="below"/>: what this sets, and what it leaves unset from there.</summary>
	public ThemeLook Over(ThemeLook? below) => below is null ? this : new()
	{
		Border = Border ?? below.Border,
		TopLeft = TopLeft ?? below.TopLeft,
		TopRight = TopRight ?? below.TopRight,
		BottomLeft = BottomLeft ?? below.BottomLeft,
		BottomRight = BottomRight ?? below.BottomRight,
		Edge = Edge ?? below.Edge,
		Side = Side ?? below.Side,
		TitleOpen = TitleOpen ?? below.TitleOpen,
		TitleClose = TitleClose ?? below.TitleClose,
		Guide = Guide ?? below.Guide,
		Bullet = Bullet ?? below.Bullet,
		GaugeOpen = GaugeOpen ?? below.GaugeOpen,
		GaugeFilled = GaugeFilled ?? below.GaugeFilled,
		GaugeEmpty = GaugeEmpty ?? below.GaugeEmpty,
		GaugeClose = GaugeClose ?? below.GaugeClose,
		Separator = Separator ?? below.Separator,
		Rule = Rule ?? below.Rule,
	};

	/// <summary>The look as a layout theme's pieces, its colours unset.</summary>
	public LayoutTheme ToTheme()
	{
		return new LayoutTheme
		{
			Border = Border is null && TitleOpen is null && TitleClose is null && TopLeft is null && Edge is null && Side is null ? null : BorderOf(BorderStyle.Preset(Border ?? "single") ?? BorderStyle.Single),
			Guide = Guide is null ? null : TreeGuide.Preset(Guide),
			Bullet = Piece(Bullet),
			GaugeOpen = Piece(GaugeOpen),
			GaugeFilled = Piece(GaugeFilled),
			GaugeEmpty = Piece(GaugeEmpty),
			GaugeClose = Piece(GaugeClose),
			FieldSeparator = Piece(Separator),
			HeaderRule = Piece(Rule),
		};
	}

	private BorderStyle BorderOf(BorderStyle preset) => preset with
	{
		TitleOpen = TitleOpen is null ? preset.TitleOpen : MarkupText.Plain(TitleOpen),
		TitleClose = TitleClose is null ? preset.TitleClose : MarkupText.Plain(TitleClose),
		TopLeft = TopLeft is null ? preset.TopLeft : MarkupText.Plain(TopLeft),
		TopRight = TopRight is null ? preset.TopRight : MarkupText.Plain(TopRight),
		BottomLeft = BottomLeft is null ? preset.BottomLeft : MarkupText.Plain(BottomLeft),
		BottomRight = BottomRight is null ? preset.BottomRight : MarkupText.Plain(BottomRight),
		Top = Edge is null ? preset.Top : MarkupText.Plain(Edge),
		Bottom = Edge is null ? preset.Bottom : MarkupText.Plain(Edge),
		Left = Side is null ? preset.Left : MarkupText.Plain(Side),
		Right = Side is null ? preset.Right : MarkupText.Plain(Side),
	};

	private static MarkupText? Piece(string? text) => text is null ? null : MarkupText.Plain(text);

	/// <summary>
	/// Reads a look from JSON: <c>border</c> and <c>guide</c> as preset names, <c>corners</c> as
	/// <c>[top-left, top-right, bottom-left, bottom-right]</c> and <c>side</c>, each one column wide,
	/// <c>edge</c> as a pattern, <c>title</c> as <c>[open, close]</c>, <c>gauge</c> as
	/// <c>[open, filled, empty, close]</c>, and <c>bullet</c>, <c>separator</c> and <c>rule</c> as text.
	/// No piece may hold a control character, and every piece but the separator takes up some width.
	/// </summary>
	public static bool TryRead(JsonElement element, out ThemeLook? look, out string? error)
	{
		look = null;
		error = null;
		if (element.ValueKind != JsonValueKind.Object)
		{
			error = "look is an object";
			return false;
		}

		var result = new ThemeLook();
		foreach (var property in element.EnumerateObject())
		{
			var value = property.Value;
			switch (property.Name)
			{
				case "border":
					if (value.ValueKind != JsonValueKind.String || BorderStyle.Preset(value.GetString()!) is null)
					{
						error = "border is one of " + string.Join(", ", BorderStyle.Presets.Select(preset => preset.Name));
						return false;
					}
					result = result with { Border = value.GetString() };
					break;
				case "guide":
					if (value.ValueKind != JsonValueKind.String || TreeGuide.Preset(value.GetString()!) is null)
					{
						error = "guide is one of " + string.Join(", ", TreeGuide.Presets.Select(preset => preset.Name));
						return false;
					}
					result = result with { Guide = value.GetString() };
					break;
				case "corners":
					if (Texts(value, 4) is not { } corners || !corners.All(OneColumn))
					{
						error = "corners is [top-left, top-right, bottom-left, bottom-right], each one column wide";
						return false;
					}
					result = result with { TopLeft = corners[0], TopRight = corners[1], BottomLeft = corners[2], BottomRight = corners[3] };
					break;
				case "edge":
					if (value.ValueKind != JsonValueKind.String || !Drawable(value.GetString()!))
					{
						error = "edge is text";
						return false;
					}
					result = result with { Edge = value.GetString() };
					break;
				case "side":
					if (value.ValueKind != JsonValueKind.String || !OneColumn(value.GetString()!))
					{
						error = "side is one column wide";
						return false;
					}
					result = result with { Side = value.GetString() };
					break;
				case "title":
					if (Texts(value, 2) is not { } title)
					{
						error = "title is [open, close]";
						return false;
					}
					result = result with { TitleOpen = title[0], TitleClose = title[1] };
					break;
				case "gauge":
					if (Texts(value, 4) is not { } gauge)
					{
						error = "gauge is [open, filled, empty, close]";
						return false;
					}
					result = result with { GaugeOpen = gauge[0], GaugeFilled = gauge[1], GaugeEmpty = gauge[2], GaugeClose = gauge[3] };
					break;
				case "bullet" or "separator" or "rule":
					if (value.ValueKind != JsonValueKind.String || !(property.Name == "separator" && value.GetString()!.Length == 0 || Drawable(value.GetString()!)))
					{
						error = $"{property.Name} is text";
						return false;
					}
					result = property.Name switch
					{
						"bullet" => result with { Bullet = value.GetString() },
						"separator" => result with { Separator = value.GetString() },
						_ => result with { Rule = value.GetString() },
					};
					break;
				default:
					error = $"a look has no '{property.Name}'";
					return false;
			}
		}
		look = result;
		return true;
	}

	/// <summary><paramref name="value"/> as <paramref name="count"/> strings, none empty, or null.</summary>
	private static string[]? Texts(JsonElement value, int count)
	{
		if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count) return null;
		var texts = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
		return texts.All(text => text is not null && Drawable(text)) ? [.. texts.Select(text => text!)] : null;
	}

	/// <summary>Whether <paramref name="text"/> takes up some width and holds no control character, which would move the cursor or start an escape.</summary>
	private static bool Drawable(string text) =>
		WellFormed(text) && DisplayWidth.Of(text) > 0 && !text.EnumerateRunes().Any(Rune.IsControl);

	/// <summary>Whether every surrogate in <paramref name="text"/> is half of a pair.</summary>
	private static bool WellFormed(string text)
	{
		for (var i = 0; i < text.Length; i++)
		{
			if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
			else if (char.IsSurrogate(text[i])) return false;
		}
		return true;
	}

	/// <summary>Whether <paramref name="text"/> is drawable and exactly one column wide.</summary>
	private static bool OneColumn(string text) => Drawable(text) && DisplayWidth.Of(text) == 1;

	/// <summary>Writes the look as <see cref="TryRead"/> reads it.</summary>
	public void Write(Utf8JsonWriter json)
	{
		ArgumentNullException.ThrowIfNull(json);
		json.WriteStartObject();
		if (Border is not null) json.WriteString("border", Border);
		if (TopLeft is not null && TopRight is not null && BottomLeft is not null && BottomRight is not null)
		{
			json.WriteStartArray("corners");
			foreach (var corner in new[] { TopLeft, TopRight, BottomLeft, BottomRight }) json.WriteStringValue(corner);
			json.WriteEndArray();
		}
		if (Edge is not null) json.WriteString("edge", Edge);
		if (Side is not null) json.WriteString("side", Side);
		if (TitleOpen is not null && TitleClose is not null)
		{
			json.WriteStartArray("title");
			json.WriteStringValue(TitleOpen);
			json.WriteStringValue(TitleClose);
			json.WriteEndArray();
		}
		if (Guide is not null) json.WriteString("guide", Guide);
		if (Bullet is not null) json.WriteString("bullet", Bullet);
		if (GaugeOpen is not null && GaugeFilled is not null && GaugeEmpty is not null && GaugeClose is not null)
		{
			json.WriteStartArray("gauge");
			foreach (var piece in new[] { GaugeOpen, GaugeFilled, GaugeEmpty, GaugeClose }) json.WriteStringValue(piece);
			json.WriteEndArray();
		}
		if (Separator is not null) json.WriteString("separator", Separator);
		if (Rule is not null) json.WriteString("rule", Rule);
		json.WriteEndObject();
	}
}
