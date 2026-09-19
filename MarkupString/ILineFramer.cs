using System.Buffers;
namespace MarkupString;

/// <summary>
/// Writes whatever a format needs at the start of every line of a rendered text — MXP's secure-line
/// prefix, which a client needs on each line before it will read the tags on that line. Registered
/// per format, alongside and independently of an <see cref="IFormatFramer"/>; at most one per format.
/// </summary>
/// <remarks>
/// The renderer calls <see cref="WriteLineStart"/> before each line that has content, after any
/// <see cref="IFormatFramer"/> preamble. A line holding nothing, or only the <c>\r</c> of a
/// <c>\r\n</c> pair, gets nothing: a prefix on an empty line would be the only thing on it.
/// </remarks>
public interface ILineFramer
{
	/// <summary>The format this frames.</summary>
	MarkupFormat Format { get; }

	/// <summary>Written at the start of every line that has content.</summary>
	void WriteLineStart(IBufferWriter<char> output);
}
