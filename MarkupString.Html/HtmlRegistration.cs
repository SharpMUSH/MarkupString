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
	public static MarkupRegistry WithHtml(this MarkupRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		return registry
			.With(new HtmlTagEmitter(MarkupFormat.Html))
			.With(new HtmlTagEmitter(MarkupFormat.Pueblo))
			.With(new HtmlTagEmitter(MarkupFormat.Mxp))
			.With(new HtmlMarkupCodec());
	}

	/// <summary>
	/// As <see cref="WithHtml(MarkupRegistry)"/>, but every tag rendered in <see cref="MarkupFormat.Html"/>
	/// is held to <paramref name="htmlPolicy"/> — <see cref="HtmlTagPolicy.BrowserSafe"/> for output a
	/// web browser renders. Pueblo and MXP output still carry tags as given: they go to MUD clients, not
	/// to a browser, and some of what they need (a command link) is exactly what a browser policy
	/// refuses. To hold those to a policy too, add <c>new HtmlTagEmitter(format, policy)</c> after this.
	/// </summary>
	public static MarkupRegistry WithHtml(this MarkupRegistry registry, HtmlTagPolicy htmlPolicy)
	{
		ArgumentNullException.ThrowIfNull(htmlPolicy);
		return registry.WithHtml().With(new HtmlTagEmitter(MarkupFormat.Html, htmlPolicy));
	}
}
