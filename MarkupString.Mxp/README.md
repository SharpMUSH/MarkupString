# MarkupString.Mxp

MXP's own elements for [`MarkupString`](https://www.nuget.org/packages/MarkupString): a sound, an image, a gauge, status text, a frame, and the rest of what [MXP](https://www.zuggsoft.com/zmud/mxp.htm) defines beyond styling and links.

```sh
dotnet add package MarkupString.Mxp
```

## Usage

```csharp
MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml().WithMxp();

var line = MarkupText.Concat(
  MxpElements.Sound("door.wav", volume: 80, url: "https://example.test/sounds/"),
  MarkupText.Plain("The door creaks open."));

line.Render(MarkupFormat.Mxp);    // <SOUND door.wav V=80 U=https://example.test/sounds/>The door creaks open.
line.Render(MarkupFormat.Ansi);   // The door creaks open.
```

An element that wraps nothing is a point in the text, carried the way a bell is; one that wraps content
(`FRAME`, `VAR`) marks the text it applies to. A format with no MXP writes **nothing at all**, carrier
included, so the same text is safe to send to every client.

It is one of MarkupString's three **tag dialects**, beside
[`MarkupString.Html`](https://www.nuget.org/packages/MarkupString.Html) (plain HTML, for browsers) and
[`MarkupString.Pueblo`](https://www.nuget.org/packages/MarkupString.Pueblo) (Pueblo's `xch_`
vocabulary). Each writes its own tags; none of them reads another's.

## What each format gets

| | MXP | HTML | everything else |
|---|---|---|---|
| `IMAGE` | the tag | `<img>`, when the element carries a URL | nothing |
| `SOUND`, `MUSIC` | the tag | `<audio>`, when it carries a URL | nothing |
| `GAUGE`, `STAT` | the tag | a `data-entity` span for the page to draw | nothing |
| `FRAME`, `VAR` | the tag around its content | a span around its content | the content |
| `EXPIRE`, `USER`, `PASSWORD`, `NOBR`, `SBR`, `RELOCATE` | the tag | nothing | nothing |

Every HTML element carries its MXP name in `data-mxp` and an `ms-mxp-*` class, so a page can style them
or take one over. To keep MXP out of the browser entirely, add `new MxpSilentEmitter(MarkupFormat.Html)`
after `WithMxp()`.

## What the client said it can render

MXP asks with `<SUPPORT>` for a reason, and `WithMxp` takes the answer:

```csharp
// report is what the client replied, from the telnet layer's <SUPPORT> exchange
var wire = MarkupRegistry.Default.WithMxp(element => !report.Refuses(element.Name));
```

An element the predicate refuses is written the way a format without MXP writes it: nothing for one
that stands alone, and **the content alone** for one that wraps — so a `FRAME` a client cannot open does
not take the text inside it somewhere the player never sees. With no predicate every element is written,
which is the right answer for a client that was never asked: never asked is not the same as refused.

## What this package does not decide

Which client you are talking to, and what it answered, belong to the connection — the `<SUPPORT>`
exchange itself lives in the telnet layer
([TelnetNegotiationCore](https://github.com/HarryCordewener/TelnetNegotiationCore)). This package renders
what it is given, and holds it to the answer you hand it.

## Licence

Apache-2.0. Source, guides and issues: [SharpMUSH/MarkupString](https://github.com/SharpMUSH/MarkupString).
