namespace MarkupString.Decoding;

/// <summary>One argument of a tag: <c>name=value</c>, <c>name</c> alone, or a value with no name.</summary>
/// <param name="Name">The name, or null for a positional value.</param>
/// <param name="Value">The value, unquoted; for a name alone, empty.</param>
public readonly record struct TagArgument(string? Name, string Value);

/// <summary>A tag as MXP and Pueblo write them: <c>&lt;name arg arg=value&gt;</c>, <c>&lt;/name&gt;</c> or <c>&lt;name/&gt;</c>.</summary>
/// <param name="Name">The tag's name, as written.</param>
/// <param name="Closing">Whether it is a closing tag.</param>
/// <param name="Arguments">Its arguments, in order.</param>
public sealed record ParsedTag(string Name, bool Closing, IReadOnlyList<TagArgument> Arguments)
{
	/// <summary>Whether it closes itself (<c>&lt;name/&gt;</c>).</summary>
	public bool SelfClosing { get; init; }

	/// <summary>
	/// The argument named <paramref name="name"/>, ignoring case, or else the positional one at
	/// <paramref name="position"/> (counting positional arguments only), or null.
	/// </summary>
	public string? Get(string name, int position = -1)
	{
		foreach (var argument in Arguments)
		{
			if (argument.Name is not null && argument.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return argument.Value;
		}

		if (position < 0) return null;
		var at = 0;
		foreach (var argument in Arguments)
		{
			if (argument.Name is not null) continue;
			if (at++ == position) return argument.Value;
		}

		return null;
	}

	/// <summary>Whether a flag (a positional argument such as <c>PROMPT</c>) or a named argument is present.</summary>
	public bool Has(string name)
	{
		foreach (var argument in Arguments)
		{
			if (string.Equals(argument.Name ?? argument.Value, name, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}
}

/// <summary>Reads tags and entities the way MXP and Pueblo write them.</summary>
public static class TagSyntax
{
	/// <summary>
	/// Parses what is between <c>&lt;</c> and <c>&gt;</c>. Values may be quoted with <c>"</c> or <c>'</c>; entities in them
	/// are decoded with <paramref name="entity"/> when it is given.
	/// </summary>
	public static ParsedTag? Parse(ReadOnlySpan<char> body, Func<string, string?>? entity = null)
	{
		body = body.Trim();
		var closing = false;
		var selfClosing = false;
		if (body.StartsWith('/'))
		{
			closing = true;
			body = body[1..].TrimStart();
		}

		if (body.EndsWith('/'))
		{
			selfClosing = true;
			body = body[..^1].TrimEnd();
		}

		var nameEnd = 0;
		while (nameEnd < body.Length && !char.IsWhiteSpace(body[nameEnd])) nameEnd++;
		if (nameEnd == 0) return null;
		var name = body[..nameEnd].ToString();
		var rest = body[nameEnd..];

		var arguments = new List<TagArgument>();
		while (true)
		{
			rest = rest.TrimStart();
			if (rest.IsEmpty) break;

			string? argumentName = null;
			if (rest[0] is not ('"' or '\''))
			{
				var end = 0;
				while (end < rest.Length && !char.IsWhiteSpace(rest[end]) && rest[end] != '=') end++;
				var word = rest[..end].ToString();
				rest = rest[end..].TrimStart();
				if (rest.IsEmpty || rest[0] != '=')
				{
					arguments.Add(new TagArgument(null, Decode(word, entity)));
					continue;
				}

				argumentName = word;
				rest = rest[1..].TrimStart();
			}

			string value;
			if (!rest.IsEmpty && rest[0] is '"' or '\'')
			{
				var close = rest[1..].IndexOf(rest[0]);
				value = close < 0 ? rest[1..].ToString() : rest.Slice(1, close).ToString();
				rest = close < 0 ? [] : rest[(close + 2)..];
			}
			else
			{
				var end = 0;
				while (end < rest.Length && !char.IsWhiteSpace(rest[end])) end++;
				value = rest[..end].ToString();
				rest = rest[end..];
			}

			arguments.Add(new TagArgument(argumentName, Decode(value, entity)));
		}

		return new ParsedTag(name, closing, arguments) { SelfClosing = selfClosing };
	}

	/// <summary>
	/// The text an entity name (what is between <c>&amp;</c> and <c>;</c>) stands for: a number (<c>#65</c>,
	/// <c>#x41</c>), one of HTML's, or what <paramref name="entity"/> returns; null when it is none of those.
	/// </summary>
	public static string? Entity(ReadOnlySpan<char> name, Func<string, string?>? entity = null)
	{
		if (name.IsEmpty) return null;
		if (name[0] == '#')
		{
			var number = name[1..];
			var hex = !number.IsEmpty && number[0] is 'x' or 'X';
			if (hex) number = number[1..];
			if (!int.TryParse(number, hex ? System.Globalization.NumberStyles.AllowHexSpecifier : System.Globalization.NumberStyles.None,
				System.Globalization.CultureInfo.InvariantCulture, out var code)) return null;
			if (code is <= 0 or > 0x10FFFF || (code is >= 0xD800 and <= 0xDFFF)) return null;
			// A control character from an entity would reach the screen as a control; it is dropped.
			if (code < 0x20 && code != '\t') return string.Empty;
			return char.ConvertFromUtf32(code);
		}

		if (entity?.Invoke(name.ToString()) is { } defined) return defined;
		var raw = $"&{name};";
		var decoded = System.Net.WebUtility.HtmlDecode(raw);
		return decoded == raw ? null : decoded;
	}

	/// <summary><paramref name="text"/> with its entities decoded; one that is not known is left as it is.</summary>
	public static string Decode(string text, Func<string, string?>? entity = null)
	{
		var amp = text.IndexOf('&');
		if (amp < 0) return text;

		var result = new System.Text.StringBuilder(text.Length);
		var at = 0;
		while (amp >= 0)
		{
			var semicolon = text.IndexOf(';', amp + 1);
			if (semicolon < 0 || semicolon - amp > 32) break;
			var decoded = Entity(text.AsSpan(amp + 1, semicolon - amp - 1), entity);
			result.Append(text, at, amp - at);
			if (decoded is null)
			{
				result.Append(text, amp, semicolon - amp + 1);
			}
			else
			{
				result.Append(decoded);
			}

			at = semicolon + 1;
			amp = text.IndexOf('&', at);
		}

		result.Append(text, at, text.Length - at);
		return result.ToString();
	}
}
