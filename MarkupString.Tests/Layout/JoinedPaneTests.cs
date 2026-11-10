using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

/// <summary>A joined <see cref="Flex"/> in a <see cref="Frame"/>: panes that share the frame's edges.</summary>
public class JoinedPaneTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithHtml();

	private static TextBlock T(string text) => new(MarkupText.Plain(text));

	/// <summary>Blocks one under another with a rule between each two.</summary>
	private static Stack Panes(params Block[] blocks) =>
		new([.. blocks.SelectMany((block, i) => i == 0 ? new[] { block } : [new Rule(), block])]);

	/// <summary>Three panes on the left, three in the middle and one on the right, under a titled frame with a footer.</summary>
	private static Frame Sheet(BorderStyle border) => new Stack(
	[
		new Flex([Panes(T("Stats"), T("Skills"), T("Gear")).Sized(BlockSize.Cells(14)), Panes(T("Bio"), T("Notes"), T("Log")), T("Portrait")]) { Join = true },
		new Rule(MarkupText.Plain("Footer")),
		T("end"),
	]).Bordered(MarkupText.Plain("Sheet"), border);

	private static string[] Lines(Block block, int width, LayoutContext? context = null) =>
		[.. BlockLayout.Lines(block, width, context).Select(line => line.ToPlainText())];

	[Test]
	public async Task SevenPanes_ShareTheFramesEdges()
	{
		await Assert.That(Lines(Sheet(BorderStyle.Ascii), 44)).IsEquivalentTo(new[]
		{
			"+--------------+--< Sheet >--+-------------+",
			"| Stats        | Bio         | Portrait    |",
			"+--------------+-------------+             |",
			"| Skills       | Notes       |             |",
			"+--------------+-------------+             |",
			"| Gear         | Log         |             |",
			"+--------------+-< Footer >--+-------------+",
			"| end                                      |",
			"+------------------------------------------+",
		});
	}

	[Test]
	public async Task Junctions_TakeTheBordersOwnPieces()
	{
		await Assert.That(Lines(Sheet(BorderStyle.Single), 44)).IsEquivalentTo(new[]
		{
			"┌──────────────┬──┤ Sheet ├──┬─────────────┐",
			"│ Stats        │ Bio         │ Portrait    │",
			"├──────────────┼─────────────┤             │",
			"│ Skills       │ Notes       │             │",
			"├──────────────┼─────────────┤             │",
			"│ Gear         │ Log         │             │",
			"├──────────────┴─┤ Footer ├──┴─────────────┤",
			"│ end                                      │",
			"└──────────────────────────────────────────┘",
		});
		await Assert.That(Lines(Sheet(BorderStyle.Double), 44)[2]).IsEqualTo("╠══════════════╬═════════════╣             ║");
		await Assert.That(Lines(Sheet(BorderStyle.Double), 44, new LayoutContext { AsciiOnly = true })[2]).IsEqualTo("+==============+=============+             |");
	}

	[Test]
	public async Task ALineUnderATitle_LeavesTheTitleWhole()
	{
		var frame = new Flex([T("a"), T("b")]) { Join = true }.Bordered(MarkupText.Plain("Title"), BorderStyle.Ascii);

		await Assert.That(Lines(frame, 13)).IsEquivalentTo(new[]
		{
			"+-< Title >-+",
			"| a   | b   |",
			"+-----+-----+",
		});
	}

	[Test]
	public async Task PanesThatDoNotFit_StackAsARowWould()
	{
		var row = new Flex([T("a").Sized(BlockSize.Cells(10)), T("b").Sized(BlockSize.Cells(10))]) { Join = true };

		var unjoined = row with { Join = false };
		await Assert.That(Lines(row.Bordered(border: BorderStyle.Ascii), 12)).IsEquivalentTo(Lines(unjoined.Bordered(border: BorderStyle.Ascii), 12));
	}

	[Test]
	public async Task OutsideAFrame_AJoinedRowIsAPlainRow()
	{
		var row = new Flex([T("a"), T("b")]) { Join = true };

		await Assert.That(Lines(row, 10)).IsEquivalentTo(Lines(row with { Join = false }, 10));
	}

	[Test]
	public async Task JoinAndJunctionPieces_SurviveTheSerializer()
	{
		var border = BorderStyle.Ascii with { TeeTop = MarkupText.Plain("v"), TeeBottom = MarkupText.Plain("^"), Cross = MarkupText.Plain("*") };
		var laid = BlockLayout.Build(Sheet(border), 44);
		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(laid, Registry), Registry);

		await Assert.That(BlockLayout.AsBlock(read) is Frame { Body: Stack { Children: [Flex { Join: true }, ..] }, Border: var kept } && kept == border).IsTrue();
		await Assert.That(Lines(BlockLayout.AsBlock(read), 44)[0]).IsEqualTo("+--------------v--< Sheet >--v-------------+");
		await Assert.That(Lines(BlockLayout.AsBlock(read), 44)[2]).IsEqualTo("+--------------*-------------+             |");
	}

	[Test]
	public async Task InHtml_APaneDrawsItsRulesAsDividers()
	{
		var html = BlockLayout.Build(Sheet(BorderStyle.Single), 44).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<div class=\"ms-flex ms-joined\"");
		await Assert.That(html).Contains("<div class=\"ms-text\">Stats</div><div class=\"ms-divider ms-border-single\" role=\"separator\"></div><div class=\"ms-text\">Skills</div>");
		await Assert.That(LayoutCss.Fixed).Contains(".ms-box > .ms-flex.ms-joined > .ms-item + .ms-item { border-left: 1px solid");
	}

	[Test]
	public async Task APicture_InAPane_KeepsTheLinesInPlace()
	{
		var cat = new Figure(new ImageMarkup("cat.png", "A cat"), MarkupText.Plain("/\\_/\\\n(o.o)"));
		var frame = new Flex([Panes(T("Bio"), cat), T("x")]) { Join = true }.Bordered(border: BorderStyle.Ascii);

		await Assert.That(Lines(frame, 20)).IsEquivalentTo(new[]
		{
			"+---------+--------+",
			"| Bio     | x      |",
			"+---------+        |",
			"| /\\_/\\   |        |",
			"| (o.o)   |        |",
			"+---------+--------+",
		});
	}

	[Test]
	public async Task ATitleHoldingTheFill_KeepsItsCharacters()
	{
		var frame = new Flex([T("a"), T("b")]) { Join = true }.Bordered(MarkupText.Plain("a-b-c-d-e-f-g-h"), BorderStyle.Ascii);

		await Assert.That(Lines(frame, 24)[0]).IsEqualTo("+--< a-b-c-d-e-f-g-h >-+");
	}

	[Test]
	public async Task AStyleWithoutTheJoinPieces_DrawsItsSides()
	{
		var border = new BorderStyle
		{
			Name = "plain",
			TopLeft = MarkupText.Plain("+"), Top = MarkupText.Plain("-"), TopRight = MarkupText.Plain("+"),
			Left = MarkupText.Plain("|"), Right = MarkupText.Plain("|"),
			BottomLeft = MarkupText.Plain("+"), Bottom = MarkupText.Plain("-"), BottomRight = MarkupText.Plain("+"),
		};
		var frame = new Flex([Panes(T("a"), T("b")), T("c")]) { Join = true }.Bordered(border: border);

		await Assert.That(Lines(frame, 20)).IsEquivalentTo(new[]
		{
			"+------------------+",
			"| a       | c      |",
			"|---------|        |",
			"| b       |        |",
			"+------------------+",
		});
	}

	[Test]
	public async Task SidesWiderThanACell_DrawTheRowUnjoined()
	{
		var border = BorderStyle.Ascii with { Left = MarkupText.Plain("||"), Right = MarkupText.Plain("||") };
		var frame = new Flex([Panes(T("a"), T("b")), T("c")]) { Join = true }.Bordered(border: border);

		var lines = Lines(frame, 20);
		await Assert.That(lines.All(line => DisplayWidth.Of(line) == 20)).IsTrue();
		await Assert.That(lines[0]).IsEqualTo("+------------------+");
	}

	[Test]
	public async Task ADividerWithItsOwnBorder_TakesItsOwnJunctions()
	{
		var frame = new Stack([new Flex([T("a"), T("b")]) { Join = true }, new Rule { Border = BorderStyle.Double }, T("c")])
			.Bordered(border: BorderStyle.Single);

		await Assert.That(Lines(frame, 13)[2]).IsEqualTo("├═════╩═════┤");
	}

	[Test]
	public async Task InHtml_APanesDividerTakesTheFramesBorder()
	{
		var html = BlockLayout.Build(Sheet(BorderStyle.Double), 44).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<div class=\"ms-text\">Stats</div><div class=\"ms-divider ms-border-double\" role=\"separator\"></div>");
	}

	[Test]
	public async Task InHtml_RowsTheTextDoesNotJoin_DrawAsPlainRows()
	{
		var row = new Flex([Panes(T("a"), T("b")), T("c")]) { Join = true };
		var borderless = BlockLayout.Build(row.Bordered(border: BorderStyle.None), 20).Render(MarkupFormat.Html, Registry);
		var vertical = BlockLayout.Build((row with { Vertical = true }).Bordered(border: BorderStyle.Single), 20).Render(MarkupFormat.Html, Registry);
		var bare = BlockLayout.Build(row, 20).Render(MarkupFormat.Html, Registry);

		foreach (var html in new[] { borderless, vertical, bare })
		{
			await Assert.That(html).DoesNotContain("ms-joined");
			await Assert.That(html).DoesNotContain("ms-divider");
		}
	}

	[Test]
	public async Task ARuleFirst_DrawsADividerUnderTheTopEdge()
	{
		var frame = new Stack([new Rule(), new Flex([T("Stats"), T("Bio")]) { Join = true }]).Bordered(border: BorderStyle.Single);

		await Assert.That(Lines(frame, 20)).IsEquivalentTo(new[]
		{
			"┌──────────────────┐",
			"├─────────┬────────┤",
			"│ Stats   │ Bio    │",
			"└─────────┴────────┘",
		});
	}
}
