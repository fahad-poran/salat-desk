// Writes docs/content-review.html: every Ayah and Hadith in the ambient bundle on one page,
// with search and a "remove" mark per item. Marked references can be copied into
// tools/content/exclude.txt; build-content.mjs then leaves them out.
//
//   node tools/content/build-review.mjs

import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..', '..');
const bundlePath = path.join(root, 'src', 'DeskDashboard.Core', 'Content', 'seed-content.json');
const outFile = path.join(root, 'docs', 'content-review.html');

const bundle = JSON.parse(await readFile(bundlePath, 'utf8'));
const data = JSON.stringify({
  generated: bundle.generated,
  credits: bundle.credits,
  items: [...bundle.quran, ...bundle.hadith].map((item) => ({
    type: item.type, ref: item.ref, source: item.source, grade: item.grade || null, ar: item.ar, en: item.en
  }))
}).replace(/</g, '\\u003c');

const html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Ambient Content Review</title>
<style>
  :root {
    --bg: #f7f6f3; --card: #ffffff; --ink: #1d1d1b; --muted: #6b6a66; --line: #e3e1db;
    --accent: #0f766e; --accent-soft: #e6f2f0; --remove: #b42318; --remove-soft: #fdecea;
  }
  @media (prefers-color-scheme: dark) {
    :root:not([data-theme="light"]) {
      --bg: #111211; --card: #1a1b1a; --ink: #ecebe7; --muted: #a09f9a; --line: #2c2d2b;
      --accent: #5eead4; --accent-soft: #10302c; --remove: #f97066; --remove-soft: #3a1714;
    }
  }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--ink);
    font: 15px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
  header { position: sticky; top: 0; z-index: 2; background: var(--bg); border-bottom: 1px solid var(--line);
    padding: 14px 16px; }
  .wrap { max-width: 980px; margin: 0 auto; }
  h1 { margin: 0 0 4px; font-size: 20px; }
  .sub { margin: 0 0 12px; color: var(--muted); font-size: 13px; }
  .controls { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
  .tabs button, .actions button { border: 1px solid var(--line); background: var(--card); color: var(--ink);
    border-radius: 999px; padding: 6px 12px; font: inherit; font-size: 13px; cursor: pointer; }
  .tabs button[aria-pressed="true"] { background: var(--accent-soft); border-color: var(--accent); color: var(--accent); }
  input[type="search"] { flex: 1 1 220px; min-width: 0; border: 1px solid var(--line); background: var(--card);
    color: var(--ink); border-radius: 8px; padding: 7px 10px; font: inherit; }
  .count { color: var(--muted); font-size: 13px; }
  .actions { display: flex; gap: 8px; margin-left: auto; }
  .actions .primary { background: var(--accent); border-color: var(--accent); color: var(--bg); }
  main { padding: 16px; }
  .group { margin: 22px 0 8px; font-size: 13px; letter-spacing: .06em; text-transform: uppercase; color: var(--muted); }
  .card { background: var(--card); border: 1px solid var(--line); border-radius: 12px; padding: 14px 16px; margin: 10px 0; }
  .card.removed { background: var(--remove-soft); border-color: var(--remove); }
  .card.removed .ar, .card.removed .en { opacity: .55; }
  .top { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
  .ref { font-weight: 600; color: var(--accent); }
  .grade { color: var(--muted); font-weight: 400; }
  .toggle { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; color: var(--muted); cursor: pointer; white-space: nowrap; }
  .card.removed .toggle { color: var(--remove); font-weight: 600; }
  .ar { margin: 10px 0 0; font-family: "Noto Naskh Arabic", "Amiri", "Traditional Arabic", serif; font-size: 22px;
    line-height: 1.9; text-align: right; }
  .en { margin: 8px 0 0; color: var(--ink); }
  .note { width: 100%; margin-top: 8px; border: 1px solid var(--line); border-radius: 8px; padding: 6px 8px;
    background: transparent; color: var(--ink); font: inherit; font-size: 13px; display: none; }
  .card.removed .note { display: block; }
  dialog { border: 1px solid var(--line); border-radius: 12px; background: var(--card); color: var(--ink);
    width: min(640px, calc(100vw - 32px)); }
  dialog textarea { width: 100%; height: 260px; font: 13px/1.4 ui-monospace, Consolas, monospace;
    background: var(--bg); color: var(--ink); border: 1px solid var(--line); border-radius: 8px; padding: 8px; }
  footer { color: var(--muted); font-size: 12px; padding: 24px 16px 40px; }
  @media (max-width: 600px) { .actions { margin-left: 0; width: 100%; } .ar { font-size: 19px; } }
</style>
</head>
<body>
<header>
  <div class="wrap">
    <h1>Ambient content review</h1>
    <p class="sub">Every Ayah and Hadith the dashboard can show. Tick <b>Remove</b> on anything you don't want, add a reason if you like, then copy the list into <code>tools/content/exclude.txt</code> and rebuild. Marks are saved in this browser.</p>
    <div class="controls">
      <div class="tabs" role="group" aria-label="Filter">
        <button type="button" data-filter="all" aria-pressed="true">All</button>
        <button type="button" data-filter="quran" aria-pressed="false">Quran</button>
        <button type="button" data-filter="hadith" aria-pressed="false">Hadith</button>
        <button type="button" data-filter="removed" aria-pressed="false">Marked</button>
      </div>
      <input type="search" id="search" placeholder="Search reference or text" aria-label="Search">
      <span class="count" id="count"></span>
      <div class="actions">
        <button type="button" id="clear">Clear marks</button>
        <button type="button" class="primary" id="export">Copy exclusion list</button>
      </div>
    </div>
  </div>
</header>
<main class="wrap" id="list"></main>
<footer class="wrap" id="credits"></footer>
<dialog id="dialog">
  <p><b>Paste this into tools/content/exclude.txt</b>, then run <code>node tools/content/build-content.mjs</code>.</p>
  <textarea id="exportText" readonly></textarea>
  <p class="count" id="copied"></p>
  <form method="dialog"><button type="submit">Close</button></form>
</dialog>
<script>
const DATA = ${data};
const KEY = 'ambientContentReview.v1';
let marks = {};
try { marks = JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch { marks = {}; }
const save = () => { try { localStorage.setItem(KEY, JSON.stringify(marks)); } catch {} };
let filter = 'all';

const list = document.getElementById('list');
const cards = [];
let lastGroup = '';
for (const item of DATA.items) {
  const group = item.type === 'quran' ? 'Quran' : item.source;
  if (group !== lastGroup) {
    const h = document.createElement('h2'); h.className = 'group'; h.textContent = group; h.dataset.group = group;
    list.appendChild(h); lastGroup = group;
  }
  const card = document.createElement('article'); card.className = 'card';
  const top = document.createElement('div'); top.className = 'top';
  const ref = document.createElement('span'); ref.className = 'ref'; ref.textContent = item.ref;
  if (item.grade) { const g = document.createElement('span'); g.className = 'grade'; g.textContent = ' · ' + item.grade; ref.appendChild(g); }
  const label = document.createElement('label'); label.className = 'toggle';
  const box = document.createElement('input'); box.type = 'checkbox';
  label.append(box, document.createTextNode('Remove'));
  top.append(ref, label);
  const ar = document.createElement('p'); ar.className = 'ar'; ar.lang = 'ar'; ar.dir = 'rtl'; ar.textContent = item.ar;
  const en = document.createElement('p'); en.className = 'en'; en.textContent = item.en;
  const note = document.createElement('input'); note.className = 'note'; note.placeholder = 'Reason (optional)';
  card.append(top, ar, en, note);
  list.appendChild(card);

  const sync = () => { card.classList.toggle('removed', Boolean(marks[item.ref])); box.checked = Boolean(marks[item.ref]); note.value = marks[item.ref]?.note || ''; };
  box.addEventListener('change', () => { if (box.checked) marks[item.ref] = { note: '' }; else delete marks[item.ref]; save(); sync(); render(); });
  note.addEventListener('input', () => { if (marks[item.ref]) { marks[item.ref].note = note.value; save(); } });
  sync();
  cards.push({ item, card, sync, text: (item.ref + ' ' + item.en + ' ' + item.ar).toLowerCase() });
}

function render() {
  const q = document.getElementById('search').value.trim().toLowerCase();
  let shown = 0;
  const visibleGroups = new Set();
  for (const c of cards) {
    const ok = (filter === 'all' || filter === c.item.type || (filter === 'removed' && marks[c.item.ref])) && (!q || c.text.includes(q));
    c.card.hidden = !ok;
    if (ok) { shown += 1; visibleGroups.add(c.item.type === 'quran' ? 'Quran' : c.item.source); }
  }
  for (const h of list.querySelectorAll('.group')) h.hidden = !visibleGroups.has(h.dataset.group);
  const marked = Object.keys(marks).length;
  document.getElementById('count').textContent = shown + ' shown · ' + marked + ' marked';
}

for (const b of document.querySelectorAll('[data-filter]')) {
  b.addEventListener('click', () => {
    filter = b.dataset.filter;
    for (const o of document.querySelectorAll('[data-filter]')) o.setAttribute('aria-pressed', String(o === b));
    render();
  });
}
document.getElementById('search').addEventListener('input', render);
document.getElementById('clear').addEventListener('click', () => {
  if (!Object.keys(marks).length || !confirm('Clear all marks?')) return;
  marks = {}; save(); cards.forEach((c) => c.sync()); render();
});
document.getElementById('export').addEventListener('click', async () => {
  const lines = ['# References left out of the ambient content bundle. One per line; text after # is a note.'];
  for (const c of cards) {
    const m = marks[c.item.ref];
    if (m) lines.push(c.item.ref + (m.note ? '  # ' + m.note.replace(/\\s+/g, ' ') : ''));
  }
  const text = lines.join('\\n') + '\\n';
  document.getElementById('exportText').value = text;
  let copied = '';
  try { await navigator.clipboard.writeText(text); copied = 'Copied to the clipboard.'; } catch { copied = 'Select the text and copy it.'; }
  document.getElementById('copied').textContent = copied;
  document.getElementById('dialog').showModal();
});
document.getElementById('credits').textContent = 'Bundle generated ' + DATA.generated + '. ' + DATA.credits.join(' ');
render();
</script>
</body>
</html>
`;

await mkdir(path.dirname(outFile), { recursive: true });
await writeFile(outFile, html);
console.log(`Wrote ${path.relative(root, outFile)} (${bundle.quran.length} ayat, ${bundle.hadith.length} hadith)`);
