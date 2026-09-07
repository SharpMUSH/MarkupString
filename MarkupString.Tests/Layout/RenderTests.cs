using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class RenderTests
{
	private sealed record Tag(string Name) : IMarkup;

	private static ColumnFormat Filled(int width, Alignment alignment) => new()
	{
		Width = width, Alignment = alignment, Fill = MarkupText.Plain("0123456789"),
	};

	private static string One(string text, ColumnFormat format) =>
		MarkupText.Plain(text).FormatColumn(format)[0].Text;

	// The four oracles below are RhostMUSH's own published output for a ten-character filler.
	[Test]
	public async Task Left_ContinuesThePatternPastTheText()
		=> await Assert.That(One("ten char filler", Filled(40, Alignment.Left)))
			.IsEqualTo("ten char filler5678901234567890123456789");

	[Test]
	public async Task Right_FillsFromColumnZero()
		=> await Assert.That(One("ten char filler", Filled(40, Alignment.Right)))
			.IsEqualTo("0123456789012345678901234ten char filler");

	[Test]
	public async Task Center_ResumesThePatternAfterTheText()
		=> await Assert.That(One("ten char filler", Filled(40, Alignment.Center)))
			.IsEqualTo("012345678901ten char filler7890123456789");

	[Test]
	public async Task Full_ShowsThePatternThroughTheGaps()
		=> await Assert.That(One("ten char filler", Filled(40, Alignment.Full)))
			.IsEqualTo("ten34567890123456char1234567890123filler");

	[Test]
	public async Task Restart_ReproducesTheOnePointXFill()
		=> await Assert.That(One("ten char filler", Filled(40, Alignment.Left) with { FillPhase = FillPhase.Restart }))
			.IsEqualTo("ten char filler0123456789012345678901234");

	// RhostMUSH's $_40s on "this is a test".
	[Test]
	public async Task Full_WidensTheGapsBetweenWords()
		=> await Assert.That(One("this is a test", new ColumnFormat { Width = 40, Alignment = Alignment.Full }))
			.IsEqualTo("this          is          a         test");

	[Test]
	public async Task Full_SingleWord_FallsBackToLeft()
		=> await Assert.That(One("word", new ColumnFormat { Width = 10, Alignment = Alignment.Full }))
			.IsEqualTo("word      ");

	[Test]
	public async Task Paragraph_JustifiesEveryLineButTheParagraphsLast()
	{
		var format = new ColumnFormat { Width = 20, Wrap = WrapMode.Word, Alignment = Alignment.Paragraph };

		var lines = MarkupText.Plain("aaa bbb ccc ddd eee fff ggg").FormatColumn(format);

		// The first line is pushed out to the full width; the last keeps its single spaces.
		await Assert.That(lines[0].DisplayWidth).IsEqualTo(20);
		await Assert.That(lines[0].Text.Contains("  ")).IsTrue();
		await Assert.That(lines[^1].DisplayWidth).IsEqualTo(20);
		await Assert.That(lines[^1].Text.TrimEnd()).IsEqualTo("fff ggg");
	}

	// RhostMUSH's default is to cut; '*' keeps the right-hand end instead.
	[Test]
	public async Task Truncate_CutsFromTheChosenEnd()
	{
		var format = new ColumnFormat { Width = 10 };

		await Assert.That(One("this is a test", format)).IsEqualTo("this is a ");
		await Assert.That(One("this is a test", format with { CutFrom = CutFrom.Start })).IsEqualTo(" is a test");
	}

	[Test]
	public async Task Overflow_KeepsTheWholeValue()
		=> await Assert.That(One("this is a test", new ColumnFormat { Width = 10, Truncation = TruncationType.Overflow }))
			.IsEqualTo("this is a test");

	[Test]
	public async Task NoFill_StopsAtTheText()
		=> await Assert.That(One("ab", new ColumnFormat { Width = 10, NoFill = true })).IsEqualTo("ab");

	[Test]
	public async Task NoFill_LeavesABlankLineEmpty()
	{
		var format = new ColumnFormat { Width = 6, Wrap = WrapMode.HardBreaks, NoFill = true };

		await Assert.That(MarkupText.Plain("a\n\nb").FormatColumn(format)[1].Length).IsEqualTo(0);
	}

	[Test]
	public async Task BlankLineFill_Spaces_LeavesBlankLinesUnpatterned()
	{
		var format = Filled(6, Alignment.Left) with { Wrap = WrapMode.HardBreaks, BlankLineFill = BlankLineFill.Spaces };

		var lines = MarkupText.Plain("a\n\nb").FormatColumn(format);

		await Assert.That(lines[1].Text).IsEqualTo("      ");
	}

	[Test]
	public async Task BlankLineFill_Pattern_FillsBlankLinesToo()
	{
		var format = Filled(6, Alignment.Left) with { Wrap = WrapMode.HardBreaks };

		await Assert.That(MarkupText.Plain("a\n\nb").FormatColumn(format)[1].Text).IsEqualTo("012345");
	}

	[Test]
	public async Task Indent_IsFilled_NotSpaced()
	{
		var format = Filled(20, Alignment.Left) with { Wrap = WrapMode.Word, Indent = new Indent(5) };

		var lines = MarkupText.Plain("this is a test with wrapping some text").FormatColumn(format);

		await Assert.That(lines[1].Text.StartsWith("01234")).IsTrue();
	}

	[Test]
	public async Task FillRight_IsUsedAfterTheText()
	{
		var format = new ColumnFormat
		{
			Width = 8, Alignment = Alignment.Center,
			Fill = MarkupText.Plain("<"), FillRight = MarkupText.Plain(">"),
		};

		await Assert.That(One("ab", format)).IsEqualTo("<<<ab>>>");
	}

	[Test]
	public async Task EveryLine_IsExactlyTheColumnWidth()
	{
		var format = new ColumnFormat { Width = 12, Wrap = WrapMode.Word };

		foreach (var line in MarkupText.Plain("the quick brown fox jumps over the lazy dog").FormatColumn(format))
			await Assert.That(line.DisplayWidth).IsEqualTo(12);
	}

	[Test]
	public async Task Markup_WrapsTheWholeLine()
	{
		var format = new ColumnFormat { Width = 6, Markup = new Tag("red") };

		var line = MarkupText.Plain("ab").FormatColumn(format)[0];

		await Assert.That(line.Text).IsEqualTo("ab    ");
		await Assert.That(line.Runs.Length).IsEqualTo(1);
		await Assert.That(line.Runs[0].Markups[0]).IsEqualTo(new Tag("red"));
	}
}
