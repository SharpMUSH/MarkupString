namespace MarkupString;

/// <summary>How <see cref="MarkupTextRenderer"/> writes the literal text of a
/// <see cref="MarkupText"/> for a given <see cref="MarkupFormat"/>.</summary>
public enum TextEncoding
{
	/// <summary>The text is copied verbatim, control characters included.</summary>
	None,

	/// <summary>C0 control characters other than <c>\t</c>, <c>\n</c> and <c>\r</c> are dropped, as is U+007F.</summary>
	StripControls,

	/// <summary>
	/// <see cref="StripControls"/>, then <c>&lt; &gt; &amp;</c> -- the characters that are markup in
	/// HTML text -- are written as HTML entities. <c>"</c> and <c>'</c> are not: they are markup only
	/// inside an attribute value, and this encoding is never applied to one.
	/// </summary>
	Html,
}
