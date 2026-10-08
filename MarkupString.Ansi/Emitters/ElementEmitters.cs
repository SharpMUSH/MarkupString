using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Clears a terminal's screen. The rest of the shared vocabulary is left to the core rule for a
/// terminal: a point writes nothing, and a markup wrapping text writes the text.
/// </summary>
internal sealed class ClearScreenAnsiEmitter : IMarkupEmitter
{
	/// <summary>Home the cursor, then erase the display.</summary>
	internal const string Sequence = "\u001b[H\u001b[2J";

	public Type MarkupType => typeof(ClearScreenMarkup);

	public MarkupFormat Format => MarkupFormat.Ansi;

	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(output);
		output.Write(Sequence);
	}
}

/// <summary>A picture as BBCode's <c>[img]</c>, in place of the text a terminal is shown.</summary>
internal sealed class ImageBBCodeEmitter : IMarkupEmitter
{
	public Type MarkupType => typeof(ImageMarkup);

	public MarkupFormat Format => MarkupFormat.BBCode;

	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(output);

		// A bracket would close the tag early, and BBCode has no way to escape one.
		var image = (ImageMarkup)markup;
		if (image.Source.AsSpan().IndexOfAny('[', ']') >= 0)
		{
			output.Write(body);
			return;
		}

		if (image.StartsPicture(context))
		{
			output.Write("[img]");
			output.Write(image.Source);
			output.Write("[/img]");
		}

		// A figure's rows keep their cells, so what is beside the picture stays where it was.
		if (image.Row is not null) output.Write(new string(' ', DisplayWidth.Of(body)));
	}
}
