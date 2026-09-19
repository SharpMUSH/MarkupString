using System.Buffers;
namespace MarkupString;

/// <summary>
/// Opens every line of <see cref="MarkupFormat.Mxp"/> output in MXP secure mode (<c>ESC[1z</c>). An MXP
/// client reads tags only on a line in secure mode, and the mode ends at the newline, so without this
/// on each line every tag in the output — a link, a <c>&lt;B&gt;</c> — reaches the player as text.
/// Install it with <see cref="MarkupRegistry.WithMxpSecureLines"/>.
/// </summary>
/// <remarks>
/// <para>It belongs to the format rather than to any kind of markup: whichever package wrote the tags,
/// the line has to be opened for them.</para>
/// <para>Opt-in: the prefix belongs to output bound for an MXP session, and a render of the same text
/// for a test, a log or a preview wants the tags alone. Use a registry with this framer at the
/// connection boundary and the plain one everywhere else. Negotiating MXP and starting MXP mode on the
/// connection are the telnet layer's; this only frames the text sent once it has.</para>
/// </remarks>
public sealed class MxpSecureLineFramer : ILineFramer
{
	/// <summary>The secure-line mode sequence, <c>ESC[1z</c>.</summary>
	public const string SecureLine = "\e[1z";

	/// <summary>The one instance; the framer carries no state.</summary>
	public static readonly MxpSecureLineFramer Instance = new();

	private MxpSecureLineFramer()
	{
	}

	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Mxp;

	/// <inheritdoc/>
	public void WriteLineStart(IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(output);
		output.Write(SecureLine);
	}
}
