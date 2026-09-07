# Formats and rendering

A `MarkupText` holds no opinion about how it looks. `Render` asks a `MarkupRegistry` how to write
it in one format, and the registry answers with the emitters the kind packages installed.

```csharp
text.Render(MarkupFormat.Ansi);                  // MarkupRegistry.Default
text.Render(MarkupFormat.Html, registry);        // an explicit registry
text.RenderTo(MarkupFormat.Ansi, bufferWriter);  // straight into a buffer, no string
```

`ToString()` is always the plain text. There is no `ToAnsi()` or `ToHtml()`: rendering is a
decision about an audience, and it is made where that audience is known.

## The six built-in formats

| Format | Text encoding | What `MarkupString.Ansi` writes | What `MarkupString.Html` writes |
|---|---|---|---|
| `Plain` | control characters stripped | nothing — the body passes through | nothing |
| `Ansi` | verbatim | SGR sequences, written as diffs between runs | folds `b`/`i`/`u`/`s` into styling |
| `Html` | HTML-escaped | `<span>` with `ms-*` classes and inline colour | the tag itself |
| `Pueblo` | HTML-escaped | SGR sequences (Pueblo reads ANSI), plus `<A XCH_CMD>` / `<A HREF>` for a link | the tag itself |
| `Mxp` | HTML-escaped | SGR sequences, plus `<send>` / `<a href>` for a link | the tag itself |
| `BBCode` | control characters stripped | `[color]`, `[b]`, `[i]`, `[u]`, `[s]` | nothing — the body passes through |

```csharp
var link = MarkupText.Wrap(
  HtmlMarkup.Create("send", "href=\"north\""),
  MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "world"));

link.Render(MarkupFormat.Html);    // <send href="north"><span style="color: #ff5555">world</span></send>
link.Render(MarkupFormat.Ansi);    // \e[1;31mworld\e[0m
link.Render(MarkupFormat.Mxp);     // <send href="north">\e[1;31mworld\e[0m</send>
link.Render(MarkupFormat.BBCode);  // [color=#ff5555]world[/color]
link.Render(MarkupFormat.Plain);   // world
```

A format nothing knows how to write is not an error: the layer is skipped and its body still comes
out. Text never disappears because a kind had no emitter.

## Colour fidelity

`AnsiColor` is a closed union — `Default`, `Standard(index, bright)`, `Xterm(index)`, `Rgb(r,g,b)`
— and stays semantic all the way to the emitter, so each format spends what it can afford:

- ANSI writes `30–37`/`90–97` for standard, `38;5;n` for xterm-256, `38;2;r;g;b` for truecolor.
- HTML writes a hex colour, resolving standard and xterm indices through `AnsiPalette`.
- A downgrade uses the redmean distance: `AnsiColor.NearestXterm`, `NearestXtermIndex` and
  `NearestStandard` are public if you need to do it yourself.

## Diffed ANSI

`SgrWriter.Transition(from, to, output)` writes only what changed between two runs, so nested
styling does not restate what is already in effect, and a run that changes nothing costs nothing.
The trailing reset is written once, at the end, and only if the document actually left the terminal
in a non-default state.

## Text encoding

Each format carries a `TextEncoding` that says what happens to the *body* text, independent of any
markup: `None` (verbatim), `StripControls`, or `Html` (strip controls and escape `&`, `<`, `>`
and `"`). It is why `MarkupFormat.Custom` takes one — a format you declare has to answer the same
question.

## Framers

An `IFormatFramer` writes a prologue before the first run and an epilogue after the last, and knows
whether anything was actually emitted. Use it for a document wrapper — a `<pre>` element, an XML
envelope, a terminal reset.

```csharp
public sealed class PreFramer : IFormatFramer
{
  public MarkupFormat Format => MarkupFormat.Html;
  public void WritePreamble(IBufferWriter<char> output) => output.Write("<pre>");
  public void WriteEpilogue(bool anyRunEmitted, IBufferWriter<char> output) => output.Write("</pre>");
}

var registry = MarkupRegistry.Empty.WithAnsi().WithHtml().With(new PreFramer());
```

One framer per format; the last one registered wins.

## Declaring a format of your own

```csharp
static readonly MarkupFormat Latex = MarkupFormat.Custom("latex", TextEncoding.None);

var registry = MarkupRegistry.Empty
  .WithAnsi()
  .With(new LatexSetEmitter())    // your IMarkupSetEmitter for the "latex" format
  .With(new LatexFramer());

text.Render(Latex, registry);
```

Formats compare by name, case-insensitively, and `Custom` refuses a name that matches one of the
six built-ins — otherwise you could address a built-in's registry slots under a different
`TextEncoding` and get output that is escaped for the wrong medium.

Writing the emitters themselves is [Custom markup kinds](custom-markup.md).
