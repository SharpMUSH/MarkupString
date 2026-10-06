namespace MarkupString.Html;

/// <summary>
/// The stylesheet for the classes the layout emitter writes: boxes, rules, flex rows, figures, fields, trees, gauges, lists, grids and tables.
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
		".ms-float-right > .ms-figure-image, .ms-float-right > .ms-figure-art { float: right; max-width: 50%; margin: 0 0 0.5em 2ch; }\n" +
		".ms-fields { display: grid; grid-template-columns: minmax(min-content, max-content) minmax(0, 1fr); column-gap: 1ch; margin: 0; }\n" +
		".ms-field { display: contents; }\n" +
		".ms-field > dt { grid-column: 1; white-space: pre-wrap; overflow-wrap: anywhere; }\n" +
		".ms-field > dd { grid-column: 2; margin: 0; min-width: 0; }\n" +
		".ms-fields.ms-label-right > .ms-field > dt { text-align: right; }\n" +
		".ms-fields.ms-leader > .ms-field > dt { display: flex; gap: 0.5ch; }\n" +
		".ms-fields.ms-leader > .ms-field > dt::after { content: \"\"; flex: 1 0 2ch; border-bottom: 1px dotted currentColor; margin-bottom: 0.3em; }\n" +
		".ms-tree, .ms-tree ul { list-style: none; margin: 0; padding: 0; }\n" +
		".ms-tree ul { margin-left: 1ch; }\n" +
		".ms-tree ul > li { position: relative; padding-left: 3ch; border-left: 1px solid currentColor; }\n" +
		".ms-tree ul > li:last-child { border-left-color: transparent; }\n" +
		".ms-tree ul > li::before { content: \"\"; position: absolute; left: -1px; top: 0; width: 2ch; height: 0.7em; border-left: 1px solid currentColor; border-bottom: 1px solid currentColor; }\n" +
		".ms-tree.ms-guide-rounded ul > li:last-child::before { border-bottom-left-radius: 0.4em; }\n" +
		".ms-tree.ms-guide-heavy ul > li, .ms-tree.ms-guide-heavy ul > li::before { border-width: 2px; }\n" +
		".ms-tree.ms-guide-double ul > li, .ms-tree.ms-guide-double ul > li::before { border-left-style: double; border-left-width: 3px; }\n" +
		".ms-tree.ms-guide-none ul > li, .ms-tree.ms-guide-none ul > li::before { border-color: transparent; }\n" +
		".ms-gauge { display: flex; align-items: center; gap: 1ch; }\n" +
		".ms-gauge > meter { flex: 1 1 8ch; min-width: 4ch; }\n" +
		".ms-gauge-bar { flex: 1 1 8ch; min-width: 4ch; height: 1em; display: flex; border: 1px solid currentColor; box-sizing: border-box; }\n" +
		".ms-gauge-fill { height: 100%; }\n" +
		".ms-bullets { margin: 0; padding-left: 3ch; }\n" +
		".ms-bullet-dash { list-style-type: \"- \"; }\n" +
		".ms-bullet-star { list-style-type: \"* \"; }\n" +
		".ms-bullet-none, .ms-bullet-custom { list-style: none; }\n" +
		".ms-bullet-custom > li > .ms-marker { display: inline-block; margin-left: -2ch; min-width: 2ch; }\n" +
		".ms-grid { list-style: none; margin: 0; padding: 0; display: grid; }\n" +
		".ms-grid.ms-down { display: block; }\n" +
		".ms-grid > li { break-inside: avoid; white-space: pre-wrap; overflow-wrap: anywhere; }\n" +
		".ms-pre { margin: 0; font: inherit; white-space: pre; overflow-x: auto; }\n" +
		".ms-shaded { -webkit-background-clip: text; background-clip: text; color: transparent; }\n" +
		".ms-shaded :is(.ms-box, .ms-gauge-bar), .ms-shaded :is(.ms-rule, .ms-divider)::before, .ms-shaded :is(.ms-rule, .ms-divider)::after { border-image: var(--ms-shade) 1; }\n" +
		".ms-table-wrap { max-width: 100%; overflow-x: auto; }\n" +
		".ms-table { border-collapse: collapse; }\n" +
		".ms-table th, .ms-table td { padding: 0 1ch; vertical-align: top; text-align: left; }\n" +
		".ms-table th { border-bottom: 1px solid currentColor; }\n" +
		"@media (max-width: 48em) { .ms-table .ms-p3 { display: none; } }\n" +
		"@media (max-width: 32em) { .ms-table .ms-p2 { display: none; } }\n";
}
