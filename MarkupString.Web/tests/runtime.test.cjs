// Runs markupstring.js in Chromium against what the HTML writer writes (fragments.json, kept current by
// MarkupWebTests). Run: npm ci, npx playwright install chromium, npm test
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const read = file => fs.readFileSync(path.join(__dirname, file), 'utf8');
const F = JSON.parse(read('fragments.json'));
const js = read('../wwwroot/markupstring.js');
const css = read('../wwwroot/markupstring.css');
let fails = 0;
const check = (ok, name, extra = '') => { console.log((ok ? 'PASS ' : 'FAIL ') + name + (ok ? '' : ' ' + extra)); if (!ok) fails++; };
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  page.on('pageerror', e => { console.log('PAGEERROR', e.message); fails++; });
  await page.route('http://t/**', r => {
    const u = r.request().url();
    if (u.endsWith('.js')) return r.fulfill({ contentType: 'text/javascript', body: js });
    if (u.endsWith('.css')) return r.fulfill({ contentType: 'text/css', body: css });
    return r.fulfill({ contentType: 'text/html', body: '<!doctype html><link rel=stylesheet href="/_content/MarkupString.Web/markupstring.css"><div id=chatpane data-ms-pane-target=chat></div><span id=hpshow data-ms-variable=hp></span><div id=out><div>old line <audio class="ms-sound" data-channel="effects" src="old.wav" preload="none"></audio></div></div>' });
  });
  await page.goto('http://t/index.html');
  await page.evaluate(async () => {
    window.played = []; window.paused = [];
    window.Audio = class { constructor(src) { this.src = src; this.paused = true; this.loop = false; this.volume = 1; this._l = {}; } addEventListener(n, f) { this._l[n] = f; } play() { this.paused = false; window.played.push(this.src); return Promise.resolve(); } pause() { this.paused = true; window.paused.push(this.src); } };
    const m = await import('/_content/MarkupString.Web/markupstring.js');
    window.sent = []; window.prompted = []; window.relocated = [];
    window.rt = m.attach(document.getElementById('out'), { send: c => sent.push(c), prompt: c => prompted.push(c), relocate: d => relocated.push(d) });
    window.add = html => { const d = document.createElement('div'); d.innerHTML = html; document.getElementById('out').append(d); return d; };
  });
  const tick = () => page.evaluate(() => new Promise(r => setTimeout(r, 20)));
  const ev = f => page.evaluate(f);
  const add = async key => { await page.evaluate(h => add(h), F[key]); await tick(); };

  check((await ev(() => played.length)) === 0, 'replay=false: old sound not played');

  await add('cmd');
  await page.click('a[xch_cmd="look"]');
  check(JSON.stringify(await ev(() => sent)) === '["look"]', 'command link click sends');
  await page.focus('a[xch_cmd="look"]'); await page.keyboard.press('Enter');
  check((await ev(() => sent.length)) === 2, 'command link Enter sends');

  await add('mxpmenu');
  await page.click('.ms-menu');
  const items = await page.$$eval('.ms-menu-popup button', bs => bs.map(b => b.textContent));
  check(JSON.stringify(items) === '["Buy","Look"]', 'menu opens with choices', JSON.stringify(items));
  check(await ev(() => document.activeElement.textContent) === 'Buy', 'first item focused');
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
  check(JSON.stringify(await ev(() => sent.slice(-1))) === '["look sword"]', 'menu choice by keyboard sends', JSON.stringify(await ev(() => sent)));
  check((await page.$$('.ms-menu-popup')).length === 0, 'menu closes after choice');
  await page.click('.ms-menu'); await page.keyboard.press('Escape');
  check((await page.$$('.ms-menu-popup')).length === 0, 'Escape closes menu');
  await page.click('.ms-menu'); await page.mouse.click(5, 500);
  check((await page.$$('.ms-menu-popup')).length === 0, 'outside click closes menu');

  await add('prompt');
  await page.click('.ms-prompt');
  check(JSON.stringify(await ev(() => prompted)) === '["say "]', 'single-choice prompt goes to prompt()');

  await add('sound');
  check(JSON.stringify(await ev(() => played)) === '["http://t/door.wav"]', 'sound plays, resolved', JSON.stringify(await ev(() => played)));
  await add('music');
  await add('music');
  check((await ev(() => played.filter(p => p.endsWith('theme.ogg')).length)) === 2 && (await ev(() => paused.includes('http://t/theme.ogg'))), 'new music replaces old');
  await add('stop');
  check(await ev(() => rt._music === null && rt._effects.size === 0), 'stop silences both');

  await add('bell');
  check(await ev(() => document.getElementById('out').classList.contains('ms-bell-ring')), 'bell rings');

  await add('grouped');
  await add('expire');
  check(await ev(() => !document.querySelector('a[xch_cmd="north"]') && !!document.querySelector('.ms-link-group .ms-expired')), 'grouped link expired');
  check(await ev(() => !!document.querySelector('a[xch_cmd="look"]')), 'other links untouched');

  await add('gauge');
  await add('vars');
  const g = await ev(() => { const e = document.querySelector('.ms-gauge'); return [e.style.getPropertyValue('--ms-gauge-fill'), e.dataset.value, e.title]; });
  check(g[0] === '0.5' && g[1] === '25' && g[2] === 'Hit Points: 25 / 50', 'gauge follows variables', JSON.stringify(g));
  check(await ev(() => document.getElementById('hpshow').textContent) === '25', 'host [data-ms-variable] updated');
  check(await ev(() => rt.variables.get('maxhp')) === '50', 'variables kept');

  await add('pane');
  check(await ev(() => document.querySelector('#chatpane .ms-pane')?.textContent) === 'chat line', 'pane text moved to its pane');

  await add('relocate');
  check(JSON.stringify(await ev(() => relocated)) === '[{"host":"h","port":23,"quiet":false}]', 'relocate goes to host option', JSON.stringify(await ev(() => relocated)));

  await ev(() => document.getElementById('out').addEventListener('ms:command', e => { if (e.detail.command === 'look') e.preventDefault(); }));
  const before = await ev(() => sent.length);
  await page.click('a[xch_cmd="look"]');
  check((await ev(() => sent.length)) === before, 'cancelled ms:command is not sent');

  await ev(() => { window.hit = []; rt.use({ selector: '.my-thing', handle: el => hit.push(el.textContent) }); rt.use({ effect: 'bell', handle: () => { hit.push('bell'); return true; } }); });
  await page.evaluate(h => { add('<span class="my-thing">x</span>'); document.getElementById('out').classList.remove('ms-bell-ring'); add(h); }, F.bell); await tick();
  check(JSON.stringify(await ev(() => hit)) === '["x","bell"]' && !(await ev(() => document.getElementById('out').classList.contains('ms-bell-ring'))), 'plugins: own selector and effect override', JSON.stringify(await ev(() => hit)));

  await add('cmd');
  check(await ev(() => getComputedStyle(document.querySelector('.ms-cmd-link')).cursor) === 'pointer', 'stylesheet applies');
  check(await ev(() => getComputedStyle(document.querySelector('.ms-menu')).cursor) === 'pointer', 'menu style applies');

  await add('clear');
  check(await ev(() => document.getElementById('out').children.length === 1 && document.getElementById('out').textContent.includes('fresh')), 'clear removes earlier lines');

  await ev(async () => {
    const m = await import('/_content/MarkupString.Web/markupstring.js');
    const d = document.createElement('div'); d.id = 'dn'; document.body.append(d);
    window.calls = [];
    const ref = { invokeMethodAsync: (n, ...a) => { calls.push([n, ...a]); return Promise.resolve(); } };
    m.attachDotNet(d, ref, { forward: ['bell'] });
  });
  await page.evaluate(F => { const x = document.createElement('div'); x.innerHTML = F.cmd + F.bell; document.getElementById('dn').append(x); }, F); await tick();
  await page.click('#dn a[xch_cmd]');
  check(JSON.stringify(await ev(() => calls)) === '[["OnEffect","bell",{}],["Send","look"]]', 'attachDotNet forwards', JSON.stringify(await ev(() => calls)));

  await ev(() => rt.detach());
  await add('cmd');
  const n = await ev(() => sent.length);
  await page.click('#out a[xch_cmd="look"]');
  check((await ev(() => sent.length)) === n, 'detach stops handling');
  await browser.close();
  console.log(fails ? `${fails} FAILED` : 'ALL PASSED');
  process.exit(fails ? 1 : 0);
})();
