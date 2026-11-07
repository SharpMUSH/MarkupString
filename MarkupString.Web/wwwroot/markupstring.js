// MarkupString runtime: makes the HTML that MarkupFormat.Html writes do what it asks for.
//
//   import { attach } from './_content/MarkupString.Web/markupstring.js';
//   const output = attach(document.querySelector('#output'), { send: command => socket.send(command) });
//
// It runs command links and menus, plays and stops sounds, rings the bell, clears, expires links, keeps
// variables and the gauges that show them, and moves pane text to its pane. Every effect is first raised
// on its element as a cancelable `ms:<effect>` DOM event, then offered to plugins, then to the host's
// option of the same name, and only then given the default behaviour, so a host can take over any of them.

const BUILT_IN = {
  sound: '.ms-sound',
  'sound-stop': '.ms-sound-stop',
  bell: '.ms-bell',
  clear: '.ms-clear',
  expire: '.ms-expire',
  relocate: '.ms-relocate',
  login: '.ms-login',
  variable: '.ms-variable',
  pane: '.ms-pane',
};

// Effects that happen once, when the output arrives. The others restore state, so they are applied again
// to output that was already on the page when the runtime attached (a reload's scrollback).
const TRANSIENT = new Set(['sound', 'sound-stop', 'bell', 'relocate', 'login']);

/**
 * Starts the runtime over `root`: the element MarkupString's HTML is added to.
 *
 * Options, all optional:
 * - send(command): run a command link or a menu choice. Without it, commands do nothing.
 * - prompt(command): put a command in the input line (a prompt link). Falls back to send.
 * - sounds: false to play nothing. soundBase: the address relative sound names resolve against.
 * - replay: true to also play sounds and ring bells in output already on the page.
 * - panes: { name: element } or a function (name, title) => element, for pane text.
 * - bell, clear, expire, relocate, login, variable, pane, sound, 'sound-stop', command, menu:
 *   a function (detail, element) that replaces that effect's default behaviour.
 */
export function attach(root, options = {}) {
  return new MarkupRuntime(root, options);
}

/**
 * As `attach`, for a host in .NET (Blazor): commands go to the object's [JSInvokable] `Send(command)` and
 * `Prompt(command)`, and each effect named in `options.forward` goes to `OnEffect(name, detail)` instead of
 * its default behaviour.
 */
export function attachDotNet(root, dotNetObject, options = {}) {
  const forward = new Set(options.forward ?? []);
  const forwarded = {};
  for (const name of forward) {
    forwarded[name] = detail => { dotNetObject.invokeMethodAsync('OnEffect', name, detail); };
  }
  return new MarkupRuntime(root, {
    ...options,
    ...forwarded,
    send: command => dotNetObject.invokeMethodAsync('Send', command),
    prompt: command => dotNetObject.invokeMethodAsync('Prompt', command),
  });
}

export class MarkupRuntime {
  constructor(root, options) {
    if (!root) throw new TypeError('attach: no root element');
    this.root = root;
    this.options = { sounds: true, replay: false, ...options };
    /** The server's variables, by name. */
    this.variables = new Map();
    this._plugins = [];
    this._seen = new WeakSet();
    this._music = null;
    this._effects = new Set();
    this._menu = null;

    this._onClick = event => this._click(event);
    this._onKey = event => this._key(event);
    this._onOutside = event => { if (this._menu && !this._menu.contains(event.target)) this.closeMenu(); };
    root.addEventListener('click', this._onClick);
    root.addEventListener('keydown', this._onKey);
    root.ownerDocument.addEventListener('mousedown', this._onOutside, true);

    this._observer = new MutationObserver(records => {
      for (const record of records) {
        for (const node of record.addedNodes) {
          if (node.nodeType === 1) this.process(node);
        }
      }
    });
    this._observer.observe(root, { childList: true, subtree: true });
    this.process(root, true);
  }

