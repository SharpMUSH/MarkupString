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
/// <para>Every attribute that survives is re-written <c>name="value"</c> with its value encoded,
/// whatever the policy, so nothing in a value can end the attribute or the tag.</para>
/// <para>This library ships no security posture: <see cref="WellFormed"/> allows any well-formed tag,
/// and the sets are yours to narrow to what the medium you render into can be trusted with. What is
/// safe in your application is your decision.</para>
/// </remarks>
public sealed record HtmlTagPolicy
{
	/// <summary>
	/// The tags allowed, ignoring case whatever comparer the given set had; <see langword="null"/>
	/// allows any valid tag name.
	/// </summary>
	public IReadOnlySet<string>? AllowedTags { get; init => field = IgnoringCase(value); }

	/// <summary>
	/// The attributes allowed, ignoring case whatever comparer the given set had;
	/// <see langword="null"/> allows any valid attribute name.
	/// </summary>
	public IReadOnlySet<string>? AllowedAttributes { get; init => field = IgnoringCase(value); }

	/// <summary>
	/// The attributes whose value this policy checks as an address: each must be one
	/// <see cref="UrlSafety.IsSafeNavigableUrl"/> accepts, with no whitespace or control character in it.
	/// Empty by default — which addresses a client of yours may be sent to is your decision, not this
	/// library's. <see cref="AddressAttributes"/> is the list of attributes HTML gives an address to,
	/// if you want to check them all.
	/// </summary>
	public IReadOnlySet<string> UrlAttributes { get; init => field = IgnoringCase(value) ?? throw new ArgumentNullException(nameof(value)); } = FrozenSet<string>.Empty;

	/// <summary>What happens to the attributes when one is not allowed.</summary>
	public HtmlAttributeViolation OnViolation { get; init; } = HtmlAttributeViolation.DropAttribute;

	/// <summary>
	/// The attributes HTML gives an address to, offered for <see cref="UrlAttributes"/>. Nothing applies
	/// it for you.
	/// </summary>
	public static IReadOnlySet<string> AddressAttributes { get; } = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
		"action", "background", "cite", "codebase", "data", "dynsrc", "formaction", "href", "longdesc", "lowsrc",
		"poster", "src", "usemap", "xlink:href");

	/// <summary>
	/// Any tag and any attribute, so long as they are well formed. What it adds over
	/// <see cref="HtmlMarkup.Create"/> is that the name cannot smuggle anything in and every value is
	/// encoded. It is the starting point for a policy of your own: narrow it with <c>with</c>.
	/// </summary>
	public static HtmlTagPolicy WellFormed { get; } = new();

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

	private static FrozenSet<string>? IgnoringCase(IReadOnlySet<string>? set) => set switch
	{
		null => null,
		FrozenSet<string> frozen when frozen.Comparer == StringComparer.OrdinalIgnoreCase => frozen,
		_ => set.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
	};

	private static bool IsSafeAddress(string value) =>
		!value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) && UrlSafety.IsSafeNavigableUrl(value);
}
