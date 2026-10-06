namespace MarkupString;

/// <summary>
/// A markup that describes a whole block of the text it covers — a box, a table, columns — which a
/// format able to draw that structure itself draws in place of the text, through an
/// <see cref="IBlockEmitter"/>.
/// </summary>
/// <remarks>
/// <para>The text stays the real value: every format without a block emitter writes it, and every
/// string operation works on it. The renderer hands a block to its emitter only when the stretch the
/// layer covers is still the text it was built over (<see cref="Covers"/>) and stands on lines of its
/// own; a block that was cut, edited or run into other text on a line renders as the text it now is.</para>
/// </remarks>
public interface IBlockMarkup : IMarkup
{
	/// <summary>Whether <paramref name="text"/> is exactly the text this block was built over.</summary>
	bool Covers(ReadOnlySpan<char> text);
}

/// <summary>Draws one kind of <see cref="IBlockMarkup"/> in one format, given the whole block at once.</summary>
public interface IBlockEmitter
{
	/// <summary>The <see cref="IBlockMarkup"/> implementation this draws.</summary>
	Type MarkupType { get; }

	/// <summary>The format this draws in.</summary>
	MarkupFormat Format { get; }

	/// <summary>
	/// Writes <paramref name="markup"/> to <paramref name="output"/>, or returns false having written
	/// nothing, in which case the block's text is rendered as usual.
	/// </summary>
	/// <param name="markup">The block.</param>
	/// <param name="region">The text the block covers, with its own markup.</param>
	/// <param name="registry">The registry the render is running against, for rendering the block's contents.</param>
	/// <param name="output">Where to write.</param>
	bool TryEmit(IBlockMarkup markup, MarkupText region, MarkupRegistry registry, System.Buffers.IBufferWriter<char> output);
}