  /**
   * Adds a plugin: `{ selector, handle(element, runtime, initial) }` acts on elements of a markup type of its
   * own; `{ effect, handle(detail, element, runtime) }` takes over a built-in effect when it returns true.
   * A plugin added later is asked first.
   */
  use(plugin) {
    this._plugins.unshift(plugin);
    if (plugin.selector) this.process(this.root, true);
    return this;
  }

  /** Acts on the effects in `node` that have not been acted on yet. The runtime calls it for added nodes. */
  process(node, initial = false) {
    for (const [effect, selector] of Object.entries(BUILT_IN)) {
      for (const element of matching(node, selector)) {
        if (this._seen.has(element)) continue;
        this._seen.add(element);
        if (initial && !this.options.replay && TRANSIENT.has(effect)) continue;
        this._effect(effect, element, read(effect, element));
      }
    }
    for (const plugin of this._plugins) {
      if (!plugin.selector) continue;
      for (const element of matching(node, plugin.selector)) {
        if (this._seen.has(element)) continue;
        this._seen.add(element);
        plugin.handle(element, this, initial);
      }
    }
  }

  /** Stops the sounds on `channel` ('effects' or 'music'), or on both. */
  stopSounds(channel) {
    if (channel !== 'effects' && this._music) {
      this._music.pause();
      this._music = null;
    }
    if (channel !== 'music') {
      for (const audio of this._effects) audio.pause();
      this._effects.clear();
    }
  }

  /** Closes an open command menu. */
  closeMenu() {
    if (!this._menu) return;
    const menu = this._menu;
    this._menu = null;
    menu.remove();
    menu._opener?.focus?.();
  }

  /** Stops the runtime: no more effects, sounds stopped, listeners removed. */
  detach() {
    this._observer.disconnect();
    this.root.removeEventListener('click', this._onClick);
    this.root.removeEventListener('keydown', this._onKey);
    this.root.ownerDocument.removeEventListener('mousedown', this._onOutside, true);
    this.closeMenu();
    this.stopSounds();
  }

  // Raises the effect's event, then asks plugins, then the host's option, then does the default.
  _effect(effect, element, detail) {
    const event = new CustomEvent(`ms:${effect}`, { bubbles: true, cancelable: true, detail });
    element.dispatchEvent(event);
    if (event.defaultPrevented) return;
    for (const plugin of this._plugins) {
      if (plugin.effect === effect && plugin.handle(detail, element, this)) return;
    }
    const own = this.options[effect];
    if (typeof own === 'function') {
      own(detail, element);
      return;
    }
    DEFAULTS[effect]?.call(this, detail, element);
  }

  _click(event) {
    if (event.button !== 0 || event.defaultPrevented) return;
    const target = event.target;
    if (!(target instanceof Element) || target.closest('.ms-menu-popup')) return;
    const menu = target.closest('.ms-menu');
    if (menu && this.root.contains(menu) && !menu.classList.contains('ms-expired')) {
      event.preventDefault();
      this._openMenu(menu, event.clientX, event.clientY);
      return;
    }
    const link = target.closest('a[xch_cmd]');
    if (link && this.root.contains(link)) {
      event.preventDefault();
      this._effect('command', link, { command: link.getAttribute('xch_cmd') });
    }
  }

  _key(event) {
    const target = event.target;
    if (!(target instanceof Element) || target.closest('.ms-menu-popup')) return;
    const activate = event.key === 'Enter' || event.key === ' ';
    const menuKey = event.key === 'ContextMenu' || (event.key === 'F10' && event.shiftKey);
    if (!activate && !menuKey) return;

    // A menu opens from its own text, or with the menu key from a link inside it.
    const menu = target.matches('.ms-menu') ? target : menuKey ? target.closest('.ms-menu') : null;
    if (menu && this.root.contains(menu) && !menu.classList.contains('ms-expired')) {
      event.preventDefault();
      const box = menu.getBoundingClientRect();
      this._openMenu(menu, box.left, box.bottom);
      return;
    }
    if (activate && target.matches('a[xch_cmd]') && this.root.contains(target)) {
      event.preventDefault();
      this._effect('command', target, { command: target.getAttribute('xch_cmd') });
    }
  }

