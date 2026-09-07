using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class ShapingTests
{
	// RhostMUSH's published output is the oracle: $-40#s on "a<tab>b<tab>c<tab>d<tab>e" gives
	// "a    b    c    d    e", four spaces per tab, and #7# gives seven. Literal substitution,
	// not alignment to tab stops.
	[Test]
	public async Task ExpandTabs_SubstitutesLiterally_NotToTabStops()
	{
		var text = MarkupText.Plain("a\tb\tc\td\te");

		await Assert.That(text.ExpandTabs(4).Text).IsEqualTo("a    b    c    d    e");
		await Assert.That(text.ExpandTabs(7).Text).IsEqualTo("a       b       c       d       e");
	}

	[Test]
	public async Task ExpandTabs_ZeroOrLess_LeavesTextAlone()
	{
		await Assert.That(MarkupText.Plain("a\tb").ExpandTabs(0).Text).IsEqualTo("a\tb");
		await Assert.That(MarkupText.Plain("a\tb").ExpandTabs(-1).Text).IsEqualTo("a\tb");
	}

	[Test]
	public async Task ExpandTabs_NoTabs_ReturnsTheSameInstance()
	{
		var text = MarkupText.Plain("abc");

		await Assert.That(ReferenceEquals(text.ExpandTabs(4), text)).IsTrue();
	}

	[Test]
	public async Task TruncateToWidth_MeasuresCells_NotCodeUnits()
	{
		var cjk = MarkupText.Plain("日本語abc");   // 6 code units, 9 cells

		await Assert.That(cjk.TruncateToWidth(4, CutFrom.End).Text).IsEqualTo("日本");
		await Assert.That(cjk.TruncateToWidth(4, CutFrom.Start).Text).IsEqualTo("abc");
	}

	[Test]
	public async Task TruncateToWidth_CutFromStart_KeepsTheTail()
	{
		// RhostMUSH's '*' option: $10*s on "this is a test" keeps " is a test".
		var text = MarkupText.Plain("this is a test");

		await Assert.That(text.TruncateToWidth(10, CutFrom.Start).Text).IsEqualTo(" is a test");
		await Assert.That(text.TruncateToWidth(10, CutFrom.End).Text).IsEqualTo("this is a ");
	}

	[Test]
	public async Task TruncateToWidth_AlreadyFits_ReturnsTheSameInstance()
	{
		var text = MarkupText.Plain("ab");

		await Assert.That(ReferenceEquals(text.TruncateToWidth(10, CutFrom.End), text)).IsTrue();
	}

	[Test]
	public async Task TruncateToWidth_ZeroOrLess_IsEmpty()
		=> await Assert.That(MarkupText.Plain("abc").TruncateToWidth(0, CutFrom.End).Length).IsEqualTo(0);

	[Test]
	public async Task TruncateToWidth_NeverSplitsACluster()
	{
		// Cutting at 1 cell cannot take half of a two-cell character.
		await Assert.That(MarkupText.Plain("日本").TruncateToWidth(1, CutFrom.End).Length).IsEqualTo(0);
		await Assert.That(MarkupText.Plain("日本").TruncateToWidth(1, CutFrom.Start).Length).IsEqualTo(0);
	}

	[Test]
	public async Task TruncateToWidth_KeepsMarkup()
	{
		var text = MarkupText.Concat(MarkupText.Plain("abc"), MarkupText.Wrap(new Tag("red"), "def"));

		var tail = text.TruncateToWidth(3, CutFrom.Start);

		await Assert.That(tail.Text).IsEqualTo("def");
		await Assert.That(tail.Runs.Length).IsEqualTo(1);
		await Assert.That(tail.Runs[0].Markups[0]).IsEqualTo(new Tag("red"));
	}

	private sealed record Tag(string Name) : IMarkup;
}
