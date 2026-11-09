namespace MarkupString;

/// <summary>
/// An output format a <see cref="MarkupText"/> can be rendered to. Two formats are equal when
/// their <see cref="Name"/>s match ordinally, ignoring case. <see cref="Custom(string, TextEncoding)"/> refuses a name
/// that matches one of the seven built-ins, so a caller cannot accidentally address a built-in's
/// registry slots under different format metadata. Custom declarations with the same name must use
/// consistent <see cref="Encoding"/> and <see cref="IsTagged"/> values; identity remains name-based.
/// </summary>
public sealed class MarkupFormat : IEquatable<MarkupFormat>
{
	public string Name { get; }
	public TextEncoding Encoding { get; }

	/// <summary>
	/// Whether fragments use angle-bracket tags and comments plus HTML-style entities, the lexical
	/// grammar measured by <see cref="DisplayWidth.OfTagged"/>. This does not mean every kind of tag:
	/// BBCode's square-bracket tags and Markdown syntax do not qualify.
	/// </summary>
	/// <remarks>
	/// Independent of <see cref="Encoding"/>, which only escapes literal source text. This capability
	/// describes the common fragment syntax, not a format's full semantics, browser layout or protocol.
	/// </remarks>
	public bool IsTagged { get; }

	private MarkupFormat(string name, TextEncoding encoding, bool isTagged = false)
	{
		Name = name;
		Encoding = encoding;
		IsTagged = isTagged;
	}

	public static readonly MarkupFormat Plain = new("plain", TextEncoding.StripControls);
	public static readonly MarkupFormat Ansi = new("ansi", TextEncoding.None);
	public static readonly MarkupFormat Html = new("html", TextEncoding.Html, isTagged: true);
	public static readonly MarkupFormat Pueblo = new("pueblo", TextEncoding.HtmlLineBreaks, isTagged: true);
	public static readonly MarkupFormat Mxp = new("mxp", TextEncoding.Html, isTagged: true);
	public static readonly MarkupFormat BBCode = new("bbcode", TextEncoding.StripControls);

	/// <summary>GitHub-flavoured Markdown, as a wiki or a README stores it. See <see cref="TextEncoding.Markdown"/>.</summary>
	public static readonly MarkupFormat Markdown = new("markdown", TextEncoding.Markdown);

	/// <summary>Declares a format outside the built-in seven, without opting into tagged-fragment parsing.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> matches a built-in format's name, ignoring case.</exception>
	public static MarkupFormat Custom(string name, TextEncoding encoding) => Custom(name, encoding, isTagged: false);

	/// <summary>Declares a custom format with an explicit <see cref="IsTagged"/> capability.</summary>
	/// <param name="name">The format's identity. Use consistent metadata for declarations sharing a name.</param>
	/// <param name="encoding">How literal source text is escaped; does not determine the fragment grammar.</param>
	/// <param name="isTagged">Whether fragments use the angle-bracket and entity grammar documented by
	/// <see cref="DisplayWidth.OfTagged"/>. Leave false for other grammars, including square-bracket tags.</param>
	/// <exception cref="ArgumentException"><paramref name="name"/> matches a built-in format's name, ignoring case.</exception>
	public static MarkupFormat Custom(string name, TextEncoding encoding, bool isTagged)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		var builtIn = TryParse(name);
		if (builtIn is not null)
		{
			throw new ArgumentException($"'{name}' names a built-in format; use MarkupFormat.{builtIn.Name} or pick a different name.", nameof(name));
		}
		return new MarkupFormat(name, encoding, isTagged);
	}

	/// <summary>Resolves one of the seven built-in formats by name, ignoring case. Unknown names give <see langword="null"/>.</summary>
	public static MarkupFormat? TryParse(string name) => name switch
	{
		null => null,
		_ when name.Equals(Plain.Name, StringComparison.OrdinalIgnoreCase) => Plain,
		_ when name.Equals(Ansi.Name, StringComparison.OrdinalIgnoreCase) => Ansi,
		_ when name.Equals(Html.Name, StringComparison.OrdinalIgnoreCase) => Html,
		_ when name.Equals(Pueblo.Name, StringComparison.OrdinalIgnoreCase) => Pueblo,
		_ when name.Equals(Mxp.Name, StringComparison.OrdinalIgnoreCase) => Mxp,
		_ when name.Equals(BBCode.Name, StringComparison.OrdinalIgnoreCase) => BBCode,
		_ when name.Equals(Markdown.Name, StringComparison.OrdinalIgnoreCase) => Markdown,
		_ => null,
	};

	public bool Equals(MarkupFormat? other) =>
		other is not null && (ReferenceEquals(this, other) || string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase));

	public override bool Equals(object? obj) => obj is MarkupFormat other && Equals(other);
	public override int GetHashCode() => string.GetHashCode(Name, StringComparison.OrdinalIgnoreCase);
	public override string ToString() => Name;
	public static bool operator ==(MarkupFormat? a, MarkupFormat? b) => a is null ? b is null : a.Equals(b);
	public static bool operator !=(MarkupFormat? a, MarkupFormat? b) => !(a == b);
}
