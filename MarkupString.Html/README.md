# MarkupString.Html

Raw HTML tag markup for [`MarkupString`](https://www.nuget.org/packages/MarkupString): an anchor, a
`<pre>`, a `<div class="...">` — carried as a layer over a span of text and written as itself in the
`Html`, `Pueblo` and `Mxp` formats.

A tag is written the same in all three, so use it for tags the three spell alike. A command link is
not one of them — Pueblo writes `<a xch_cmd>`, MXP `<send href>` — so build that with
`MarkupString.Ansi`'s `AnsiMarkup.Create(linkUrl: ..., linkKind: LinkKind.Command)`, which each
format writes in its own dialect.

Where a tag has no meaning — a terminal — it does not vanish silently: `b`, `strong`, `i`, `em`,
`u`, `s`, `strike` and `del` fold into the run's terminal styling through
[`MarkupString.Ansi`](https://www.nuget.org/packages/MarkupString.Ansi), and any other tag leaves
its body untouched.

`WithHtml()` also writes core's shared vocabulary for a browser: a sound as `<audio>`, a picture as
`<img>`, and what HTML has no element for — a pane, a gauge, clearing the screen — as an element with
an `ms-` class for the page to act on. See
[the shared vocabulary](https://github.com/SharpMUSH/MarkupString/blob/main/docs/formats.md#the-shared-vocabulary).

```sh
dotnet add package MarkupString.Html
```

## Usage

```csharp
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();

var text = MarkupText.Wrap(
  HtmlMarkup.Tag("a", new HtmlAttribute("href", "https://example.org/")),
  MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "north"));

text.Render(MarkupFormat.Html);   // <a href="https://example.org/"><span style="color: #ff5555">north</span></a>
text.Render(MarkupFormat.Ansi);   // \e[1;31mnorth\e[0m
text.Render(MarkupFormat.Plain);  // north

var json = MarkupTextSerializer.Serialize(text);
var back = MarkupTextSerializer.Deserialize(json);
```

`WithAnsi()` must be applied as well: the terminal fold for `b`/`i`/`u`/`s` comes from that
package.

## Untrusted tags

`HtmlMarkup.Create(name, attributes)` writes both exactly as given. For anything you did not write
yourself:

- `HtmlMarkup.Tag(name, params attributes)` checks the name and encodes every value.
- `HtmlTagPolicy.TryCreate(name, rawAttributes, out markup)` reads a raw attribute string and keeps
  only what the policy allows, re-encoded.
- `WithHtml(policy)` holds every tag rendered in the `Html` format to that policy as it is written, so
  markup that arrived deserialised or built with `Create` is held to it too.

**The library ships no security posture.** `HtmlTagPolicy.WellFormed` allows any well-formed tag; what
is safe in your application is your decision, and a policy is a record you narrow with `with`:

```csharp
var policy = HtmlTagPolicy.WellFormed with
{
  AllowedTags = new HashSet<string> { "a", "b", "i", "u", "font", "pre", "span", "img" },
  AllowedAttributes = new HashSet<string> { "href", "src", "alt", "title", "color", "face", "size" },
  // Checked as addresses. HtmlTagPolicy.AddressAttributes is the list HTML gives an address to;
  // nothing applies it for you.
  UrlAttributes = HtmlTagPolicy.AddressAttributes,
  OnViolation = HtmlAttributeViolation.DropAllAttributes,
};

MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml(policy);
```

## Styling

The HTML emitters write `ms-*` classes for the text attributes that have a fixed rendering, and
inline `style` for colours, which are open-ended. Every one of those classes comes from
[`MarkupString.Ansi`](https://www.nuget.org/packages/MarkupString.Ansi), so the stylesheet does
too: include `AnsiCss.Fixed` once per page, or copy its rules into your own sheet.

## Extension points

- `IMarkup` + `IMarkupEmitter` — register an emitter for `HtmlMarkup` in a format of your own, or
  replace this package's by adding yours to the registry afterwards.
- `IMarkupCodec` — `HtmlMarkupCodec` is the wire shape under kind `"html"`.
- `IAnsiStyleSource` — `HtmlMarkup` implements it; that is how a tag becomes terminal styling.

## AOT and trimming

`IsAotCompatible`; no reflection, no dynamic code. Registration is an explicit `WithHtml()` call.

## Licence

Apache-2.0. Source, guides and issues: [SharpMUSH/MarkupString](https://github.com/SharpMUSH/MarkupString).
