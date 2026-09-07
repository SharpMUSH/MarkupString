using MarkupString.Ansi;

namespace MarkupString.Html;

/// <summary>
/// The stylesheet for the <c>ms-*</c> classes written when rendering to HTML.
/// </summary>
public static class HtmlCss
{
	/// <summary>
	/// The rules from <see cref="AnsiCss.Fixed"/>, which is where they live: every <c>ms-*</c>
	/// class is written by the Ansi package's HTML emitter, and this package writes none of its
	/// own. Reach for <see cref="AnsiCss.Fixed"/> instead — it is available to a consumer that
	/// renders HTML without taking this package at all.
	/// </summary>
	[Obsolete("Use AnsiCss.Fixed. Every ms-* class is written by MarkupString.Ansi, so the stylesheet belongs there; a consumer rendering HTML with only MarkupString.Ansi cannot reach it here.")]
	public static readonly string Fixed = AnsiCss.Fixed;
}
