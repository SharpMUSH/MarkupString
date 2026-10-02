using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run for an MXP client: colours and attributes as SGR, command links as
/// <c>&lt;SEND&gt;</c> and URL links as <c>&lt;A HREF&gt;</c>.
/// </summary>
/// <param name="colorDepth">The colour the client can display; each style is written at this depth.</param>
public sealed class AnsiMxpEmitter(AnsiColorDepth colorDepth) : IMarkupSetEmitter
{
	/// <summary>Every colour as it is.</summary>
	public AnsiMxpEmitter() : this(AnsiColorDepth.TrueColor)
	{
	}

	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Mxp;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);
		AnsiEmitterSupport.EmitTagged(set, body, context, output, TagFlavour.Mxp, colorDepth);
		return true;
	}
}
