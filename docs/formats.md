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
| `Pueblo` | HTML-escaped, and a line ending becomes `<BR>` | SGR sequences (Pueblo reads ANSI), plus `<A XCH_CMD>` / `<A HREF>` for a link | the tag itself |
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
out. Text never disappears because a kind had no emitter. The one exception is a point
([below](#points)), which is not text and so leaves nothing.

## A bell

`MarkupText.Bell()` is a point in the text rather than a property of any of it: the client is asked to
get someone's attention where it sits.

```csharp
var line = MarkupText.Concat(MarkupText.Plain("Someone pages you"), MarkupText.Bell());

line.Render(MarkupFormat.Ansi);    // Someone pages you\a
line.Render(MarkupFormat.Html);    // Someone pages you<span class="ms-bell" role="alert"></span>
line.Render(MarkupFormat.Plain);   // Someone pages you
```

It rides on the one U+0007 it marks, which measures zero display cells, so slicing, padding and
concatenation carry it without shifting a column. The text encodings drop control characters, so this
is the only way one reaches rendered output; what the HTML element means — a sound, a flash, a title
change, nothing — is the page's to decide.

## The shared vocabulary

Sounds, pictures, panes and the rest are things a game says once. Their types are core's and say what
a thing is; each format's package says how that format writes it — or what it stands in for it.

```csharp
MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp().WithPueblo();

var line = MarkupText.Concat([
  MarkupText.Sound("door.wav"),
  MarkupText.Image("map.png", "A map of the city"),
  MarkupText.Plain(" The door creaks.")]);

line.Render(MarkupFormat.Mxp);     // <SOUND door.wav><IMAGE map.png> The door creaks.
line.Render(MarkupFormat.Pueblo);  // <img xch_sound="play" href="door.wav"><img src="map.png" alt="A map of the city"> The door creaks.
line.Render(MarkupFormat.Html);    // <audio class="ms-sound" …></audio><img class="ms-image" src="map.png" alt="A map of the city"> The door creaks.
line.Render(MarkupFormat.Ansi);    // A map of the city The door creaks.
```

| Factory | MXP (`WithMxp`) | Pueblo (`WithPueblo`) | HTML (`WithHtml`) | ANSI / BBCode (`WithAnsi`) and Plain |
|---|---|---|---|---|
| `Sound`, `Music` | `<SOUND>`, `<MUSIC>` | `<img xch_sound="play">`, or `"loop"` | `<audio preload="none">` | nothing |
| `StopSound` | `<SOUND Off>` / `<MUSIC Off>` | `<img xch_sound="stop" xch_device>` | `ms-sound-stop` | nothing |
| `Image` | `<IMAGE>` | `<img>` | `<img>` | the description, or the address; BBCode `[img]` |
| `Pane` | `<FRAME name><DEST name>…</DEST>` | `<xch_pane action="redirect">…` and back to `_previous` | `ms-pane` around the text | the text |
| `ClearScreen` | nothing | `<xch_page clear="text">` | `ms-clear` | ANSI `ESC[H ESC[2J` |
| `Prefetch` | nothing | `<xch_prefetch href xch_prob>` | `<link rel="prefetch">` | nothing |
| `ExpireLinks` | `<EXPIRE>` | nothing | `ms-expire` | nothing |
| `Variable`, `Gauge`, `Status` | `<VAR>`, `<GAUGE>`, `<STAT>` | the text | `ms-variable`, `ms-gauge`, `ms-status` around the text | the text |
| `Relocate`, `LoginPrompt` | `<RELOCATE>`, `<USER>` / `<PASSWORD>` | nothing | nothing | nothing |
| `Bell` | U+0007 | U+0007 | `ms-bell` | ANSI U+0007 |

An `ms-` element is a hook: the page decides what a clear, a pane or a gauge looks like. Addresses are
written as given — whether a page fetches or plays one is its own policy, not this library's.

A link is already part of this vocabulary: `AnsiMarkup` with a `LinkKind` is written as each dialect
spells a link, so a picture inside a link is a clickable picture everywhere.

### Points

A sound, a bell or a clear stands at a point rather than marking text. It implements `IPointMarkup` and
rides on a carrier — `MarkupText.PointCarrier`, a zero-width space, or the bell's own U+0007 — so it
keeps its position through slicing, concatenation and padding and measures nothing. The carrier is not
text: `ToPlainText()`, `ToString()` and equality leave it out, and the renderer never writes it. A
format with an emitter for the point writes the point; a format with none writes nothing, not even the
styling around it. `MarkupText.Point(markup)` makes a point of your own.

A point marks its carrier and nothing else. `MarkupText.Wrap` refuses a point over anything but its own
carrier, so a point can never stand over words that a format would then swallow, and a cover read back
with one out of place drops the point rather than the text. Styling *around* a point is fine: the
styling is written only if the point is.

### What an MXP client said it supports

MXP asks a client which elements it can render with `<SUPPORT>`, and a client that answered `-image`
should not be sent one. `WithMxp(supports)` takes the answer as a predicate over the element names in
`MxpRegistration.Elements`; an element it refuses is written as if the format had no MXP. Asking is the
telnet layer's job, and the answer belongs to one connection, so build a registry per answer.

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
- Build a sound, a picture or a pane from [the shared vocabulary](#the-shared-vocabulary), never from
  one dialect's tag.
- Keep `HtmlMarkup` for tags the formats spell alike — `b`, `i`, `pre`, `font`, `a href`. It is
  written unchanged in `Html`, `Pueblo` and `Mxp`.
- Render MXP for a live connection through a registry with `WithMxpSecureLines()`, so every line
  opens in secure mode. See [Line framers](#line-framers).

### Line endings

A Pueblo client renders the stream as HTML, where a newline is whitespace: without help, every line
runs into the one after it. So `MarkupFormat.Pueblo` encodes a line ending as `<BR>` and a newline —
what PennMUSH's `queue_eol` writes in HTML mode — and the `\r` of a `\r\n` goes with it, the break
being the tag now.

```csharp
MarkupText.Plain("north\nsouth\n").Render(MarkupFormat.Pueblo);   // north<BR>\nsouth<BR>\n
```

A blank line is a break like any other; text that does not end in a newline gets none, so rendering
pieces separately and joining them adds nothing.

The other two formats that encode as HTML keep their newlines, and the difference is deliberate. A
browser page decides its own line handling in its stylesheet, and whether a break is a `<br>` or a
paragraph belongs to the document rather than to this library. An MXP client is a line-oriented MUD
client that reads a newline as a break already.

This is the line discipline of the whole stream. A region that wants MUD-text conventions inside an
HTML-mode connection — Pueblo's `<xch_mudtext>` — is not expressible yet: it needs a markup that can
override the encoding for the text it covers, which is
[issue #21](https://github.com/SharpMUSH/MarkupString/issues/21).

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
