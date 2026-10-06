using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class BlockLayoutTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static LayoutNode Finger() => new BoxNode(
		new StackNode(
		[
			new FlexNode(
			[
				new FlexItem(new TextNode(P("Sex: Male\nSpecies: Human (Machinery Child)")), BlockSize.Cells(35)),
				new FlexItem(new TextNode(P("Job: Dark Warrior / Mad Scientist\nOrigin: Super Robot Wars AG\nOnline: 1h, Idle: 0s")), BlockSize.Cells(36)),
			], new FlexOptions { Separator = P(" | ") }),
			new RuleNode(P("Factions"), BorderStyle.Mush),
			new TextNode(P("MEDJAI, Court of Stardust")),
			new RuleNode(P("Quote"), BorderStyle.Mush),
			new TextNode(P("Hooooo?")),
		]),
		BorderStyle.Mush with { TitleOpen = P("<< "), TitleClose = P(" >>") },
		P("Mannaz Byron (Mannaz)"));

	private const string FingerText =
		"+=========================<< Mannaz Byron (Mannaz) >>========================+\n" +
		"| Sex: Male                           | Job: Dark Warrior / Mad Scientist    |\n" +
		"| Species: Human (Machinery Child)    | Origin: Super Robot Wars AG          |\n" +
		"|                                     | Online: 1h, Idle: 0s                 |\n" +
		"+================================< Factions >================================+\n" +
		"| MEDJAI, Court of Stardust                                                  |\n" +
		"+==================================< Quote >=================================+\n" +
		"| Hooooo?                                                                    |\n" +
		"+============================================================================+";

	[Test]
	public async Task Box_LaysOutAsAMushFingerBox()
	{
		var text = BlockLayout.Build(Finger(), 78);

		await Assert.That(text.ToPlainText()).IsEqualTo(FingerText);
		await Assert.That(text.Render(MarkupFormat.Ansi, Registry)).IsEqualTo(FingerText);
	}

	[Test]
	public async Task Box_DrawsAsStructureInHtml()
	{
		var html = BlockLayout.Build(Finger(), 78).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).StartsWith("<div class=\"ms-layout\" style=\"max-width:78ch\"><fieldset class=\"ms-box ms-border-mush\"><legend class=\"ms-box-title\">Mannaz Byron (Mannaz)</legend>");
		await Assert.That(html).Contains("<div class=\"ms-flex ms-divided\" style=\"column-gap:3ch\"><div class=\"ms-item\" style=\"flex:1 1 35ch\">");
		await Assert.That(html).Contains("<div class=\"ms-divider ms-border-mush\" role=\"separator\"><span class=\"ms-rule-title\">Factions</span></div>");
		await Assert.That(html).DoesNotContain("+===");
	}

	[Test]
	public async Task CellMarkup_SurvivesIntoTheHtml()
	{
		var red = MarkupText.Wrap(AnsiCodeParser.Parse("r"), "Hooooo?");
		var html = BlockLayout.Build(new BoxNode(new TextNode(red), BorderStyle.Single), 20).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<div class=\"ms-text\">" + red.Render(MarkupFormat.Html, Registry) + "</div>");
	}

	[Test]
	public async Task EditedBlock_RendersAsItsText()
	{
		var text = BlockLayout.Build(Finger(), 78);
		var edited = text.Replace(text.Text.IndexOf("Male", StringComparison.Ordinal), 4, P("Ma1e"));

		var html = edited.Render(MarkupFormat.Html, Registry);

		await Assert.That(html).DoesNotContain("ms-layout");
		await Assert.That(html).Contains("Sex: Ma1e");
	}

	[Test]
	public async Task SlicedBlock_RendersAsItsText()
	{
		var text = BlockLayout.Build(Finger(), 78);
		var firstLine = text.Substring(0, text.Text.IndexOf('\n'));

		await Assert.That(firstLine.Render(MarkupFormat.Html, Registry)).DoesNotContain("ms-layout");
	}

	[Test]
	public async Task BlockSharingALine_RendersAsItsText()
	{
		var text = MarkupText.Concat(P("Look: "), BlockLayout.Build(new RuleNode(P("Hi"), BorderStyle.Mush), 20));

		await Assert.That(text.Render(MarkupFormat.Html, Registry)).IsEqualTo("Look: =======&lt; Hi &gt;=======");
	}

	[Test]
	public async Task BlockOnLinesOfItsOwn_DrawsAmongOtherText()
	{
		var text = MarkupText.Join(MarkupText.NewLine, [P("Before"), BlockLayout.Build(new RuleNode(P("Hi"), BorderStyle.Mush), 20), P("After")]);

		await Assert.That(text.Render(MarkupFormat.Html, Registry)).IsEqualTo(
			"Before\n<div class=\"ms-layout\" style=\"max-width:20ch\"><div class=\"ms-rule ms-border-mush\" role=\"separator\"><span class=\"ms-rule-title\">Hi</span></div></div>\nAfter");
	}

	[Test]
	public async Task Serializer_RoundTripsTheTree()
	{
		var text = BlockLayout.Build(Finger(), 78, fluid: true);

		var json = MarkupTextSerializer.Serialize(text, Registry);
		var read = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(read.Text).IsEqualTo(text.Text);
		await Assert.That(read.Render(MarkupFormat.Html, Registry)).IsEqualTo(text.Render(MarkupFormat.Html, Registry));
		await Assert.That(MarkupTextSerializer.Serialize(read, Registry)).IsEqualTo(json);
		await Assert.That(BlockLayout.Relayout(read, 40, BlockRenderOptions.Default).ToPlainText())
			.IsEqualTo(BlockLayout.Build(Finger(), 40).ToPlainText());
	}

	[Test]
	public async Task Serializer_KeepsCustomBorderPieces()
	{
		var border = BorderStyle.Double with { Top = MarkupText.Wrap(AnsiCodeParser.Parse("b"), "=-") };
		var text = BlockLayout.Build(new BoxNode(new TextNode(P("x")), border, P("T")), 12);

		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, Registry), Registry);

		await Assert.That(BlockLayout.Relayout(read, 0, BlockRenderOptions.Default with { AsciiOnly = false }).Render(MarkupFormat.Ansi, Registry))
			.IsEqualTo(text.Render(MarkupFormat.Ansi, Registry));
		await Assert.That(text.ToPlainText().Split('\n')[0]).IsEqualTo("╔=-=╡ T ╞=-╗");
	}

	[Test]
	public async Task Relayout_FitsAFluidBlockToTheReader()
	{
		var text = BlockLayout.Build(new BoxNode(new TextNode(P("one two three four five six")), BorderStyle.Ascii), 40, fluid: true);

		var narrow = BlockLayout.Relayout(text, 16, BlockRenderOptions.Default);

		await Assert.That(narrow.ToPlainText()).IsEqualTo(
			"+--------------+\n" +
			"| one two      |\n" +
			"| three four   |\n" +
			"| five six     |\n" +
			"+--------------+");
	}

	[Test]
	public async Task Relayout_KeepsAFixedBlocksWidth()
	{
		var text = BlockLayout.Build(new RuleNode(null, BorderStyle.Ascii), 10);

		await Assert.That(BlockLayout.Relayout(text, 30, BlockRenderOptions.Default).ToPlainText()).IsEqualTo("----------");
	}

	[Test]
	public async Task Relayout_AsciiOnly_ReplacesBoxDrawing()
	{
		var text = BlockLayout.Build(new BoxNode(new TextNode(P("hi")), BorderStyle.Double, P("T")), 10);

		var ascii = BlockLayout.Relayout(text, 0, new BlockRenderOptions { AsciiOnly = true });

		await Assert.That(ascii.ToPlainText()).IsEqualTo(
			"+==< T >=+\n" +
			"| hi     |\n" +
			"+========+");
	}

	[Test]
	[Arguments("single", "+---+")]
	[Arguments("heavy", "+===+")]
	[Arguments("rounded", "+---+")]
	public async Task AsciiOnly_KeepsTheWeightOfTheLine(string preset, string top)
	{
		var text = BlockLayout.Build(new BoxNode(new TextNode(P("x")), BorderStyle.Preset(preset)!), 5);

		var ascii = BlockLayout.Relayout(text, 0, new BlockRenderOptions { AsciiOnly = true });

		await Assert.That(ascii.ToPlainText().Split('\n')[0]).IsEqualTo(top);
	}

	/// <summary>A piece that is not box drawing has no stand-in to guess at, so it takes the plain ASCII one.</summary>
	[Test]
	public async Task AsciiOnly_ReplacesAPieceItCannotTranslate()
	{
		var style = BorderStyle.Single with { Top = P("★") };
		var text = BlockLayout.Build(new BoxNode(new TextNode(P("x")), style), 5);

		await Assert.That(BlockLayout.Relayout(text, 0, new BlockRenderOptions { AsciiOnly = true }).ToPlainText().Split('\n')[0])
			.IsEqualTo("+---+");
	}

	[Test]
	public async Task AsciiOnly_TranslatesAFlexSeparator()
	{
		var flex = new FlexNode([new FlexItem(new TextNode(P("a")), BlockSize.Cells(3)), new FlexItem(new TextNode(P("b")), BlockSize.Cells(3))],
			FlexOptions.Default with { Separator = P(" │ ") });
		var text = BlockLayout.Build(flex, 9);

		await Assert.That(BlockLayout.Relayout(text, 0, new BlockRenderOptions { AsciiOnly = true }).ToPlainText()).IsEqualTo("a   | b  ");
	}

	[Test]
	public async Task Linear_ReadsTheContentInOrder()
	{
		var lines = BlockLayout.Lines(Finger(), 78, new BlockRenderOptions { Linear = true });

		await Assert.That(string.Join("\n", lines.Select(l => l.ToPlainText()))).IsEqualTo(
			"Mannaz Byron (Mannaz)\nSex: Male\nSpecies: Human (Machinery Child)\nJob: Dark Warrior / Mad Scientist\n" +
			"Origin: Super Robot Wars AG\nOnline: 1h, Idle: 0s\nFactions\nMEDJAI, Court of Stardust\nQuote\nHooooo?");
	}

	[Test]
	public async Task Flex_StacksWhenItemsDoNotFit()
	{
		var flex = new FlexNode(
		[
			new FlexItem(new TextNode(P("left")), BlockSize.Cells(10)),
			new FlexItem(new TextNode(P("right")), BlockSize.Cells(10)),
		], FlexOptions.Default);

		await Assert.That(string.Join("|", BlockLayout.Lines(flex, 15).Select(l => l.ToPlainText()))).IsEqualTo("left           |right          ");
		await Assert.That(string.Join("|", BlockLayout.Lines(flex, 22).Select(l => l.ToPlainText()))).IsEqualTo("left        right     ");
	}

	[Test]
	public async Task Flex_AutoItemsShareTheRest()
	{
		var flex = new FlexNode(
		[
			new FlexItem(new TextNode(P("a")), BlockSize.Cells(4)),
			new FlexItem(new TextNode(P("b"))),
			new FlexItem(new TextNode(P("c")), Grow: 2),
		], new FlexOptions { Gap = 1 });

		// 20 cells, two gaps: 18 to share. 4 fixed; 14 split 1:2 as 4 and 9, the odd cell to the first.
		await Assert.That(BlockLayout.Lines(flex, 20)[0].ToPlainText()).IsEqualTo("a    b     c        ");
	}

	[Test]
	public async Task Figure_TextFlowsRoundTheArt()
	{
		var figure = new FigureNode(
			new ImageMarkup("https://example.com/cat.png", "A cat"),
			P("/\\_/\\\n( o.o )\n > ^ <"),
			FigureFloat.Left,
			new TextNode(P("The cat sits by the fire and watches the door all night long.")),
			Gap: 1);

		var lines = BlockLayout.Lines(figure, 24).Select(l => l.ToPlainText()).ToArray();

		await Assert.That(string.Join("\n", lines)).IsEqualTo(
			"/\\_/\\   The cat sits by \n" +
			"( o.o ) the fire and    \n" +
			" > ^ <  watches the door\n" +
			"all night long.         ");
	}

	[Test]
	public async Task Figure_InHtml_FloatsThePicture()
	{
		var figure = new FigureNode(new ImageMarkup("https://example.com/cat.png", "A \"cat\""), P("art"), FigureFloat.Right, new TextNode(P("Text")));

		var html = BlockLayout.Build(figure, 30).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<div class=\"ms-figure ms-float-right\"><img class=\"ms-figure-image\" src=\"https://example.com/cat.png\" alt=\"A &quot;cat&quot;\"><div class=\"ms-text\">Text</div></div>");
	}

	[Test]
	public async Task Figure_RefusedPicture_ShowsItsArt()
	{
		var registry = Registry.WithLayoutImages(source => !source.Contains("example.com", StringComparison.Ordinal));
		var figure = new FigureNode(new ImageMarkup("https://example.com/cat.png", "A cat"), P("=^.^="));

		var html = BlockLayout.Build(figure, 30).Render(MarkupFormat.Html, registry);

		await Assert.That(html).Contains("<pre class=\"ms-figure-art\" role=\"img\" aria-label=\"A cat\">=^.^=</pre>");
		await Assert.That(html).DoesNotContain("<img");
	}

	[Test]
	public async Task Figure_ScriptAddress_IsNeverShown()
	{
		var figure = new FigureNode(new ImageMarkup("javascript:alert(1)", "x"), MarkupText.Empty);

		await Assert.That(BlockLayout.Build(figure, 30).Render(MarkupFormat.Html, Registry)).DoesNotContain("javascript");
	}

	[Test]
	[Arguments("/images/cat.png", true)]
	[Arguments("images/cat.png", true)]
	[Arguments("//elsewhere.example/cat.png", false)]
	[Arguments("file:///etc/passwd", false)]
	public async Task Figure_RelativeAddresses_AreTheGamesOwn(string source, bool shown)
	{
		var figure = new FigureNode(new ImageMarkup(source, "x"), MarkupText.Empty);

		var html = BlockLayout.Build(figure, 30).Render(MarkupFormat.Html, Registry);
		await Assert.That(html.Contains("<img")).IsEqualTo(shown).Because(html);
	}

	/// <summary>
	/// Two layouts that draw the same text are still two layouts: one never takes the other's place,
	/// as an interned run's layer would if they compared equal.
	/// </summary>
	[Test]
	public async Task LookAlikeLayouts_KeepTheirOwnTrees()
	{
		var shown = BlockLayout.Build(new FigureNode(new ImageMarkup("https://example.com/a.png", "x"), MarkupText.Empty), 30);
		var refused = BlockLayout.Build(new FigureNode(new ImageMarkup("file:///a.png", "x"), MarkupText.Empty), 30);

		await Assert.That(refused.Render(MarkupFormat.Html, Registry)).DoesNotContain("<img");
		await Assert.That(shown.Render(MarkupFormat.Html, Registry)).Contains("src=\"https://example.com/a.png\"");
	}

	[Test]
	public async Task TheSameBoxTwice_IsTwoBoxes()
	{
		var box = new BoxNode(new TextNode(P("Hi")), BorderStyle.Ascii);
		var html = MarkupText.Concat([BlockLayout.Build(box, 10), P("\n"), BlockLayout.Build(box, 10)]).Render(MarkupFormat.Html, Registry);

		await Assert.That(html.Split("<fieldset").Length - 1).IsEqualTo(2);
	}

	[Test]
	[Arguments(Alignment.Left, "=< T >========")]
	[Arguments(Alignment.Right, "========< T >=")]
	[Arguments(Alignment.Center, "=====< T >====")]
	public async Task Rule_TitleToOneSide_StaysACellIn(Alignment alignment, string expected)
		=> await Assert.That(BlockLayout.Build(new RuleNode(P("T"), BorderStyle.Mush, alignment), 14).ToPlainText()).IsEqualTo(expected);

	[Test]
	public async Task AsNode_AdoptsAWholeBlock()
	{
		var inner = BlockLayout.Build(new RuleNode(P("Inner"), BorderStyle.Ascii), 30);

		await Assert.That(BlockLayout.AsNode(inner)).IsTypeOf<RuleNode>();
		await Assert.That(BlockLayout.AsNode(MarkupText.Concat(inner, P("!")))).IsTypeOf<TextNode>();

		// Adopted as a rule, it becomes the box's divider rather than a quoted line of text.
		var outer = BlockLayout.Build(new BoxNode(BlockLayout.AsNode(inner), BorderStyle.Ascii), 20);
		await Assert.That(outer.ToPlainText()).IsEqualTo(
			"+------------------+\n" +
			"+-----< Inner >----+\n" +
			"+------------------+");
	}

	[Test]
	public async Task RepeatedBlock_MergesAndFallsBackToText()
	{
		var rule = BlockLayout.Build(new RuleNode(null, BorderStyle.Ascii), 4);
		var twice = MarkupText.Concat(rule, rule);

		await Assert.That(twice.Render(MarkupFormat.Html, Registry)).IsEqualTo("--------");
	}

	[Test]
	public async Task BorderStyle_PresetsAreFoundByName()
	{
		await Assert.That(BorderStyle.Preset("DOUBLE")).IsSameReferenceAs(BorderStyle.Double);
		await Assert.That(BorderStyle.Preset("nope")).IsNull();
		await Assert.That(BorderStyle.Mush.IsAscii).IsTrue();
		await Assert.That(BorderStyle.Rounded.IsAscii).IsFalse();
	}

	[Test]
	[Arguments("auto", BlockSizeKind.Auto, 0)]
	[Arguments("35", BlockSizeKind.Cells, 35)]
	[Arguments("40%", BlockSizeKind.Percent, 40)]
	public async Task BlockSize_Parses(string text, BlockSizeKind kind, int value)
	{
		await Assert.That(BlockSize.TryParse(text, out var size)).IsTrue();
		await Assert.That(size.Kind).IsEqualTo(kind);
		await Assert.That(size.Value).IsEqualTo(value);
	}

	[Test]
	[Arguments("-3")]
	[Arguments("101%")]
	[Arguments("wide")]
	public async Task BlockSize_RefusesNonsense(string text) =>
		await Assert.That(BlockSize.TryParse(text, out _)).IsFalse();

	[Test]
	public async Task Nodes_SplitsBlocksFromTheTextAroundThem()
	{
		var rule = BlockLayout.Build(new RuleNode(P("Quote"), BorderStyle.Mush), 20);
		var body = MarkupText.Join(MarkupText.NewLine, [P("Intro"), rule, P("Hooooo?")]);

		var nodes = BlockLayout.Nodes(body);

		await Assert.That(nodes.Count).IsEqualTo(3);
		await Assert.That(((TextNode)nodes[0]).Content.ToPlainText()).IsEqualTo("Intro");
		await Assert.That(nodes[1]).IsTypeOf<RuleNode>();
		await Assert.That(((TextNode)nodes[2]).Content.ToPlainText()).IsEqualTo("Hooooo?");
		await Assert.That(BlockLayout.Nodes(MarkupText.Concat(P("x "), rule)).Single()).IsTypeOf<TextNode>();
	}
}
