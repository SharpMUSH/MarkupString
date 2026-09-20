using System.Buffers;

namespace MarkupString.Mxp;

/// <summary>
/// Writes an <see cref="MxpElement"/> as the MXP tag it is: <c>&lt;SOUND door.wav V=80&gt;</c> for one
/// that stands alone, and <c>&lt;FRAME …&gt;body&lt;/FRAME&gt;</c> for one that wraps content.
/// </summary>
/// <remarks>
/// The carrier a standalone element rides on is dropped rather than written: the tag stands in its
/// place, and a zero-width space left behind would travel to clients that never asked for one.
/// </remarks>
public sealed class MxpElementEmitter : IMarkupEmitter
{
	/// <summary>The one instance; the emitter carries no state.</summary>
	public static readonly MxpElementEmitter Instance = new();

	private MxpElementEmitter()
	{
	}

	/// <inheritdoc/>
	public Type MarkupType => typeof(MxpElement);

	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Mxp;

	/// <inheritdoc/>
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		var element = (MxpElement)markup;
		output.Write(element.ToString());

		if (!element.WrapsContent) return;

		output.Write(body);
		output.Write("</");
		output.Write(element.Name);
		output.Write(">");
	}
}

/// <summary>
/// Writes an <see cref="MxpElement"/> for a format that has no MXP: the body for an element that wraps
/// content, and nothing at all for one that stands alone, carrier included.
/// </summary>
/// <remarks>
/// Registered for every format this package does not translate, so the same text can go to every client:
/// a terminal is not sent a tag it would show as text, and it is not sent the zero-width space the tag
/// rode on either.
/// </remarks>
public sealed class MxpSilentEmitter(MarkupFormat format) : IMarkupEmitter
{
	/// <inheritdoc/>
	public Type MarkupType => typeof(MxpElement);

	/// <inheritdoc/>
	public MarkupFormat Format { get; } = format;

	/// <inheritdoc/>
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		if (((MxpElement)markup).WrapsContent) output.Write(body);
	}
}
