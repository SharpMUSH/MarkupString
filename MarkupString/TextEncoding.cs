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

	/// <summary>
	/// <see cref="Html"/>, and a line ending becomes <c>&lt;BR&gt;</c> and a newline: a client that
	/// renders the stream as HTML reads a newline as whitespace, so without this every line runs into
	/// the one after it. The <c>\r</c> of a <c>\r\n</c> goes with it — the break is the tag now, and the
	/// newline is there to keep the wire readable. PennMUSH's <c>queue_eol</c> writes exactly
	/// <c>&lt;BR&gt;\n</c> in HTML mode.
	/// </summary>
	/// <remarks>
	/// This is the line discipline of a whole stream, which is what <see cref="MarkupFormat.Pueblo"/> is.
	/// It is deliberately not <see cref="MarkupFormat.Html"/>'s: a page decides its own line handling in
	/// its stylesheet, and whether a break is a <c>&lt;br&gt;</c> or a paragraph belongs to the document.
	/// Nor is it MXP's, whose clients are line-oriented and read a newline as a break already.
	/// </remarks>
	HtmlLineBreaks,
}
