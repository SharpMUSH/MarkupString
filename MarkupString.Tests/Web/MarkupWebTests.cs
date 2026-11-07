using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Html;
using MarkupString.Mxp.Decoding;
using MarkupString.Web;

namespace MarkupString.Tests.Web;

/// <summary>The files the web package serves are the text <see cref="MarkupWeb"/> gives.</summary>
public class MarkupWebTests
{
	private static string Shipped(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
		Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "MarkupString.Web", "wwwroot", name);

	private static string Fixture([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
		Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "MarkupString.Web", "tests", "fragments.json");

	private static bool Updating => Environment.GetEnvironmentVariable("MARKUPSTRING_UPDATE_WEB") == "1";

	[Test]
	public async Task Stylesheet_HoldsEveryFixedSheet()
	{
		await Assert.That(MarkupWeb.Stylesheet).StartsWith(AnsiCss.Fixed + LayoutCss.Fixed);
		await Assert.That(MarkupWeb.Stylesheet).EndsWith(MarkupWeb.RuntimeStylesheet);
	}

	/// <summary>
	/// The served stylesheet is generated from the C# sheets. Run the tests with <c>MARKUPSTRING_UPDATE_WEB=1</c>
	/// to write it again after changing one.
	/// </summary>
	[Test]
	public async Task ServedStylesheet_IsTheStylesheet()
	{
		var path = Shipped("markupstring.css");
		if (Updating) await File.WriteAllTextAsync(path, MarkupWeb.Stylesheet);

		await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(MarkupWeb.Stylesheet);
	}

	[Test]
	public async Task Script_IsTheServedModule()
	{
		await Assert.That(MarkupWeb.Script).IsEqualTo(await File.ReadAllTextAsync(Shipped("markupstring.js")));
		await Assert.That(MarkupWeb.Script).Contains("export function attach(");
		await Assert.That(MarkupWeb.ScriptPath).IsEqualTo("_content/MarkupString.Web/markupstring.js");
	}

	/// <summary>
	/// What the HTML writer writes for each effect, which the script's browser test (MarkupString.Web/tests) runs
	/// against. Written again with <c>MARKUPSTRING_UPDATE_WEB=1</c>.
	/// </summary>
	[Test]
	public async Task BrowserTestFragments_AreWhatTheHtmlWriterWrites()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml();
		string Html(MarkupText text) => text.Render(MarkupFormat.Html, registry);
		static MarkupText Point(IPointMarkup point) => MarkupText.Wrap(point, point.Carrier);
		static MarkupText Command(string command, string text) => MarkupText.Wrap(AnsiMarkup.Create(linkUrl: command, linkKind: LinkKind.Command), text);

		var mxp = DecoderRegistry.Empty.WithVt().WithMxp().CreateDecoder();
		mxp.EnableProtocol(MxpDecoding.Protocol);

		var fragments = new SortedDictionary<string, string>(StringComparer.Ordinal)
		{
			["cmd"] = Html(Command("look", "look")),
			["mxpmenu"] = Html(mxp.Feed("\e[1z<SEND href=\"buy sword|look sword\" hint=\"Sword|Buy|Look\">sword</SEND>\n")[0].Content),
			["prompt"] = Html(MarkupText.Wrap(new CommandMenuMarkup([new("Say", "say ")], Prompt: true), "talk")),
			["sound"] = Html(MarkupText.Concat([MarkupText.Plain("door"), MarkupText.Sound("door.wav")])),
			["music"] = Html(MarkupText.Music("theme.ogg")),
			["stop"] = Html(MarkupText.StopSound()),
			["bell"] = Html(MarkupText.Concat([MarkupText.Plain("ding"), Point(BellMarkup.Instance)])),
			["clear"] = Html(MarkupText.Concat([MarkupText.Plain("fresh"), Point(ClearScreenMarkup.Instance)])),
			["grouped"] = Html(MarkupText.Wrap(new LinkGroupMarkup("exits"), Command("north", "north"))),
			["expire"] = Html(Point(new ExpireLinksMarkup("exits"))),
			["gauge"] = Html(MarkupText.Gauge(MarkupText.Plain("HP"), "hp", "maxhp", "Hit Points", "red")),
			["vars"] = Html(MarkupText.Concat([MarkupText.Variable(MarkupText.Plain("50"), "maxhp"), MarkupText.Variable(MarkupText.Plain("25"), "hp")])),
			["pane"] = Html(MarkupText.Pane(MarkupText.Plain("chat line"), "chat", "Chat")),
			["relocate"] = Html(Point(new RelocateMarkup("h", 23))),
		};
		var json = System.Text.Json.JsonSerializer.Serialize(fragments, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";

		if (Updating) await File.WriteAllTextAsync(Fixture(), json);
		await Assert.That(await File.ReadAllTextAsync(Fixture())).IsEqualTo(json);
	}
}
