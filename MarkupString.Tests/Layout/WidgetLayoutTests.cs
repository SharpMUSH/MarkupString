using System.Collections.Immutable;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class WidgetLayoutTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static readonly LayoutContext Ascii = new() { AsciiOnly = true };

	private static string[] Lines(Block block, int width, LayoutContext? context = null) =>
		[.. BlockLayout.Lines(block, width, context).Select(line => line.ToPlainText().TrimEnd())];

	[Test]
	public async Task Gauge_FillsTheWidth()
	{
		var gauge = new Gauge(6, 12) { Label = P("HP") };

		await Assert.That(Lines(gauge, 20)).IsEquivalentTo(new[] { "HP [██████░░░░░] 50%" });
		await Assert.That(Lines(gauge, 20, Ascii)).IsEquivalentTo(new[] { "HP [######-----] 50%" });
		await Assert.That(Lines(gauge, 20, new LayoutContext { Linear = true })).IsEquivalentTo(new[] { "HP: 6 of 12 (50%)" });
	}

	[Test]
	public async Task Gauge_FixedBar_ShowsTheValue()
		=> await Assert.That(Lines(new Gauge(3, 4) { BarWidth = 8, Show = GaugeShow.Value, Filled = P("="), Empty = P(" ") }, 40))
			.IsEquivalentTo(new[] { "[======  ] 3/4" });

	[Test]
	public async Task Gauge_InHtml_IsAMeter()
		=> await Assert.That(BlockLayout.Build(new Gauge(6, 12) { Label = P("HP") }, 20).Render(MarkupFormat.Html, Registry))
			.Contains("<div class=\"ms-gauge\"><span class=\"ms-gauge-label\">HP</span><meter min=\"0\" max=\"12\" value=\"6\"></meter><span class=\"ms-gauge-value\">50%</span></div>");

	private static Bullets List(BulletStyle style, int start = 1, params string[] items) =>
		new([.. items.Select(item => (Block)P(item))]) { Style = style, Start = start };

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
		=> await Assert.That(Bullets.Letters(number)).IsEqualTo(letters);

	[Test]
	[Arguments(4, "iv")]
	[Arguments(9, "ix")]
	[Arguments(14, "xiv")]
	[Arguments(1994, "mcmxciv")]
	public async Task Roman_Numerals(int number, string numeral)
		=> await Assert.That(Bullets.Roman(number)).IsEqualTo(numeral);

	private static readonly ImmutableArray<MarkupText> Names =
		[P("Mannaz"), P("Raya"), P("Tomas"), P("Ilse"), P("Quill"), P("Ottoline"), P("Bram")];

	[Test]
	public async Task Grid_FillsDownEachColumn()
		=> await Assert.That(Lines(new Grid(Names), 32)).IsEquivalentTo(new[]
		{
			"Mannaz    Ilse      Bram",
			"Raya      Quill",
			"Tomas     Ottoline",
		});

	[Test]
	public async Task Grid_Across_FillsEachRow()
		=> await Assert.That(Lines(new Grid(Names) { Across = true }, 32)).IsEquivalentTo(new[]
		{
			"Mannaz    Raya      Tomas",
			"Ilse      Quill     Ottoline",
			"Bram",
		});

	[Test]
	public async Task Grid_InHtml_IsAColumnedList()
		=> await Assert.That(BlockLayout.Build(new Grid(Names), 32).Render(MarkupFormat.Html, Registry))
			.Contains("<ul class=\"ms-grid ms-down\" style=\"columns:8ch;column-gap:2ch\"><li>Mannaz</li>");

	private static Table Who(int nameMin = 6) => new(
	[
		new TableColumn(P("Name")) { Min = nameMin },
		new TableColumn(P("Idle")) { Alignment = Alignment.Right, Priority = 2, Wrap = false },
		new TableColumn(P("Doing")) { Min = 8, Priority = 3 },
	],
	[
		[P("Mannaz"), P("0s"), P("Hooooo?")],
		[P("Raya"), P("5m"), P("Writing a scene in the garden")],
	]);

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

		await Assert.That(html).Contains("<div class=\"ms-table-wrap\"><table class=\"ms-table\"><thead><tr><th scope=\"col\">Name</th><th scope=\"col\" class=\"ms-p2 ms-nowrap\" style=\"text-align:right\">Idle</th><th scope=\"col\" class=\"ms-p3\">Doing</th></tr></thead>");
		await Assert.That(html).DoesNotContain("-----");
	}

	[Test]
	public async Task Table_InHtml_KeepsAColumnThatDoesNotWrapOnOneLine()
	{
		var html = BlockLayout.Build(Who(), 60).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<th scope=\"col\" class=\"ms-p2 ms-nowrap\" style=\"text-align:right\">Idle</th>");
		await Assert.That(html).Contains("<td class=\"ms-p2 ms-nowrap\" style=\"text-align:right\"><div class=\"ms-text\">0s</div></td>");
		await Assert.That(html).Contains("<td><div class=\"ms-text\">Mannaz</div></td>");
		await Assert.That(LayoutCss.Fixed).Contains(".ms-table .ms-nowrap, .ms-table .ms-nowrap .ms-text { white-space: pre; overflow-wrap: normal; }");
	}

	[Test]
	public async Task Table_InHtml_NoWrapWithoutPriority_HasOnlyItsClass()
	{
		var table = new Table([new TableColumn(P("When")) { Wrap = false }], [[P("Thu Oct 8")]]);

		await Assert.That(BlockLayout.Build(table, 60).Render(MarkupFormat.Html, Registry))
			.Contains("<th scope=\"col\" class=\"ms-nowrap\">When</th></tr></thead><tbody><tr><td class=\"ms-nowrap\"><div class=\"ms-text\">Thu Oct 8</div></td>");
	}

	private static Table Growing(int nameShare, int doingShare) => Who() with
	{
		Columns =
		[
			new TableColumn(P("Name")) { Min = 6, Grow = nameShare },
			new TableColumn(P("Idle")) { Alignment = Alignment.Right, Priority = 2, Wrap = false },
			new TableColumn(P("Doing")) { Min = 8, Priority = 3, Grow = doingShare },
		],
	};

	[Test]
	public async Task Table_AColumnThatGrows_FillsTheWidth()
	{
		var lines = BlockLayout.Build(Growing(0, 1), 60).ToPlainText().Split('\n');

		await Assert.That(lines[1]).IsEqualTo(new string('-', 60)).Because("the heading rule spans the table, which now spans its width");
		await Assert.That(lines[0].TrimEnd()).IsEqualTo("Name    Idle  Doing");
		await Assert.That(lines[3].TrimEnd()).IsEqualTo("Raya      5m  Writing a scene in the garden");
	}

	[Test]
	public async Task Table_GrowingColumns_ShareTheSpareWidthByShare()
		// 43 cells as drawn leaves 17 at 60: Name gets 17/4 = 4 and the cell the shares round away (11 wide, then the gap), Doing 12.
		=> await Assert.That(Lines(Growing(1, 3), 60)[0]).IsEqualTo("Name" + new string(' ', 9) + "Idle  Doing");

	[Test]
	public async Task Table_AGrowingColumn_StopsAtItsMax()
	{
		var table = Growing(0, 1) with { Columns = [.. Growing(0, 1).Columns.SetItem(2, Growing(0, 1).Columns[2] with { Max = 35 })] };

		await Assert.That(Lines(table, 60)[1]).IsEqualTo(new string('-', 49)).Because("Doing grows from 29 to its 35 and no further");
	}

	[Test]
	public async Task Table_TooWide_GrowsNothing()
		=> await Assert.That(Lines(Growing(1, 3), 30)).IsEquivalentTo(Lines(Who(), 30));

	[Test]
	public async Task Table_InHtml_AGrowingTableFillsThePage()
	{
		var html = BlockLayout.Build(Growing(1, 3), 60).Render(MarkupFormat.Html, Registry);

		// Drawn as text at 60 the columns are 11, 4 and 41 cells; the page gets them as shares of 56.
		await Assert.That(html).Contains("<table class=\"ms-table ms-fill\"><colgroup><col style=\"width:20%\"><col style=\"width:7%\"><col style=\"width:73%\"></colgroup><thead><tr><th scope=\"col\">Name</th><th scope=\"col\" class=\"ms-p2 ms-nowrap\" style=\"text-align:right\">Idle</th><th scope=\"col\" class=\"ms-p3\">Doing</th>");
		await Assert.That(BlockLayout.Build(Who(), 60).Render(MarkupFormat.Html, Registry)).DoesNotContain("<colgroup>")
			.Because("a table that does not fill is as wide as its cells");
		await Assert.That(LayoutCss.Fixed).Contains(".ms-table.ms-fill { width: 100%; }");
		await Assert.That(LayoutCss.Fixed).Contains(".ms-table.ms-fill > colgroup > col { width: auto !important; }")
			.Because("on a page narrow enough to hide columns, their shares are given up");
	}

	[Test]
	public async Task Table_Grow_SurvivesTheSerializer()
	{
		var text = BlockLayout.Build(Growing(1, 3), 60, fluid: true);

		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, Registry), Registry);

		await Assert.That(BlockLayout.Relayout(read, 70, LayoutContext.Default).ToPlainText())
			.IsEqualTo(BlockLayout.Build(Growing(1, 3), 70).ToPlainText());
	}

	/// <summary>
	/// The line under the headings spans the columns as drawn, gaps and separators included, whatever
	/// the mode, never less than the widest row; a box round a narrower table is wider than its rule.
	/// </summary>
	[Test]
	[Arguments(60, false, null)]
	[Arguments(30, false, null)]
	[Arguments(18, false, null)]
	[Arguments(10, false, null)]
	[Arguments(60, true, " │ ")]
	[Arguments(30, true, " │ ")]
	[Arguments(60, false, " │ ")]
	[Arguments(30, true, "|")]
	public async Task Table_HeaderRule_SpansTheWidestRow(int width, bool ascii, string? separator)
	{
		var table = Who() with { Separator = separator is null ? null : P(separator) };
		var lines = BlockLayout.Lines(table, width, new LayoutContext { AsciiOnly = ascii }).Select(line => line.ToPlainText().TrimEnd()).ToArray();
		var rule = lines[1];

		await Assert.That(rule.Length).IsGreaterThan(0);
		await Assert.That(rule.Distinct().Count()).IsEqualTo(1);
		await Assert.That(rule.Length).IsGreaterThanOrEqualTo(lines.Where((_, i) => i != 1).Max(line => line.Length));
	}

	/// <summary>A separator with no ASCII form is drawn as " | " for an ASCII reader, and the columns make room for it.</summary>
	[Test]
	public async Task Table_SeparatorWithoutAnAsciiForm_FitsAnAsciiReader()
		=> await Assert.That(Lines(Who() with { Separator = P("•") }, 24, Ascii)).IsEquivalentTo(new[]
		{
			"Name   | Idle | Doing",
			"------------------------",
			"Mannaz |   0s | Hooooo?",
			"Raya   |   5m | Writing",
			"       |      | a scene",
			"       |      | in the",
			"       |      | garden",
		});

	[Test]
	public async Task Widgets_SurviveTheSerializer()
	{
		var red = MarkupText.Wrap(AnsiCodeParser.Parse("r"), "#");
		var node = new Stack(
		[
			new Gauge(2.5, 10) { Label = P("XP"), Filled = red, Show = GaugeShow.Value, BarWidth = 6 },
			List(BulletStyle.Alpha, 2, "one", "two"),
			new Grid(Names) { Gap = 3, Across = true },
			Who() with { Separator = P(" | "), HeaderRule = P("=") },
		]);
		var text = BlockLayout.Build(node, 60);

		var json = MarkupTextSerializer.Serialize(text, Registry);
		var read = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(read.Render(MarkupFormat.Html, Registry)).IsEqualTo(text.Render(MarkupFormat.Html, Registry));
		await Assert.That(BlockLayout.Relayout(read, 0, LayoutContext.Default with { AsciiOnly = false }).Render(MarkupFormat.Ansi, Registry))
			.IsEqualTo(text.Render(MarkupFormat.Ansi, Registry));
		await Assert.That(MarkupTextSerializer.Serialize(read, Registry)).IsEqualTo(json);
	}

	/// <summary>A coloured piece that draws the default's character keeps its colour through the serializer.</summary>
	[Test]
	public async Task ColouredDefaultPiece_KeepsItsColour()
	{
		var red = MarkupText.Wrap(AnsiCodeParser.Parse("r"), "=");
		var text = BlockLayout.Build(new Rule { Border = BorderStyle.Mush with { Top = red } }, 4);

		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, Registry), Registry);
		var relaid = BlockLayout.Relayout(read, 0, new LayoutContext { AsciiOnly = true });

		await Assert.That(relaid.Render(MarkupFormat.Ansi, Registry)).IsEqualTo(text.Render(MarkupFormat.Ansi, Registry));
	}
}
