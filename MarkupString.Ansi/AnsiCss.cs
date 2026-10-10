namespace MarkupString.Ansi;

/// <summary>
/// The stylesheet for the <c>ms-*</c> classes this package's HTML-family emitters write for
/// attributes that have a fixed rendering — as opposed to colours, which are open-ended and go
/// inline instead (see <c>AnsiHtmlEmitter</c>). A page that renders <see cref="MarkupFormat.Html"/>
/// includes this once.
/// </summary>
/// <remarks>
/// The sixteen standard colours are written as <c>var(--ms-ansi-0, #000000)</c> to
/// <c>var(--ms-ansi-15, #ffffff)</c>: 0-7 black, red, green, yellow, blue, magenta, cyan and white, 8-15
/// their bright forms. A page sets those properties to give <c>ansi()</c> colours its own scheme, as a
/// terminal's colour scheme does; one that sets none shows the usual colours.
/// </remarks>
public static class AnsiCss
{
	/// <summary>
	/// One rule per <c>ms-*</c> class the emitters can write, plus the keyframes
	/// <c>.ms-blink</c> animates against. Every name here is <c>ms-</c>-prefixed so dropping the
	/// sheet into a page cannot collide with the page's own rules.
	/// </summary>
	/// <remarks>
	/// Deliberately not a <see langword="const"/>: a const is copied into the consumer at compile
	/// time, so a page built against one version would keep serving that version's rules after
	/// upgrading the package — the exact staleness this sheet exists to prevent.
	/// </remarks>
	public static readonly string Fixed =
		".ms-bold { font-weight: bold; }\n" +
		".ms-faint { opacity: 0.6; }\n" +
		".ms-italic { font-style: italic; }\n" +
		".ms-underline { text-decoration: underline; }\n" +
		".ms-strike { text-decoration: line-through; }\n" +
		".ms-overline { text-decoration: overline; }\n" +
		".ms-blink { animation: ms-blink 1s step-start infinite; }\n" +
		".ms-invert { color: var(--ms-bg, #000); background-color: var(--ms-fg, #fff); }\n" +
		".ms-cmd-link { cursor: pointer; text-decoration: underline dotted; }\n" +
		"@keyframes ms-blink { 50% { opacity: 0; } }\n";
}
