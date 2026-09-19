using System.Net;
using MarkupString.Ansi;
namespace MarkupString.Html;

/// <summary>
/// A markup layer carrying one raw HTML tag: <c>&lt;{TagName} {Attributes}&gt;…&lt;/{TagName}&gt;</c>.
/// Value-equal through <see cref="TagName"/> and <see cref="Attributes"/>, so two identically
/// tagged spans coalesce.
/// </summary>
/// <remarks>
/// <see cref="Create"/> takes the tag name and attribute string as given and writes them unchecked.
/// <see cref="Tag"/> builds one from a checked name and <see cref="HtmlAttribute"/>s it encodes, and
/// <see cref="HtmlTagPolicy.TryCreate"/> does the same for a raw attribute string from somewhere
/// untrusted, keeping only what the policy allows.
/// </remarks>
public sealed record HtmlMarkup(string TagName, string? Attributes) : IMarkup, IAnsiStyleSource
{
	/// <summary>Creates a layer for <paramref name="tagName"/>, optionally with a raw attribute string, both unchecked.</summary>
	public static HtmlMarkup Create(string tagName, string? attributes = null) => new(tagName, attributes);

	/// <summary>
	/// Creates a layer for <paramref name="tagName"/> with <paramref name="attributes"/>, each written
	/// <c>name="value"</c> with its value encoded.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// <paramref name="tagName"/> fails <see cref="IsValidTagName"/>, or an attribute's name fails
	/// <see cref="IsValidAttributeName"/>.
	/// </exception>
	public static HtmlMarkup Tag(string tagName, params ReadOnlySpan<HtmlAttribute> attributes)
	{
		ArgumentNullException.ThrowIfNull(tagName);
		if (!IsValidTagName(tagName))
		{
			throw new ArgumentException($"'{tagName}' is not a tag name: a letter, then letters, digits or hyphens.", nameof(tagName));
		}

		foreach (var attribute in attributes)
		{
			if (attribute.Name is null || !IsValidAttributeName(attribute.Name))
			{
				throw new ArgumentException($"'{attribute.Name}' is not an attribute name.", nameof(attributes));
			}
		}

		return new HtmlMarkup(tagName, Join(attributes));
	}

	/// <summary>
	/// Whether <paramref name="name"/> can stand as a tag name: an ASCII letter, then ASCII letters,
	/// digits or hyphens. Anything else — a space, a quote, a <c>&gt;</c> — would let the name carry
	/// attributes of its own or close the tag.
	/// </summary>
	public static bool IsValidTagName(ReadOnlySpan<char> name)
	{
		if (name.IsEmpty || !char.IsAsciiLetter(name[0])) return false;
		foreach (var c in name[1..])
		{
			if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
		}

		return true;
	}

	/// <summary>
	/// Whether <paramref name="name"/> can stand as an attribute name: an ASCII letter, <c>_</c> or
	/// <c>:</c>, then ASCII letters, digits, <c>_</c>, <c>:</c>, <c>.</c> or <c>-</c>.
	/// </summary>
	public static bool IsValidAttributeName(ReadOnlySpan<char> name)
	{
		if (name.IsEmpty || !(char.IsAsciiLetter(name[0]) || name[0] is '_' or ':')) return false;
		foreach (var c in name[1..])
		{
			if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or ':' or '.' or '-')) return false;
		}

		return true;
	}

	/// <summary>
	/// Reads a raw attribute string — <c>href="x" title='y' size=3 noshade</c> — into its attributes,
	/// with each value as a client reads it: out of its quotes, and with its entities decoded, so
	/// <c>&amp;#106;avascript:</c> is checked as the <c>javascript:</c> it is. False when the string is
	/// malformed: a name that fails <see cref="IsValidAttributeName"/>, or a quote that is never closed.
	/// </summary>
	public static bool TryParseAttributes(string? attributes, out IReadOnlyList<HtmlAttribute> parsed)
	{
		List<HtmlAttribute> found = [];
		parsed = found;
		var rest = (attributes ?? string.Empty).AsSpan().Trim();
		while (!rest.IsEmpty)
		{
			var nameEnd = rest.IndexOfAny("= \t\r\n");
			var name = nameEnd < 0 ? rest : rest[..nameEnd];
			if (!IsValidAttributeName(name)) return false;

			rest = rest[name.Length..].TrimStart();
			if (rest.IsEmpty || rest[0] != '=')
			{
				found.Add(new HtmlAttribute(name.ToString(), string.Empty));
				continue;
			}

			rest = rest[1..].TrimStart();
			if (!TakeValue(ref rest, out var value)) return false;
			found.Add(new HtmlAttribute(name.ToString(), value));
			rest = rest.TrimStart();
		}

		return true;
	}

	private static bool TakeValue(ref ReadOnlySpan<char> rest, out string value)
	{
		value = string.Empty;
		if (rest.IsEmpty) return true;

		if (rest[0] is '"' or '\'')
		{
			var close = rest[1..].IndexOf(rest[0]);
			if (close < 0) return false;
			value = WebUtility.HtmlDecode(rest.Slice(1, close).ToString());
			rest = rest[(close + 2)..];
			return true;
		}

		var end = rest.IndexOfAny(" \t\r\n");
		value = WebUtility.HtmlDecode((end < 0 ? rest : rest[..end]).ToString());
		rest = end < 0 ? [] : rest[end..];
		return true;
	}

	internal static string? Join(ReadOnlySpan<HtmlAttribute> attributes)
	{
		if (attributes.IsEmpty) return null;
		List<string> written = new(attributes.Length);
		foreach (var attribute in attributes) written.Add(attribute.ToString());
		return string.Join(' ', written);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Only <see cref="MarkupFormat.Ansi"/> and <see cref="MarkupFormat.BBCode"/> have a terminal
	/// equivalent for a handful of tags — <c>b</c>/<c>strong</c>, <c>i</c>/<c>em</c>, <c>u</c>, and
	/// <c>s</c>/<c>strike</c>/<c>del</c> — so those tags fold into the run's style there. Every other
	/// tag, and every tag in Html/Pueblo/Mxp, answers <see langword="false"/>: Html/Pueblo/Mxp
	/// render the tag itself through <see cref="HtmlTagEmitter"/>, and an unknown tag in
	/// Ansi/BBCode leaves its body untouched.
	/// </remarks>
	public bool TryGetAnsiStyle(MarkupFormat format, out AnsiStyle style)
	{
		if (format != MarkupFormat.Ansi && format != MarkupFormat.BBCode)
		{
			style = AnsiStyle.None;
			return false;
		}

		switch (TagName.ToLowerInvariant())
		{
			case "b" or "strong":
				style = AnsiStyle.None with { Bold = true };
				return true;
			case "i" or "em":
				style = AnsiStyle.None with { Italic = true };
				return true;
			case "u":
				style = AnsiStyle.None with { Underlined = true };
				return true;
			case "s" or "strike" or "del":
				style = AnsiStyle.None with { StrikeThrough = true };
				return true;
			default:
				style = AnsiStyle.None;
				return false;
		}
	}
}
