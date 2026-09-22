using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run as BBCode. The vocabulary is small: one colour, four attributes and a URL tag.
/// Faint, blink and overline have no BBCode spelling and are dropped, and so is a command link —
/// a forum post has nowhere to send it.
/// </summary>
public sealed class AnsiBBCodeEmitter : IMarkupSetEmitter
{
	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.BBCode;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);

		AnsiEmitterSupport.EmitSegmented(set, body, context, output, WriteTags);
		return true;
	}

	/// <summary>Writes one stretch of folded layers as BBCode tags.</summary>
	private static void WriteTags(in AnsiStyle style, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		// Reverse video has no BBCode form either, so the colour that would show as the text colour
		// is the one written.
		var foreground = style.Inverted ? style.Background : style.Foreground;
		var hex = foreground?.ToHex() ?? string.Empty;

		var link = style.LinkKind == LinkKind.Url
			&& style.LinkUrl is { Length: > 0 } candidate
			&& UrlSafety.IsSafeNavigableUrl(candidate)
			? candidate
			: null;

		if (hex.Length > 0)
		{
			output.Write("[color=");
			output.Write(hex);
			output.Write("]");
		}
		if (link is not null)
		{
			output.Write("[url=");
			output.Write(link);
			output.Write("]");
		}
		if (style.Bold) output.Write("[b]");
		if (style.Italic) output.Write("[i]");
		if (style.Underlined) output.Write("[u]");
		if (style.StrikeThrough) output.Write("[s]");

		output.Write(body);

		if (style.StrikeThrough) output.Write("[/s]");
		if (style.Underlined) output.Write("[/u]");
		if (style.Italic) output.Write("[/i]");
		if (style.Bold) output.Write("[/b]");
		if (link is not null) output.Write("[/url]");
		if (hex.Length > 0) output.Write("[/color]");
	}
}
