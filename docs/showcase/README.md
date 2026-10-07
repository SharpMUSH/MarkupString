# README showcase captures

The README screenshots are generated from MarkupString values rather than recreated by hand.
The checked-in captures use this supported Linux x64 environment:

- the .NET SDK selected by the repository's `global.json`;
- Python Playwright 1.59.0 with Chromium v1217 (`Chrome for Testing 147.0.7727.15`);
- ImageMagick 7.1.2-32; and
- DejaVu Sans and DejaVu Sans Mono 2.37.

Install Playwright and its browser with:

```sh
uv tool install playwright==1.59.0
playwright install chromium
```

ImageMagick, Fontconfig, and the two DejaVu fonts come from the operating system. The capture script
checks every prerequisite and stops with a specific error when the supported environment is not
present. After restoring the repository once, run from its root:

```sh
docs/showcase/capture.sh
```

The format pages place the same value's ANSI rendering beside its semantic HTML rendering. The
theme page places the same layout under a preset and a generated palette. ANSI is parsed back only
to make its terminal styling visible in the browser capture; its text and SGR styling come from
`Render(MarkupFormat.Ansi)`. The capture script applies rounded alpha masks and transparent padding
so the dark cards sit cleanly on both GitHub README themes, and omits volatile PNG timestamps so a
repeat capture is byte-for-byte identical.
