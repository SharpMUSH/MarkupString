using System.Collections.Immutable;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class WidgetLayoutTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static readonly BlockRenderOptions Ascii = new() { AsciiOnly = true };

	private static string[] Lines(LayoutNode node, int width, BlockRenderOptions? options = null) =>
		[.. BlockLayout.Lines(node, width, options).Select(line => line.ToPlainText().TrimEnd())];

	[Test]
	public async Task Gauge_FillsTheWidth()
	{
		var gauge = new GaugeNode(6, 12, P("HP"), GaugeOptions.Default);

		await Assert.That(Lines(gauge, 20)).IsEquivalentTo(new[] { "HP [██████░░░░░] 50%" });
		await Assert.That(Lines(gauge, 20, Ascii)).IsEquivalentTo(new[] { "HP [######-----] 50%" });
		await Assert.That(Lines(gauge, 20, new BlockRenderOptions { Linear = true })).IsEquivalentTo(new[] { "HP: 6 of 12 (50%)" });
	}

	[Test]
	public async Task Gauge_FixedBar_ShowsTheValue()
		=> await Assert.That(Lines(new GaugeNode(3, 4, null, GaugeOptions.Default with { BarWidth = 8, Show = GaugeShow.Value, Filled = P("="), Empty = P(" ") }), 40))
			.IsEquivalentTo(new[] { "[======  ] 3/4" });

	[Test]
	public async Task Gauge_InHtml_IsAMeter()
		=> await Assert.That(BlockLayout.Build(new GaugeNode(6, 12, P("HP"), GaugeOptions.Default), 20).Render(MarkupFormat.Html, Registry))
			.Contains("<div class=\"ms-gauge\"><span class=\"ms-gauge-label\">HP</span><meter min=\"0\" max=\"12\" value=\"6\"></meter><span class=\"ms-gauge-value\">50%</span></div>");

	private static BulletsNode List(BulletStyle style, int start = 1, params string[] items) =>
		new([.. items.Select(item => (LayoutNode)new TextNode(P(item)))], BulletOptions.Default with { Style = style, Start = start });

	[Test]
	public async Task Bullets_HangUnderTheirText()
		=> await Assert.That(Lines(List(BulletStyle.Bullet, 1, "Be kind to other players", "No spam"), 16)).IsEquivalentTo(new[]
		{
			"• Be kind to",
			"  other players",
			"• No spam",
		});

	[Test]
	public async Task Bullets_NumbersLineUpOnTheRight()
		=> await Assert.That(Lines(List(BulletStyle.Number, 9, "Nine", "Ten"), 20)).IsEquivalentTo(new[] { " 9. Nine", "10. Ten" });

	[Test]
	[Arguments(BulletStyle.Alpha, "a. x")]
	[Arguments(BulletStyle.Roman, "i. x")]
	[Arguments(BulletStyle.Dash, "- x")]
	[Arguments(BulletStyle.None, "  x")]
	public async Task Bullets_Styles(BulletStyle style, string first)
		=> await Assert.That(Lines(List(style, 1, "x"), 20)[0]).IsEqualTo(first);

	[Test]
	public async Task Bullets_AsciiOnly_UseAStar()
		=> await Assert.That(Lines(List(BulletStyle.Bullet, 1, "x"), 20, Ascii)[0]).IsEqualTo("* x");

	[Test]
	public async Task Bullets_InHtml_AreAnOrderedList()
		=> await Assert.That(BlockLayout.Build(List(BulletStyle.Roman, 3, "x"), 20).Render(MarkupFormat.Html, Registry))
			.Contains("<ol class=\"ms-bullets ms-bullet-roman\" type=\"i\" start=\"3\"><li><div class=\"ms-text\">x</div></li></ol>");

	[Test]
	[Arguments(1, "a")]
	[Arguments(26, "z")]
	[Arguments(27, "aa")]
	[Arguments(702, "zz")]
	[Arguments(703, "aaa")]
	public async Task Letters_CountLikeSpreadsheetColumns(int number, string letters)
		=> await Assert.That(BlockLayout.Letters(number)).IsEqualTo(letters);

	[Test]
	[Arguments(4, "iv")]
	[Arguments(9, "ix")]
	[Arguments(14, "xiv")]
	[Arguments(1994, "mcmxciv")]
	public async Task Roman_Numerals(int number, string numeral)
		=> await Assert.That(BlockLayout.Roman(number)).IsEqualTo(numeral);

	private static readonly ImmutableArray<MarkupText> Names =
		[P("Mannaz"), P("Raya"), P("Tomas"), P("Ilse"), P("Quill"), P("Ottoline"), P("Bram")];

	[Test]
	public async Task Grid_FillsDownEachColumn()
		=> await Assert.That(Lines(new GridNode(Names), 32)).IsEquivalentTo(new[]
		{
			"Mannaz    Ilse      Bram",
			"Raya      Quill",
			"Tomas     Ottoline",
		});

	[Test]
	public async Task Grid_Across_FillsEachRow()
		=> await Assert.That(Lines(new GridNode(Names, Across: true), 32)).IsEquivalentTo(new[]
		{
			"Mannaz    Raya      Tomas",
			"Ilse      Quill     Ottoline",
			"Bram",
		});

	[Test]
	public async Task Grid_InHtml_IsAColumnedList()
		=> await Assert.That(BlockLayout.Build(new GridNode(Names), 32).Render(MarkupFormat.Html, Registry))
			.Contains("<ul class=\"ms-grid ms-down\" style=\"columns:8ch;column-gap:2ch\"><li>Mannaz</li>");

	private static TableNode Who(TableOptions? options = null, int nameMin = 6) => new(
	[
		new TableColumn(P("Name"), Min: nameMin, Priority: 1),
		new TableColumn(P("Idle"), Alignment.Right, Priority: 2, Wrap: false),
		new TableColumn(P("Doing"), Min: 8, Priority: 3),
	],
	[
		[new TextNode(P("Mannaz")), new TextNode(P("0s")), new TextNode(P("Hooooo?"))],
		[new TextNode(P("Raya")), new TextNode(P("5m")), new TextNode(P("Writing a scene in the garden"))],
	], options ?? TableOptions.Default);

	[Test]
	public async Task Table_ColumnsFitTheirWidestCell()
		=> await Assert.That(Lines(Who(), 60)).IsEquivalentTo(new[]
		{
			"Name    Idle  Doing",
			"-------------------------------------------",
			"Mannaz    0s  Hooooo?",
			"Raya      5m  Writing a scene in the garden",
		});

	[Test]
	public async Task Table_TooWide_WrapsFirst()
		=> await Assert.That(Lines(Who(), 30)).IsEquivalentTo(new[]
		{
			"Name    Idle  Doing",
			"------------------------------",
			"Mannaz    0s  Hooooo?",
			"Raya      5m  Writing a scene",
			"              in the garden",
		});

	[Test]
	public async Task Table_TooWideToWrap_LeavesOutTheLeastImportant()
		=> await Assert.That(Lines(Who(), 18)).IsEquivalentTo(new[]
		{
			"Name    Idle",
			"------------",
			"Mannaz    0s",
			"Raya      5m",
		});

	[Test]
	public async Task Table_NoColumnFits_RowsBecomeCards()
		=> await Assert.That(Lines(Who(nameMin: 12), 10)).IsEquivalentTo(new[]
		{
			"Name:", "  Mannaz", "Idle:", "  0s", "Doing:", "  Hooooo?",
			"",
			"Name:", "  Raya", "Idle:", "  5m", "Doing:", "  Writing", "  a scene", "  in the", "  garden",
		});

	[Test]
	public async Task Table_AColumnThatDoesNotWrap_IsWholeOrLeftOut()
		=> await Assert.That(Lines(Who(), 10)).IsEquivalentTo(new[] { "Name", "------", "Mannaz", "Raya" });

	[Test]
	public async Task Table_InHtml_IsATableThatDropsColumnsWhenNarrow()
	{
		var html = BlockLayout.Build(Who(), 60).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<div class=\"ms-table-wrap\"><table class=\"ms-table\"><thead><tr><th scope=\"col\">Name</th><th scope=\"col\" class=\"ms-p2\" style=\"text-align:right\">Idle</th><th scope=\"col\" class=\"ms-p3\">Doing</th></tr></thead>");
		await Assert.That(html).DoesNotContain("-----");
	}

	[Test]
	public async Task Widgets_SurviveTheSerializer()
	{
		var red = MarkupText.Wrap(AnsiCodeParser.Parse("r"), "#");
		var node = new StackNode(
		[
			new GaugeNode(2.5, 10, P("XP"), GaugeOptions.Default with { Filled = red, Show = GaugeShow.Value, BarWidth = 6 }),
			List(BulletStyle.Alpha, 2, "one", "two"),
			new GridNode(Names, 3, true),
			Who(TableOptions.Default with { Separator = P(" | "), HeaderRule = P("=") }),
		]);
		var text = BlockLayout.Build(node, 60);

		var json = MarkupTextSerializer.Serialize(text, Registry);
		var read = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(read.Render(MarkupFormat.Html, Registry)).IsEqualTo(text.Render(MarkupFormat.Html, Registry));
		await Assert.That(BlockLayout.Relayout(read, 0, BlockRenderOptions.Default with { AsciiOnly = false }).Render(MarkupFormat.Ansi, Registry))
			.IsEqualTo(text.Render(MarkupFormat.Ansi, Registry));
		await Assert.That(MarkupTextSerializer.Serialize(read, Registry)).IsEqualTo(json);
	}

	/// <summary>A coloured piece that draws the default's character keeps its colour through the serializer.</summary>
	[Test]
	public async Task ColouredDefaultPiece_KeepsItsColour()
	{
		var red = MarkupText.Wrap(AnsiCodeParser.Parse("r"), "=");
		var text = BlockLayout.Build(new RuleNode(null, BorderStyle.Mush with { Top = red }), 4);

		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, Registry), Registry);
		var relaid = BlockLayout.Relayout(read, 0, new BlockRenderOptions { AsciiOnly = true });

		await Assert.That(relaid.Render(MarkupFormat.Ansi, Registry)).IsEqualTo(text.Render(MarkupFormat.Ansi, Registry));
	}
}
