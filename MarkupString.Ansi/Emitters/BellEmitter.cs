using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Writes a <see cref="BellMarkup"/>: the bell character itself for a client that reads one, and for
/// <see cref="MarkupFormat.Html"/> an empty <c>ms-bell</c> element, which is a page's cue to do
/// whatever it does about a bell — a sound, a flash, a title change, nothing.
/// </summary>
/// <remarks>
/// One instance per format. The body is the U+0007 the bell rides on, and it is not written through:
/// the Html, Pueblo and Mxp encodings drop control characters from text, so the character a terminal
/// needs is written here rather than left to survive an encoding that removes it.
/// </remarks>
public sealed class BellEmitter(MarkupFormat format) : IMarkupEmitter
{
	/// <summary>The element an HTML page receives in place of the character.</summary>
	public const string HtmlElement = "<span class=\"ms-bell\" role=\"alert\"></span>";

	/// <inheritdoc/>
	public Type MarkupType => typeof(BellMarkup);

	/// <inheritdoc/>
	public MarkupFormat Format { get; } = format;

	/// <inheritdoc/>
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		output.Write(Format == MarkupFormat.Html ? HtmlElement : BellMarkup.Character);
	}
}
