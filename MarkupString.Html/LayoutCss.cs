namespace MarkupString.Html;

/// <summary>
/// The stylesheet for the classes the layout emitter writes: boxes, rules, flex rows and figures.
/// A page that shows rendered layouts includes it, or carries its own copy of these rules.
/// </summary>
/// <remarks>
/// Borders and rules take <c>currentColor</c>, so they follow the colour of the text around them.
/// Everything is sized in <c>ch</c>, the width of a cell in the terminal font the layout was laid out
/// in, and every row wraps, so nothing scrolls sideways on a narrow page.
/// </remarks>
public static class LayoutCss
{
	/// <summary>The rules, one per line.</summary>
	public static readonly string Fixed =
		".ms-layout { display: block; max-width: 100%; white-space: normal; }\n" +
		".ms-text { white-space: pre-wrap; overflow-wrap: anywhere; }\n" +
		".ms-box { margin: 0; padding: 0 1ch; min-width: 0; border: 1px solid currentColor; }\n" +
		".ms-box.ms-border-none { border: none; padding: 0; }\n" +
		".ms-box.ms-border-double { border: 3px double currentColor; }\n" +
		".ms-box.ms-border-heavy { border-width: 2px; }\n" +
		".ms-box.ms-border-rounded { border-radius: 0.5em; }\n" +
		".ms-box-title { padding: 0 1ch; margin-inline: auto; }\n" +
		".ms-box-title[data-align=\"left\"] { margin-inline: 0 auto; }\n" +
		".ms-box-title[data-align=\"right\"] { margin-inline: auto 0; }\n" +
		".ms-rule, .ms-divider { display: flex; align-items: center; gap: 1ch; }\n" +
		".ms-rule::before, .ms-rule::after, .ms-divider::before, .ms-divider::after { content: \"\"; flex: 1 1 0; border-top: 1px solid currentColor; }\n" +
		".ms-rule[data-align=\"left\"]::before, .ms-divider[data-align=\"left\"]::before { flex: 0 0 1ch; }\n" +
		".ms-rule[data-align=\"right\"]::after, .ms-divider[data-align=\"right\"]::after { flex: 0 0 1ch; }\n" +
		".ms-divider { margin: 0 -1ch; }\n" +
		".ms-border-double.ms-rule::before, .ms-border-double.ms-rule::after { border-top: 3px double currentColor; }\n" +
		".ms-border-none.ms-rule::before, .ms-border-none.ms-rule::after { border-top: none; }\n" +
		".ms-flex { display: flex; flex-wrap: wrap; }\n" +
		".ms-flex.ms-vertical { flex-direction: column; }\n" +
		".ms-item { min-width: 0; }\n" +
		".ms-flex.ms-divided > .ms-item + .ms-item { border-left: 1px solid currentColor; padding-left: 1ch; }\n" +
		".ms-figure { display: flow-root; }\n" +
		".ms-figure-image { max-width: 100%; height: auto; }\n" +
		".ms-figure-art { margin: 0; font: inherit; white-space: pre; }\n" +
		".ms-float-left > .ms-figure-image, .ms-float-left > .ms-figure-art { float: left; max-width: 50%; margin: 0 2ch 0.5em 0; }\n" +
		".ms-float-right > .ms-figure-image, .ms-float-right > .ms-figure-art { float: right; max-width: 50%; margin: 0 0 0.5em 2ch; }\n";
}
