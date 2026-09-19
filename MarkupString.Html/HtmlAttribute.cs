namespace MarkupString.Html;

/// <summary>
/// One attribute of an <see cref="HtmlMarkup"/> tag: a name and its unencoded value. It is written
/// as <c>name="value"</c> with the value encoded, so a quote or a <c>&gt;</c> in it cannot end the
/// attribute or the tag.
/// </summary>
/// <param name="Name">The attribute name, checked by <see cref="HtmlMarkup.IsValidAttributeName"/> wherever it is written.</param>
/// <param name="Value">The value as it should read after decoding; the empty string for a bare attribute.</param>
public readonly record struct HtmlAttribute(string Name, string Value)
{
	/// <summary>The attribute as written in a tag: <c>name="value"</c>, the value encoded.</summary>
	public override string ToString() => $"{Name}=\"{Encode(Value)}\"";

	/// <summary>
	/// Encodes the four characters that matter inside a double-quoted attribute value: <c>&amp;</c>,
	/// <c>"</c>, <c>&lt;</c> and <c>&gt;</c>. Nothing else is touched — a numeric entity for an
	/// accented letter is one more thing an MXP or Pueblo client may not decode.
	/// </summary>
	internal static string Encode(string value) =>
		value.AsSpan().IndexOfAny("&\"<>") < 0
			? value
			: value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}
