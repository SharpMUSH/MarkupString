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
| `Mxp` | HTML-escaped | SGR sequences, plus `<SEND HREF>` / `<A HREF>` for a link | the tag itself |
| `BBCode` | control characters stripped | `[color]`, `[b]`, `[i]`, `[u]`, `[s]` | nothing — the body passes through |

```csharp
var link = MarkupText.Wrap(
  AnsiMarkup.Create(linkUrl: "north", linkKind: LinkKind.Command, linkText: "Go north"),
  MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "north"));

link.Render(MarkupFormat.Html);    // <span style="color: #ff5555"><a class="ms-cmd-link" role="button" tabindex="0" xch_cmd="north" title="Go north">north</a></span>
link.Render(MarkupFormat.Pueblo);  // \e[1;31m<A XCH_CMD="north" XCH_HINT="Go north">north</A>\e[0m
link.Render(MarkupFormat.Mxp);     // \e[1;31m<SEND HREF="north" HINT="Go north">north</SEND>\e[0m
link.Render(MarkupFormat.Ansi);    // \e[1;31mnorth\e[0m
link.Render(MarkupFormat.BBCode);  // [color=#ff5555]north[/color]
link.Render(MarkupFormat.Plain);   // north
```

A format nothing knows how to write is not an error: the layer is skipped and its body still comes
out. Text never disappears because a kind had no emitter.

## Pueblo and MXP

Pueblo and MXP are two different dialects, not one extending the other. Most formatting tags are
spelled alike, but links are not, and each client prints the other's tags as text:

| | Pueblo | MXP | Html (the portal's terminal) |
|---|---|---|---|
| Command link | `<A XCH_CMD="cmd" XCH_HINT="hint">` | `<SEND HREF="cmd" HINT="hint">` | `<a class="ms-cmd-link" xch_cmd="cmd" title="hint">` |
| URL link | `<A HREF="url">` | `<A HREF="url">` | `<a href="url">` |
| Tag read on | any line | a line opened in secure mode (`ESC[1z`) | — |

So:

- Build a link with `AnsiMarkup.Create(linkUrl: ..., linkKind: ...)`, never with an `HtmlMarkup`
  that spells one dialect's tag. The Ansi package writes the link each format's own way.
- Keep `HtmlMarkup` for tags the formats spell alike — `b`, `i`, `pre`, `font`, `a href`. It is
  written unchanged in `Html`, `Pueblo` and `Mxp`.
- Render MXP for a live connection through a registry with `WithMxpSecureLines()`, so every line
  opens in secure mode. See [Line framers](#line-framers).

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
markup: `None` (verbatim), `StripControls`, or `Html` (strip controls and escape `&`, `<` and
`>`). It is why `MarkupFormat.Custom` takes one — a format you declare has to answer the same
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

## MXP's own elements

`MarkupString.Mxp` carries the elements MXP defines beyond styling and links — `SOUND`, `MUSIC`,
`IMAGE`, `GAUGE`, `STAT`, `FRAME`, `VAR`, `EXPIRE`, `RELOCATE`, `USER`, `PASSWORD`, `NOBR`, `SBR` — and
anything else through `MxpElement` itself.

```csharp
MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp();

var line = MarkupText.Concat(
  MxpElements.Sound("door.wav", volume: 80, url: "https://example.test/sounds/"),
  MarkupText.Plain("The door creaks open."));

line.Render(MarkupFormat.Mxp);   // <SOUND door.wav V=80 U=...>The door creaks open.
line.Render(MarkupFormat.Html);  // <audio ... autoplay></audio>The door creaks open.
line.Render(MarkupFormat.Ansi);  // The door creaks open.
```

An element that wraps nothing is a point in the text; one that wraps content (`FRAME`, `VAR`) marks the
text it applies to. **A format with no MXP writes nothing at all**, including the zero-width carrier a
standalone element rides on, so one piece of text is safe to send to every client — a terminal is sent
neither the tag nor the character it rode on.

Whether a given client can render an element is a different question, answered by MXP's `<SUPPORT>`
exchange at the telnet layer; what to do about the answer is the application's.

## Line framers

An `ILineFramer` writes a prefix at the start of every line that has content — a line holding
nothing, or only the `\r` of a `\r\n`, gets none. It has its own slot, so it sits alongside a
document framer for the same format rather than replacing it.

The one shipped is `MxpSecureLineFramer`, which opens each line of `Mxp` output in secure mode
(`ESC[1z`) — without it an MXP client prints the tags, whichever package wrote them. It is opt-in,
because the prefix belongs to output bound for a connection. Negotiating MXP and starting MXP mode
are the telnet layer's job; this only frames the text sent once they are done:

```csharp
var wire = MarkupRegistry.Default.WithMxpSecureLines();   // at the connection boundary
text.Render(MarkupFormat.Mxp, wire);                      // \e[1z<SEND HREF="north">north</SEND>
text.Render(MarkupFormat.Mxp);                            // <SEND HREF="north">north</SEND>
```

## Untrusted tags in HTML

`HtmlMarkup.Create` writes its tag name and attribute string as given. `HtmlTagPolicy` is the machinery
for holding one to a list of allowed tags and attributes, with any address-bearing attribute checked
against `UrlSafety`; `WithHtml(policy)` applies it to every tag rendered in `Html` as it is written, so
a refused tag leaves its body unwrapped and a refused attribute is dropped.

The library ships no posture of its own — `HtmlTagPolicy.WellFormed` allows any well-formed tag, and
what is safe in your application is yours to declare. `Pueblo` and `Mxp` are unaffected unless you
register a policy for them too. See the
[MarkupString.Html README](../MarkupString.Html/README.md#untrusted-tags).

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
