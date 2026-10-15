using System.Buffers;
using System.Globalization;
namespace MarkupString.Ansi;

/// <summary>The pieces of Markdown more than one of this package's Markdown emitters writes.</summary>
internal static class MarkdownSyntax
{
	/// <summary>
	/// <paramref name="url"/> as a link destination: whitespace, control characters, parentheses, angle
	/// brackets, the backslash and the pipe percent-encoded, so the address ends where the link does and
	/// a table cell does not end inside it.
	/// </summary>
	public static string Destination(string url)
	{
		var plain = true;
		foreach (var c in url)
		{
			if (NeedsEncoding(c))
			{
				plain = false;
				break;
			}
		}
		if (plain) return url;

		var encoded = new System.Text.StringBuilder(url.Length + 8);
		foreach (var c in url)
		{
			if (NeedsEncoding(c) && c <= 0x7f) encoded.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
			else if (!char.IsControl(c) && !char.IsWhiteSpace(c)) encoded.Append(c);
		}
		return encoded.ToString();
	}

	private static bool NeedsEncoding(char c) =>
		c is '(' or ')' or '<' or '>' or '\\' or '|' or ' ' or '`' || char.IsControl(c) || char.IsWhiteSpace(c);

	/// <summary>
	/// <paramref name="source"/> as an image's destination when a page may load it — an http(s) or relative
	/// address with no whitespace in it — or null when it is to be described instead.
	/// </summary>
	public static string? ImageSource(string? source)
	{
		if (string.IsNullOrWhiteSpace(source) || source.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return null;

		// On Unix a rooted path parses as an absolute file: address and "//host" as one on another host,
		// so both are decided before the parser sees them.
		if (source.StartsWith("//", StringComparison.Ordinal) || source.StartsWith('\\')) return null;
		if (!source.StartsWith('/'))
		{
			if (!Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out var uri)) return null;
			if (uri.IsAbsoluteUri && uri.Scheme is not ("http" or "https")) return null;
		}
		return Destination(source);
	}

	/// <summary>Writes an image, <c>![description](destination)</c>, its description on one line.</summary>
	public static void WriteImage(string? description, string destination, IBufferWriter<char> output)
	{
		output.Write("![");
		if (description is { Length: > 0 })
			MarkupTextRenderer.EncodeText(description.ReplaceLineEndings(" "), TextEncoding.Markdown, output);
		output.Write("](");
		output.Write(destination);
		output.Write(")");
	}
}
