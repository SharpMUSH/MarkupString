# Serialization

`MarkupTextSerializer` reads and writes a compact JSON shape designed for storage and for the wire
— a game database, a message bus, a cache — where the same text is written once and read many
times, by processes that may not all have the same packages installed.

```csharp
var json = MarkupTextSerializer.Serialize(text);              // MarkupRegistry.Default
var json = MarkupTextSerializer.Serialize(text, registry);
MarkupTextSerializer.Serialize(text, utf8BufferWriter);       // straight to UTF-8, no string

var back = MarkupTextSerializer.Deserialize(json);
var back = MarkupTextSerializer.Deserialize(utf8Bytes, registry);
```

## The shape

```json
{"t":"Hello, world!","p":[null,[{"k":"ansi","f":9}]],"r":[7,0,5,1,1,0]}
```

| Key | |
|---|---|
| `t` | the plain text; omitted when empty |
| `p` | the palette of **distinct** `MarkupSet`s. Index 0 is always `null`, meaning "no markup"; every other entry is an array of markup objects, innermost first |
| `r` | a flat `[length, paletteIndex, …]` cover of `t`. Starts are the running sum, so no start, end or total length is stored; unmarked stretches take slot 0 |

Both `p` and `r` are omitted when nothing carries markup, so plain text — the overwhelmingly
common case in a game database — costs its characters plus eight bytes:

```json
{"t":"bare"}
```

The palette holds one entry per *distinct* set, not per run, so a document that repeats the same
three styles a thousand times stores three of them. Non-ASCII text is written as literal UTF-8
rather than `\uXXXX` escapes, which matters for the games written in CJK and Cyrillic.

## Forward compatibility

Each markup object carries a `"k"` kind discriminator, written by the serializer; a codec writes
and reads only its own properties.

A kind that **no codec in the reading registry claims** does not fail and is not dropped. It
becomes an `UnknownMarkup` holding the raw JSON object, renders as nothing (its body still comes
out), and is written back **verbatim** on the next save:

```csharp
var withSpoilers = MarkupRegistry.Empty.WithAnsi().WithHtml().WithSpoilers();
var withoutThem  = MarkupRegistry.Empty.WithAnsi().WithHtml();

var json = MarkupTextSerializer.Serialize(text, withSpoilers);
// {"t":"he was dead all along","p":[null,[{"k":"spoiler","why":"ending"}]],"r":[21,1]}

var read = MarkupTextSerializer.Deserialize(json, withoutThem);
read.Render(MarkupFormat.Html, withoutThem);          // he was dead all along
MarkupTextSerializer.Serialize(read, withoutThem);    // byte-identical to the original
```

That is the property that makes a rolling deployment safe: an old reader that meets markup from a
newer writer neither loses it nor corrupts it.

Writing is stricter than reading. A markup with **no codec at write time** throws
`InvalidOperationException` naming the type, because silently dropping markup you asked to store is
a different class of problem from not styling it:

```
No markup codec is registered for SpoilerMarkup. Add one with MarkupRegistry.With(IMarkupCodec).
```

## Validation

Both `Deserialize` overloads reject trailing content after the document. A run cover that overflows
the text is clipped rather than rejected, so a truncated or slightly-wrong `r` degrades to less
markup instead of an exception.

## Writing a codec

See [Custom markup kinds](custom-markup.md#4-the-codec). Two rules:

- **Pick a `Kind` unlikely to clash.** It is the discriminator; two codecs claiming the same string
  means one of them loses at read time.
- **Never change what a `Kind` means.** Add properties, tolerate their absence when reading, and
  take a new `Kind` if the shape has to change incompatibly — old rows will outlive the change.
