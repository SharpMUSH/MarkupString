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

static MarkupText P(string text) => MarkupText.Plain(text);

static void WritePage(string path, string title, string subtitle, MarkupText value, MarkupRegistry registry)
{
	var ansi = value.Render(MarkupFormat.Ansi, registry);
	var ansiForBrowser = AnsiEscapeParser.Parse(ansi).Render(MarkupFormat.Html, registry);
	var html = value.Render(MarkupFormat.Html, registry);
	var escapedAnsi = WebUtility.HtmlEncode(ansi.Replace("\u001b", "\\e", StringComparison.Ordinal));

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
.source { display: none; }
</style>
</head>
<body>
<main>
  <header>
    <div><h1>{{WebUtility.HtmlEncode(title)}}</h1><p class="subtitle">{{WebUtility.HtmlEncode(subtitle)}}</p></div>
    <div class="flow">MarkupText <span>→</span> renderer <span>→</span> native output</div>
  </header>
  <section class="comparison">
    <article class="panel">
      <div class="panel-head"><i class="dot"></i><span class="label">ANSI terminal</span><span class="native">SGR + cells</span></div>
      <pre class="terminal">{{ansiForBrowser}}</pre>
    </article>
    <article class="panel">
      <div class="panel-head"><i class="dot"></i><span class="label">HTML browser</span><span class="native">semantic elements</span></div>
      <div class="browser">{{html}}</div>
    </article>
  </section>
  <pre class="source">{{escapedAnsi}}</pre>
</main>
</body>
</html>
""");
}
