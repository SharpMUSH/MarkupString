using Markdig;
using MarkupString.Ansi;
using MarkupString.Layout;
using MarkupString.Tests.Ansi;

namespace MarkupString.Tests.Layout;

/// <summary>
/// A layout in <see cref="MarkupFormat.Markdown"/>: drawn from its tree as Markdown's own structure — a pipe
/// table, lists, rules, bold titles — and never laid out to a width.
/// </summary>
public class MarkdownLayoutTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi();

	private static readonly AnsiMarkup Bold = AnsiMarkup.Create(bold: true);

	private static string Render(Block block, int width = 60) => BlockLayout.Build(block, width).Render(MarkupFormat.Markdown, Registry);

	private static string Html(Block block) => MarkdownRenderTests.ToHtml(Render(block));

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static TextBlock T(string text) => new(MarkupText.Plain(text));

	// ── Tables ──────────────────────────────────────────────────────────────────

	private static readonly Table Scores = new(
		[
			new TableColumn(MarkupText.Wrap(Bold, "Name")),
			new TableColumn(P("Score")) { Alignment = Alignment.Right },
			new TableColumn(P("Note")) { Alignment = Alignment.Center },
			new TableColumn(P("Seen")) { Alignment = Alignment.Full },
		],
		[
			[T("Lyra"), T("10"), T("a|b\nc"), T("today")],
			[T("Bo"), T(""), T("*x*")],
		]);

	[Test]
	public async Task ATableIsAPipeTable()
		=> await Assert.That(Render(Scores)).IsEqualTo(
			"| **Name** | Score | Note | Seen |\n" +
			"| --- | ---: | :---: | --- |\n" +
			"| Lyra | 10 | a\\|b<br>c | today |\n" +
			"| Bo | | \\*x\\* | |");

	[Test]
	public async Task ATableReadsBackAsATable()
	{
		var html = Html(Scores);
		await Assert.That(html).Contains("<th><strong>Name</strong></th>");
		await Assert.That(html).Contains("<th style=\"text-align: right;\">Score</th>");
		await Assert.That(html).Contains("<td style=\"text-align: center;\">a|b<br>c</td>");
		await Assert.That(html).Contains("<td style=\"text-align: center;\">*x*</td>");
		await Assert.That(html.Split("<tr>").Length - 1).IsEqualTo(3);
	}

	[Test]
	public async Task MarkdownIsNotLaidOutToAWidth()
	{
		var wide = new Table([new TableColumn(P("Text"))], [[T("a long cell that a terminal would wrap at twenty columns")]]);
		await Assert.That(Render(wide, 20)).IsEqualTo(Render(wide, 200));
		await Assert.That(Render(wide, 20)).IsEqualTo("| Text |\n| --- |\n| a long cell that a terminal would wrap at twenty columns |");
	}

	[Test]
	public async Task ATableWithNoColumnsIsNothing()
		=> await Assert.That(Render(new Stack([new Table([], [[T("x")]]), T("after")]))).IsEqualTo("after");

	[Test]
	public async Task ABlockInACellIsItsLinesInReadingOrder()
	{
		var table = new Table(
			[new TableColumn(P("Who")), new TableColumn(P("What"))],
			[[T("Lyra"), new Stack([new Bullets([T("one"), T("two")]), new Rule(), new Rule(P("End"))])]]);
		await Assert.That(Render(table)).IsEqualTo("| Who | What |\n| --- | --- |\n| Lyra | - one<br>- two<br>**End** |");
	}

	[Test]
	public async Task ATableInACellIsItsRowsAsLabelledValues()
	{
		var inner = new Table([new TableColumn(P("k")), new TableColumn(P("v"))], [[T("a"), T("1")], [T("b")]]);
		var table = new Table([new TableColumn(P("Data"))], [[inner]]);
		await Assert.That(Render(table)).IsEqualTo("| Data |\n| --- |\n| - **k:** a<br>- **v:** 1<br>- **k:** b<br>- **v:** |");
	}

	// ── Placement among text ────────────────────────────────────────────────────

	[Test]
	public async Task ABlockStandsApartFromTheTextAroundIt()
	{
		var text = MarkupText.Concat([P("Intro\n"), BlockLayout.Build(new Rule(), 20), P("\nOutro")]);
		var markdown = text.Render(MarkupFormat.Markdown, Registry);
		await Assert.That(markdown).IsEqualTo("Intro\n\n---\n\nOutro");
		await Assert.That(MarkdownRenderTests.ToHtml(markdown)).IsEqualTo("<p>Intro</p>\n<hr />\n<p>Outro</p>");
	}

	[Test]
	public async Task ALineAfterATableIsNotARowOfIt()
	{
		var text = MarkupText.Concat([BlockLayout.Build(Scores, 40), P("\nOutro")]);
		var html = MarkdownRenderTests.ToHtml(text.Render(MarkupFormat.Markdown, Registry));
		await Assert.That(html).EndsWith("</table>\n<p>Outro</p>");
	}

	[Test]
	public async Task ALayoutThatWasCutIsItsText()
	{
		var laid = BlockLayout.Build(new Stack([T("# one"), T("two")]), 20);
		var cut = laid.Substring(0, 5);
		await Assert.That(cut.Render(MarkupFormat.Markdown, Registry)).IsEqualTo("\\# one");
	}

	// ── Text ────────────────────────────────────────────────────────────────────

	[Test]
	public async Task TextIsEscapedAndItsLineEndingsKept()
	{
		await Assert.That(Render(T("# not a heading\n- nor a list\n\nnew *paragraph*"))).IsEqualTo("\\# not a heading\\\n\\- nor a list\n\nnew \\*paragraph\\*");
		await Assert.That(Html(T("one\ntwo"))).IsEqualTo("<p>one<br />\ntwo</p>");
	}

	[Test]
	public async Task StyledTextKeepsItsStyling()
		=> await Assert.That(Render(new TextBlock(MarkupText.Concat([P("a "), MarkupText.Wrap(Bold, "b"), P(" c")])))).IsEqualTo("a **b** c");

	[Test]
	public async Task ALayoutInsideTextIsDrawnAsItsOwnBlocks()
	{
		var inner = BlockLayout.Build(new Bullets([T("x"), T("y")]), 20);
		var outer = new Stack([new TextBlock(MarkupText.Concat([P("before\n"), inner, P("\nafter")]))]);
		await Assert.That(Render(outer)).IsEqualTo("before\n\n- x\n- y\n\nafter");
	}

	// ── Rules and boxes ─────────────────────────────────────────────────────────

	[Test]
	public async Task ARuleIsAThematicBreak()
		=> await Assert.That(Render(new Stack([T("a"), new Rule(), T("b")]))).IsEqualTo("a\n\n---\n\nb");

	[Test]
	public async Task ATitledRuleIsItsTitleInBold()
	{
		var rule = new Rule(P("Middle")) { Titles = [new EdgeTitle(P("Right"), Alignment.Right), new EdgeTitle(P("Left"), Alignment.Left)] };
		await Assert.That(Render(rule)).IsEqualTo("**Left, Middle, Right**");
	}

	[Test]
	public async Task ATitleAlreadyBoldIsBoldOnce()
	{
		await Assert.That(Render(new Rule(MarkupText.Wrap(Bold, "Mail")))).IsEqualTo("**Mail**");
		await Assert.That(Render(new Rule(MarkupText.Concat([P("New "), MarkupText.Wrap(AnsiMarkup.Create(italic: true), "Mail")])))).IsEqualTo("**New *Mail***");
		await Assert.That(Render(new Rule(P("two\nlines")))).IsEqualTo("**two** **lines**");
		await Assert.That(Render(new Fields([new Field(MarkupText.Wrap(Bold, "Name"), T("x"))]))).IsEqualTo("- **Name:** x");
	}

	[Test]
	public async Task ABoxIsItsTitleOverWhatItHolds()
	{
		var box = new Stack([T("Lyra Vale"), new Rule(), T("Wayfinder")]).Bordered(P("JOURNAL")) with
		{
			BottomTitles = [new EdgeTitle(P("page 1"))],
		};
		await Assert.That(Render(box)).IsEqualTo("**JOURNAL**\n\nLyra Vale\n\n---\n\nWayfinder\n\npage 1");
		await Assert.That(Render(T("only").Bordered())).IsEqualTo("only");
	}

	[Test]
	public async Task ASectionIsATitleAContentAndALine()
	{
		var section = new Stack([new Rule(P("Mail")), T("You have no mail."), new Rule()]);
		await Assert.That(Html(section)).IsEqualTo("<p><strong>Mail</strong></p>\n<p>You have no mail.</p>\n<hr />");
	}

	// ── Rows and modifiers ──────────────────────────────────────────────────────

	[Test]
	public async Task ARowIsItsItemsOneAfterTheOther()
	{
		var row = new Flex([T("left").Sized(BlockSize.Cells(10)), T("right")]) { Separator = P(" | ") };
		await Assert.That(Render(row)).IsEqualTo("left\n\nright");
	}

	[Test]
	public async Task AlignmentShadingAndThemesAreDropped()
	{
		var block = new Stack([
			T("centred").Aligned(Alignment.Center),
			T("shaded").Shaded(new ColorGradient([AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0)), AnsiMarkup.Create(foreground: new AnsiColor.Rgb(0, 0, 255))])),
			T("themed").Themed(ThemePalette.Preset("fantasy")!.ToLayoutTheme()),
		]);
		await Assert.That(Render(block)).IsEqualTo("centred\n\nshaded\n\nthemed");
	}

	[Test]
	public async Task AColouredBlockKeepsWhatMarkdownCanSay()
	{
		await Assert.That(Render(new Stack([T("a"), T("b")]).Colored(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false))))).IsEqualTo("a\n\nb");
		await Assert.That(Render(T("loud").Colored(Bold))).IsEqualTo("**loud**");
	}

	// ── Lists ───────────────────────────────────────────────────────────────────

	[Test]
	[Arguments(BulletStyle.Bullet)]
	[Arguments(BulletStyle.Dash)]
	[Arguments(BulletStyle.Star)]
	[Arguments(BulletStyle.Custom)]
	public async Task BulletsAreAList(BulletStyle style)
		=> await Assert.That(Render(new Bullets([T("one"), T("- two")]) { Style = style, Marker = P(">>") })).IsEqualTo("- one\n- \\- two");

	[Test]
	[Arguments(BulletStyle.Number)]
	[Arguments(BulletStyle.Alpha)]
	[Arguments(BulletStyle.Roman)]
	public async Task CountedMarkersAreANumberedList(BulletStyle style)
	{
		var list = new Bullets([T("one"), T("two")]) { Style = style, Start = 9 };
		await Assert.That(Render(list)).IsEqualTo("9. one\n10. two");
		await Assert.That(Html(list)).IsEqualTo("<ol start=\"9\">\n<li>one</li>\n<li>two</li>\n</ol>");
	}

	[Test]
	public async Task BulletsWithNoMarkerAreTheirItems()
		=> await Assert.That(Render(new Bullets([T("one"), T("two")]) { Style = BulletStyle.None })).IsEqualTo("one\n\ntwo");

	[Test]
	public async Task AnItemOfSeveralLinesAndBlocksStaysInItsItem()
	{
		var list = new Bullets([new Stack([T("first\nline"), new Bullets([T("inner")]), T("more")]), T("second")]);
		await Assert.That(Render(list)).IsEqualTo("- first\\\n  line\n  - inner\n\n  more\n- second");
		await Assert.That(Html(list)).IsEqualTo(
			"<ul>\n<li><p>first<br />\nline</p>\n<ul>\n<li>inner</li>\n</ul>\n<p>more</p>\n</li>\n<li><p>second</p>\n</li>\n</ul>");
	}

	[Test]
	public async Task ListsOneAfterTheOtherStayTwoLists()
	{
		var block = new Stack([new Bullets([T("a")]), new Bullets([T("b")]), new Bullets([T("c")]), new Bullets([T("1")]) { Style = BulletStyle.Number }, new Bullets([T("2")]) { Style = BulletStyle.Number }]);
		await Assert.That(Render(block)).IsEqualTo("- a\n\n* b\n\n- c\n\n1. 1\n\n1) 2");
		await Assert.That(Html(block).Split("<ul>").Length - 1).IsEqualTo(3);
		await Assert.That(Html(block).Split("<ol>").Length - 1).IsEqualTo(2);
	}

	[Test]
	public async Task ATreeIsANestedList()
	{
		var tree = new Tree([
			new TreeItem(T("root"), [new TreeItem(T("branch"), [new TreeItem(T("leaf"))]), new TreeItem(T("twig"))]),
			new TreeItem(T("other")),
		]);
		await Assert.That(Render(tree)).IsEqualTo("- root\n  - branch\n    - leaf\n  - twig\n- other");
	}

	[Test]
	public async Task AGridIsAList()
		=> await Assert.That(Render(new Grid([P("Lyra"), P("Bo"), P("Ash")]) { Across = true })).IsEqualTo("- Lyra\n- Bo\n- Ash");

	// ── Fields and gauges ───────────────────────────────────────────────────────

	[Test]
	public async Task FieldsAreAListOfLabelledValues()
	{
		var fields = new Fields([
			new Field(P("Name"), T("Lyra Vale")),
			new Field(P("Gear"), new Bullets([T("rope"), T("lamp")])),
			new Field(MarkupText.Empty, T("unlabelled")),
		])
		{ Columns = 2 };
		await Assert.That(Render(fields)).IsEqualTo("- **Name:** Lyra Vale\n- **Gear:**\n  - rope\n  - lamp\n- unlabelled");
	}

	[Test]
	public async Task AFieldsSeparatorOrLeaderIsHonoured()
	{
		await Assert.That(Render(new Fields([new Field(P("HP"), T("10"))]) { Separator = P(" = ") })).IsEqualTo("- **HP =** 10");
		await Assert.That(Render(new Fields([new Field(P("HP"), T("10"))]) { Leader = P(".") })).IsEqualTo("- **HP** 10");
		var themed = new Fields([new Field(P("HP"), T("10"))]).Themed(new LayoutTheme { FieldSeparator = P(" -> ") });
		await Assert.That(Render(themed)).IsEqualTo("- **HP ->** 10");
	}

	[Test]
	public async Task AGaugeIsItsFigures()
		=> await Assert.That(Render(new Gauge(7, 10) { Label = P("Trail") })).IsEqualTo("Trail: 7 of 10 (70%)");

	// ── Pictures ────────────────────────────────────────────────────────────────

	[Test]
	public async Task AFigureIsAnImageAndWhatIsBesideIt()
	{
		var figure = new Figure(new ImageMarkup("https://example.test/map.png", "A map"), P("[map]")) { Beside = T("The city.") };
		await Assert.That(Render(figure)).IsEqualTo("![A map](https://example.test/map.png)\n\nThe city.");
	}

	[Test]
	public async Task AFigureAPageCannotShowIsItsArtOrItsDescription()
	{
		await Assert.That(Render(new Figure(new ImageMarkup("file:///map.png", "A map"), P(" /\\\n/__\\")))).IsEqualTo("```\n /\\\n/__\\\n```");
		await Assert.That(Render(new Figure(new ImageMarkup("file:///map.png", "A map"), MarkupText.Empty))).IsEqualTo("\\[A map\\]");
	}

	// ── What Markdown has nothing for ───────────────────────────────────────────

	private sealed record Banner(string Text) : Block
	{
		public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
		{
			lines.Add(MarkupText.Plain("+" + new string('-', Text.Length) + "+"));
			lines.Add(MarkupText.Plain("|" + Text + "|"));
			lines.Add(MarkupText.Plain("+" + new string('-', Text.Length) + "+"));
		}
	}

	[Test]
	public async Task ABlockOfYourOwnIsTheLinesItDrawsAsCode()
	{
		await Assert.That(Render(new Banner("hi"))).IsEqualTo("```\n+--+\n|hi|\n+--+\n```");
		await Assert.That(Render(new Banner("a ``` b"))).IsEqualTo("````\n+-------+\n|a ``` b|\n+-------+\n````");
		await Assert.That(Html(new Banner("*x*"))).IsEqualTo("<pre><code>+---+\n|*x*|\n+---+\n</code></pre>");
	}

	[Test]
	public async Task TheJournalReadsBackAsItsStructure()
	{
		var sheet = new Stack(
		[
			new Fields([new Field(P("Name"), T("Lyra Vale")), new Field(P("Role"), T("Wayfinder"))]),
			new Gauge(7, 10) { Label = P("Trail") },
		]).Bordered(P("WAYFINDER'S JOURNAL"));
		await Assert.That(Html(sheet)).IsEqualTo(
			"<p><strong>WAYFINDER'S JOURNAL</strong></p>\n<ul>\n<li><strong>Name:</strong> Lyra Vale</li>\n<li><strong>Role:</strong> Wayfinder</li>\n</ul>\n<p>Trail: 7 of 10 (70%)</p>");
	}
}
