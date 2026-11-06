namespace MarkupString.Ansi;

/// <summary>Whether a link target may be rendered as something a client will navigate to.</summary>
public static class UrlSafety
{
	/// <summary>
	/// True when <paramref name="url"/> is safe to render as a navigable hyperlink. Relative URLs
	/// (no scheme) and a small allow-list of schemes pass; dangerous schemes such as
	/// <c>javascript:</c>, <c>data:</c>, <c>vbscript:</c> and <c>file:</c> are rejected, so a link
	/// carrying one renders as plain text instead. This applies to URL links only — a command link
	/// carries a MUSH command, never a browser-navigable href.
	/// </summary>
	public static bool IsSafeNavigableUrl(string? url) => url is not null && UrlPolicy.Safe.IsAllowed(url);
}
