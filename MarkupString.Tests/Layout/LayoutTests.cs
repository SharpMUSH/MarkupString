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

		await Assert.That(rows).IsEquivalentTo(new[] { "a  |x y", "   |z  " });
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

		await Assert.That(rows).IsEquivalentTo(new[] { "a  |x y", "    z  " });
	}

	// PennMUSH's '.' option.
	[Test]
	public async Task Repeat_CyclesWhileAnotherColumnStillHasText()
	{
		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("*"), new ColumnFormat { Width = 1, Repeat = true }),
			Column("x y z", 1, WrapMode.Word));

		await Assert.That(rows).IsEquivalentTo(new[] { "*x", "*y", "*z" });
	}

	[Test]
	public async Task Repeat_DoesNotDriveTheRowCount()
	{
		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("a\nb\nc"), new ColumnFormat { Width = 1, Wrap = WrapMode.HardBreaks, Repeat = true }),
			Column("x", 1));

		await Assert.That(rows).IsEquivalentTo(new[] { "ax" });
	}

	[Test]
	public async Task AnExhaustedColumn_ContributesItsFill()
	{
		var rows = Rows(
			new LayoutColumn(MarkupText.Plain("a"), new ColumnFormat { Width = 3, Fill = MarkupText.Plain(".") }),
			Column("x\ny", 1, WrapMode.HardBreaks));

		await Assert.That(rows).IsEquivalentTo(new[] { "a..x", "...y" });
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
