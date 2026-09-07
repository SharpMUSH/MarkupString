using TUnit.Assertions.Enums;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class LayoutTests
{
	private static readonly LayoutOptions Plain = new();

	private static string[] Rows(params LayoutCell[] cells) =>
		TextLayout.Rows(cells, Plain).Select(r => r.Text).ToArray();

	private static LayoutColumn Column(string text, int width, WrapMode wrap = WrapMode.None) =>
		new(MarkupText.Plain(text), new ColumnFormat { Width = width, Wrap = wrap });

	[Test]
	public async Task RaggedColumns_FillTheShorterOnesOut()
	{
		var rows = Rows(
			Column("a", 3),
			new LayoutSeparator(MarkupText.Plain("|")),
			Column("x y z", 3, WrapMode.Word));

		await Assert.That(rows).IsEquivalentTo(new[] { "a  |x y", "   |z  " }, CollectionOrdering.Matching);
	}

	// PennMUSH inserts its separator on every row.
	[Test]
	public async Task SeparatorRows_EveryRow_RepeatsIt()
	{
		var rows = Rows(
			Column("a", 3),
			new LayoutSeparator(MarkupText.Plain("|"), SeparatorRows.EveryRow),
			Column("x y z", 3, WrapMode.Word));

		await Assert.That(rows[1]).IsEqualTo("   |z  ");
	}

	// RhostMUSH blanks it: its continuation rows emit spaces the width of the literal.
	[Test]
	public async Task SeparatorRows_FirstRowOnly_BlanksItAfterwards()
	{
		var rows = Rows(
			Column("a", 3),
			new LayoutSeparator(MarkupText.Plain("|"), SeparatorRows.FirstRowOnly),
			Column("x y z", 3, WrapMode.Word));

		await Assert.That(rows).IsEquivalentTo(new[] { "a  |x y", "    z  " }, CollectionOrdering.Matching);
	}

	// PennMUSH's '.' option.
	[Test]
	public async Task Repeat_CyclesWhileAnotherColumnStillHasText()
	{
		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("*"), new ColumnFormat { Width = 1, Repeat = true }),
			Column("x y z", 1, WrapMode.Word));

		await Assert.That(rows).IsEquivalentTo(new[] { "*x", "*y", "*z" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task Repeat_DoesNotDriveTheRowCount()
	{
		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("a\nb\nc"), new ColumnFormat { Width = 1, Wrap = WrapMode.HardBreaks, Repeat = true }),
			Column("x", 1));

		await Assert.That(rows).IsEquivalentTo(new[] { "ax" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task AnExhaustedColumn_ContributesItsFill()
	{
		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("a"), new ColumnFormat { Width = 3, Fill = MarkupText.Plain(".") }),
			Column("x\ny", 1, WrapMode.HardBreaks));

		await Assert.That(rows).IsEquivalentTo(new[] { "a..x", "...y" }, CollectionOrdering.Matching);
	}

	private static LayoutColumn Empty(string text, int width, WhenEmpty whenEmpty) =>
		new(MarkupText.Plain(text), new ColumnFormat { Width = width, Wrap = WrapMode.HardBreaks, WhenEmpty = whenEmpty });

	// RhostMUSH's backtick option: printf($&-10s$&`-10s, 1%r2%r3%r4%r5%r6, A%rB%rC%rD) draws
	// rows 5 and 6 of the left column at the right column's position, leaving the left column's
	// own slot blank.
	[Test]
	public async Task PushLeftColumnRight_MovesTheLeftColumnsText()
	{
		var rows = Rows(
			Column("1\n2\n3\n4\n5\n6", 10, WrapMode.HardBreaks),
			Empty("A\nB\nC\nD", 10, WhenEmpty.PushLeftColumnRight));

		await Assert.That(rows[0]).IsEqualTo("1         A         ");
		await Assert.That(rows[3]).IsEqualTo("4         D         ");
		await Assert.That(rows[4]).IsEqualTo("          5         ");
		await Assert.That(rows[5]).IsEqualTo("          6         ");
	}

	// RhostMUSH's apostrophe option: printf($&-10's$&-68s, 1%r2%r3%r4, A%rB%rC%rD%rE%rF) draws
	// rows 5 and 6 of the right column at the left column's position.
	[Test]
	public async Task PullRightColumnLeft_MovesTheRightColumnsText()
	{
		var rows = Rows(
			Empty("1\n2\n3\n4", 10, WhenEmpty.PullRightColumnLeft),
			Column("A\nB\nC\nD\nE\nF", 10, WrapMode.HardBreaks));

		await Assert.That(rows[0]).IsEqualTo("1         A         ");
		await Assert.That(rows[4]).IsEqualTo("E                   ");
		await Assert.That(rows[5]).IsEqualTo("F                   ");
	}

	// PennMUSH's backtick option merges rather than shifting: the left column widens in place
	// and goes on wrapping into the room the empty column gave it.
	[Test]
	public async Task GiveSpaceToLeft_WidensTheLeftColumnInPlace()
	{
		var rows = Rows(
			Column("aa bb cc dd", 6, WrapMode.Word),
			Empty("X", 6, WhenEmpty.GiveSpaceToLeft));

		await Assert.That(rows[0]).IsEqualTo("aa bb X     ");
		await Assert.That(rows[1]).IsEqualTo("cc dd       ");
	}

	[Test]
	public async Task GiveSpaceToLeft_LeavesTheRowWidthAlone()
	{
		var rows = TextLayout.Rows(
		[
			Column("aa bb cc dd ee ff", 6, WrapMode.Word),
			Empty("X", 6, WhenEmpty.GiveSpaceToLeft),
		], Plain);

		foreach (var row in rows) await Assert.That(row.DisplayWidth).IsEqualTo(12);
	}

	// PennMUSH's apostrophe option merges the other way: the pair renders at the empty column's
	// position.
	[Test]
	public async Task GiveSpaceToRight_WidensTheRightColumnAtTheEmptyColumnsPosition()
	{
		var rows = Rows(
			Empty("X", 6, WhenEmpty.GiveSpaceToRight),
			Column("aa bb cc dd", 6, WrapMode.Word));

		await Assert.That(rows[0]).IsEqualTo("X     aa bb ");
		await Assert.That(rows[1]).IsEqualTo("cc dd       ");
	}

	[Test]
	public async Task WhenEmpty_WithNoNeighbour_IsIgnored()
	{
		var rows = Rows(Empty("a", 3, WhenEmpty.GiveSpaceToLeft));

		await Assert.That(rows).IsEquivalentTo(new[] { "a  " }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task AnEntirelyEmptyColumn_GivesItsSpaceFromTheFirstRow()
	{
		var rows = Rows(
			Column("aa bb cc", 6, WrapMode.Word),
			Empty("", 6, WhenEmpty.GiveSpaceToLeft));

		await Assert.That(rows[0]).IsEqualTo("aa bb cc    ");
	}

	// Every one of these is a row-width invariant, which the merge and shift paths broke while
	// the example-based tests above still passed.
	[Test]
	public async Task MergedColumn_ThatRunsOutOfLines_StillFillsItsWidenedWidth()
	{
		var rows = TextLayout.Rows(
		[
			new LayoutColumn(MarkupText.Plain("aaaa bbbb"), new ColumnFormat { Width = 6, Wrap = WrapMode.Word }),
			Empty("X", 6, WhenEmpty.GiveSpaceToLeft),
			Column("1\n2\n3\n4\n5", 4, WrapMode.HardBreaks),
		], Plain);

		await Assert.That(rows.Length).IsEqualTo(5);
		foreach (var row in rows) await Assert.That(row.DisplayWidth).IsEqualTo(16);
	}

	[Test]
	public async Task MergeOntoAnIndentedNeighbour_LeavesTheRowWidthAlone()
	{
		// The neighbour already carries an indent, so it cannot also carry the merge's widening —
		// a single Indent cannot express two different widths from two different lines. The merge
		// is abandoned rather than half-applied, which would drop the cells entirely.
		var rows = TextLayout.Rows(
		[
			new LayoutColumn(
				MarkupText.Plain("aa bb cc dd"),
				new ColumnFormat { Width = 6, Wrap = WrapMode.Word, Indent = new Indent(2) }),
			Empty("X", 6, WhenEmpty.GiveSpaceToLeft),
		], Plain);

		foreach (var row in rows) await Assert.That(row.DisplayWidth).IsEqualTo(12);
	}

	[Test]
	public async Task SuppressBlankLast_KeepsARowARepeatingColumnStillDrawsOn()
	{
		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("*"), new ColumnFormat { Width = 1, Repeat = true, SuppressBlankLast = true }),
			new LayoutColumn(
				MarkupText.Plain("a\nb\n"),
				new ColumnFormat { Width = 1, Wrap = WrapMode.HardBreaks, SuppressBlankLast = true }));

		await Assert.That(rows).IsEquivalentTo(new[] { "*a", "*b", "* " }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task EveryWhenEmpty_KeepsTheRowWidthConstant()
	{
		foreach (var whenEmpty in Enum.GetValues<WhenEmpty>())
		{
			var rows = TextLayout.Rows(
			[
				Column("aa bb cc dd ee", 6, WrapMode.Word),
				new LayoutColumn(
					MarkupText.Plain("X\nY"),
					new ColumnFormat { Width = 6, Wrap = WrapMode.HardBreaks, WhenEmpty = whenEmpty }),
				Column("1\n2\n3\n4", 4, WrapMode.HardBreaks),
			], Plain);

			foreach (var row in rows)
				await Assert.That(row.DisplayWidth).IsEqualTo(16).Because($"WhenEmpty.{whenEmpty}");
		}
	}

	[Test]
	public async Task SpaceOnlyLine_UnderFullJustification_StillFillsTheColumn()
	{
		// A line of nothing but spaces has no words, so there is no gap to widen. It must still
		// occupy its column rather than collapsing to nothing.
		var format = new ColumnFormat { Width = 6, Wrap = WrapMode.HardBreaks, Alignment = Alignment.Full };

		var lines = MarkupText.Plain("a\n   \nb").FormatColumn(format);

		foreach (var line in lines) await Assert.That(line.DisplayWidth).IsEqualTo(6);
	}

	[Test]
	public async Task ZeroWidthColumn_WithAnIndent_NeverStartsBeforeTheColumn()
	{
		var format = new ColumnFormat { Width = 0, Wrap = WrapMode.Word, Indent = new Indent(2) };

		foreach (var line in MarkupText.Plain("aa bb cc").Shape(format))
			await Assert.That(line.Start).IsGreaterThanOrEqualTo(0);

		foreach (var line in MarkupText.Plain("aa bb cc").FormatColumn(format))
			await Assert.That(line.DisplayWidth).IsEqualTo(0);
	}

	[Test]
	public async Task ChainedMerges_KeepTheRowWidthConstant()
	{
		// The middle column receives a merge and would also give one away. It cannot pass on
		// cells it does not own at its base width, so the merge out of it is abandoned and the
		// merge into it wins.
		var rows = TextLayout.Rows(
		[
			new LayoutColumn(MarkupText.Plain("aaa bbb ccc ddd eee"), new ColumnFormat { Width = 6, Wrap = WrapMode.Word }),
			new LayoutColumn(
				MarkupText.Plain("xx yy zz ww vv"),
				new ColumnFormat { Width = 6, Wrap = WrapMode.Word, WhenEmpty = WhenEmpty.GiveSpaceToLeft }),
			new LayoutColumn(
				MarkupText.Plain("Q"),
				new ColumnFormat { Width = 6, Wrap = WrapMode.HardBreaks, WhenEmpty = WhenEmpty.GiveSpaceToLeft }),
		], Plain);

		await Assert.That(rows.Length).IsGreaterThan(3);
		foreach (var row in rows) await Assert.That(row.DisplayWidth).IsEqualTo(18);
	}

	[Test]
	public async Task WhenEmpty_WithSuppressBlankLast_KeepsTheRowWidthConstant()
	{
		var rows = TextLayout.Rows(
		[
			new LayoutColumn(
				MarkupText.Plain("aa\n"),
				new ColumnFormat { Width = 4, Wrap = WrapMode.HardBreaks, SuppressBlankLast = true }),
			new LayoutColumn(
				MarkupText.Plain("X\n"),
				new ColumnFormat
				{
					Width = 4, Wrap = WrapMode.HardBreaks, SuppressBlankLast = true,
					WhenEmpty = WhenEmpty.GiveSpaceToLeft,
				}),
		], Plain);

		foreach (var row in rows) await Assert.That(row.DisplayWidth).IsEqualTo(8);
	}

	[Test]
	public async Task Render_JoinsRowsWithTheRowSeparator()
	{
		var cells = new LayoutCell[] { Column("a\nb", 1, WrapMode.HardBreaks) };

		await Assert.That(TextLayout.Render(cells, Plain).Text).IsEqualTo("a\nb");
	}

	[Test]
	public async Task Render_UsesACustomRowSeparator()
	{
		var cells = new LayoutCell[] { Column("a\nb", 1, WrapMode.HardBreaks) };

		var options = new LayoutOptions { RowSeparator = MarkupText.Plain(" / ") };

		await Assert.That(TextLayout.Render(cells, options).Text).IsEqualTo("a / b");
	}

	// RhostMUSH's full-stop option, which suppresses a trailing all-blank row only when every
	// field carries it.
	[Test]
	public async Task SuppressBlankLast_DropsAFinalAllBlankRow()
	{
		var format = new ColumnFormat { Width = 3, Wrap = WrapMode.HardBreaks, SuppressBlankLast = true };

		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("a\n"), format),
			new LayoutColumn(MarkupText.Plain("b\n"), format));

		await Assert.That(rows).IsEquivalentTo(new[] { "a  b  " }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task SuppressBlankLast_NeedsEveryColumnToCarryIt()
	{
		var on = new ColumnFormat { Width = 3, Wrap = WrapMode.HardBreaks, SuppressBlankLast = true };
		var off = on with { SuppressBlankLast = false };

		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("a\n"), on),
			new LayoutColumn(MarkupText.Plain("b\n"), off));

		await Assert.That(rows.Length).IsEqualTo(2);
	}

	[Test]
	public async Task SuppressBlankLast_KeepsARowThatStillHasText()
	{
		var format = new ColumnFormat { Width = 3, Wrap = WrapMode.HardBreaks, SuppressBlankLast = true };

		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("a\n"), format),
			new LayoutColumn(MarkupText.Plain("b\nc"), format));

		await Assert.That(rows).IsEquivalentTo(new[] { "a  b  ", "   c  " }, CollectionOrdering.Matching);
	}

	// PennMUSH's hash option.
	[Test]
	public async Task NoSeparatorAfter_SkipsTheFollowingSeparator()
	{
		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("a"), new ColumnFormat { Width = 1, NoSeparatorAfter = true }),
			new LayoutSeparator(MarkupText.Plain("|")),
			Column("b", 1));

		await Assert.That(rows).IsEquivalentTo(new[] { "ab" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task NoCells_YieldsNoRows()
		=> await Assert.That(TextLayout.Rows([], Plain).Length).IsEqualTo(0);

	[Test]
	public async Task EveryRow_HasTheSameDisplayWidth()
	{
		var rows = TextLayout.Rows(
		[
			Column("a b c d", 3, WrapMode.Word),
			Column("日本語です", 4, WrapMode.Cell),
		], Plain);

		foreach (var row in rows) await Assert.That(row.DisplayWidth).IsEqualTo(7);
	}
}
