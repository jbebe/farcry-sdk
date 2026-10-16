// node inbox.mjs        prints each thread in Downloads/nexus-*.json that has unhandled items
// node inbox.mjs done   records every item in those files as seen and deletes the files
// node inbox.mjs link   prints the bookmarklet URL to paste into a bookmark
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const AUTHOR = 'bajuh';
const mode = process.argv[2];
if (mode === 'link') {
  console.log('javascript:' + encodeURIComponent(fs.readFileSync(path.join(import.meta.dirname, 'bookmarklet.js'), 'utf8')));
  process.exit();
}

const downloads = path.join(os.homedir(), 'Downloads');
const seenFile = path.join(import.meta.dirname, 'seen.json');
const seen = new Set(fs.existsSync(seenFile) ? JSON.parse(fs.readFileSync(seenFile, 'utf8')) : []);
const files = fs.readdirSync(downloads).filter(f => /^nexus-.+\.json$/.test(f)).map(f => path.join(downloads, f));
const pages = files.map(f => JSON.parse(fs.readFileSync(f, 'utf8')));
const key = (kind, item) => `${kind}:${item.id}`;

if (mode === 'done') {
  for (const page of pages) for (const item of page.items) seen.add(key(page.kind, item));
  fs.writeFileSync(seenFile, JSON.stringify([...seen].sort(), null, 1));
  files.forEach(f => fs.unlinkSync(f));
  console.log(`${files.length} files consumed, ${seen.size} items seen in total`);
  process.exit();
}

const threads = new Map();
for (const page of pages) for (const item of page.items) {
  const t = `${page.kind}:${item.thread}`;
  if (!threads.has(t)) threads.set(t, { kind: page.kind, source: page.source, items: new Map() });
  threads.get(t).items.set(item.id, item);
}

// Handled: already seen, yours, followed by your reply, or a bug whose status has moved on from New.
const out = [];
for (const { kind, source, items } of threads.values()) {
  const thread = [...items.values()];
  const fresh = thread.filter(item => !seen.has(key(kind, item))
    && item.author !== AUTHOR
    && !thread.some(c => c.author === AUTHOR && c.date > item.date)
    && (!item.status || item.status === 'New'));
  if (fresh.length) out.push({ kind, source, new: fresh.map(i => i.id), thread });
}
console.log(JSON.stringify(out, null, 1));
