using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class BlockLayoutTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static TextBlock T(string text) => new(P(text));

	private static Block Finger() => new Stack(
	[
		new Flex(
		[
			T("Sex: Male\nSpecies: Human (Machinery Child)").Sized(BlockSize.Cells(35)),
			T("Job: Dark Warrior / Mad Scientist\nOrigin: Super Robot Wars AG\nOnline: 1h, Idle: 0s").Sized(BlockSize.Cells(36)),
		]) { Separator = P(" | ") },
		new Rule(P("Factions")) { Border = BorderStyle.Mush },
		P("MEDJAI, Court of Stardust"),
		new Rule(P("Quote")) { Border = BorderStyle.Mush },
		P("Hooooo?"),
	]).Bordered(P("Mannaz Byron (Mannaz)"), BorderStyle.Mush with { TitleOpen = P("<< "), TitleClose = P(" >>") });

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
		var html = BlockLayout.Build(new TextBlock(red).Bordered(border: BorderStyle.Single), 20).Render(MarkupFormat.Html, Registry);

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
		var text = MarkupText.Concat(P("Look: "), BlockLayout.Build(new Rule(P("Hi")) { Border = BorderStyle.Mush }, 20));

		await Assert.That(text.Render(MarkupFormat.Html, Registry)).IsEqualTo("Look: =======&lt; Hi &gt;=======");
	}

	[Test]
	public async Task BlockOnLinesOfItsOwn_DrawsAmongOtherText()
	{
		var text = MarkupText.Join(MarkupText.NewLine, [P("Before"), BlockLayout.Build(new Rule(P("Hi")) { Border = BorderStyle.Mush }, 20), P("After")]);

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
		await Assert.That(BlockLayout.Relayout(read, 40, LayoutContext.Default).ToPlainText())
			.IsEqualTo(BlockLayout.Build(Finger(), 40).ToPlainText());
	}

	[Test]
	public async Task Serializer_KeepsCustomBorderPieces()
	{
		var border = BorderStyle.Double with { Top = MarkupText.Wrap(AnsiCodeParser.Parse("b"), "=-") };
		var text = BlockLayout.Build(new TextBlock(P("x")).Bordered(P("T"), border), 12);

		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, Registry), Registry);

		await Assert.That(BlockLayout.Relayout(read, 0, LayoutContext.Default with { AsciiOnly = false }).Render(MarkupFormat.Ansi, Registry))
			.IsEqualTo(text.Render(MarkupFormat.Ansi, Registry));
		await Assert.That(text.ToPlainText().Split('\n')[0]).IsEqualTo("╔=-=╡ T ╞=-╗");
	}

	[Test]
	public async Task Relayout_FitsAFluidBlockToTheReader()
	{
		var text = BlockLayout.Build(new TextBlock(P("one two three four five six")).Bordered(border: BorderStyle.Ascii), 40, fluid: true);

		var narrow = BlockLayout.Relayout(text, 16, LayoutContext.Default);

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
		var text = BlockLayout.Build(new Rule { Border = BorderStyle.Ascii }, 10);

		await Assert.That(BlockLayout.Relayout(text, 30, LayoutContext.Default).ToPlainText()).IsEqualTo("----------");
	}

	[Test]
	public async Task Relayout_AsciiOnly_ReplacesBoxDrawing()
	{
		var text = BlockLayout.Build(new TextBlock(P("hi")).Bordered(P("T"), BorderStyle.Double), 10);

		var ascii = BlockLayout.Relayout(text, 0, new LayoutContext { AsciiOnly = true });

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
		var text = BlockLayout.Build(new TextBlock(P("x")).Bordered(border: BorderStyle.Preset(preset)!), 5);

		var ascii = BlockLayout.Relayout(text, 0, new LayoutContext { AsciiOnly = true });

		await Assert.That(ascii.ToPlainText().Split('\n')[0]).IsEqualTo(top);
	}

	/// <summary>A piece that is not box drawing has no stand-in to guess at, so it takes the plain ASCII one.</summary>
	[Test]
	public async Task AsciiOnly_ReplacesAPieceItCannotTranslate()
	{
		var style = BorderStyle.Single with { Top = P("★") };
		var text = BlockLayout.Build(new TextBlock(P("x")).Bordered(border: style), 5);

		await Assert.That(BlockLayout.Relayout(text, 0, new LayoutContext { AsciiOnly = true }).ToPlainText().Split('\n')[0])
			.IsEqualTo("+---+");
	}

	[Test]
	public async Task AsciiOnly_TranslatesAFlexSeparator()
	{
		var flex = new Flex([T("a").Sized(BlockSize.Cells(3)), T("b").Sized(BlockSize.Cells(3))]) { Separator = P(" │ ") };
		var text = BlockLayout.Build(flex, 9);

		await Assert.That(BlockLayout.Relayout(text, 0, new LayoutContext { AsciiOnly = true }).ToPlainText()).IsEqualTo("a   | b  ");
	}

	[Test]
	public async Task Linear_ReadsTheContentInOrder()
	{
		var lines = BlockLayout.Lines(Finger(), 78, new LayoutContext { Linear = true });

		await Assert.That(string.Join("\n", lines.Select(l => l.ToPlainText()))).IsEqualTo(
			"Mannaz Byron (Mannaz)\nSex: Male\nSpecies: Human (Machinery Child)\nJob: Dark Warrior / Mad Scientist\n" +
			"Origin: Super Robot Wars AG\nOnline: 1h, Idle: 0s\nFactions\nMEDJAI, Court of Stardust\nQuote\nHooooo?");
	}

	[Test]
	public async Task Flex_StacksWhenItemsDoNotFit()
	{
		var flex = new Flex(
		[
			T("left").Sized(BlockSize.Cells(10)),
			T("right").Sized(BlockSize.Cells(10)),
		]);

		await Assert.That(string.Join("|", BlockLayout.Lines(flex, 15).Select(l => l.ToPlainText()))).IsEqualTo("left           |right          ");
		await Assert.That(string.Join("|", BlockLayout.Lines(flex, 22).Select(l => l.ToPlainText()))).IsEqualTo("left        right     ");
	}

	[Test]
	public async Task Flex_AutoItemsShareTheRest()
	{
		var flex = new Flex(
		[
			T("a").Sized(BlockSize.Cells(4)),
			P("b"),
			T("c").Sized(grow: 2),
		])
		{ Gap = 1 };

		// 20 cells, two gaps: 18 to share. 4 fixed; 14 split 1:2 as 4 and 9, the odd cell to the first.
		await Assert.That(BlockLayout.Lines(flex, 20)[0].ToPlainText()).IsEqualTo("a    b     c        ");
	}

	[Test]
	public async Task Flex_PercentageItemsLeaveRoomForTheirSeparatorInHtml()
	{
		var flex = new Flex(
		[
			T("left").Sized(BlockSize.Percent(50)),
			T("right").Sized(BlockSize.Percent(50)),
		])
		{ Separator = P(" │ ") };

		var html = BlockLayout.Build(flex, 40).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("flex:1 1 calc(50% - 1.5ch)");
		await Assert.That(LayoutCss.Fixed).Contains(".ms-item { min-width: 0; box-sizing: border-box; }");
	}

	[Test]
	public async Task Figure_TextFlowsRoundTheArt()
	{
		var figure = new Figure(new ImageMarkup("https://example.com/cat.png", "A cat"), P("/\\_/\\\n( o.o )\n > ^ <"))
		{
			Float = FigureFloat.Left,
			Beside = P("The cat sits by the fire and watches the door all night long."),
			Gap = 1,
		};

		var lines = BlockLayout.Lines(figure, 24).Select(l => l.ToPlainText()).ToArray();

		await Assert.That(string.Join("\n", lines)).IsEqualTo(
			"/\\_/\\   The cat sits by \n" +
			"( o.o ) the fire and    \n" +
			" > ^ <  watches the door\n" +
			"all night long.         ");
	}

	/// <summary>A figure on its own takes the alignment around it, its art moving as one piece.</summary>
	[Test]
	[Arguments(Alignment.Left, "/\\_/\\       \n( o.o )     \n > ^ <      ")]
	[Arguments(Alignment.Center, "  /\\_/\\     \n  ( o.o )   \n   > ^ <    ")]
	[Arguments(Alignment.Right, "     /\\_/\\  \n     ( o.o )\n      > ^ < ")]
	public async Task Figure_OnItsOwn_TakesTheAlignmentAroundIt(Alignment alignment, string expected)
	{
		var figure = new Figure(new ImageMarkup("cat.png", "A cat"), P("/\\_/\\\n( o.o )\n > ^ <"));

		var lines = BlockLayout.Lines(figure.Aligned(alignment), 12).Select(l => l.ToPlainText());

		await Assert.That(string.Join("\n", lines)).IsEqualTo(expected);
	}

	[Test]
	public async Task Figure_WithNoArt_CentresItsDescription()
	{
		var figure = new Figure(new ImageMarkup("cat.png", "A cat"), MarkupText.Empty);

		await Assert.That(BlockLayout.Lines(figure.Aligned(Alignment.Center), 11)[0].ToPlainText().TrimEnd()).IsEqualTo("  [A cat]");
	}

	[Test]
	public async Task Figure_InHtml_FloatsThePicture()
	{
		var figure = new Figure(new ImageMarkup("https://example.com/cat.png", "A \"cat\""), P("art")) { Float = FigureFloat.Right, Beside = P("Text") };

		var html = BlockLayout.Build(figure, 30).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<div class=\"ms-figure ms-float-right\"><img class=\"ms-figure-image\" src=\"https://example.com/cat.png\" alt=\"A &quot;cat&quot;\"><div class=\"ms-text\">Text</div></div>");
	}

	[Test]
	public async Task Figure_AloneInABox_TheBoxFitsThePicture()
	{
		var picture = new Figure(new ImageMarkup("https://example.com/cat.png", "A cat"), MarkupText.Empty);
		var beside = picture with { Float = FigureFloat.Left, Beside = P("Text") };
		var refused = Registry.WithLayoutImages(source => !source.Contains("example.com", StringComparison.Ordinal));

		var fitted = BlockLayout.Build(new Frame(picture), 30).Render(MarkupFormat.Html, Registry);
		var withText = BlockLayout.Build(new Frame(beside), 30).Render(MarkupFormat.Html, Registry);
		var art = BlockLayout.Build(new Frame(picture with { Art = P("=^.^=") }), 30).Render(MarkupFormat.Html, refused);
		var tight = BlockLayout.Build(new Frame(P("Text")) { Padding = 0 }, 30).Render(MarkupFormat.Html, Registry);

		await Assert.That(fitted).Contains("<fieldset class=\"ms-box ms-border-single ms-box-picture\"><div class=\"ms-figure ms-float-none\">");
		await Assert.That(withText).Contains("<fieldset class=\"ms-box ms-border-single\">");
		await Assert.That(art).Contains("<fieldset class=\"ms-box ms-border-single\">");
		await Assert.That(tight).Contains("<fieldset class=\"ms-box ms-border-single\" style=\"--ms-pad:0ch\">");
		// The picture sits on the top of its line rather than its baseline, so no gap for descenders is left under it.
		await Assert.That(LayoutCss.Fixed).Contains(".ms-figure-image { max-width: 100%; height: auto; vertical-align: top; }");
		await Assert.That(LayoutCss.Fixed).Contains(".ms-box.ms-box-picture { padding: 0; width: fit-content; max-width: 100%; box-sizing: border-box; }");
		await Assert.That(LayoutCss.Fixed).Contains(".ms-box { margin: 0; padding: 0 var(--ms-pad, 1ch);");
	}

	[Test]
	public async Task Figure_RefusedPicture_ShowsItsArt()
	{
		var registry = Registry.WithLayoutImages(source => !source.Contains("example.com", StringComparison.Ordinal));
		var figure = new Figure(new ImageMarkup("https://example.com/cat.png", "A cat"), P("=^.^="));

		var html = BlockLayout.Build(figure, 30).Render(MarkupFormat.Html, registry);

		await Assert.That(html).Contains("<pre class=\"ms-figure-art\" role=\"img\" aria-label=\"A cat\">=^.^=</pre>");
		await Assert.That(html).DoesNotContain("<img");
	}

	[Test]
	public async Task Figure_ScriptAddress_IsNeverShown()
	{
		var figure = new Figure(new ImageMarkup("javascript:alert(1)", "x"), MarkupText.Empty);

		await Assert.That(BlockLayout.Build(figure, 30).Render(MarkupFormat.Html, Registry)).DoesNotContain("javascript");
	}

	[Test]
	[Arguments("/images/cat.png", true)]
	[Arguments("images/cat.png", true)]
	[Arguments("//elsewhere.example/cat.png", false)]
	[Arguments("file:///etc/passwd", false)]
	public async Task Figure_RelativeAddresses_AreTheGamesOwn(string source, bool shown)
	{
		var figure = new Figure(new ImageMarkup(source, "x"), MarkupText.Empty);

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
		var shown = BlockLayout.Build(new Figure(new ImageMarkup("https://example.com/a.png", "x"), MarkupText.Empty), 30);
		var refused = BlockLayout.Build(new Figure(new ImageMarkup("file:///a.png", "x"), MarkupText.Empty), 30);

		await Assert.That(refused.Render(MarkupFormat.Html, Registry)).DoesNotContain("<img");
		await Assert.That(shown.Render(MarkupFormat.Html, Registry)).Contains("src=\"https://example.com/a.png\"");
	}

	[Test]
	public async Task TheSameBoxTwice_IsTwoBoxes()
	{
		var box = new TextBlock(P("Hi")).Bordered(border: BorderStyle.Ascii);
		var html = MarkupText.Concat([BlockLayout.Build(box, 10), P("\n"), BlockLayout.Build(box, 10)]).Render(MarkupFormat.Html, Registry);

		await Assert.That(html.Split("<fieldset").Length - 1).IsEqualTo(2);
	}

	[Test]
	[Arguments(Alignment.Left, "=< T >========")]
	[Arguments(Alignment.Right, "========< T >=")]
	[Arguments(Alignment.Center, "=====< T >====")]
	public async Task Rule_TitleToOneSide_StaysACellIn(Alignment alignment, string expected)
		=> await Assert.That(BlockLayout.Build(new Rule(P("T")) { Border = BorderStyle.Mush, TitleAlignment = alignment }, 14).ToPlainText()).IsEqualTo(expected);

	[Test]
	public async Task AsBlock_AdoptsAWholeBlock()
	{
		var inner = BlockLayout.Build(new Rule(P("Inner")) { Border = BorderStyle.Ascii }, 30);

		await Assert.That(BlockLayout.AsBlock(inner)).IsTypeOf<Rule>();
		await Assert.That(BlockLayout.AsBlock(MarkupText.Concat(inner, P("!")))).IsTypeOf<TextBlock>();

		// Adopted as a rule, it becomes the box's divider rather than a quoted line of text.
		var outer = BlockLayout.Build(BlockLayout.AsBlock(inner).Bordered(border: BorderStyle.Ascii), 20);
		await Assert.That(outer.ToPlainText()).IsEqualTo(
			"+------------------+\n" +
			"+-----< Inner >----+\n" +
			"+------------------+");
	}

	[Test]
	public async Task RepeatedBlock_MergesAndFallsBackToText()
	{
		var rule = BlockLayout.Build(new Rule { Border = BorderStyle.Ascii }, 4);
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
	public async Task Blocks_SplitsBlocksFromTheTextAroundThem()
	{
		var rule = BlockLayout.Build(new Rule(P("Quote")) { Border = BorderStyle.Mush }, 20);
		var body = MarkupText.Join(MarkupText.NewLine, [P("Intro"), rule, P("Hooooo?")]);

		var nodes = BlockLayout.Blocks(body);

		await Assert.That(nodes.Count).IsEqualTo(3);
		await Assert.That(((TextBlock)nodes[0]).Content.ToPlainText()).IsEqualTo("Intro");
		await Assert.That(nodes[1]).IsTypeOf<Rule>();
		await Assert.That(((TextBlock)nodes[2]).Content.ToPlainText()).IsEqualTo("Hooooo?");
		await Assert.That(BlockLayout.Blocks(MarkupText.Concat(P("x "), rule)).Single()).IsTypeOf<TextBlock>();
	}
}
