using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class FillTests
{
	private static readonly MarkupText Digits = MarkupText.Plain("0123456789");

	// RhostMUSH's $-40:0123456789:s on a fifteen-cell value resumes the pattern at 5.
	[Test]
	public async Task Continuous_SamplesAtTheAbsolutePosition()
		=> await Assert.That(FillPattern.Slice(Digits, 15, 25, FillPhase.Continuous).Text)
			.IsEqualTo("5678901234567890123456789");

	[Test]
	public async Task Restart_SamplesFromZero()
		=> await Assert.That(FillPattern.Slice(Digits, 15, 25, FillPhase.Restart).Text)
			.IsEqualTo("0123456789012345678901234");

	[Test]
	public async Task Continuous_AtAWholeMultiple_IsTheSameAsRestart()
		=> await Assert.That(FillPattern.Slice(Digits, 20, 10, FillPhase.Continuous).Text)
			.IsEqualTo(FillPattern.Slice(Digits, 0, 10, FillPhase.Restart).Text);

	[Test]
	public async Task SingleCharacterFill_IsUnaffectedByPhase()
	{
		var dot = MarkupText.Plain(".");

		await Assert.That(FillPattern.Slice(dot, 7, 4, FillPhase.Continuous).Text).IsEqualTo("....");
		await Assert.That(FillPattern.Slice(dot, 7, 4, FillPhase.Restart).Text).IsEqualTo("....");
	}

	[Test]
	public async Task WideFill_TakesASpaceForTheCellItCannotExpress()
		=> await Assert.That(FillPattern.Slice(MarkupText.Plain("日"), 0, 3, FillPhase.Restart).Text)
			.IsEqualTo("日 ");

	[Test]
	public async Task ZeroWidthFill_TakesSpaces()
		=> await Assert.That(FillPattern.Slice(MarkupText.Empty, 0, 3, FillPhase.Continuous).Text)
			.IsEqualTo("   ");

	[Test]
	public async Task NoCells_IsEmpty()
		=> await Assert.That(FillPattern.Slice(Digits, 3, 0, FillPhase.Continuous).Length).IsEqualTo(0);

	[Test]
	public async Task Markup_OnTheFill_Survives()
	{
		var marked = MarkupText.Wrap(new Tag("red"), "ab");

		var slice = FillPattern.Slice(marked, 1, 4, FillPhase.Continuous);

		await Assert.That(slice.Text).IsEqualTo("baba");
		await Assert.That(slice.Runs[0].Markups[0]).IsEqualTo(new Tag("red"));
	}

	private sealed record Tag(string Name) : IMarkup;
}
