using System.Collections.Immutable;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class ShapeTests
{
	private static ImmutableArray<TextLine> Shape(string text, ColumnFormat format) =>
		MarkupText.Plain(text).Shape(format);

	private static readonly ColumnFormat Wrapping = new() { Width = 20, Wrap = WrapMode.Word };

	// RhostMUSH's published output for $-20"|;5;s, which wraps continuation lines at width
	// minus indent rather than laying the indent on afterwards.
	[Test]
	public async Task Indent_WrapsContinuationLinesAtTheNarrowerWidth()
	{
		var lines = Shape("this is a test with wrapping some text", Wrapping with { Indent = new Indent(5) });

		await Assert.That(lines.Select(l => l.Text.Text).ToArray())
			.IsEquivalentTo(new[] { "this is a test with", "wrapping some", "text" });
		await Assert.That(lines.Select(l => l.Start).ToArray()).IsEquivalentTo(new[] { 0, 5, 5 });
	}

	[Test]
	public async Task Indent_FromLine_DelaysTheIndent()
	{
		var format = new ColumnFormat { Width = 8, Wrap = WrapMode.Word, Indent = new Indent(3, FromLine: 2) };

		var lines = Shape("aaa bbb ccc ddd eee fff", format);

		await Assert.That(lines[0].Start).IsEqualTo(0);
		await Assert.That(lines[1].Start).IsEqualTo(0);
		await Assert.That(lines[2].Start).IsEqualTo(3);
	}

	[Test]
	public async Task Indent_Widen_GrowsTheColumnFromThatLine()
	{
		var format = Wrapping with { Indent = new Indent(8, FromLine: 2, Widen: 8) };

		var lines = Shape("this is a test with wrapping some more text in it", format);

		await Assert.That(lines[0].Width).IsEqualTo(20);
		await Assert.That(lines[1].Width).IsEqualTo(20);
		await Assert.That(lines[2].Width).IsEqualTo(28);
		await Assert.That(lines[2].Start).IsEqualTo(8);
	}

	[Test]
	public async Task Indent_WiderThanTheColumn_StillLeavesRoomForText()
	{
		var lines = Shape("aaa bbb ccc", new ColumnFormat { Width = 4, Wrap = WrapMode.Word, Indent = new Indent(90) });

		await Assert.That(lines[1].Start).IsEqualTo(3);
		await Assert.That(lines.Length).IsLessThan(20);
	}

	[Test]
	public async Task MaxLines_StopsAfterN()
	{
		var lines = Shape("a b c d e f g h i j k", new ColumnFormat { Width = 3, Wrap = WrapMode.Word, MaxLines = 2 });

		await Assert.That(lines.Length).IsEqualTo(2);
	}

	[Test]
	public async Task MaxCells_CutsTheInputBeforeWrapping()
	{
		var lines = Shape("aaa bbb ccc ddd", new ColumnFormat { Width = 10, Wrap = WrapMode.Word, MaxCells = 5 });

		await Assert.That(lines.Select(l => l.Text.Text).ToArray()).IsEquivalentTo(new[] { "aaa b" });
	}

	[Test]
	public async Task TabWidth_IsAppliedBeforeWrapping()
	{
		var lines = Shape("a\tb", new ColumnFormat { Width = 3, Wrap = WrapMode.Cell, TabWidth = 4 });

		await Assert.That(lines.Select(l => l.Text.Text).ToArray()).IsEquivalentTo(new[] { "a  ", "  b" });
	}

	[Test]
	public async Task EndsParagraph_IsTrueOnlyBeforeAHardBreakOrTheEnd()
	{
		var lines = Shape("aaa bbb ccc\nddd", new ColumnFormat { Width = 10, Wrap = WrapMode.Word });

		await Assert.That(lines[0].EndsParagraph).IsFalse();
		await Assert.That(lines[1].EndsParagraph).IsTrue();
		await Assert.That(lines[2].EndsParagraph).IsTrue();
	}

	[Test]
	public async Task EndsParagraph_IsFalseOnALineTheBudgetCutShort()
	{
		var lines = Shape("aaa bbb ccc", new ColumnFormat { Width = 3, Wrap = WrapMode.Word, MaxLines = 1 });

		await Assert.That(lines[0].EndsParagraph).IsFalse();
	}

	[Test]
	public async Task Width_IsCarriedOnEveryLine()
	{
		var lines = Shape("aaa bbb ccc", Wrapping);

		foreach (var line in lines) await Assert.That(line.Width).IsEqualTo(20);
	}
}
