# MarkupString.Web

The browser side of MarkupString's HTML output.

- `markupstring.css`: a rule for every class `MarkupFormat.Html` writes, and for the script's own elements.
- `markupstring.js`: an ES module. `attach(root, { send })` runs command links and menus, plays and stops
  sounds, rings the bell, clears, expires links, keeps variables and gauges, and moves pane text to its pane.
  Each effect is a cancelable `ms:<effect>` DOM event first, so a page can take any of them over.

An ASP.NET Core or Blazor app serves both at `_content/MarkupString.Web/`. Other hosts take the text from
`MarkupWeb.Stylesheet` and `MarkupWeb.Script`.

```html
<link rel="stylesheet" href="_content/MarkupString.Web/markupstring.css">
<script type="module">
  import { attach } from './_content/MarkupString.Web/markupstring.js';
  attach(document.getElementById('output'), { send: command => socket.send(command) });
</script>
```

See [Web pages](https://github.com/SharpMUSH/MarkupString/blob/main/docs/web.md).
