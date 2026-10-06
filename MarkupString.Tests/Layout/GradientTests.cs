using System.Collections.Immutable;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

public class GradientTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static IColorMarkup Stop(byte r, byte g, byte b) => AnsiMarkup.Create(foreground: new AnsiColor.Rgb(r, g, b));

	private static ColorGradient Gradient(GradientSpace space, params IColorMarkup[] stops) => new([.. stops], space);

	private static readonly IColorMarkup Red = Stop(255, 0, 0);
	private static readonly IColorMarkup Lime = Stop(0, 255, 0);
	private static readonly IColorMarkup Blue = Stop(0, 0, 255);

	[Test]
	[Arguments(GradientSpace.Oklch)]
	[Arguments(GradientSpace.Oklab)]
	[Arguments(GradientSpace.Hsl)]
	public async Task TheEnds_AreTheStops(GradientSpace space)
	{
		var gradient = Gradient(space, Stop(200, 40, 90), Stop(20, 160, 220));

		await Assert.That(gradient.At(0)).IsEqualTo(new RgbColor(200, 40, 90));
		await Assert.That(gradient.At(1)).IsEqualTo(new RgbColor(20, 160, 220));
	}

	/// <summary>sRGB's midpoint of red and green is a dark olive (128, 128, 0); in OKLCH it stays bright.</summary>
	[Test]
	public async Task Oklch_RedToGreen_PassesThroughABrightYellow()
	{
		var middle = Gradient(GradientSpace.Oklch, Red, Lime).At(0.5);

		await Assert.That((int)middle.R).IsGreaterThan(200);
		await Assert.That((int)middle.G).IsGreaterThan(150);
		await Assert.That((int)middle.B).IsLessThan(60);
	}

	[Test]
	public async Task Hsl_TakesTheShorterWayRound()
		=> await Assert.That(Gradient(GradientSpace.Hsl, Red, Blue).At(0.5)).IsEqualTo(new RgbColor(255, 0, 255));

	[Test]
	[Arguments(GradientSpace.Oklch)]
	[Arguments(GradientSpace.Hsl)]
	public async Task AGrey_TakesTheOtherColoursHue(GradientSpace space)
	{
		// White has no hue. Read as hue 0 (red), the way to blue would swing through magenta.
		var middle = Gradient(space, Stop(255, 255, 255), Blue).At(0.5);

		await Assert.That((int)middle.B).IsGreaterThan(middle.R + 30);
		await Assert.That((int)middle.B).IsGreaterThan(middle.G + 30);
		await Assert.That((int)middle.G).IsGreaterThanOrEqualTo(middle.R - 5);
	}

	[Test]
	public async Task BlackToWhite_StaysGrey()
	{
		var middle = Gradient(GradientSpace.Oklch, Stop(0, 0, 0), Stop(255, 255, 255)).At(0.5);

		await Assert.That((int)middle.R).IsEqualTo(middle.G).Within(1);
		await Assert.That((int)middle.G).IsEqualTo(middle.B).Within(1);
	}

	[Test]
	public async Task ThreeStops_MeetAtTheMiddle()
		=> await Assert.That(Gradient(GradientSpace.Oklch, Red, Lime, Blue).At(0.5)).IsEqualTo(new RgbColor(0, 255, 0));

	[Test]
	public async Task Css_NamesTheSpace()
		=> await Assert.That(Gradient(GradientSpace.Oklch, Red, Blue).ToCss()).IsEqualTo("linear-gradient(to right in oklch, #ff0000, #0000ff)");

	private static Gauge Gauge(GaugeShade shade, double value = 6) =>
		new(value, 12) { BarWidth = 4, Show = GaugeShow.None, Gradient = Gradient(GradientSpace.Oklch, Red, Lime), Shade = shade };

	[Test]
	public async Task Gauge_ByCell_EachCellTakesItsPlace()
	{
		var ansi = BlockLayout.Build(Gauge(GaugeShade.Cells, 12), 20).Render(MarkupFormat.Ansi, Registry);

		await Assert.That(ansi).Contains("\u001b[38;2;255;0;0m█");
		await Assert.That(ansi).Contains("\u001b[38;2;0;255;0m█");
		await Assert.That(BlockLayout.Build(Gauge(GaugeShade.Cells, 12), 20).ToPlainText().TrimEnd()).IsEqualTo("[████]");
	}

	[Test]
	public async Task Gauge_ByValue_IsOneColour()
	{
		var text = BlockLayout.Build(Gauge(GaugeShade.Value, 0), 20);
		var full = BlockLayout.Build(Gauge(GaugeShade.Value, 12), 20).Render(MarkupFormat.Ansi, Registry);

		await Assert.That(text.ToPlainText().TrimEnd()).IsEqualTo("[░░░░]");
		await Assert.That(full).Contains("\u001b[38;2;0;255;0m████");
	}

	[Test]
	public async Task Gauge_InHtml_ShowsAsMuchOfTheGradientAsTheValueReaches()
	{
		var html = BlockLayout.Build(Gauge(GaugeShade.Cells), 20).Render(MarkupFormat.Html, Registry);

		await Assert.That(html).Contains("<div class=\"ms-gauge-bar\" role=\"meter\" aria-valuemin=\"0\" aria-valuemax=\"12\" aria-valuenow=\"6\" style=\"flex:0 1 4ch\"><div class=\"ms-gauge-fill\" style=\"width:50%;background-image:linear-gradient(to right, #ff0000, ");
		await Assert.That(html).Contains(";background-image:linear-gradient(to right in oklch, #ff0000, #00ff00);background-size:200% 100%\"></div></div>");
	}

	[Test]
	public async Task Gauge_ByValue_InHtml_IsOneColour()
		=> await Assert.That(BlockLayout.Build(Gauge(GaugeShade.Value, 12), 20).Render(MarkupFormat.Html, Registry))
			.Contains("<div class=\"ms-gauge-fill\" style=\"width:100%;background-color:#00ff00\"></div>");

	[Test]
	public async Task Gauge_Gradient_SurvivesTheSerializer()
	{
		var text = BlockLayout.Build(Gauge(GaugeShade.Value) with { Gradient = Gradient(GradientSpace.Hsl, Red, Blue) }, 20);

		var read = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, Registry), Registry);

		await Assert.That(BlockLayout.Relayout(read, 0, LayoutContext.Default).Render(MarkupFormat.Ansi, Registry))
			.IsEqualTo(text.Render(MarkupFormat.Ansi, Registry));
		await Assert.That(read.Render(MarkupFormat.Html, Registry)).IsEqualTo(text.Render(MarkupFormat.Html, Registry));
	}

	[Test]
	public async Task Paint_KeepsTheRestOfTheStyle()
	{
		var bold = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0), bold: true);
		var painted = new ColorGradient([bold, Blue]).Paint(MarkupText.Plain("x"), 1);

		await Assert.That(painted.Render(MarkupFormat.Ansi, Registry)).Contains("\u001b[1;38;2;0;0;255mx");
	}
}
