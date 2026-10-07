using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;
using MarkupString.Mxp;
using MarkupString.Pueblo;
namespace MarkupString.Tests.Layout;

/// <summary>
/// A figure's picture is one <see cref="ImageMarkup"/> over the rows it is laid out in, whoever reads them. A
/// format with a picture element of its own writes it once and keeps the cells; one without writes the art.
/// </summary>
/// <remarks>
/// <c>figure()</c> in SharpMUSH reached an MXP client as its text art alone: the picture was marked on its
/// rows only for a terminal that draws pictures in its cells, so <c>&lt;IMAGE&gt;</c> had nothing to stand on.
/// </remarks>
public class FigurePictureTests
{
	private static readonly MarkupRegistry Registry =
		MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp().WithPueblo();

	private static readonly ImageMarkup Cat = new("https://example.test/img/cat.png", "A cat");

	private static readonly MarkupText Art = MarkupText.Plain("/\\_/\\\n( o.o )\n  ^ ^");

	private static string[] Lines(MarkupText laid, MarkupFormat format, MarkupRegistry? registry = null) =>
		laid.Render(format, registry ?? Registry).Split('\n');

	private static string Trimmed(string text) => string.Join('\n', text.Split('\n').Select(line => line.TrimEnd()));

	private static int Count(string text, string part) => text.Split(part).Length - 1;

	[Test]
	public async Task MxpWritesTheFiguresPictureOnceAndKeepsItsCells()
	{
		var laid = BlockLayout.Build(new Figure(Cat, Art), 20);

		var lines = Lines(laid, MarkupFormat.Mxp);

		await Assert.That(lines[0].TrimEnd()).IsEqualTo("<IMAGE cat.png URL=https://example.test/img/>");
		await Assert.That(Count(string.Join('\n', lines), "<IMAGE")).IsEqualTo(1);
		await Assert.That(lines.Skip(1).All(line => line.Trim().Length == 0)).IsTrue();
		await Assert.That(lines.Length).IsEqualTo(3);
	}

	[Test]
	public async Task AFigureWithNoArtIsItsPictureNotItsDescription()
	{
		var laid = BlockLayout.Build(new Figure(Cat, MarkupText.Empty), 20);

		var mxp = laid.Render(MarkupFormat.Mxp, Registry);

		await Assert.That(mxp.TrimEnd()).IsEqualTo("<IMAGE cat.png URL=https://example.test/img/>");
		await Assert.That(laid.ToPlainText().TrimEnd()).IsEqualTo("[A cat]");
	}

	[Test]
	public async Task ArtOfLineBreaksAloneIsNoArt()
	{
		var laid = BlockLayout.Build(new Figure(Cat, MarkupText.Plain("\n\r\n")), 20);

		var mxp = laid.Render(MarkupFormat.Mxp, Registry);

		await Assert.That(mxp.TrimEnd()).IsEqualTo("<IMAGE cat.png URL=https://example.test/img/>");
		await Assert.That(laid.ToPlainText().TrimEnd()).IsEqualTo("[A cat]");
	}

	/// <summary>The words beside a floated picture stay in the column they are in for a reader of the art.</summary>
	[Test]
	public async Task TextBesideAFloatedPictureStaysWhereItWas()
	{
		var figure = new Figure(Cat, Art) { Float = FigureFloat.Left, Beside = new TextBlock(MarkupText.Plain("The cat sits by the fire.")) };
		var laid = BlockLayout.Build(figure, 30);

		var plain = laid.ToPlainText().Split('\n');
		var mxp = Lines(laid, MarkupFormat.Mxp);

		await Assert.That(mxp[0]).StartsWith("<IMAGE cat.png URL=https://example.test/img/>");
		var untagged = mxp.Select(line => line.Replace("<IMAGE cat.png URL=https://example.test/img/>", string.Empty)).ToArray();
		await Assert.That(untagged.Length).IsEqualTo(plain.Length);
		// The art is seven cells and the gap two: the picture's cells are blank, and the words start where they did.
		for (var row = 0; row < plain.Length; row++)
		{
			await Assert.That(untagged[row][..9].Trim()).IsEmpty();
			await Assert.That(untagged[row][9..].TrimEnd()).IsEqualTo(plain[row][9..].TrimEnd());
		}
		await Assert.That(plain[0][9..]).StartsWith("The cat");
	}

	[Test]
	public async Task AClientThatRefusesImagesReadsTheArt()
	{
		var refusing = MarkupRegistry.Empty.WithAnsi().WithMxp(element => element != "IMAGE");
		var laid = BlockLayout.Build(new Figure(Cat, Art), 20);

		await Assert.That(Trimmed(laid.Render(MarkupFormat.Mxp, refusing))).IsEqualTo(Trimmed(laid.ToPlainText()));
	}

