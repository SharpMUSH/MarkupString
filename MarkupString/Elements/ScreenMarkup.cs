namespace MarkupString;

/// <summary>
/// Clears what the player has been shown. <see cref="MarkupText.ClearScreen"/> builds one.
/// </summary>
/// <remarks>
/// A terminal gets <c>ESC[H ESC[2J</c>, Pueblo <c>&lt;xch_page clear="text"&gt;</c>, and an HTML page an
/// empty <c>ms-clear</c> element to act on. MXP has no such instruction and is sent nothing.
/// </remarks>
public sealed class ClearScreenMarkup : IPointMarkup
{
	/// <summary>The one instance; clearing carries no state.</summary>
	public static readonly ClearScreenMarkup Instance = new();

	private ClearScreenMarkup()
	{
	}
}

/// <summary>
/// Makes links already shown stop working: every one, or those in a named group.
/// <see cref="MarkupText.ExpireLinks"/> builds one.
/// </summary>
/// <remarks>
/// MXP writes <c>&lt;EXPIRE&gt;</c>; an HTML page gets an empty <c>ms-expire</c> element to act on.
/// Clients without the idea are sent nothing, and their old links go on working.
/// </remarks>
/// <param name="Group">The group of links to expire; every link when null.</param>
public sealed record ExpireLinksMarkup(string? Group = null) : IPointMarkup;

/// <summary>
/// Asks the client to fetch something now that it will want soon — a picture, a sound — so that it is
/// already there when it is used. <see cref="MarkupText.Prefetch"/> builds one.
/// </summary>
/// <remarks>Pueblo writes <c>&lt;xch_prefetch&gt;</c>, and HTML a <c>&lt;link rel="prefetch"&gt;</c>.</remarks>
/// <param name="Source">The address to fetch.</param>
public sealed record PrefetchMarkup(string Source) : IPointMarkup;

/// <summary>Which login prompt a <see cref="LoginPromptMarkup"/> marks.</summary>
public enum LoginField
{
	/// <summary>The prompt that asks for a name.</summary>
	User,

	/// <summary>The prompt that asks for a password.</summary>
	Password,
}

/// <summary>
/// Marks a login prompt, so a client that stores a player's name and password knows which to send.
/// <see cref="MarkupText.LoginPrompt"/> builds one.
/// </summary>
/// <remarks>MXP writes <c>&lt;USER&gt;</c> or <c>&lt;PASSWORD&gt;</c>; nothing else has the idea.</remarks>
/// <param name="Field">Which prompt this is.</param>
public sealed record LoginPromptMarkup(LoginField Field) : IPointMarkup;

/// <summary>
/// Asks the client to disconnect and connect to another address. <see cref="MarkupText.Relocate"/>
/// builds one.
/// </summary>
/// <remarks>MXP writes <c>&lt;RELOCATE&gt;</c>; nothing else has the idea.</remarks>
/// <param name="Host">The host to connect to.</param>
/// <param name="Port">The port to connect to.</param>
/// <param name="Quiet">Whether the client reconnects without telling the player.</param>
public sealed record RelocateMarkup(string Host, int Port, bool Quiet = false) : IPointMarkup;
