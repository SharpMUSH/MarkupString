using MarkupString.Ansi;

public class AnsiCodeWriterTests
{
	[Test]
	[Arguments("r", "r")]
	[Arguments("hr", "hr")]
	[Arguments("h", "h")]
	[Arguments("rhB", "hBr")]
	[Arguments("ufi", "fiu")]
	[Arguments("U", "U")]
	[Arguments("hrH", "Hr")]
	[Arguments("n", "n")]
	[Arguments("nr", "nr")]
	[Arguments("d", "d")]
	[Arguments("D", "D")]
	[Arguments("200", "+xterm200")]
	[Arguments("+xterm200", "+xterm200")]
	[Arguments("#FF0000", "#ff0000")]
	[Arguments("#ff0000!#0000ff", "#ff0000!#0000ff")]
	[Arguments("R/+xterm20", "!+xterm20")]
	[Arguments("<255 0 0>", "#ff0000")]
	public async Task Write_GivesTheCodesInPennMushOrder(string codes, string expected)
	{
		await Assert.That(AnsiCodeWriter.Write(AnsiCodeParser.Parse(codes).Style)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("hr")]
	[Arguments("hBr")]
	[Arguments("fiuFHIU")]
	[Arguments("nhu")]
	[Arguments("u#ff0000!+xterm20")]
	[Arguments("dD")]
	public async Task Write_ParsesBackToTheSameStyle(string codes)
	{
		var style = AnsiCodeParser.Parse(codes).Style;
		await Assert.That(AnsiCodeParser.Parse(AnsiCodeWriter.Write(style)).Style).IsEqualTo(style);
	}

	[Test]
	public async Task Write_BrightBackground_IsItsXtermEntry()
	{
		var style = new AnsiStyle { Background = new AnsiColor.Standard(1, true) };
		await Assert.That(AnsiCodeWriter.Write(style)).IsEqualTo("!+xterm9");
		await Assert.That(AnsiCodeParser.Parse("!+xterm9").Style.Background).IsEqualTo(new AnsiColor.Xterm(9));
	}

	[Test]
	public async Task Write_NothingAnsiCanSay_IsEmpty()
	{
		var style = new AnsiStyle { Italic = true, StrikeThrough = true, LinkUrl = "https://example.com" };
		await Assert.That(AnsiCodeWriter.Write(style)).IsEqualTo(string.Empty);
	}
}
