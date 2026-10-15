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

	/// <summary>
	/// GitHub-flavoured Markdown: <see cref="StripControls"/>, a line ending is a newline (the <c>\r</c> of a
	/// <c>\r\n</c> dropped), and every character that would start Markdown syntax is escaped with a backslash,
	/// so the text reads back as itself. <c>\</c>, <c>`</c>, <c>*</c>, <c>_</c>, <c>[</c>, <c>]</c>, <c>&lt;</c>,
	/// <c>|</c> and <c>~</c> are escaped wherever they are; <c>&amp;</c> where it would start an entity; and at the
	/// start of a line <c>#</c>, <c>&gt;</c>, <c>-</c>, <c>+</c>, <c>=</c> and the <c>.</c> or <c>)</c> of a
	/// number that would start a numbered list. Indentation, which a reader would take for code or for the
	/// inside of a list item and shows none of, is dropped.
	/// </summary>
	/// <remarks>
	/// <para>A render knows where on its line each stretch of text starts. A stretch given to
	/// <see cref="MarkupTextRenderer.EncodeText"/> alone may start partway along one, so its start is escaped as
	/// a line's would be, which is more than a reader needs and never less, and its indentation is one space.</para>
	/// <para>Markdown keeps whitespace no more than HTML does: a run of it reads as one space. A single line
	/// ending is a soft break, which a reader joins into the paragraph, and a blank line ends the paragraph.</para>
	/// <para>A block a format draws as its own structure — a layout's table, list or rule — is set apart from
	/// the text before and after it by a blank line, without which a rule under a line of text would make it a
	/// heading, and a line after a table a row of it.</para>
	/// </remarks>
	Markdown,
}
