namespace MarkupString;

/// <summary>
/// The text a reader whose client cannot show the text it covers is sent instead: <c>P</c> for a 📌, say,
/// where the built-in stand-ins would send <c>??</c>. <see cref="MarkupText.StandIn"/> builds one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AsciiFold"/> replaces the whole covered text with <see cref="Text"/> when any character in it
/// would be replaced, and leaves it alone when the reader shows all of it, so a Latin-1 reader keeps an
/// <c>é</c> covered by one. The replacement carries the markup of the first character it replaces. It comes
/// before a game's own stand-ins and the built-in ones.
/// </para>
/// <para>
/// No format writes anything for it (<see cref="IDataMarkup"/>): a reader that shows the text gets the
/// text. Two next to each other with the same text are one, as equal layers always are.
/// </para>
/// </remarks>
/// <param name="Text">Printable ASCII, or empty to leave the covered text out.</param>
public sealed record StandInMarkup(string Text) : IDataMarkup;
