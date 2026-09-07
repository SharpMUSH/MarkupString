# Custom markup kinds

A markup kind is three things: a value type that says *what* the layer is, one or more emitters
that say how it is *written*, and a codec that says how it is *stored*. Nothing is discovered by
reflection — you hand them to a registry, which is why the packages stay AOT- and trim-clean.

`MarkupString.Ansi` and `MarkupString.Html` are ordinary consumers of these same three interfaces.
Yours is a peer of theirs, not a plugin into them.

## 1. The layer

Implement `IMarkup` on an immutable value with value equality — a `record` is the obvious choice.
Equality is what makes identically marked neighbouring spans coalesce into one run, so getting it
right is what keeps the run array small.

```csharp
using MarkupString;

public sealed record SpoilerMarkup(string? Reason) : IMarkup;
```

## 2. The emitters

Two shapes, and the difference matters:

- **`IMarkupEmitter`** is keyed on `(markup type, format)` and writes *one layer*, wrapping the
  body it is handed. Unrelated kinds never collide here. This is what you almost always want.
- **`IMarkupSetEmitter`** is keyed on the format alone and takes over *the whole run* — every layer
  on it, from every package. There is one per format and the last registration wins, so registering
  one for `Html` silently takes that format away from whatever registered it before.

```csharp
using System.Buffers;
using MarkupString;

public sealed class SpoilerHtmlEmitter : IMarkupEmitter
{
  public Type MarkupType => typeof(SpoilerMarkup);
  public MarkupFormat Format => MarkupFormat.Html;

  public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
  {
    var spoiler = (SpoilerMarkup)markup;
    output.Write("<span class=\"spoiler\"");
    if (spoiler.Reason is { } reason)
    {
      output.Write(" title=\"");
      MarkupTextRenderer.EncodeText(reason, context.Format.Encoding, output);
      output.Write("\"");
    }
    output.Write(">");
    output.Write(body);          // already encoded for this format
    output.Write("</span>");
  }
}
```

The body arrives already encoded for the format — do not escape it again. Attribute values you
supply yourself are *not* encoded; run them through `MarkupTextRenderer.EncodeText` as above.

`EmitContext` tells you the format, the registry (so you can delegate to another layer's emitter),
the previous and next run's `MarkupSet`, and whether this is the first or last run — enough to
write state diffs rather than restating style on every run.

Register an emitter per format you care about. A format you skip is not an error: the layer is
passed over and its body still comes out.

## 3. Terminal styling without a terminal emitter

If your layer means "bold" or "this colour", do not write an ANSI emitter for it. Implement
`IAnsiStyleSource` and `MarkupString.Ansi`'s fold picks it up — in ANSI, and in every other format
that package covers — with no per-format work from you:

```csharp
using MarkupString.Ansi;

public sealed record SpoilerMarkup(string? Reason) : IMarkup, IAnsiStyleSource
{
  public bool TryGetAnsiStyle(MarkupFormat format, out AnsiStyle style)
  {
    // Hidden text on a terminal: same foreground and background.
    style = AnsiStyle.None with { Faint = true };
    return format != MarkupFormat.Html;   // HTML has its own emitter above
  }
}
```

Returning `false` for a format means "I have my own emitter there, leave me alone" — this is
exactly how `HtmlMarkup` keeps its real tag in HTML/Pueblo/MXP while still folding `<b>` into bold
on a terminal.

## 4. The codec

Without a codec your layer cannot be serialized. With one, it round-trips — and a reader that does
not have your package keeps it as `UnknownMarkup` and writes it back verbatim.

```csharp
using System.Text.Json;

public sealed class SpoilerCodec : IMarkupCodec
{
  public Type MarkupType => typeof(SpoilerMarkup);
  public string Kind => "spoiler";       // the "k" discriminator; pick something unlikely to clash

  public void Write(Utf8JsonWriter writer, IMarkup markup)
  {
    // The object and its "k" are written for you; write only your own properties.
    if (((SpoilerMarkup)markup).Reason is { } reason) writer.WriteString("why", reason);
  }

  public IMarkup Read(JsonElement element) =>
    new SpoilerMarkup(element.TryGetProperty("why", out var why) ? why.GetString() : null);
}
```

## 5. Register it

```csharp
public static class SpoilerRegistration
{
  public static MarkupRegistry WithSpoilers(this MarkupRegistry registry) =>
    registry
      .With(new SpoilerHtmlEmitter())
      .With(new SpoilerCodec());
}

MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml().WithSpoilers();
```

`MarkupRegistry` is immutable — each `With` returns a new one — and the extension-method
convention is what lets a host compose kinds in one readable line.

## Using it

```csharp
var text = MarkupText.Wrap(new SpoilerMarkup("ending"), "he was dead all along");

text.Render(MarkupFormat.Html);   // <span class="spoiler" title="ending">he was dead all along</span>
text.Render(MarkupFormat.Ansi);   // \e[2mhe was dead all along\e[0m
text.Render(MarkupFormat.Plain);  // he was dead all along
```

## Rules of composition

- **One set emitter per format, last one wins.** If you register an `IMarkupSetEmitter`, you own
  that format for every kind, including kinds you have never heard of. Prefer `IMarkupEmitter`, or
  `IAnsiStyleSource` when what you want is styling.
- **Per-layer emitters never collide** — the key includes your type.
- **Kinds must be unique across the codecs in a registry.** A duplicate `Kind` means one of them
  loses at read time.
- **A missing emitter is silence, not an error.** A missing *codec* at write time throws, because
  silently dropping stored markup is a different class of problem from not styling it.
