using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Html;
using MarkupString.Mxp.Decoding;

namespace MarkupString.Tests.Html;

/// <summary>The shared vocabulary that used to write nothing in HTML: menus, highlights, link groups, relocation, login prompts.</summary>
public class VocabularyHtmlTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static string Html(MarkupText text) => text.Render(MarkupFormat.Html, Registry);

	private static MarkupText Point(IPointMarkup point) =>
		MarkupText.Concat([MarkupText.Plain("a"), MarkupText.Wrap(point, point.Carrier)]);

	[Test]
	public async Task CommandMenu_IsAMenuElementWithItsChoices()
	{
		var menu = MarkupText.Wrap(new CommandMenuMarkup([new("Look", "look sword"), new("Take", "take sword")], "A sword"), "sword");

		await Assert.That(Html(menu)).IsEqualTo(
			"<span class=\"ms-menu\" role=\"button\" tabindex=\"0\" aria-haspopup=\"menu\" title=\"A sword\" data-commands=\"" +
			"[{&quot;label&quot;:&quot;Look&quot;,&quot;command&quot;:&quot;look sword&quot;},{&quot;label&quot;:&quot;Take&quot;,&quot;command&quot;:&quot;take sword&quot;}]" +
			"\">sword</span>");
	}

	[Test]
	public async Task CommandMenu_OfOneChoice_HasNoPopup_AndAPromptIsMarked()
	{
		var menu = MarkupText.Wrap(new CommandMenuMarkup([new("Say", "say ")], Prompt: true), "talk");

		await Assert.That(Html(menu)).IsEqualTo(
			"<span class=\"ms-menu ms-prompt\" role=\"button\" tabindex=\"0\" data-commands=\"" +
			"[{&quot;label&quot;:&quot;Say&quot;,&quot;command&quot;:&quot;say &quot;}]\">talk</span>");
	}

	[Test]
	public async Task CommandMenu_EscapesWhatItCarries()
	{
		var html = Html(MarkupText.Wrap(new CommandMenuMarkup([new("<b>", "say \"hi\" & <x>")], "\"q\""), "x"));

		await Assert.That(html).DoesNotContain("<b>");
		await Assert.That(html).DoesNotContain("<x>");
		await Assert.That(html).Contains("title=\"&quot;q&quot;\"");
	}

	[Test]
	public async Task MxpSendWithSeveralCommands_IsAMenuAroundItsDefaultLink()
	{
		var decoder = DecoderRegistry.Empty.WithVt().WithMxp().CreateDecoder();
		decoder.EnableProtocol(MxpDecoding.Protocol);
		var line = decoder.Feed("\e[1z<SEND href=\"buy sword|look sword\" hint=\"Sword|Buy|Look\">sword</SEND>\n")[0];

		var html = Html(line.Content);
		await Assert.That(html).StartsWith("<span class=\"ms-menu\"");
		await Assert.That(html).Contains("&quot;command&quot;:&quot;look sword&quot;");
		await Assert.That(html).Contains("<a class=\"ms-cmd-link\" role=\"button\" tabindex=\"0\" xch_cmd=\"buy sword\" title=\"Sword\">sword</a>");
	}

	[Test]
	public async Task Highlight_PaintsTheBackground()
	{
		await Assert.That(Html(MarkupText.Wrap(new HighlightMarkup(new RgbColor(10, 20, 30)), "hi")))
			.IsEqualTo("<span class=\"ms-highlight\" style=\"background-color: #0a141e\">hi</span>");
	}

	[Test]
	public async Task LinkGroup_WrapsItsLinks()
	{
		var link = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command), "x");

		await Assert.That(Html(MarkupText.Wrap(new LinkGroupMarkup("g1"), link))).IsEqualTo(
			"<span class=\"ms-link-group\" data-group=\"g1\"><a class=\"ms-cmd-link\" role=\"button\" tabindex=\"0\" xch_cmd=\"look\">x</a></span>");
	}

	[Test]
	public async Task Relocate_AndLoginPrompt_AreEmptyElements()
	{
		await Assert.That(Html(Point(new RelocateMarkup("mud.example", 4201, Quiet: true))))
			.IsEqualTo("a<span class=\"ms-relocate\" data-host=\"mud.example\" data-port=\"4201\" data-quiet=\"true\"></span>");
		await Assert.That(Html(Point(new LoginPromptMarkup(LoginField.Password))))
			.IsEqualTo("a<span class=\"ms-login\" data-field=\"password\"></span>");
	}

	[Test]
	public async Task APolicyThatRefusesTheElement_LeavesTheText()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml(new HtmlTagPolicy { AllowedTags = new HashSet<string> { "b" } });
		var menu = MarkupText.Wrap(new CommandMenuMarkup([new("Look", "look")]), "sword");

		await Assert.That(menu.Render(MarkupFormat.Html, registry)).IsEqualTo("sword");
		await Assert.That(Point(new RelocateMarkup("h", 1)).Render(MarkupFormat.Html, registry)).IsEqualTo("a");
	}
}
