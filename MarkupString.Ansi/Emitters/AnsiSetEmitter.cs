using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run to a terminal. Every layer of the run that carries a style folds into one
/// <see cref="AnsiStyle"/>, which is diffed against the style the previous run left in effect, so
/// the stream carries only what actually changes. The state is closed with <c>ESC[0m</c> when the
/// run is the last one or plain text follows it; between adjacent runs the next run's diff does it.
/// </summary>
/// <param name="colorDepth">The colour the client can display; each style is written at this depth.</param>
/// <param name="hyperlinks">Whether a URL link is written as an OSC 8 hyperlink, or as its text alone.</param>
public sealed class AnsiSetEmitter(AnsiColorDepth colorDepth, bool hyperlinks) : IMarkupSetEmitter
{
	/// <summary>Every colour as it is, and URL links as OSC 8 hyperlinks.</summary>
	public AnsiSetEmitter() : this(AnsiColorDepth.TrueColor, hyperlinks: true)
	{
	}

	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Ansi;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);

		var effective = AnsiEmitterSupport.Fold(set, context.Format).AtDepth(colorDepth);
		var previous = AnsiEmitterSupport.Fold(context.Previous, context.Format).AtDepth(colorDepth);

		using var core = new PooledCharWriter(body.Length + 32);
		SgrWriter.Transition(previous, effective, core);
		if (hyperlinks) AnsiEmitterSupport.WriteHyperlinked(effective, body, core);
		else core.Write(body);

		// Nothing follows that would diff this state away, so close it here rather than leaving the
		// terminal coloured for whatever the connection writes next.
		if (context.Next is null && AnsiEmitterSupport.LeavesState(effective)) SgrWriter.Reset(core);

		AnsiEmitterSupport.WriteWrapped(set, core.WrittenSpan, context, output);
		return true;
	}
}
