namespace MarkupString;

/// <summary>
/// Sends the text it wraps to a named pane — a window or region of its own — rather than to the main
/// output, opening the pane if it is not already open. <see cref="MarkupText.Pane"/> builds one.
/// </summary>
/// <remarks>
/// MXP writes a <c>&lt;FRAME&gt;</c> and a <c>&lt;DEST&gt;</c> around the text, Pueblo redirects it with
/// <c>&lt;xch_pane&gt;</c> and back again, and HTML marks it for the page to place. A client with no
/// panes shows the text where it is, which is why the text travels inside the markup rather than after it.
/// </remarks>
/// <param name="Name">The pane's name; text sent to the same name goes to the same pane.</param>
/// <param name="Title">The title the pane is shown with, or its name when null.</param>
public sealed record PaneMarkup(string Name, string? Title = null) : IMarkup;
