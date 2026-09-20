using System.Text;
namespace MarkupString.Html;

/// <summary>
/// One attribute of an <see cref="HtmlMarkup"/> tag: a name and its unencoded value. It is written
/// as <c>name="value"</c> with the value encoded, so nothing in it can end the attribute, the tag
/// or the line.
/// </summary>
/// <param name="Name">The attribute name, which must pass <see cref="HtmlMarkup.IsValidAttributeName"/>.</param>
/// <param name="Value">The value as it should read after decoding; the empty string for a bare attribute.</param>
/// <exception cref="ArgumentException"><paramref name="Name"/> is not an attribute name.</exception>
public readonly record struct HtmlAttribute(string Name, string Value)
{
	/// <summary>The four characters that are markup inside a double-quoted attribute value.</summary>
	private const string Significant = "&\"<>";

	/// <summary>
	/// Whether <paramref name="value"/> holds anything this has to rewrite: one of
	/// <see cref="Significant"/>, or a control character. A newline in a value would end the line the tag
	/// sits on — in MXP, a line in secure mode whose successor is not — and an ESC would open an escape
	/// sequence inside the tag. <see cref="MarkupTextRenderer"/> drops controls from body text; a value
	/// is no different, and a value read out of somewhere untrusted is where they arrive.
	/// </summary>
	private static bool NeedsEncoding(ReadOnlySpan<char> value) =>
		value.IndexOfAny(Significant) >= 0
		|| value.ContainsAnyInRange('\u0000', '\u001f')
		|| value.Contains('\u007f');

	/// <summary>The attribute name, which must pass <see cref="HtmlMarkup.IsValidAttributeName"/>.</summary>
	/// <remarks>
	/// Checked here rather than only in <see cref="HtmlMarkup.Tag"/>: <see cref="ToString"/> is public, and
	/// a name holding a space or a quote would write a second attribute out of one.
	/// </remarks>
	public string Name { get; init => field = Validated(value); } = Validated(Name);

	/// <exception cref="ArgumentException"><paramref name="name"/> is not an attribute name.</exception>
	private static string Validated(string? name) =>
		name is null || HtmlMarkup.IsValidAttributeName(name)
			? name!
			: throw new ArgumentException($"'{name}' is not an attribute name.", nameof(name));

	/// <summary>The attribute as written in a tag: <c>name="value"</c>, the value encoded.</summary>
	public override string ToString() => $"{Name}=\"{Encode(Value)}\"";

	/// <summary>
	/// Encodes the four characters that are markup inside a double-quoted attribute value —
	/// <c>&amp;</c>, <c>"</c>, <c>&lt;</c> and <c>&gt;</c> — and drops control characters. Nothing else
	/// is touched: a numeric entity for an accented letter is one more thing an MXP or Pueblo client may
	/// not decode.
	/// </summary>
	internal static string Encode(string? value)
	{
		if (string.IsNullOrEmpty(value)) return string.Empty;

		var text = value.AsSpan();
		if (!NeedsEncoding(text)) return value;

		var written = new StringBuilder(value.Length + 16);
		foreach (var c in text)
		{
			switch (c)
			{
				case '&': written.Append("&amp;"); break;
				case '"': written.Append("&quot;"); break;
				case '<': written.Append("&lt;"); break;
				case '>': written.Append("&gt;"); break;
				default:
					if (!char.IsControl(c)) written.Append(c);
					break;
			}
		}

		return written.ToString();
	}
}