  _openMenu(element, x, y) {
    const detail = {
      choices: parseChoices(element.getAttribute('data-commands')),
      prompt: element.classList.contains('ms-prompt'),
      hint: element.getAttribute('title'),
      x,
      y,
    };
    if (detail.choices.length === 0) return;
    this._effect('menu', element, detail);
  }

  _run(command, prompt, element) {
    this._effect('command', element, { command, prompt });
  }
}

// What each effect does when nothing took it over. `this` is the runtime.
const DEFAULTS = {
  command({ command, prompt }) {
    if (!command) return;
    const run = prompt ? this.options.prompt ?? this.options.send : this.options.send;
    run?.(command);
  },

  menu({ choices, prompt, x, y }, element) {
    if (choices.length === 1) {
      this._run(choices[0].command, prompt, element);
      return;
    }
    this.closeMenu();
    const doc = this.root.ownerDocument;
    const popup = doc.createElement('div');
    popup.className = 'ms-menu-popup';
    popup.setAttribute('role', 'menu');
    popup._opener = element;
    for (const choice of choices) {
      const item = doc.createElement('button');
      item.type = 'button';
      item.setAttribute('role', 'menuitem');
      item.textContent = choice.label || choice.command;
      item.title = choice.command;
      item.addEventListener('click', event => {
        event.preventDefault();
        this.closeMenu();
        this._run(choice.command, prompt, element);
      });
      popup.append(item);
    }
    popup.addEventListener('keydown', event => {
      const items = [...popup.querySelectorAll('button')];
      const at = items.indexOf(doc.activeElement);
      if (event.key === 'Escape') {
        event.preventDefault();
        this.closeMenu();
      } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const step = event.key === 'ArrowDown' ? 1 : -1;
        items[(at + step + items.length) % items.length].focus();
      }
    });
    doc.body.append(popup);
    const box = popup.getBoundingClientRect();
    const view = doc.defaultView;
    popup.style.left = `${Math.max(0, Math.min(x, view.innerWidth - box.width))}px`;
    popup.style.top = `${Math.max(0, Math.min(y, view.innerHeight - box.height))}px`;
    this._menu = popup;
    popup.querySelector('button')?.focus();
  },

  sound(detail) {
    if (!this.options.sounds || !detail.src) return;
    const url = new URL(detail.src, this.options.soundBase ?? this.root.ownerDocument.baseURI).href;
    if (detail.channel === 'music') {
      if (detail.continues && this._music && this._music.src === url && !this._music.paused) return;
      this.stopSounds('music');
    }
    const audio = new Audio(url);
    if (detail.volume != null) audio.volume = Math.min(1, Math.max(0, detail.volume / 100));
    audio.loop = detail.loops;
    let left = detail.loops ? 0 : Math.max(1, detail.repeats ?? 1) - 1;
    audio.addEventListener('ended', () => {
      if (left-- > 0) {
        audio.currentTime = 0;
        audio.play().catch(() => {});
      } else {
        this._effects.delete(audio);
        if (this._music === audio) this._music = null;
      }
    });
    if (detail.channel === 'music') this._music = audio;
    else this._effects.add(audio);
    // A browser refuses to play before the reader has used the page; that sound is lost, not queued.
    audio.play().catch(() => {});
  },

  'sound-stop'({ channel }) {
    this.stopSounds(channel ?? undefined);
  },

  bell() {
    this.root.classList.remove('ms-bell-ring');
    void this.root.offsetWidth;
    this.root.classList.add('ms-bell-ring');
  },

  clear(_, element) {
    // Everything before the line the clear stands in goes.
    let line = element;
    while (line.parentNode && line.parentNode !== this.root) line = line.parentNode;
    if (line.parentNode !== this.root) return;
    while (line.previousSibling) line.previousSibling.remove();
  },

  expire({ group }, element) {
    const scope = group == null
      ? [this.root]
      : [...this.root.querySelectorAll(`.ms-link-group[data-group="${CSS.escape(group)}"]`)];
    for (const container of scope) {
      for (const link of container.querySelectorAll('a[xch_cmd], .ms-menu, a[href]')) {
        // Only links that came before the expiry.
        if (!(link.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING)) continue;
        if (group == null && link.matches('a[href]') && !link.closest('.ms-link-group')) continue;
        link.removeAttribute('xch_cmd');
        link.removeAttribute('href');
        link.removeAttribute('data-commands');
        link.removeAttribute('role');
        link.removeAttribute('tabindex');
        link.setAttribute('aria-disabled', 'true');
        link.classList.add('ms-expired');
      }
    }
  },

  variable({ name, value }) {
    if (value == null) this.variables.delete(name);
    else this.variables.set(name, value);
    const doc = this.root.ownerDocument;
    const quoted = CSS.escape(name);
    const shows = `[data-variable="${quoted}"], [data-maximum="${quoted}"], [data-ms-variable="${quoted}"]`;
    for (const shown of doc.querySelectorAll(shows)) this._show(shown);
  },

  pane({ name, title }, element) {
    const panes = this.options.panes;
    const target = typeof panes === 'function'
      ? panes(name, title)
      : panes?.[name] ?? this.root.ownerDocument.querySelector(`[data-ms-pane-target="${CSS.escape(name)}"]`);
    if (target && target !== element.parentNode) target.append(element);
  },
};

