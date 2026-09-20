using System.Buffers;

namespace MarkupString.Pueblo;

/// <summary>
/// Writes a <see cref="PuebloElement"/> as the Pueblo tag it is: <c>&lt;xch_page clear="text"&gt;</c> for
/// one that stands alone, and <c>&lt;xch_pane …&gt;body&lt;/xch_pane&gt;</c> for one that wraps content.
/// </summary>
/// <remarks>
/// The carrier a standalone element rides on is dropped rather than written: the tag stands in its
/// place, and a zero-width space left behind would travel to clients that never asked for one.
/// </remarks>
public sealed class PuebloElementEmitter : IMarkupEmitter
{
	private readonly Func<PuebloElement, bool>? _supports;

	/// <summary>An emitter that writes every element, for a connection whose client was never asked.</summary>
	public static readonly PuebloElementEmitter Instance = new(null);

	/// <summary>
	/// An emitter that writes only the elements <paramref name="supports"/> accepts. Pueblo has no exchange
	/// that asks a client what it renders — a world learns that from its handshake and its own
	/// configuration — so this is here for a consumer that knows something the library cannot.
	/// </summary>
	/// <remarks>
	/// An element the predicate refuses is written the way a format without Pueblo writes it: nothing for
	/// one that stands alone, and the content alone for one that wraps, so a pane a client cannot open
	/// does not take the text inside it.
	/// </remarks>
	public PuebloElementEmitter(Func<PuebloElement, bool>? supports)
	{
		_supports = supports;
	}

	/// <inheritdoc/>
	public Type MarkupType => typeof(PuebloElement);

	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Pueblo;

	/// <inheritdoc/>
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		var element = (PuebloElement)markup;
		if (_supports is not null && !_supports(element))
		{
			if (element.WrapsContent) output.Write(body);
			return;
		}

		output.Write(element.ToString());

		if (!element.WrapsContent) return;

		output.Write(body);
		output.Write("</");
		output.Write(element.Name);
		output.Write(">");
	}
}

/// <summary>
/// Writes an <see cref="PuebloElement"/> for a format that has no Pueblo: the body for an element that wraps
/// content, and nothing at all for one that stands alone, carrier included.
/// </summary>
/// <remarks>
/// Registered for every format this package does not translate, so the same text can go to every client:
/// a terminal is not sent a tag it would show as text, and it is not sent the zero-width space the tag
/// rode on either.
/// </remarks>
public sealed class PuebloSilentEmitter(MarkupFormat format) : IMarkupEmitter
{
	/// <inheritdoc/>
	public Type MarkupType => typeof(PuebloElement);

	/// <inheritdoc/>
	public MarkupFormat Format { get; } = format;

	/// <inheritdoc/>
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		if (((PuebloElement)markup).WrapsContent) output.Write(body);
	}
}
