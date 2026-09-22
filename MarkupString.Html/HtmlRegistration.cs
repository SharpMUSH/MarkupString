namespace MarkupString.Html;

/// <summary>Installs this package's emitters and codec into a <see cref="MarkupRegistry"/>.</summary>
public static class HtmlRegistration
{
	/// <summary>
	/// Returns a registry that renders <see cref="HtmlMarkup"/> as its own tag in Html, Pueblo and
	/// Mxp, and serialises it under kind <c>"html"</c>. Plain and BBCode need no emitter here — Plain
	/// passes the body through untouched, and BBCode's formatting for the handful of tags
	/// <see cref="HtmlMarkup"/> understands (bold, italic, underline, strikethrough) comes from the
	/// Ansi package's fold via <see cref="HtmlMarkup.TryGetAnsiStyle"/>, which requires
	/// <c>WithAnsi()</c> to already be applied.
	/// </summary>
	/// <remarks>
	/// It also writes the shared vocabulary for a browser: a sound as <c>&lt;audio&gt;</c>, a picture as
	/// <c>&lt;img&gt;</c>, and what HTML has no element for — a pane, a gauge, a clear — as an element
	/// with an <c>ms-</c> class for the page to act on.
	/// </remarks>
	public static MarkupRegistry WithHtml(this MarkupRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		registry = registry
			.With(new HtmlTagEmitter(MarkupFormat.Html))
			.With(new HtmlTagEmitter(MarkupFormat.Pueblo))
			.With(new HtmlTagEmitter(MarkupFormat.Mxp))
			.With(new HtmlMarkupCodec());

		foreach (var type in ElementHtmlEmitter.Types) registry = registry.With(new ElementHtmlEmitter(type));
		return registry;
	}

	/// <summary>
	/// As <see cref="WithHtml(MarkupRegistry)"/>, but every tag rendered in <see cref="MarkupFormat.Html"/>
	/// is held to <paramref name="htmlPolicy"/> — the tags and attributes your application is willing to
	/// put in front of a browser, the shared vocabulary's own elements included. Pueblo and MXP output
	/// still carries tags as given: they go to MUD
	/// clients, and some of what they need (a command link) is what a browser policy typically refuses.
	/// To hold those to a policy too, add <c>new HtmlTagEmitter(format, policy)</c> after this.
	/// </summary>
	public static MarkupRegistry WithHtml(this MarkupRegistry registry, HtmlTagPolicy htmlPolicy)
	{
		ArgumentNullException.ThrowIfNull(htmlPolicy);

		registry = registry.WithHtml().With(new HtmlTagEmitter(MarkupFormat.Html, htmlPolicy));
		foreach (var type in ElementHtmlEmitter.Types) registry = registry.With(new ElementHtmlEmitter(type, htmlPolicy));
		return registry;
	}
}
