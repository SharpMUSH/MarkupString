namespace MarkupString;

/// <summary>
/// Text laid out by its own spacing: a table, a map, a listing. Line endings inside it are the text's
/// own and its font is fixed-width. <see cref="MarkupText.Preformatted"/> builds one.
/// </summary>
/// <remarks>
/// Pueblo writes <c>&lt;xch_mudtext&gt;</c>, which puts the client back on MUD-text conventions for the
/// text it wraps, and HTML writes <c>&lt;pre&gt;</c>. A terminal already lays text out this way and is
/// given it unchanged.
/// </remarks>
public sealed class PreformattedMarkup : ITextEncodingSource
{
	/// <summary>The one instance; preformatting carries no state.</summary>
	public static readonly PreformattedMarkup Instance = new();

	private PreformattedMarkup()
	{
	}

	/// <summary>
	/// Suspends a format's line-break substitution for the text this covers: a client reading the region
	/// as MUD text breaks the lines itself, and a <c>&lt;BR&gt;</c> as well would double every one of
	/// them. Everything else about the encoding — the entities, the dropped control characters — stays.
	/// </summary>
	public bool TryGetEncoding(MarkupFormat format, out TextEncoding encoding)
	{
		ArgumentNullException.ThrowIfNull(format);

		encoding = TextEncoding.Html;
		return format.Encoding == TextEncoding.HtmlLineBreaks;
	}
}
