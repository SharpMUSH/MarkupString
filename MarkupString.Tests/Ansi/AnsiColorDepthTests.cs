using MarkupString.Ansi;

public class AnsiColorDepthTests
{
	private const string Esc = "\e";
	private const string Bel = "\u0007";

	private static readonly AnsiColor.Rgb PureRed = new(255, 0, 0);

	private static string Render(MarkupText text, MarkupFormat format, AnsiColorDepth depth, bool hyperlinks = true) =>
		text.Render(format, MarkupRegistry.Empty.WithAnsi().WithAnsiOutput(depth, hyperlinks));

	private static MarkupText Styled(AnsiStyle style, string text) => MarkupText.Wrap(new AnsiMarkup(style), text);

	[Test]
	public async Task TrueColor_WritesRgbAsItIs()
	{
		var text = Styled(new AnsiStyle { Foreground = PureRed }, "a");
		await Assert.That(Render(text, MarkupFormat.Ansi, AnsiColorDepth.TrueColor)).IsEqualTo($"{Esc}[38;2;255;0;0ma{Esc}[0m");
	}

	[Test]
	public async Task Xterm256_WritesRgbAsItsNearestPaletteEntry()
	{
		// The cube's corner 196 is exactly #ff0000.
		var text = Styled(new AnsiStyle { Foreground = PureRed, Background = PureRed }, "a");
		await Assert.That(Render(text, MarkupFormat.Ansi, AnsiColorDepth.Xterm256)).IsEqualTo($"{Esc}[38;5;196;48;5;196ma{Esc}[0m");
	}

	[Test]
	public async Task Xterm256_LeavesStandardColoursAlone()
	{
		var style = new AnsiStyle { Foreground = new AnsiColor.Standard(1, true) };
		await Assert.That(style.AtDepth(AnsiColorDepth.Xterm256)).IsEqualTo(style);
	}

	[Test]
	[Arguments((byte)1, (byte)1, false)]
	[Arguments((byte)9, (byte)1, true)]
	[Arguments((byte)15, (byte)7, true)]
	public async Task Standard_TheFirstSixteenXtermEntriesAreTheStandardColours(byte xterm, byte index, bool bright)
	{
		var style = new AnsiStyle { Foreground = new AnsiColor.Xterm(xterm) }.AtDepth(AnsiColorDepth.Standard);
		await Assert.That(style.Foreground).IsEqualTo(new AnsiColor.Standard(index, bright));
	}

	[Test]
	public async Task Standard_WritesRgbAndPaletteColoursAsTheNearestStandardColour()
	{
		var style = new AnsiStyle { Foreground = PureRed, Background = new AnsiColor.Xterm(196) }.AtDepth(AnsiColorDepth.Standard);
		await Assert.That(style.Foreground).IsEqualTo(AnsiColor.NearestStandard(PureRed));
		await Assert.That(style.Background).IsEqualTo(AnsiColor.NearestStandard(PureRed));
		await Assert.That(style.Foreground).IsTypeOf<AnsiColor.Standard>();
	}

	[Test]
	public async Task Standard_AnOffCodeKeepsAnInheritedColourFromBeingWrittenBold()
	{
		// xterm 9 is bright red, which the standard palette writes as bold; H inside it turns that off.
		var text = MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Xterm(9)), MarkupText.Wrap(AnsiCodeParser.Parse("H"), "a"));
		await Assert.That(Render(text, MarkupFormat.Ansi, AnsiColorDepth.Standard)).IsEqualTo($"{Esc}[31ma{Esc}[0m");
	}

	[Test]
	public async Task Attributes_DropsColourButKeepsABrightForegroundAsBold()
	{
		var text = MarkupText.Concat([
			Styled(new AnsiStyle { Foreground = new AnsiColor.Standard(1, true) }, "a"),
			MarkupText.Plain("-"),
			Styled(new AnsiStyle { Foreground = PureRed, Underlined = true }, "b"),
			MarkupText.Plain("-"),
			Styled(new AnsiStyle { Foreground = new AnsiColor.Standard(1, false) }, "c")]);
		await Assert.That(Render(text, MarkupFormat.Ansi, AnsiColorDepth.Attributes))
			.IsEqualTo($"{Esc}[1ma{Esc}[0m-{Esc}[4mb{Esc}[0m-c");
	}

	[Test]
	public async Task None_WritesNoSgr()
	{
		var text = MarkupText.Concat(
			Styled(new AnsiStyle { Foreground = PureRed, Bold = true, Underlined = true }, "a"),
			Styled(new AnsiStyle { Clear = true }, "b"));
		await Assert.That(Render(text, MarkupFormat.Ansi, AnsiColorDepth.None)).IsEqualTo("ab");
	}

	[Test]
	public async Task None_KeepsALink()
	{
		var text = Styled(new AnsiStyle { Foreground = PureRed, LinkUrl = "https://example.com", LinkKind = LinkKind.Url }, "a");
		await Assert.That(Render(text, MarkupFormat.Ansi, AnsiColorDepth.None))
			.IsEqualTo($"{Esc}]8;;https://example.com{Bel}a{Esc}]8;;{Bel}");
	}

	[Test]
	public async Task WithoutHyperlinks_WritesALinkAsItsText()
	{
		var text = Styled(new AnsiStyle { Underlined = true, LinkUrl = "https://example.com", LinkKind = LinkKind.Url }, "a");
		await Assert.That(Render(text, MarkupFormat.Ansi, AnsiColorDepth.TrueColor, hyperlinks: false))
			.IsEqualTo($"{Esc}[4ma{Esc}[0m");
	}

	[Test]
	[Arguments("Mxp")]
	[Arguments("Pueblo")]
	public async Task TaggedFormats_WriteColourAtTheDepthToo(string formatName)
	{
		var format = formatName == "Mxp" ? MarkupFormat.Mxp : MarkupFormat.Pueblo;
		var text = Styled(new AnsiStyle { Foreground = PureRed, Bold = true }, "a");
		await Assert.That(Render(text, format, AnsiColorDepth.Xterm256)).IsEqualTo($"{Esc}[1;38;5;196ma{Esc}[0m");
		await Assert.That(Render(text, format, AnsiColorDepth.Attributes)).IsEqualTo($"{Esc}[1ma{Esc}[0m");
		await Assert.That(Render(text, format, AnsiColorDepth.None)).IsEqualTo("a");
	}

	[Test]
	public async Task TaggedFormats_KeepTheirLinkTagsAtEveryDepth()
	{
		var text = Styled(new AnsiStyle { Foreground = PureRed, LinkUrl = "look", LinkKind = LinkKind.Command }, "a");
		await Assert.That(Render(text, MarkupFormat.Mxp, AnsiColorDepth.None)).IsEqualTo("<SEND HREF=\"look\">a</SEND>");
	}

	[Test]
	public async Task AtDepth_UndefinedDepth_Throws()
	{
		await Assert.That(() => AnsiStyle.None.AtDepth((AnsiColorDepth)99)).Throws<ArgumentOutOfRangeException>();
	}
}
