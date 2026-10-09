using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

/// <summary>Several titles in one line or box edge: each at its side, the ones that do not fit left out in order.</summary>
public class EdgeTitleTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static string Line(Block block, int width) => BlockLayout.Build(block, width).ToPlainText();

	private static Rule LeftMiddleRight() => new(P("A"))
	{
		Border = BorderStyle.Mush,
		TitleAlignment = Alignment.Left,
		Titles = [new EdgeTitle(P("B")), new EdgeTitle(P("C"), Alignment.Right)],
	};

	private static Rule PoseHeader() => new(P("Wren"))
	{
		Border = BorderStyle.Mush,
		TitleAlignment = Alignment.Left,
		Titles = [new EdgeTitle(P("Scene 3"), Alignment.Right), new EdgeTitle(P("21:04"), Alignment.Right)],
	};

	[Test]
	public async Task OneLeft_OneInTheMiddle_OneRight()
		=> await Assert.That(Line(LeftMiddleRight(), 30)).IsEqualTo("=< A >=======< B >======< C >=");

	[Test]
	public async Task TwoOnTheRight_SitInOrder_ACellApart()
		=> await Assert.That(Line(PoseHeader(), 40)).IsEqualTo("=< Wren >=========< Scene 3 >=< 21:04 >=");

	[Test]
	public async Task TooNarrow_DropsTheRightTitleFarthestFromTheEnd()
		=> await Assert.That(Line(PoseHeader(), 31)).IsEqualTo("=< Wren >============< 21:04 >=");

	[Test]
	public async Task TooNarrow_DropsTheMiddleFirst()
		=> await Assert.That(Line(LeftMiddleRight(), 18)).IsEqualTo("=< A >======< C >=");

	[Test]
	public async Task TheMiddleTitle_MovesOver_ToKeepACellClear()
	{
		var rule = new Rule(P("Long left title"))
		{
			Border = BorderStyle.Mush,
			TitleAlignment = Alignment.Left,
			Titles = [new EdgeTitle(P("M"))],
		};

		await Assert.That(Line(rule, 30)).IsEqualTo("=< Long left title >=< M >====");
	}

	[Test]
	public async Task OneTitleLeft_IsDrawnAsASingleTitleAlwaysWas()
	{
		var rule = new Rule { Border = BorderStyle.Mush, Titles = [new EdgeTitle(P("T"), Alignment.Right)] };

		await Assert.That(Line(rule, 14)).IsEqualTo("========< T >=");
	}

	[Test]
	public async Task ABox_TakesTitlesInItsBottomEdge()
	{
		var box = new Frame(new TextBlock(P("x")))
		{
			Border = BorderStyle.Mush,
			BottomTitles = [new EdgeTitle(P("1/3"), Alignment.Right)],
		};

		await Assert.That(Line(box, 12).Split('\n')[^1]).IsEqualTo("+==< 1/3 >=+");
	}

	[Test]
	public async Task ABox_TakesSeveralTitlesInItsTopEdge()
	{
		var box = new Frame(new TextBlock(P("x")))
		{
			Border = BorderStyle.Mush,
			Title = P("Finger"),
			TitleAlignment = Alignment.Left,
			Titles = [new EdgeTitle(P("On"), Alignment.Right)],
		};

		await Assert.That(Line(box, 24).Split('\n')[0]).IsEqualTo("+=< Finger >====< On >=+");
	}

	[Test]
	public async Task AScreenReader_HearsTheTitlesLeftToRight()
	{
		var rule = new Rule(P("C")) { TitleAlignment = Alignment.Right, Titles = [new EdgeTitle(P("A"), Alignment.Left), new EdgeTitle(P("B"))] };

		var lines = BlockLayout.Lines(rule, 40, new LayoutContext { Linear = true });

		await Assert.That(lines.Select(line => line.ToPlainText())).IsEquivalentTo(new[] { "A, B, C" });
	}

	[Test]
	public async Task TheSerializer_KeepsEveryTitle()
	{
		var text = BlockLayout.Build(LeftMiddleRight(), 30, fluid: true);

		var json = MarkupTextSerializer.Serialize(text, Registry);
		var read = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(json).Contains("\"tt\"");
		await Assert.That(MarkupTextSerializer.Serialize(read, Registry)).IsEqualTo(json);
		await Assert.That(BlockLayout.Relayout(read, 18, LayoutContext.Default).ToPlainText()).IsEqualTo("=< A >======< C >=");
	}

	[Test]
	public async Task TheSerializer_KeepsABoxsBottomTitles()
	{
		var box = new Frame(new TextBlock(P("x"))) { Border = BorderStyle.Mush, BottomTitles = [new EdgeTitle(P("1/3"), Alignment.Right)] };

		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(BlockLayout.Build(box, 12), Registry), Registry);

		await Assert.That(BlockLayout.Relayout(read, 12, LayoutContext.Default).ToPlainText().Split('\n')[^1]).IsEqualTo("+==< 1/3 >=+");
	}

	[Test]
	public async Task InHtml_TheLineRunsBetweenTheTitles()
		=> await Assert.That(BlockLayout.Build(LeftMiddleRight(), 30).Render(MarkupFormat.Html, Registry)).Contains(
			"<div class=\"ms-rule ms-titles ms-border-mush\" role=\"separator\"><span class=\"ms-line ms-end\"></span><span class=\"ms-rule-title\">A</span>" +
			"<span class=\"ms-line\"></span><span class=\"ms-rule-title\">B</span><span class=\"ms-line\"></span><span class=\"ms-rule-title\">C</span>" +
			"<span class=\"ms-line ms-end\"></span></div>");

	[Test]
	public async Task InHtml_ABoxsBottomTitlesDrawItsBottomEdge()
	{
		var box = new Frame(new TextBlock(P("x"))) { Border = BorderStyle.Mush, BottomTitles = [new EdgeTitle(P("1/3"), Alignment.Right)] };

		await Assert.That(BlockLayout.Build(box, 12).Render(MarkupFormat.Html, Registry)).Contains(
			"<div class=\"ms-box-bottom ms-titles ms-border-mush\"><span class=\"ms-line\"></span><span class=\"ms-rule-title\">1/3</span><span class=\"ms-line ms-end\"></span></div></fieldset>");
	}

	[Test]
	public async Task APriority_KeepsATitleTheSideWouldDrop()
	{
		var rule = new Rule
		{
			Border = BorderStyle.Mush,
			Titles =
			[
				new EdgeTitle(P("Wren"), Alignment.Left) { Priority = 2 },
				new EdgeTitle(P("Pose 17")) { Priority = 1 },
				new EdgeTitle(P("Scene 3"), Alignment.Right),
			],
		};

		await Assert.That(Line(rule, 40)).IsEqualTo("=< Wren >======< Pose 17 >==< Scene 3 >=");
		await Assert.That(Line(rule, 26)).IsEqualTo("=< Wren >=< Pose 17 >=====").Because("the right title is 2 by its side, and among equals the right goes before the left");
		await Assert.That(Line(rule, 16)).IsEqualTo("===< Pose 17 >==");
	}

	[Test]
	public async Task TheSerializer_KeepsAPriority()
	{
		var rule = new Rule { Border = BorderStyle.Mush, Titles = [new EdgeTitle(P("A"), Alignment.Left) { Priority = 2 }, new EdgeTitle(P("B")) { Priority = 1 }] };

		var json = MarkupTextSerializer.Serialize(BlockLayout.Build(rule, 20, fluid: true), Registry);
		var read = MarkupTextSerializer.Deserialize(json, Registry);

		await Assert.That(json).Contains("\"p\":2");
		await Assert.That(BlockLayout.Relayout(read, 6, LayoutContext.Default).ToPlainText()).IsEqualTo("=< B >");
	}
}
