using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run to a terminal. Every layer of the run that carries a style folds into one
/// <see cref="AnsiStyle"/>, which is diffed against the style the previous run left in effect, so
/// the stream carries only what actually changes. The state is closed with <c>ESC[0m</c> when the
/// run is the last one or plain text follows it; between adjacent runs the next run's diff does it.
/// </summary>
/// <remarks>
/// Links and pictures are written as the client's <see cref="AnsiOutputOptions.Features"/> allow: a URL link
/// as OSC 8, a command link as MSLP, a row of a picture's cells (<see cref="PictureCellsMarkup"/>) as the
/// picture. Without the feature, a link is its text and a picture its text art.
/// </remarks>
/// <param name="options">What the client is sent: its colour depth and what else its terminal can do.</param>
public sealed class AnsiSetEmitter(AnsiOutputOptions options) : IMarkupSetEmitter
{
	/// <summary>Every colour as it is, and URL links as OSC 8 hyperlinks.</summary>
	public AnsiSetEmitter() : this(new AnsiOutputOptions())
	{
	}

	/// <summary>Every colour at <paramref name="colorDepth"/>, and URL links as OSC 8 hyperlinks or as their text alone.</summary>
	/// <param name="colorDepth">The colour the client can display; each style is written at this depth.</param>
	/// <param name="hyperlinks">Whether a URL link is written as an OSC 8 hyperlink, or as its text alone.</param>
	public AnsiSetEmitter(AnsiColorDepth colorDepth, bool hyperlinks)
		: this(new AnsiOutputOptions(colorDepth, hyperlinks ? TerminalFeatures.Hyperlinks : TerminalFeatures.None))
	{
	}

	private readonly AnsiOutputOptions _options = options ?? throw new ArgumentNullException(nameof(options));
	private readonly TerminalFeatures _pictureMethod = options.Pictures is null ? TerminalFeatures.None : TerminalPictureWriter.Method(options.Features);

	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Ansi;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);

		var depth = _options.ColorDepth;
		var effective = AnsiEmitterSupport.Fold(set, context.Format).AtDepth(depth);
		var previous = AnsiEmitterSupport.Fold(context.Previous, context.Format).AtDepth(depth);

		using var core = new PooledCharWriter(body.Length + 32);
		SgrWriter.Transition(previous, effective, core);
		if (Picture(set) is { } picture)
		{
			// The run that starts the row draws all of it; the others in the same row draw nothing, so the row is
			// as many cells as the text it stands over.
			if (context.StartsRegion(picture.Cells))
				TerminalPictureWriter.Write(_pictureMethod, picture.Cells, picture.Pixels, effective, _options, core, output);
		}
		else
		{
			AnsiEmitterSupport.WriteLinked(effective, body, _options.Features, core);
		}

		// Nothing follows that would diff this state away, so close it here rather than leaving the
		// terminal coloured for whatever the connection writes next.
		if (context.Next is null && AnsiEmitterSupport.LeavesState(effective)) SgrWriter.Reset(core);

		AnsiEmitterSupport.WriteWrapped(set, core.WrittenSpan, context, output);
		return true;
	}

	/// <summary>The picture this run is a row of, when this client draws it and its pixels are to hand.</summary>
	private (PictureCellsMarkup Cells, TerminalPicture Pixels)? Picture(MarkupSet set)
	{
		if (_pictureMethod == TerminalFeatures.None) return null;
		for (var i = 0; i < set.Count; i++)
		{
			if (set[i] is not PictureCellsMarkup cells) continue;
			return TerminalPictureWriter.CanDraw(_pictureMethod, cells)
				&& _options.Pictures!.TryGetPicture(cells.Image, out var pixels)
				? (cells, pixels)
				: null;
		}
		return null;
	}
}
