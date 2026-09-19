using MarkupString.Ansi;
using MarkupString.Html;
namespace MarkupString.Tests.Html;

/// <summary>
/// <see cref="HtmlMarkup.Tag"/>, <see cref="HtmlMarkup.TryParseAttributes"/> and
/// <see cref="HtmlTagPolicy"/>: a tag built from untrusted parts cannot carry more than its policy
/// allows, and nothing in a value can end the attribute or the tag.
/// </summary>
public class HtmlTagPolicyTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	private static string Html(HtmlMarkup markup, string body = "x") =>
		MarkupText.Wrap(markup, body).Render(MarkupFormat.Html, Registry);

	// ── Tag: checked names, encoded values ─────────────────────────────────────

	[Test]
	public async Task Tag_EncodesEveryValue()
	{
		var markup = HtmlMarkup.Tag("font", new HtmlAttribute("color", "red\"><script>&"));

		await Assert.That(Html(markup)).IsEqualTo("<font color=\"red&quot;&gt;&lt;script&gt;&amp;\">x</font>");
	}

	[Test]
	public async Task Tag_LeavesAccentedTextAlone()
	{
		await Assert.That(HtmlMarkup.Tag("span", new HtmlAttribute("title", "Café")).Attributes)
			.IsEqualTo("title=\"Café\"")
			.Because("a numeric entity is one more thing an MXP or Pueblo client may not decode");
	}

	[Test]
	[Arguments("img src=x onerror=alert(1)")]
	[Arguments("b>")]
	[Arguments("")]
	[Arguments("1b")]
	public async Task Tag_RefusesANameThatIsNotATagName(string name)
	{
		await Assert.That(() => HtmlMarkup.Tag(name)).Throws<ArgumentException>();
	}

	[Test]
	public async Task Tag_RefusesAnAttributeNameThatCouldCarryMore()
	{
		await Assert.That(() => HtmlMarkup.Tag("a", new HtmlAttribute("href=x onclick", "y"))).Throws<ArgumentException>();
	}

	// ── TryParseAttributes ──────────────────────────────────────────────────────

	[Test]
	public async Task TryParseAttributes_ReadsEveryQuotingStyle()
	{
		await Assert.That(HtmlMarkup.TryParseAttributes("href=\"a b\" title='c' size=3 noshade", out var parsed)).IsTrue();
		await Assert.That(parsed).IsEquivalentTo(new[]
		{
			new HtmlAttribute("href", "a b"),
			new HtmlAttribute("title", "c"),
			new HtmlAttribute("size", "3"),
			new HtmlAttribute("noshade", ""),
		});
	}

	[Test]
	public async Task TryParseAttributes_DecodesEntities_SoTheyAreCheckedAsAClientReadsThem()
	{
		await Assert.That(HtmlMarkup.TryParseAttributes("href=\"&#106;avascript:x\" title=\"a &amp; b\"", out var parsed)).IsTrue();
		await Assert.That(parsed[0].Value).IsEqualTo("javascript:x");
		await Assert.That(parsed[1].Value).IsEqualTo("a & b");
	}

	[Test]
	[Arguments("href=\"never closed")]
	[Arguments("\"quoted\"=name")]
	[Arguments("a>b=c")]
	public async Task TryParseAttributes_RefusesAMalformedString(string attributes)
	{
		await Assert.That(HtmlMarkup.TryParseAttributes(attributes, out _)).IsFalse();
	}

	// ── BrowserSafe ─────────────────────────────────────────────────────────────

	[Test]
	[Arguments("script")]
	[Arguments("iframe")]
	[Arguments("style")]
	[Arguments("form")]
	public async Task BrowserSafe_RefusesATagABrowserWouldRun(string tag)
	{
		await Assert.That(HtmlTagPolicy.BrowserSafe.TryCreate(tag, null, out _)).IsFalse();
	}

	[Test]
	[Arguments("href=\"javascript:alert(1)\"")]
	[Arguments("href=\"java\tscript:alert(1)\"")]
	[Arguments("href=\"&#106;avascript:alert(1)\"")]
	[Arguments("href=\"data:text/html,x\"")]
	[Arguments("onclick=\"alert(1)\"")]
	[Arguments("style=\"position:fixed\"")]
	[Arguments("xch_cmd=\"@destroy me\"")]
	public async Task BrowserSafe_DropsAnAttributeABrowserWouldRun(string attributes)
	{
		await Assert.That(HtmlTagPolicy.BrowserSafe.TryCreate("a", attributes, out var markup)).IsTrue();
		await Assert.That(markup!.Attributes).IsNull();
	}

	[Test]
	public async Task BrowserSafe_KeepsTheRest_ReEncoded()
	{
		await Assert.That(HtmlTagPolicy.BrowserSafe.TryCreate("a", "href='https://example.test/?a=1&amp;b=2' onclick=x title=Go", out var markup)).IsTrue();
		await Assert.That(markup!.Attributes).IsEqualTo("href=\"https://example.test/?a=1&amp;b=2\" title=\"Go\"")
			.Because("DropAttribute removes only the one that failed, and &amp; survives the decode and re-encode");
	}

	[Test]
	public async Task DropAllAttributes_KeepsNoneWhenOneFails()
	{
		var policy = HtmlTagPolicy.BrowserSafe with { OnViolation = HtmlAttributeViolation.DropAllAttributes };

		await Assert.That(policy.TryCreate("a", "href=\"https://example.test/\" onclick=x", out var markup)).IsTrue();
		await Assert.That(markup!.Attributes).IsNull();
	}

	[Test]
	public async Task AMalformedAttributeString_KeepsTheTagBare()
	{
		await Assert.That(HtmlTagPolicy.WellFormed.TryCreate("b", "title=\"never closed", out var markup)).IsTrue();
		await Assert.That(markup!.Attributes).IsNull();
	}

	[Test]
	public async Task WellFormed_AllowsAnyTagButStillChecksTheName()
	{
		await Assert.That(HtmlTagPolicy.WellFormed.TryCreate("send", "href=north", out var send)).IsTrue();
		await Assert.That(send!.Attributes).IsEqualTo("href=\"north\"");
		await Assert.That(HtmlTagPolicy.WellFormed.TryCreate("send x=y", null, out _)).IsFalse();
	}

	[Test]
	public async Task APolicyIgnoresCase_WhateverSetItWasGiven()
	{
		var policy = HtmlTagPolicy.WellFormed with
		{
			AllowedTags = new HashSet<string> { "font" },
			AllowedAttributes = new HashSet<string> { "color" },
		};

		await Assert.That(policy.TryCreate("FONT", "COLOR=red", out var markup)).IsTrue();
		await Assert.That(markup!.Attributes).IsEqualTo("COLOR=\"red\"");
	}

	// ── The emitter holds deserialised or unchecked markup to it too ────────────

	[Test]
	public async Task AnEmitterWithAPolicy_HoldsUncheckedMarkupToIt()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml(HtmlTagPolicy.BrowserSafe);
		var unchecked_ = MarkupText.Concat(
			MarkupText.Wrap(HtmlMarkup.Create("a", "href=\"javascript:alert(1)\" title=\"t\""), "link"),
			MarkupText.Wrap(HtmlMarkup.Create("script"), "alert(1)"));

		await Assert.That(unchecked_.Render(MarkupFormat.Html, registry))
			.IsEqualTo("<a title=\"t\">link</a>alert(1)")
			.Because("the refused tag leaves its body in place, and the body is encoded text");
	}

	[Test]
	public async Task WithHtmlPolicy_LeavesPuebloAndMxpAsGiven()
	{
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml(HtmlTagPolicy.BrowserSafe);
		var link = MarkupText.Wrap(HtmlMarkup.Create("a", "xch_cmd=\"look\""), "look");

		await Assert.That(link.Render(MarkupFormat.Pueblo, registry)).IsEqualTo("<a xch_cmd=\"look\">look</a>");
		await Assert.That(link.Render(MarkupFormat.Html, registry)).IsEqualTo("<a>look</a>");
	}
}
