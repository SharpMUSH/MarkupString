using System.Net;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

var registry = MarkupRegistry.Empty
		.WithAnsi()
		.WithHtml()
		.WithLayoutImages(_ => true);

var output = Path.Combine("docs", "showcase", "output");
Directory.CreateDirectory(output);

var box = new Stack(
[
		new Fields(
		[
				new Field(P("Value"), P("Immutable styled text")),
				new Field(P("Width"), P("Display cells")),
				new Field(P("Targets"), P("ANSI · HTML · MXP")),
				new Field(P("Unicode"), P("Grapheme-safe")),
		]) { Columns = 2, Gap = 4, Leader = P("·") },
		new Rule(P("OUTPUT HEALTH")),
		new Gauge(92, 100) { Label = P("Layout"), Show = GaugeShow.Percent },
		new Rule(P("ONE VALUE · MANY FORMATS")),
		new Flex(
		[
				P("Terminal\nbox drawing").ToBlock().Sized(BlockSize.Percent(50), min: 16),
				P("Browser\nsemantic HTML").ToBlock().Sized(BlockSize.Percent(50), min: 16),
		]) { Separator = P(" │ ") },
])
		.Bordered(P("MarkupString · BlockLayout"), BorderStyle.Double)
		.Themed(new LayoutTheme
		{
			Border = BorderStyle.Double,
			BorderColor = AnsiCodeParser.Parse("#8b5cf6"),
			TitleColor = AnsiCodeParser.Parse("h#22d3ee"),
			LabelColor = AnsiCodeParser.Parse("#fb7185"),
			SeparatorColor = AnsiCodeParser.Parse("#64748b"),
			GaugeFilledColor = AnsiCodeParser.Parse("#22d3ee"),
			GaugeEmptyColor = AnsiCodeParser.Parse("#334155"),
		});

var boxValue = BlockLayout.Build(box, 62);
WritePage(
		Path.Combine(output, "box-drawing.html"),
		"Box drawing adapts to its renderer",
		"A single block tree becomes precise terminal cells or responsive browser structure.",
		boxValue,
		registry);

var figure = new Figure(
		new ImageMarkup("../../assets/markupstring-logo.png", "MarkupString logo", Width: 220),
		MarkupText.Wrap(AnsiCodeParser.Parse("h#22d3ee"),
				"     ╭────────╮\n" +
				"  >──┤   M    ├──≡\n" +
				"     ╰────────╯"))
{
	Float = FigureFloat.Left,
	Gap = 3,
	Beside = MarkupText.Concat(
		[
				MarkupText.Wrap(AnsiCodeParser.Parse("h#8b5cf6"), "One value, chosen at the boundary. "),
				P("A terminal receives compact text art and readable copy. A browser receives the real image, alt text, and content that flows around it.")
		]),
};

var figureValue = BlockLayout.Build(figure, 58);
WritePage(
		Path.Combine(output, "image-drawing.html"),
		"Images degrade into useful terminal art",
		"The Figure keeps one intent while each renderer chooses the native representation.",
		figureValue,
		registry);

var character = new Stack(
[
		new Fields(
		[
				new Field(P("Name"), P("Lyra Vale")),
				new Field(P("Role"), P("Wayfinder")),
				new Field(P("Region"), P("Glasswood")),
				new Field(P("Status"), P("Ready")),
		]) { Columns = 2, Gap = 3, Leader = P("·") },
		new Rule(P("CURRENT QUEST")),
		new Gauge(7, 10) { Label = P("Trail"), Show = GaugeShow.Percent },
		new Bullets([P("Map the moonwell"), P("Return before dawn")]),
]).Bordered(P("WAYFINDER'S JOURNAL"));

var fantasy = BlockLayout.Build(character.Themed(ThemePalette.Preset("fantasy")!.ToLayoutTheme()), 46);
var generatedPalette = ThemePalette.Generate(new RgbColor(34, 211, 238), ThemeHarmony.Triadic, contrast: 0.35);
var generated = BlockLayout.Build(character.Themed(generatedPalette.ToLayoutTheme()), 46);
WriteThemePage(
		Path.Combine(output, "theming.html"),
		"One layout, a whole new world",
		"Presets and generated palettes recolour every semantic part — and can change the shapes too.",
		fantasy,
		generated,
		registry);

static MarkupText P(string text) => MarkupText.Plain(text);

static void WritePage(string path, string title, string subtitle, MarkupText value, MarkupRegistry registry)
{
	var ansi = value.Render(MarkupFormat.Ansi, registry);
	var ansiForBrowser = AnsiEscapeParser.Parse(ansi).Render(MarkupFormat.Html, registry);
	var html = value.Render(MarkupFormat.Html, registry);
	WriteComparisonPage(
			path, title, subtitle, "MarkupText <span>→</span> renderer <span>→</span> native output",
			"ANSI terminal", "SGR + cells", "terminal", ansiForBrowser,
			"HTML browser", "semantic elements", "browser", html);
}

