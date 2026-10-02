using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run for a Pueblo client: colours and attributes as SGR — Pueblo clients read ANSI —
/// and links as <c>&lt;A XCH_CMD&gt;</c> (command) or <c>&lt;A HREF&gt;</c> (URL).
/// </summary>
/// <param name="colorDepth">The colour the client can display; each style is written at this depth.</param>
public sealed class AnsiPuebloEmitter(AnsiColorDepth colorDepth) : IMarkupSetEmitter
{
	/// <summary>Every colour as it is.</summary>
	public AnsiPuebloEmitter() : this(AnsiColorDepth.TrueColor)
	{
	}

	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Pueblo;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);
		AnsiEmitterSupport.EmitTagged(set, body, context, output, TagFlavour.Pueblo, colorDepth);
		return true;
	}
}
