# Getting started

## Install

```sh
dotnet add package MarkupString
dotnet add package MarkupString.Ansi
dotnet add package MarkupString.Html
```

`MarkupString` alone gives you the text type and every operation on it, but it renders nothing:
emitters live in the kind packages. Take `MarkupString.Ansi` for terminal styling (which also
covers HTML, Pueblo, MXP and BBCode output of that styling), and `MarkupString.Html` if you also
need raw tags such as an MXP `<send>`.

Requires .NET 10 or later.

## Wire up the registry, once

Rendering and serialization both go through a `MarkupRegistry` — the table that says which emitter
writes which markup in which format. Build one at startup and install it as the default:

```csharp
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();
```

`Default` is **set-once**: assigning a second, different registry throws `InvalidOperationException`,
and so does reading it before anything has been assigned — the message names the call you are
missing. Assigning the same instance again is a no-op, so a `if (!MarkupRegistry.IsConfigured)`
guard in a library's startup path is safe.

If you would rather not have process-wide state — a test suite, a host that renders for two
different consumers — skip `Default` entirely and pass a registry per call:

```csharp
var registry = MarkupRegistry.Empty.WithAnsi();
text.Render(MarkupFormat.Ansi, registry);
MarkupTextSerializer.Serialize(text, registry);
```

## Build some text

```csharp
// Unmarked text.
var hello = MarkupText.Plain("Hello, ");

// A layer over a whole span. AnsiCodeParser takes MUSH ansi() codes:
// "h" highlight, "r" red, "/R" red background, "+xterm200", "#ff5555", "<255 0 0>".
var world = MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "world");

// Or build the style directly.
var alert = MarkupText.Wrap(
  AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 85, 85), bold: true),
  "alert");

// Layers nest: the inner keeps its own styling, the outer wraps around it.
var link = MarkupText.Wrap(HtmlMarkup.Create("send", "href=\"north\""), world);

var line = MarkupText.Concat([hello, world, MarkupText.Plain("!")]);
var list = MarkupText.Join(MarkupText.Plain(", "), [hello, world]);
```

`MarkupText` is immutable; every operation returns a new value. Two values are equal when their
**plain text** is equal — markup is ignored — because searching and comparing text is the common
case. Use `Equals(other, format)` when you need "renders identically in this format", and
`TextEquals(string)` to compare against a bare string.

## Render

```csharp
line.Render(MarkupFormat.Ansi);    // Hello, \e[1;31mworld\e[0m!
line.Render(MarkupFormat.Html);    // Hello, <span style="color: #ff5555">world</span>!
line.Render(MarkupFormat.Plain);   // Hello, world!

line.ToString();                   // Hello, world!  — always plain, never format-specific
line.ToPlainText();                // the same thing, said explicitly
```

When you already have a buffer, render into it instead of allocating a string:

```csharp
var writer = new ArrayBufferWriter<char>();
line.RenderTo(MarkupFormat.Ansi, writer);
```

## Read existing markup back in

`AnsiEscapeParser.Parse` turns a string that already contains SGR escape sequences into a
`MarkupText` with the styling lifted into runs:

```csharp
var parsed = AnsiEscapeParser.Parse("\e[1;31mdanger\e[0m");
parsed.Text;                       // "danger"
parsed.Render(MarkupFormat.Html);  // <span style="color: #aa0000" class="ms-bold">danger</span>
```

## Store and restore

```csharp
var json = MarkupTextSerializer.Serialize(line);
var back = MarkupTextSerializer.Deserialize(json);
```

The JSON keeps every layer, including layers this process has no codec for — see
[Serialization](serialization.md).

## Where to go next

- [Text operations](text-operations.md) — slicing, padding, alignment and the Unicode rules.
- [Formats and rendering](formats.md) — what each format emits, and declaring your own.
- [Custom markup kinds](custom-markup.md) — adding a kind of your own.
