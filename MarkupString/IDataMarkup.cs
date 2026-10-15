namespace MarkupString;

/// <summary>
/// A markup that says what the text it covers is, rather than how it looks: that a character is a list
/// separator, say, and not part of an item.
/// </summary>
/// <remarks>
/// <para>No format writes anything for a data layer. The renderer leaves it out before any emitter sees the
/// run, so the text it covers is written as the layers around it would write it, and an emitter registered
/// for a data type is never called.</para>
/// <para>Data stays where visual markup goes: <see cref="MarkupText.WithoutVisualMarkup"/> takes every other
/// layer off and keeps these. Like every layer, it is ignored by <see cref="MarkupText.ToPlainText"/> and
/// equality, carried through slicing, concatenation and joining, and stored through its codec
/// (<see cref="IMarkupCodec"/>); a reader without the codec keeps it as an <see cref="UnknownMarkup"/>.</para>
/// </remarks>
public interface IDataMarkup : IMarkup;
