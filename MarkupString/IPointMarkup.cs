namespace MarkupString;

/// <summary>
/// A markup that stands at a point in the text rather than marking any of it: a sound, a bell, an
/// instruction to clear the screen.
/// </summary>
/// <remarks>
/// <para>A point rides on a carrier character — <see cref="MarkupText.PointCarrier"/>, or the bell's own
/// U+0007 — so that it has a position which survives slicing and concatenation. The carrier is not
/// text: <see cref="MarkupText.ToPlainText"/> leaves it out, it measures no display cells, and the
/// renderer never writes it.</para>
/// <para>A format with an emitter for the point writes what the emitter writes, once per carrier. A format
/// with none writes nothing at all, so the same text can go to every client and each one gets only what
/// it can use.</para>
/// </remarks>
public interface IPointMarkup : IMarkup
{
}