// Brings a gauge, a status or a host's own [data-ms-variable] element up to date with the variables.
MarkupRuntime.prototype._show = function (element) {
  if (element.hasAttribute('data-ms-variable')) {
    element.textContent = this.variables.get(element.getAttribute('data-ms-variable')) ?? '';
    return;
  }
  const value = this.variables.get(element.getAttribute('data-variable'));
  if (value == null) return;
  element.setAttribute('data-value', value);
  const maximum = element.getAttribute('data-maximum');
  const top = maximum == null ? NaN : Number(this.variables.get(maximum) ?? maximum);
  if (element.classList.contains('ms-gauge')) {
    const fill = top > 0 ? Math.min(1, Math.max(0, Number(value) / top)) : 0;
    element.style.setProperty('--ms-gauge-fill', String(Number.isFinite(fill) ? fill : 0));
    const color = element.getAttribute('data-color');
    if (color) element.style.setProperty('--ms-gauge', color);
  }
  const caption = element.getAttribute('data-caption');
  element.title = `${caption ? `${caption}: ` : ''}${value}${Number.isFinite(top) ? ` / ${top}` : ''}`;
};

// The detail of each effect, read from the attributes the HTML emitter writes.
function read(effect, element) {
  const number = name => {
    const raw = element.getAttribute(name);
    return raw == null || raw === '' ? null : Number(raw);
  };
  switch (effect) {
    case 'sound':
      return {
        src: element.getAttribute('src'),
        channel: element.getAttribute('data-channel') === 'music' ? 'music' : 'effects',
        volume: number('data-volume'),
        repeats: number('data-repeats'),
        loops: element.hasAttribute('loop'),
        continues: element.getAttribute('data-continues') === 'true',
      };
    case 'sound-stop':
      return { channel: element.getAttribute('data-channel') };
    case 'expire':
      return { group: element.getAttribute('data-group') };
    case 'relocate':
      return {
        host: element.getAttribute('data-host'),
        port: number('data-port'),
        quiet: element.getAttribute('data-quiet') === 'true',
      };
    case 'login':
      return { field: element.getAttribute('data-field') };
    case 'variable':
      return { name: element.getAttribute('data-name'), value: element.textContent };
    case 'pane':
      return { name: element.getAttribute('data-pane'), title: element.getAttribute('data-title') };
    default:
      return {};
  }
}

function parseChoices(raw) {
  try {
    const parsed = JSON.parse(raw ?? '[]');
    return Array.isArray(parsed)
      ? parsed.filter(c => c && typeof c.command === 'string').map(c => ({ label: String(c.label ?? ''), command: c.command }))
      : [];
  } catch {
    return [];
  }
}

function* matching(node, selector) {
  if (node.matches?.(selector)) yield node;
  if (node.querySelectorAll) yield* node.querySelectorAll(selector);
}