	/// <summary><c>figure()</c> passes no address when the caller may not show pictures: there is no picture to mark.</summary>
	[Test]
	public async Task AFigureWithNoAddressIsItsArtEverywhere()
	{
		var laid = BlockLayout.Build(new Figure(new ImageMarkup(string.Empty, "A cat"), Art), 20);

		await Assert.That(laid.Runs.SelectMany(run => run.Markups).OfType<ImageMarkup>()).IsEmpty();
		await Assert.That(Trimmed(laid.Render(MarkupFormat.Mxp, Registry))).IsEqualTo(Trimmed(laid.ToPlainText()));
	}

	[Test]
	public async Task PuebloAndBBCodeWriteThePictureOnce()
	{
		var laid = BlockLayout.Build(new Figure(Cat, Art), 20);

		await Assert.That(Count(laid.Render(MarkupFormat.Pueblo, Registry), "<img ")).IsEqualTo(1);
		await Assert.That(Count(laid.Render(MarkupFormat.BBCode, Registry), "[img]")).IsEqualTo(1);
	}

	[Test]
	public async Task AnsiWithoutThePicturesPixelsIsTheArt()
	{
		var laid = BlockLayout.Build(new Figure(Cat, Art), 20);

		await Assert.That(laid.Render(MarkupFormat.Ansi, Registry)).IsEqualTo(laid.ToPlainText());
		await Assert.That(laid.Render(MarkupFormat.Plain, Registry)).IsEqualTo(laid.ToPlainText());
	}

	[Test]
	public async Task HtmlWritesThePictureOnRowZeroAndBlanksTheRest()
	{
		var first = MarkupText.Wrap(Cat with { Row = new PictureRow(0, 2, 7) }, "/\\_/\\");
		var second = MarkupText.Wrap(Cat with { Row = new PictureRow(1, 2, 7) }, "( o.o )");

		await Assert.That(first.Render(MarkupFormat.Html, Registry)).StartsWith("<img class=\"ms-image\"");
		await Assert.That(second.Render(MarkupFormat.Html, Registry)).IsEqualTo("       ");
	}

	/// <summary>A picture the policy refuses is refused on every row, so the art is whole.</summary>
	[Test]
	public async Task HtmlHeldToAPolicyThatRefusesThePictureIsTheArt()
	{
		var policy = HtmlTagPolicy.WellFormed with { AllowedTags = new HashSet<string> { "span" } };
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml(policy);
		var first = MarkupText.Wrap(Cat with { Row = new PictureRow(0, 2, 7) }, "/\\_/\\");
		var second = MarkupText.Wrap(Cat with { Row = new PictureRow(1, 2, 7) }, "( o.o )");

		await Assert.That(first.Render(MarkupFormat.Html, registry)).IsEqualTo("/\\_/\\");
		await Assert.That(second.Render(MarkupFormat.Html, registry)).IsEqualTo("( o.o )");
	}

	[Test]
	public async Task ARowOfAPictureSurvivesSerialisation()
	{
		var laid = BlockLayout.Build(new Figure(Cat, Art), 20);

		var back = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(laid, Registry), Registry);

		await Assert.That(back.Runs.SelectMany(run => run.Markups).OfType<ImageMarkup>().Select(image => image.Row).Distinct())
			.IsEquivalentTo(new PictureRow?[] { new PictureRow(0, 3, 7), new PictureRow(1, 3, 7), new PictureRow(2, 3, 7) });
		await Assert.That(back.Render(MarkupFormat.Mxp, Registry)).IsEqualTo(laid.Render(MarkupFormat.Mxp, Registry));
	}

	/// <summary>A description in more than one run is still one picture.</summary>
	[Test]
	public async Task AnInlinePictureOverSeveralRunsIsWrittenOnce()
	{
		var described = MarkupText.Concat(MarkupText.Plain("A "), MarkupText.Wrap(AnsiMarkup.Create(bold: true), "cat"));
		var image = MarkupText.Wrap(new ImageMarkup("cat.png", "A cat"), described);

		await Assert.That(image.Render(MarkupFormat.Mxp, Registry)).IsEqualTo("<IMAGE cat.png>");
		await Assert.That(Count(image.Render(MarkupFormat.Pueblo, Registry), "<img ")).IsEqualTo(1);
		await Assert.That(Count(image.Render(MarkupFormat.Html, Registry), "<img ")).IsEqualTo(1);
	}
}
