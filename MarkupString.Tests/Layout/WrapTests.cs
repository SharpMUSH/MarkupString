using TUnit.Assertions.Enums;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class WrapTests
{
	private sealed record Tag(string Name) : IMarkup;

	private static string[] Texts(MarkupText[] lines) => lines.Select(l => l.Text).ToArray();

	private static string[] Shaped(string text, ColumnFormat format) =>
		MarkupText.Plain(text).Shape(format).Select(l => l.Text.Text).ToArray();

	// RhostMUSH's published output for $-|"10s on "this is wrapping the text".
	[Test]
	public async Task Word_BreaksAtTheLastFittingSpace()
	{
		var lines = MarkupText.Plain("this is wrapping the text").WrapLines(10, WrapMode.Word);

		await Assert.That(Texts(lines)).IsEquivalentTo(new[] { "this is", "wrapping", "the text" }, CollectionOrdering.Matching);
	}

	// RhostMUSH's published output for $|10s on the same string: it wraps mid-word.
	[Test]
	public async Task Cell_BreaksMidWord()
	{
		var lines = MarkupText.Plain("this is wrapping the text").WrapLines(10, WrapMode.Cell);

		await Assert.That(Texts(lines)).IsEquivalentTo(new[] { "this is wr", "apping the", " text" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Word_OverLongWord_FallsBackToACellBreak()
	{
		var lines = MarkupText.Plain("ab abcdefghijkl cd").WrapLines(6, WrapMode.Word);

		await Assert.That(Texts(lines)).IsEquivalentTo(new[] { "ab", "abcdef", "ghijkl", "cd" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task ClusterWiderThanTheColumn_StillAdvances()
	{
		var lines = MarkupText.Plain("日本語").WrapLines(1, WrapMode.Cell);

		await Assert.That(Texts(lines)).IsEquivalentTo(new[] { "日", "本", "語" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task CombiningMarks_MeasureAsCellsNotCodeUnits()
	{
		// "Text Editor" under a pile of combining marks: dozens of code units, 11 display cells.
		var text = MarkupText.Plain("T͆́͂ͯe͕͓ͨx̼̀ͣt̜̭̪͒̉͗ͦ͂ ͕̈E͈̬̮̥͒ͣd͚͖̭͚ͩ̃͌i̺͑ͬ͊ͯt̞͔̂̏͒ͨo̥͓ͤͤ͗r̮͖̼͙͐");

		await Assert.That(text.DisplayWidth).IsEqualTo(11);
		await Assert.That(text.Length).IsGreaterThan(60);
		await Assert.That(text.WrapLines(11, WrapMode.Cell).Length).IsEqualTo(1);
		await Assert.That(text.WrapLines(10, WrapMode.Cell).Length).IsEqualTo(2);
	}

	[Test]
	public async Task HardBreaks_BreakAndAreDropped()
	{
		var lines = MarkupText.Plain("aaaa\r\nbbbb\ncc").WrapLines(80, WrapMode.HardBreaks);

		await Assert.That(Texts(lines)).IsEquivalentTo(new[] { "aaaa", "bbbb", "cc" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task HardBreaks_DoNotWrap_HoweverLongTheLine()
	{
		var lines = MarkupText.Plain("aaaaaaaaaa\nbb").WrapLines(3, WrapMode.HardBreaks);

		await Assert.That(Texts(lines)).IsEquivalentTo(new[] { "aaaaaaaaaa", "bb" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task HardBreaks_ATrailingNewlineLeavesAnEmptyLine()
	{
		var lines = MarkupText.Plain("a\n").WrapLines(10, WrapMode.HardBreaks);

		await Assert.That(Texts(lines)).IsEquivalentTo(new[] { "a", "" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Word_HardBreakPastTheWidth_StillBreaks()
	{
		// The defect this replaces compared a code-unit index against a cell count, so a newline
		// at or past the width leaked into the line instead of ending it.
		var lines = MarkupText.Plain("aaaa\nbbbb").WrapLines(4, WrapMode.Word);

		await Assert.That(Texts(lines)).IsEquivalentTo(new[] { "aaaa", "bbbb" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task None_LeavesNewlinesAlone()
	{
		var lines = Shaped("aaaa\nbbbb", new ColumnFormat { Width = 2, Wrap = WrapMode.None });

		await Assert.That(lines).IsEquivalentTo(new[] { "aaaa\nbbbb" }, CollectionOrdering.Matching);
	}

	// PennMUSH cuts at the break space; the spaces before it stay on the line.
	[Test]
	public async Task BreakSpace_Drop_EndsTheLineAtTheSpace()
	{
		var lines = Shaped("ab   cd", new ColumnFormat { Width = 4, Wrap = WrapMode.Word });

		await Assert.That(lines).IsEquivalentTo(new[] { "ab  ", "cd" }, CollectionOrdering.Matching);
	}

	// RhostMUSH breaks one past the space, so the line can run a cell wide.
	[Test]
	public async Task BreakSpace_Keep_KeepsTheSpaceOnTheLine()
	{
		var format = new ColumnFormat { Width = 4, Wrap = WrapMode.Word, BreakSpace = BreakSpace.Keep };

		var lines = Shaped("ab   cd", format);

		await Assert.That(lines).IsEquivalentTo(new[] { "ab   ", "cd" }, CollectionOrdering.Matching);
		await Assert.That(MarkupText.Plain(lines[0]).DisplayWidth).IsEqualTo(5);
	}

	[Test]
	public async Task Markup_SurvivesABreak()
	{
		var text = MarkupText.Wrap(new Tag("red"), "hello world");

		var lines = text.WrapLines(5, WrapMode.Word);

		await Assert.That(Texts(lines)).IsEquivalentTo(new[] { "hello", "world" }, CollectionOrdering.Matching);
		await Assert.That(lines[1].Runs.Length).IsEqualTo(1);
		await Assert.That(lines[1].Runs[0].Markups[0]).IsEqualTo(new Tag("red"));
	}

	[Test]
	public async Task WrapLines_EmptyText_YieldsNoLines()
		=> await Assert.That(MarkupText.Empty.WrapLines(10).Length).IsEqualTo(0);

	[Test]
	public async Task WrapLines_NonPositiveWidth_YieldsTheTextUnbroken()
		=> await Assert.That(Texts(MarkupText.Plain("a b").WrapLines(0))).IsEquivalentTo(new[] { "a b" }, CollectionOrdering.Matching);

	[Test]
	public async Task Shape_EmptyText_YieldsOneEmptyLine()
	{
		var lines = MarkupText.Empty.Shape(new ColumnFormat { Width = 4, Wrap = WrapMode.Word });

		await Assert.That(lines.Length).IsEqualTo(1);
		await Assert.That(lines[0].Text.Length).IsEqualTo(0);
	}
}
