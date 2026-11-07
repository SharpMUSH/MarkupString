# Dependency injection

Everything in MarkupString can be built by hand: the registries are immutable values, and options are
records. A host that uses `Microsoft.Extensions.DependencyInjection` (ASP.NET Core, the generic host)
can instead register it all at once with `MarkupString.Extensions.DependencyInjection`.

```csharp
builder.Services.AddMarkupString()
    .BindConfiguration()                                     // the "MarkupString" section
    .AddDecoders(registry => registry.WithMxp().WithPueblo())
    .AddMarkup(registry => registry.WithHtml().WithMxp().WithPueblo());
builder.Services.AddMetrics();                               // optional: decode metrics on the host's meter factory
```

Then, for each connection:

```csharp
public sealed class Connection(MarkupDecoderFactory decoders, IStyleResolver styles, DisplayOptions display)
{
    private readonly Decoder _decoder = decoders.CreateDecoderFor(new ClientIdentity { Columns = 100 });

    public IEnumerable<IReadOnlyList<DisplaySpan>> Receive(string text) =>
        _decoder.Feed(text).Select(line => SpanRenderer.Render(line.Content, styles, display));
}
```

## What is registered

Every service is a singleton, added only when nothing of that type is registered yet, so registering
your own first replaces it.

| Service | What it is |
|---|---|
| `MarkupRegistry` | `MarkupRegistry.Empty.WithAnsi().WithLinks(policy)` and each `AddMarkup` step |
| `DecoderRegistry` | `WithVt()`, `WithPictures(cache)` unless pictures are off, and each `AddDecoders` step |
| `MarkupDecoderFactory`, `IDecoderFactory` | Makes a decoder per connection with the configured limits, link policy, metrics and picture size |
| `IPictureCache` | One `MemoryPictureCache` that every connection shares |
| `DisplayRegistry`, `IStyleResolver` | `DisplayRegistry.Core.WithAnsi()` and each `AddDisplay` step |
| `DisplayOptions` | The configured output, colour level and link policy |
| `IUrlPolicy` | `UrlPolicy.WithSchemes(LinkSchemes)` |
| `EffectDispatcher` | Hands [output effects](display.md#effects) to every `IEffectHandler` added with `AddEffectHandler<T>()` or `AddEffectHandler(handler)`, newest first |

`UsePictureCache<T>()` and `UseUrlPolicy<T>()` swap in your own as singletons. A cache of your own
(on disk, in Redis) must be safe to call from several threads; see [Pictures](decoding.md#pictures).

## Configuration

`AddMarkupString(options => …)` sets `MarkupStringOptions` in code, and `BindConfiguration(path)`
binds it from configuration. The binder is source-generated, so it works in a native AOT app.

```json
{
  "MarkupString": {
    "LinkSchemes": [ "mudclient" ],
    "Decoding": { "MaxLineLength": 65536, "MaxOpenElements": 64, "AnswerCursorPosition": false },
    "Pictures": { "Enabled": true, "Budget": 268435456, "MaxPixels": 16777216 },
    "Display": { "Output": "Browser", "Colors": "Xterm256", "BoldIsBright": false }
  }
}
```

`Display:Output` is `Browser`, `CellGrid` or `PlainText`; `Display:Colors`, when set, lowers its
colour level. The options are read once, when each service is first made.

## The interfaces underneath

These are in the core packages and need no container.

- `IDecoderFactory`: `CreateDecoder(options)`. `DecoderRegistry` implements it.
- `IStyleResolver`: `Resolve(markups, options)`. `DisplayRegistry` implements it, and `SpanRenderer`
  and `CellRenderer` take one.
- `IUrlPolicy`: which link addresses are allowed. `UrlPolicy.Safe` is the default;
  `UrlPolicy.WithSchemes("mudclient")` adds schemes, never `javascript`, `data`, `vbscript` or `file`.
  Set it as `DecoderOptions.Links` (what a server's links become), `DisplayOptions.Links` (what is
  drawn as clickable), `MarkupRegistry.WithLinks` (what every writer, ANSI, HTML, MXP, Pueblo, BBCode
  and Markdown, writes as a link) and `HtmlTagPolicy.Links` (which addresses a checked tag may hold).
  `AddMarkupString` sets the first three to the registered policy.
- `IPictureCache`: where decoded pictures are kept; `WithPictures(cache)` shares one.
- `IColorResolver`: how palette colours become RGB, as `DisplayOptions.Colors`.

## Metrics and logging

Each decoder reports what it drops on the `MarkupString.Decoding` meter, as the counter
`markupstring.decoding.dropped` with a `kind` tag: `abandoned-sequence`, `unhandled-sequence`,
`unhandled-control`, `limit` or `picture`. With `DecoderOptions.Meters` (the factory sets it to the
host's `IMeterFactory` when one is registered) the meter comes from that factory, so
`dotnet-counters` and OpenTelemetry see it with the host's other meters.

`decoder.Diagnostics.Dropped` is raised with a `DecodeDrop` and a count each time one goes up. When the
host registers an `ILoggerFactory`, the factory logs each one at `Debug` under the category
`MarkupString.Decoding` (event 1, `Decoder dropped {Count} × {Kind}`); it subscribes only when that
level is on, so a quiet host pays nothing.
