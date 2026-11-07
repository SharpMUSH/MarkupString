# Web pages

`Render(MarkupFormat.Html)` writes elements that ask for more than a look: a command link, a menu of
commands, a sound, a bell, a clear, links to expire, a variable, text for another pane. `MarkupString.Web`
is the browser side of that: one stylesheet for every class the HTML writer uses, and a script module that
makes those elements do what they ask. A page renders with the HTML writer, adds the two files, and calls
`attach` once.

```html
<link rel="stylesheet" href="_content/MarkupString.Web/markupstring.css">
<div id="output"></div>
<script type="module">
  import { attach } from './_content/MarkupString.Web/markupstring.js';

  const socket = new WebSocket('wss://mud.example/ws');
  const output = attach(document.getElementById('output'), {
    send: command => socket.send(command),
    soundBase: 'https://mud.example/sounds/',
  });
  socket.onmessage = event => {
    const line = document.createElement('div');
    line.innerHTML = event.data;            // a line the server rendered with MarkupFormat.Html
    document.getElementById('output').append(line);
  };
</script>
```

An ASP.NET Core or Blazor app serves both files from the package as static web assets at those paths
(`MarkupWeb.StylesheetPath`, `MarkupWeb.ScriptPath`). A host that serves pages another way takes the text
from `MarkupWeb.Stylesheet` and `MarkupWeb.Script`.

The stylesheet is `AnsiCss.Fixed`, `LayoutCss.Fixed` and the rules the script's elements need (menus,
expired links, gauges, the bell), in that order. Theme it with the same `--ms-*` variables, and
`--ms-menu-fg`, `--ms-menu-bg`, `--ms-menu-border`, `--ms-menu-active-fg`, `--ms-menu-active-bg` and
`--ms-bell` for what it adds.

## What the script does

`attach(root, options)` watches `root` for added elements and acts on each one once.

| Element | Effect | What happens |
|---|---|---|
| `a[xch_cmd]`, clicked, or Enter or Space | `command` | `send(command)` |
| `.ms-menu`, clicked, or Enter, Space or the menu key | `menu` | A menu of its choices; a choice sends its command. A menu of one choice sends it at once. |
| `.ms-menu.ms-prompt` | `menu`, then `command` with `prompt: true` | `prompt(command)`, which puts it in the input line; `send` when there is no `prompt` |
| `audio.ms-sound` | `sound` | Plays it at its volume, as many times as it says, or looping. Music replaces the music playing, unless it continues the same piece. |
| `.ms-sound-stop` | `sound-stop` | Stops the effects, the music, or both |
| `.ms-bell` | `bell` | Flashes `root` (the `ms-bell-ring` class) |
| `.ms-clear` | `clear` | Removes the children of `root` before the one the clear is in |
| `.ms-expire` | `expire` | Links before it in its `.ms-link-group`, or every command link when it names no group, stop working and take `ms-expired` |
| `.ms-variable` | `variable` | Keeps the value in `runtime.variables`, and brings up to date every `.ms-gauge`, `.ms-status` and `[data-ms-variable]` on the page that shows it |
| `.ms-pane` | `pane` | Moves the text to the pane's element: `panes[name]`, `panes(name, title)`, or `[data-ms-pane-target="name"]` |
| `.ms-relocate`, `.ms-login` | `relocate`, `login` | Nothing but the event and the host's option: only the host knows how to reconnect or what to type |

A gauge sets `--ms-gauge-fill` (0 to 1) and `data-value`, and the stylesheet draws it as a bar under its
text. A maximum may name another variable.

Output that is already in `root` when `attach` runs (a reload's scrollback) is treated as state, not news:
expiry, variables, panes and clears are applied, but sounds, bells, relocation and login prompts are not.
`replay: true` acts on all of them.

### Options

| Option | |
|---|---|
| `send(command)` | Runs a command. Without it, commands do nothing. |
| `prompt(command)` | Puts a command in the input line |
| `sounds` | `false` plays nothing |
| `soundBase` | The address relative sound names are resolved against; the page's own address when unset |
| `replay` | `true` to also act on transient effects already on the page |
| `panes` | An object of pane elements by name, or a function `(name, title) => element` |
| an effect's name | A function `(detail, element)` that replaces that effect's default: `bell`, `clear`, `relocate`, `login`, `sound`, … |

## Taking an effect over

Every effect goes through four steps, and the first that takes it ends it:

1. A cancelable `ms:<effect>` DOM event on the element, which bubbles. `preventDefault()` stops the rest.
2. Plugins, newest first: `runtime.use({ effect: 'bell', handle(detail, element, runtime) { …; return true; } })`.
3. The host's option of the same name.
4. The default above.

```js
document.addEventListener('ms:sound', event => {
  if (muted) event.preventDefault();
});
```

A markup type of your own needs an emitter that writes an element with a class of its own (see
[Custom markup](custom-markup.md)), and a plugin with a `selector` that acts on it:

```js
output.use({
  selector: '.my-compass',
  handle(element, runtime, initial) {
    drawCompass(element.dataset.exits.split(','));
  },
});
```

`runtime.process(node)` acts on a node at once, without waiting for the observer. `runtime.stopSounds()`,
`runtime.closeMenu()` and `runtime.detach()` do what they say.

## Blazor

`attachDotNet(root, dotNetObject, { forward })` sends commands to the object's `[JSInvokable] Send(string)`
and `Prompt(string)`, and each effect named in `forward` to `OnEffect(string name, JsonElement detail)`
instead of its default.

```csharp
public sealed class OutputPane(IJSRuntime js, GameConnection game) : IAsyncDisposable
{
    private IJSObjectReference? _runtime;

    public async Task AttachAsync(ElementReference output)
    {
        var module = await js.InvokeAsync<IJSObjectReference>("import", "./" + MarkupWeb.ScriptPath);
        _runtime = await module.InvokeAsync<IJSObjectReference>("attachDotNet", output, DotNetObjectReference.Create(this),
            new { forward = new[] { "clear", "relocate" } });
    }

    [JSInvokable] public Task Send(string command) => game.SendAsync(command);
    [JSInvokable] public Task Prompt(string command) => game.SetInputAsync(command);
    [JSInvokable] public Task OnEffect(string name, JsonElement detail) => name switch
    {
        "clear" => game.ClearScrollbackAsync(),   // the lines are the component's, not the script's
        "relocate" => game.RelocateAsync(detail.GetProperty("host").GetString()!, detail.GetProperty("port").GetInt32()),
        _ => Task.CompletedTask,
    };

    public async ValueTask DisposeAsync()
    {
        if (_runtime is not null) await _runtime.InvokeVoidAsync("detach");
    }
}
```

Forward `clear` when the lines belong to a component: the default removes elements from the DOM, which
the component did not do.

A client that does not show HTML reads the same things as [output effects](display.md#effects).