static void WriteThemePage(
	string path,
	string title,
	string subtitle,
	MarkupText preset,
	MarkupText generated,
	MarkupRegistry registry)
{
	var presetHtml = AnsiEscapeParser.Parse(preset.Render(MarkupFormat.Ansi, registry)).Render(MarkupFormat.Html, registry);
	var generatedHtml = AnsiEscapeParser.Parse(generated.Render(MarkupFormat.Ansi, registry)).Render(MarkupFormat.Html, registry);
	WriteComparisonPage(
			path, title, subtitle, "layout <span>→</span> palette <span>→</span> themed output",
			"Fantasy preset", "colours + glyphs", "terminal", presetHtml,
			"Generated palette", "triadic harmony", "terminal", generatedHtml);
}

static void WriteComparisonPage(
	string path,
	string title,
	string subtitle,
	string flowHtml,
	string leftLabel,
	string leftNative,
	string leftClass,
	string leftContent,
	string rightLabel,
	string rightNative,
	string rightClass,
	string rightContent)
{
	static string PanelBody(string cssClass, string content) => cssClass == "terminal"
			? "<pre class=\"terminal\">" + content + "</pre>"
			: "<div class=\"browser\">" + content + "</div>";

	File.WriteAllText(path, $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{WebUtility.HtmlEncode(title)}}</title>
<style>
{{AnsiCss.Fixed}}
{{LayoutCss.Fixed}}
:root { color-scheme: dark; font-family: "DejaVu Sans", sans-serif; }
* { box-sizing: border-box; }
body { margin: 0; width: 1164px; height: 639px; overflow: hidden; color: #e2e8f0; background: radial-gradient(circle at 50% -20%, #312e81 0, #111827 42%, #070b14 100%); }
main { height: 100%; padding: 36px 30px 30px; }
header { display: flex; align-items: end; justify-content: space-between; gap: 28px; margin-bottom: 24px; }
h1 { margin: 0 0 7px; color: #f8fafc; font-size: 30px; letter-spacing: -0.035em; }
.subtitle { margin: 0; color: #94a3b8; font-size: 16px; }
.flow { display: flex; align-items: center; gap: 8px; color: #67e8f9; font: 700 13px "DejaVu Sans Mono", monospace; white-space: nowrap; }
.flow span { color: #64748b; }
.comparison { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; height: 478px; }
.panel { min-width: 0; overflow: hidden; border: 1px solid #334155; border-radius: 16px; background: rgba(15, 23, 42, .88); box-shadow: 0 24px 70px rgba(0, 0, 0, .3); }
.panel-head { height: 48px; display: flex; align-items: center; gap: 10px; padding: 0 17px; border-bottom: 1px solid #334155; background: rgba(30, 41, 59, .72); }
.dot { width: 10px; height: 10px; border-radius: 50%; background: #fb7185; box-shadow: 17px 0 #fbbf24, 34px 0 #22d3ee; margin-right: 32px; }
.label { font: 700 12px "DejaVu Sans Mono", monospace; letter-spacing: .08em; color: #cbd5e1; text-transform: uppercase; }
.native { margin-left: auto; padding: 4px 8px; border: 1px solid #475569; border-radius: 99px; color: #94a3b8; font: 600 10px "DejaVu Sans Mono", monospace; }
.terminal { height: calc(100% - 48px); margin: 0; padding: 25px 22px; overflow: hidden; color: #e2e8f0; background: #080d18; font: 13px/1.55 "DejaVu Sans Mono", monospace; white-space: pre; }
.browser { height: calc(100% - 48px); padding: 28px; overflow: hidden; color: #cbd5e1; background: #080d18; font: 15px/1.45 "DejaVu Sans Mono", monospace; }
.browser .ms-layout { margin-inline: auto; }
.browser .ms-box { border-color: #8b5cf6; background: #0f172a; box-shadow: 0 14px 32px rgba(0, 0, 0, .32); }
.browser .ms-box-title, .browser .ms-rule-title { color: #a78bfa; font-weight: 800; }
.browser .ms-gauge meter { accent-color: #0891b2; }
.browser .ms-figure-image { max-width: 220px; margin: 0 20px 14px 0; filter: drop-shadow(0 10px 20px rgba(0, 0, 0, .35)); }
.browser .ms-figure { font-family: "DejaVu Sans", sans-serif; }
.browser .ms-text { line-height: 1.65; }
</style>
</head>
<body>
<main>
  <header>
    <div><h1>{{WebUtility.HtmlEncode(title)}}</h1><p class="subtitle">{{WebUtility.HtmlEncode(subtitle)}}</p></div>
    <div class="flow">{{flowHtml}}</div>
  </header>
  <section class="comparison">
    <article class="panel">
      <div class="panel-head"><i class="dot"></i><span class="label">{{WebUtility.HtmlEncode(leftLabel)}}</span><span class="native">{{WebUtility.HtmlEncode(leftNative)}}</span></div>
      {{PanelBody(leftClass, leftContent)}}
    </article>
    <article class="panel">
      <div class="panel-head"><i class="dot"></i><span class="label">{{WebUtility.HtmlEncode(rightLabel)}}</span><span class="native">{{WebUtility.HtmlEncode(rightNative)}}</span></div>
      {{PanelBody(rightClass, rightContent)}}
    </article>
  </section>
</main>
</body>
</html>
""");
}
