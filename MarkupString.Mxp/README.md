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

## What each format gets

| | MXP | HTML | everything else |
|---|---|---|---|
| `IMAGE` | the tag | `<img>`, when the element carries a URL | nothing |
| `SOUND`, `MUSIC` | the tag | `<audio autoplay>`, when it carries a URL | nothing |
| `GAUGE`, `STAT` | the tag | a `data-entity` span for the page to draw | nothing |
| `FRAME`, `VAR` | the tag around its content | a span around its content | the content |
| `EXPIRE`, `USER`, `PASSWORD`, `NOBR`, `SBR`, `RELOCATE` | the tag | nothing | nothing |

Every HTML element carries its MXP name in `data-mxp` and an `ms-mxp-*` class, so a page can style them
or take one over. To keep MXP out of the browser entirely, add `new MxpSilentEmitter(MarkupFormat.Html)`
after `WithMxp()`.

## What this package does not decide

Whether a client can render an element is answered by MXP's `<SUPPORT>` exchange, which belongs to the
telnet layer ([TelnetNegotiationCore](https://github.com/HarryCordewener/TelnetNegotiationCore)), and what
to do about the answer is the application's. This package renders what it is given.

## Licence

Apache-2.0. Source, guides and issues: [SharpMUSH/MarkupString](https://github.com/SharpMUSH/MarkupString).
