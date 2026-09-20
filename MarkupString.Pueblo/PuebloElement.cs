using System.Collections.Immutable;
using System.Text;

namespace MarkupString.Pueblo;

/// <summary>One attribute of a <see cref="PuebloElement"/>, written HTML-style as <c>name="value"</c>.</summary>
/// <param name="Name">The attribute name, e.g. <c>xch_cmd</c>.</param>
/// <param name="Value">The value, unencoded; the empty string for a flag such as <c>ismap</c>.</param>
public readonly record struct PuebloAttribute(string Name, string Value)
{
	/// <summary>The attribute as it is written in a tag, with the four characters that are markup encoded.</summary>
	public override string ToString() => Value.Length == 0 ? Name : $"{Name}=\"{Encode(Value)}\"";

	/// <summary>
	/// Pueblo reads HTML, so a value is encoded as an HTML attribute value is: <c>&amp;</c>, <c>"</c>,
	/// <c>&lt;</c> and <c>&gt;</c> as entities, everything else as the caller wrote it.
	/// </summary>
	internal static string Encode(string value) =>
		value.AsSpan().IndexOfAny("&\"<>") < 0
			? value
			: value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>
/// One of Pueblo's own extensions as a markup layer: a pane, a page instruction, a sound, a prefetch, a
/// mode switch. <see cref="PuebloElements"/> builds the ones the client understands; this type is also
/// the way to write one it does not.
/// </summary>
/// <remarks>
/// <para>Pueblo renders an HTML subset, and plain HTML belongs in <c>MarkupString.Html</c>'s
/// <c>HtmlMarkup</c>. What lives here is the part that is Pueblo's alone — the <c>xch_</c> vocabulary
/// and the elements around it — which means nothing to a browser or to an MXP client.</para>
/// <para>An element that wraps nothing rides on a zero-width carrier, as MXP's do; one that wraps content
/// marks the text it applies to. A format that cannot express an element writes nothing at all, carrier
/// included, so the same text is safe to send to every client.</para>
/// <para>Styling and links are not here: bold, colour and a command link travel as <c>AnsiMarkup</c>,
/// which every format already writes in its own dialect — <c>&lt;A XCH_CMD&gt;</c> for Pueblo.</para>
/// </remarks>
/// <param name="Name">The element's name, e.g. <c>xch_page</c>.</param>
/// <param name="Attributes">Its attributes, in the order they are written.</param>
/// <param name="WrapsContent">Whether the element closes after the text it marks, rather than standing alone.</param>
public sealed record PuebloElement(string Name, ImmutableArray<PuebloAttribute> Attributes, bool WrapsContent) : IMarkup
{
	/// <summary>
	/// The character a standalone element rides on: a zero-width space, which measures nothing and is
	/// never written out — every format either writes the element or writes nothing.
	/// </summary>
	public const string Carrier = "​";

	/// <summary>Creates an element and the text it stands in, for one that wraps nothing.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> is not an element name.</exception>
	public static MarkupText Standalone(string name, params ReadOnlySpan<PuebloAttribute> attributes) =>
		MarkupText.Wrap(new PuebloElement(Checked(name), [.. attributes], false), Carrier);

	/// <summary>Creates an element wrapping <paramref name="content"/>.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> is not an element name.</exception>
	public static MarkupText Wrapping(string name, MarkupText content, params ReadOnlySpan<PuebloAttribute> attributes) =>
		MarkupText.Wrap(new PuebloElement(Checked(name), [.. attributes], true), content);

	/// <summary>The tag as Pueblo reads it: <c>&lt;name attr="value"&gt;</c>.</summary>
	public override string ToString()
	{
		var written = new StringBuilder("<").Append(Name);
		foreach (var attribute in Attributes) written.Append(' ').Append(attribute.ToString());
		return written.Append('>').ToString();
	}

	/// <summary>An element name is a letter, then letters, digits, hyphens or underscores.</summary>
	public static bool IsValidName(string name)
	{
		if (string.IsNullOrEmpty(name) || !char.IsAsciiLetter(name[0])) return false;

		foreach (var c in name)
		{
			if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_') return false;
		}

		return true;
	}

	private static string Checked(string name) =>
		IsValidName(name)
			? name
			: throw new ArgumentException($"'{name}' is not an element name.", nameof(name));
}
