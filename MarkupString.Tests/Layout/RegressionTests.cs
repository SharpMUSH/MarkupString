using TUnit.Assertions.Enums;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

/// <summary>
/// The defects that motivated the engine, kept honest, plus the invariants the layout promises.
/// The first two were verified against SharpMUSH's hand-rolled column code before this existed.
/// </summary>
public class RegressionTests
{
	[Test]
	public async Task ClusterWiderThanTheColumn_Terminates()
	{
		// A width of one cell against two-cell characters: the old code took no cells, made no
		// progress, and align(1, <a wide character>) never returned. The walk now advances a
		// cluster at a time, so it terminates. The column is still too narrow to draw a two-cell
		// character in one cell, and truncation blanks it rather than breaking the row's width —
		// the caller gave it nowhere to go.
		var format = new ColumnFormat { Width = 1, Wrap = WrapMode.Cell };

		var lines = MarkupText.Plain("日本語").FormatColumn(format);

		await Assert.That(lines.Length).IsEqualTo(3);
		foreach (var line in lines) await Assert.That(line.DisplayWidth).IsEqualTo(1);

		// One cell more and the text comes through intact.
		var wider = MarkupText.Plain("日本語").FormatColumn(format with { Width = 2 });

		await Assert.That(wider.Select(l => l.Text).ToArray()).IsEquivalentTo(new[] { "日", "本", "語" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task NewlinePastTheColumnWidth_StillBreaks()
	{
		// The old code compared a code-unit index against a cell count, so a newline at or past
		// the width stopped counting as a break and leaked into the rendered line.
		var lines = MarkupText.Plain("aaaa\nbbbb").FormatColumn(new ColumnFormat { Width = 4, Wrap = WrapMode.Word });

		await Assert.That(lines.Length).IsEqualTo(2);
		await Assert.That(lines[0].Text.Contains('\n')).IsFalse();
		await Assert.That(lines[1].Text).IsEqualTo("bbbb");
	}

	[Test]
	public async Task EveryLine_IsExactlyTheColumnWidth()
	{
		var format = new ColumnFormat { Width = 9, Wrap = WrapMode.Word };

		foreach (var line in MarkupText.Plain("the quick brown fox jumps over the lazy dog").FormatColumn(format))
			await Assert.That(line.DisplayWidth).IsEqualTo(9);
	}

	[Test]
	public async Task EveryLine_IsExactlyTheColumnWidth_EvenWithWideCharacters()
	{
		var format = new ColumnFormat { Width = 7, Wrap = WrapMode.Cell };

		foreach (var line in MarkupText.Plain("日本語です、これはテストです").FormatColumn(format))
			await Assert.That(line.DisplayWidth).IsEqualTo(7);
	}

	[Test]
	public async Task EveryRow_OfAMixedLayout_HasTheSameWidth()
	{
		var rows = TextLayout.Rows(
		[
			new LayoutColumn(MarkupText.Plain("the quick brown fox"), new ColumnFormat { Width = 8, Wrap = WrapMode.Word }),
			new LayoutSeparator(MarkupText.Plain(" | ")),
			new LayoutColumn(MarkupText.Plain("日本語です、これは"), new ColumnFormat { Width = 6, Wrap = WrapMode.Cell }),
			new LayoutSeparator(MarkupText.Plain(" | ")),
			new LayoutColumn(MarkupText.Plain("a\nb"), new ColumnFormat { Width = 4, Wrap = WrapMode.HardBreaks }),
		], new LayoutOptions());

		await Assert.That(rows.Length).IsGreaterThan(1);
		foreach (var row in rows) await Assert.That(row.DisplayWidth).IsEqualTo(24);
	}

	[Test]
	public async Task Wrapping_NeverLosesOrDuplicatesAWord()
	{
		const string source = "the quick brown fox jumps over the lazy dog and keeps on going";
		var format = new ColumnFormat { Width = 11, Wrap = WrapMode.Word };

		var words = MarkupText.Plain(source).FormatColumn(format)
			.SelectMany(line => line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

		await Assert.That(string.Join(' ', words)).IsEqualTo(source);
	}

	[Test]
	public async Task Wrapping_TerminatesOnEveryWidthFromOneUpwards()
	{
		var text = MarkupText.Plain("日本 abc\nx  yz 日");

		for (var width = 1; width <= 12; width++)
		{
			var format = new ColumnFormat { Width = width, Wrap = WrapMode.Word };

			await Assert.That(text.FormatColumn(format).Length).IsLessThan(40);
		}
	}
}
