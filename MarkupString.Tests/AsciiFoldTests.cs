using MarkupString.Ansi;
using MarkupString.Layout;

namespace MarkupString.Tests;

public class AsciiFoldTests
{
	private static readonly AnsiMarkup Red = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0));

	[Test]
	[Arguments("Scene 3 · 21:04", "Scene 3 * 21:04")]
	[Arguments("one — two – three", "one - two - three")]
	[Arguments("“quoted” ‘single’", "\"quoted\" 'single'")]
	[Arguments("café naïve Ångström", "cafe naive Angstrom")]
	[Arguments("café", "cafe")]
	[Arguments("a b", "a b")]
	[Arguments("→ ← ×", "> < x")]
	[Arguments("Straße Øre", "Strase Ore")]
	[Arguments("┌─┐ ═ █", "+-+ = #")]
	[Arguments("plain ascii\ttab\n", "plain ascii\ttab\n")]
	public async Task Fold_ReplacesWithTheNearestAscii(string text, string expected)
		=> await Assert.That(AsciiFold.Default.Fold(text)).IsEqualTo(expected);

	[Test]
	public async Task Fold_KeepsEachCharactersWidth()
	{
		// A wide character is two question marks, a fullwidth letter the letter and a space.
		await Assert.That(AsciiFold.Default.Fold("日本")).IsEqualTo("????");
		await Assert.That(AsciiFold.Default.Fold("😀!")).IsEqualTo("??!");
		await Assert.That(AsciiFold.Default.Fold("ＡＢ")).IsEqualTo("A B ");
		await Assert.That(AsciiFold.Default.Fold("👩‍👩‍👧")).IsEqualTo("??");
	}

	[Test]
	public async Task Fold_ForLatin1_KeepsWhatLatin1Has()
	{
		var fold = new AsciiFold(latin1: true);
		await Assert.That(fold.Fold("· café — ½")).IsEqualTo("· café - ½");
		await Assert.That(fold.Fold("café")).IsEqualTo("café");
	}

	[Test]
	public async Task Translations_ComeBeforeTheBuiltInOnes()
	{
		var fold = new AsciiFold([new("·", " - "), new("…", "..."), new("❤️", "<3")]);
		await Assert.That(fold.Fold("a·b…")).IsEqualTo("a - b...");
		await Assert.That(fold.Fold("I ❤️ it")).IsEqualTo("I <3 it");
		await Assert.That(new AsciiFold([new("·", "+")], latin1: true).Fold("a·b")).IsEqualTo("a+b");
	}

	[Test]
	[Arguments("ab", "x")]
	[Arguments("a", "x")]
	[Arguments("", "x")]
	[Arguments("·", "é")]
	[Arguments("·", "\n")]
	public async Task Translations_RejectAKeyOrValueThatCannotWork(string key, string value)
		=> await Assert.That(() => new AsciiFold([new(key, value)])).Throws<ArgumentException>();

	[Test]
	public async Task Fold_KeepsMarkupOnTheCharacterItReplaces()
	{
		var text = MarkupText.Concat([MarkupText.Plain("a"), MarkupText.Wrap(Red, "·…"), MarkupText.Plain("b")]);
		var folded = new AsciiFold([new("…", "...")]).Fold(text);

		await Assert.That(folded.Text).IsEqualTo("a*...b");
		await Assert.That(folded.Runs.Length).IsEqualTo(1);
		await Assert.That(folded.Runs[0]).IsEqualTo(new Run(1, 4, MarkupSet.Of(Red)));
	}

	[Test]
	public async Task Fold_LeavesAsciiTextAsItIs()
	{
		var text = MarkupText.Wrap(Red, "nothing to do");
		await Assert.That(AsciiFold.Default.Fold(text)).IsSameReferenceAs(text);
	}

	[Test]
	public async Task Fold_KeepsAPointsCarrier()
	{
		var text = MarkupText.Concat([MarkupText.Plain("·"), MarkupText.Sound("ping.wav")]);
		var folded = AsciiFold.Default.Fold(text);

		await Assert.That(folded.Text).IsEqualTo("*" + MarkupText.PointCarrier);
		await Assert.That(folded.Runs.Single().Start).IsEqualTo(1);
	}

	[Test]
	public async Task Layout_MeasuresTheFoldedText()
	{
		var table = new Table([new TableColumn(MarkupText.Plain("Scene")), new TableColumn(MarkupText.Plain("When"))],
		[
			[MarkupText.Plain("Tea · Garden"), MarkupText.Plain("21:04")],
			[MarkupText.Plain("Duel…"), MarkupText.Plain("now")],
		]);
		var block = new Stack([new Rule(MarkupText.Plain("Wren · Scenes")), table]);
		var context = new LayoutContext { AsciiOnly = true, Fold = new AsciiFold([new("·", " - "), new("…", "...")]) };

		var lines = BlockLayout.Lines(block, 30, context).Select(line => line.ToPlainText()).ToArray();

		await Assert.That(lines[0]).Contains("Wren  -  Scenes");
		await Assert.That(lines.Any(line => line.Contains("Tea  -  Garden"))).IsTrue();
		await Assert.That(lines.Any(line => line.Contains("Duel..."))).IsTrue();
		await Assert.That(lines.All(line => System.Text.Ascii.IsValid(line))).IsTrue();
		// The column after the folded text still starts at one place on every row.
		var when = lines.Where(line => line.Contains("21:04") || line.Contains("now")).Select(line => line.IndexOf(line.Contains("21:04") ? "21:04" : "now")).Distinct();
		await Assert.That(when.Count()).IsEqualTo(1);
	}

	[Test]
	public async Task Relayout_KeepsTheTreeAsGiven()
	{
		var built = BlockLayout.Build(new Rule(MarkupText.Plain("a · b")), 20, fluid: true);
		var relaid = BlockLayout.Relayout(built, 20, new LayoutContext { AsciiOnly = true, Fold = AsciiFold.Default });

		await Assert.That(relaid.ToPlainText()).Contains("a * b");
		var layout = relaid.Runs.SelectMany(run => run.Markups).OfType<LayoutMarkup>().Single();
		await Assert.That(((Rule)layout.Root).Title!.ToPlainText()).IsEqualTo("a · b");
	}
}
