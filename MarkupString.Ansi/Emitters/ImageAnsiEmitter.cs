using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>The terminal picture fallback for a dialect that cannot draw an image itself.</summary>
internal sealed class ImageAnsiEmitter(AnsiOutputOptions options) : IMarkupEmitter
{
	public Type MarkupType => typeof(ImageMarkup);
	public MarkupFormat Format => MarkupFormat.Ansi;

	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		// ANSI's set emitter has already drawn the picture before wrapping its remaining layers.
		if (context.Format == MarkupFormat.Ansi)
		{
			output.Write(body);
			return;
		}

		var image = (ImageMarkup)markup;
		var method = TerminalPictureWriter.Method(options.ColorDepth is AnsiColorDepth.Attributes or AnsiColorDepth.None
			? options.Features & ~TerminalFeatures.BlockArt : options.Features);
		if (method == TerminalFeatures.None || image.Row is not { } cells || !TerminalPictureWriter.CanDraw(method, cells)
			|| options.Pictures is null || !options.Pictures.TryGetPicture(image with { Row = null }, out var pixels))
		{
			output.Write(body);
			return;
		}

		if (context.StartsRegion(image))
		{
			var style = AnsiEmitterSupport.Fold(context.Markups, context.Format).AtDepth(options.ColorDepth);
			SgrWriter.Transition(AnsiStyle.None, style, output);
			TerminalPictureWriter.Write(method, cells, pixels, style, options, output, output);
			if (AnsiEmitterSupport.LeavesState(style)) SgrWriter.Reset(output);
		}
	}
}
