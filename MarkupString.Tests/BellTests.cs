using MarkupString.Ansi;
using MarkupString.Html;
namespace MarkupString.Tests;

/// <summary>
/// A bell is a point in the text, not a property of any of it: it asks the client to get someone's
/// attention where it sits, measures nothing, and survives every operation that carries the text.
/// </summary>
public class BellTests
{
	private const string Bel = BellMarkup.Character;

	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static string Render(MarkupText text, MarkupFormat format) => text.Render(format, Registry);

	[Test]
	public async Task ABellIsWrittenForEveryClientThatReadsOne()
	{
		var text = MarkupText.Concat(MarkupText.Plain("Hey"), MarkupText.Bell());

		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo("Hey" + Bel);
		await Assert.That(Render(text, MarkupFormat.Pueblo)).IsEqualTo("Hey" + Bel);
		await Assert.That(Render(text, MarkupFormat.Mxp)).IsEqualTo("Hey" + Bel);
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("Hey" + BellEmitter.HtmlElement);
	}

	/// <summary>
	/// Pueblo and MXP drop control characters from text, which is why the emitter writes the character
	/// rather than letting the body through: a bell has to survive the encoding that removes it.
	/// </summary>
	[Test]
	public async Task AControlCharacterInOrdinaryTextIsStillDropped()
	{
		await Assert.That(Render(MarkupText.Plain("Hey" + Bel), MarkupFormat.Pueblo)).IsEqualTo("Hey");
		await Assert.That(Render(MarkupText.Plain("Hey" + Bel), MarkupFormat.Html)).IsEqualTo("Hey");
	}

	[Test]
	public async Task AFormatWithNoBellLeavesNothingBehind()
	{
		var text = MarkupText.Concat(MarkupText.Plain("Hey"), MarkupText.Bell());

		await Assert.That(Render(text, MarkupFormat.Plain)).IsEqualTo("Hey");
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("Hey");
	}

	[Test]
	public async Task ABellMeasuresNothing()
	{
		var text = MarkupText.Concat(MarkupText.Bell(), MarkupText.Plain("ab"));

		await Assert.That(DisplayWidth.Of(text.Text)).IsEqualTo(2);
		await Assert.That(text.ToPlainText()).IsEqualTo(Bel + "ab")
			.Because("the plain text keeps the position; only a render decides what to do with it");
	}

	/// <summary>
	/// It rides on one real character, so the operations that carry text carry it too, and a column it
	/// sits in is not one cell narrower than its neighbours.
	/// </summary>
	[Test]
	public async Task ABellSurvivesTheOperationsThatCarryText()
	{
		var line = MarkupText.Concat(MarkupText.Concat(MarkupText.Plain("a"), MarkupText.Bell()), MarkupText.Plain("bc"));

		await Assert.That(Render(line.Substring(0, 3), MarkupFormat.Ansi)).IsEqualTo("a" + Bel + "b");
		await Assert.That(Render(line.Pad(MarkupText.Plain(" "), 5, PadType.Right, TruncationType.Truncate), MarkupFormat.Ansi))
			.IsEqualTo("a" + Bel + "bc  ")
			.Because("the bell is zero cells wide, so padding measures the three that show");
	}

	[Test]
	public async Task ABellRoundTripsThroughTheSerializer()
	{
		var text = MarkupText.Concat(MarkupText.Plain("Hey"), MarkupText.Bell());

		var back = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, Registry), Registry);

		await Assert.That(Render(back, MarkupFormat.Ansi)).IsEqualTo("Hey" + Bel);
		await Assert.That(back.Runs.Any(run => run.Markups.Any(markup => markup is BellMarkup))).IsTrue();
	}
}
