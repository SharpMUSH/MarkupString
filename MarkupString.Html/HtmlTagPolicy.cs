using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using MarkupString.Ansi;
namespace MarkupString.Html;

/// <summary>What happens to a tag's attributes when one of them is not allowed.</summary>
public enum HtmlAttributeViolation
{
	/// <summary>Only the attribute that failed is dropped.</summary>
	DropAttribute,

	/// <summary>
	/// Every attribute is dropped and the tag is kept bare, so no partial reading of the author's
	/// intent survives — PennMUSH's rule for <c>tagwrap()</c>.
	/// </summary>
	DropAllAttributes,
}

/// <summary>
/// Which tags and attributes an <see cref="HtmlMarkup"/> may carry. It turns an untrusted tag name and
/// raw attribute string into a checked <see cref="HtmlMarkup"/> with <see cref="TryCreate"/>, and a
/// <see cref="HtmlTagEmitter"/> given one re-checks every tag it writes, so markup that arrived from
/// elsewhere — deserialised, or built with the unchecked <see cref="HtmlMarkup.Create"/> — is held to it
/// too.
/// </summary>
/// <remarks>
/// Every attribute that survives is re-written <c>name="value"</c> with its value encoded, whatever
/// the policy, so nothing in a value can end the attribute or the tag.
/// </remarks>
public sealed record HtmlTagPolicy
{
	/// <summary>The tags allowed, ignoring case; <see langword="null"/> allows any valid tag name.</summary>
	public IReadOnlySet<string>? AllowedTags { get; init; }

	/// <summary>The attributes allowed, ignoring case; <see langword="null"/> allows any valid attribute name.</summary>
	public IReadOnlySet<string>? AllowedAttributes { get; init; }

	/// <summary>
	/// The attributes whose value is an address a client may navigate to or load. Their value must be
	/// one <see cref="UrlSafety.IsSafeNavigableUrl"/> accepts, with no whitespace or control character
	/// in it — a browser drops those before reading the scheme, so <c>java&#9;script:</c> would
	/// otherwise pass as a relative address.
	/// </summary>
	public IReadOnlySet<string> UrlAttributes { get; init; } = DefaultUrlAttributes;

	/// <summary>What happens to the attributes when one is not allowed.</summary>
	public HtmlAttributeViolation OnViolation { get; init; } = HtmlAttributeViolation.DropAttribute;

	/// <summary>The attributes that carry an address in HTML.</summary>
	public static IReadOnlySet<string> DefaultUrlAttributes { get; } = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
		"action", "background", "cite", "codebase", "data", "dynsrc", "formaction", "href", "longdesc", "lowsrc",
		"poster", "src", "usemap", "xlink:href");

	/// <summary>
	/// Any tag and any attribute, so long as they are well formed; addresses are not checked. What it
	/// adds over <see cref="HtmlMarkup.Create"/> is that the name cannot smuggle anything in and every
	/// value is encoded.
	/// </summary>
	public static HtmlTagPolicy WellFormed { get; } = new() { UrlAttributes = FrozenSet<string>.Empty };

	/// <summary>
	/// Formatting that a web browser renders and cannot be made to run: text-level and block tags,
	/// tables, lists, anchors and images; presentational attributes; and addresses with a safe scheme.
	/// No <c>script</c>, <c>style</c>, <c>iframe</c>, form control or document-level tag, no <c>on*</c>
	/// handler, no <c>style</c> attribute, and no <c>javascript:</c> or <c>data:</c> address.
	/// </summary>
	public static HtmlTagPolicy BrowserSafe { get; } = new()
	{
		AllowedTags = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
			"a", "abbr", "acronym", "address", "b", "bdi", "bdo", "big", "blockquote", "br", "caption", "center",
			"cite", "code", "col", "colgroup", "dd", "del", "dfn", "dir", "div", "dl", "dt", "em", "font", "h1", "h2",
			"h3", "h4", "h5", "h6", "hr", "i", "img", "ins", "kbd", "li", "mark", "menu", "ol", "p", "pre", "q", "s",
			"samp", "small", "span", "strike", "strong", "sub", "sup", "table", "tbody", "td", "tfoot", "th", "thead",
			"time", "tr", "tt", "u", "ul", "var", "wbr"),
		AllowedAttributes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
			"align", "alt", "bgcolor", "border", "cellpadding", "cellspacing", "class", "color", "cols", "colspan",
			"datetime", "dir", "face", "height", "href", "lang", "rows", "rowspan", "size", "span", "src", "start",
			"title", "type", "valign", "value", "width"),
	};

	/// <summary>
	/// Builds the checked tag for <paramref name="tagName"/> and <paramref name="attributes"/>: false
	/// when the name is not a tag name or not allowed; otherwise the tag with the attributes this
	/// policy keeps, re-encoded. A malformed attribute string keeps none of them.
	/// </summary>
	public bool TryCreate(string tagName, string? attributes, [NotNullWhen(true)] out HtmlMarkup? markup)
	{
		ArgumentNullException.ThrowIfNull(tagName);
		markup = null;
		if (!HtmlMarkup.IsValidTagName(tagName) || AllowedTags is { } tags && !tags.Contains(tagName)) return false;

		markup = new HtmlMarkup(tagName, HtmlMarkup.TryParseAttributes(attributes, out var parsed)
			? HtmlMarkup.Join(Keep(parsed).ToArray())
			: null);
		return true;
	}

	/// <summary>
	/// <paramref name="markup"/> held to this policy: the same tag with only the attributes it keeps,
	/// or <see langword="null"/> when the tag itself is not allowed.
	/// </summary>
	public HtmlMarkup? Apply(HtmlMarkup markup)
	{
		ArgumentNullException.ThrowIfNull(markup);
		return TryCreate(markup.TagName, markup.Attributes, out var held) ? held : null;
	}

	private IEnumerable<HtmlAttribute> Keep(IReadOnlyList<HtmlAttribute> attributes)
	{
		var kept = attributes.Where(Allows).ToList();
		return OnViolation == HtmlAttributeViolation.DropAllAttributes && kept.Count != attributes.Count ? [] : kept;
	}

	private bool Allows(HtmlAttribute attribute) =>
		(AllowedAttributes is null || AllowedAttributes.Contains(attribute.Name))
		&& (!UrlAttributes.Contains(attribute.Name) || IsSafeAddress(attribute.Value));

	private static bool IsSafeAddress(string value) =>
		!value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) && UrlSafety.IsSafeNavigableUrl(value);
}
