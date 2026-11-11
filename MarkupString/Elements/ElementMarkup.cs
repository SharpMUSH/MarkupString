namespace MarkupString;

/// <summary>
/// Which element the text is in, numbered by the decoder that read it. Two elements that are otherwise equal, such as
/// two links to one place or two pictures of one file side by side, differ by it, so they stay two runs (and two links,
/// two pictures) rather than becoming one.
/// </summary>
/// <remarks>
/// A decoder adds one to the elements whose every occurrence is its own thing: links, menus, pictures, variables and
/// flagged elements. Elements that only style the text (bold, a colour) need none, since two of them side by side look
/// as one would. No format writes anything for it (<see cref="IDataMarkup"/>).
/// </remarks>
/// <param name="Number">The decoder's number for the element, from 1.</param>
public sealed record ElementMarkup(int Number) : IDataMarkup;
