// Captures the open Nexus page (a mod's Posts or Bugs tab, or a PM conversation) to Downloads/nexus-*.json.
(() => {
  const text = el => el?.innerText.trim() ?? '';
  const save = (kind, id, source, items) => {
    const a = document.createElement('a');
    a.href = 'data:application/json,' + encodeURIComponent(JSON.stringify({ kind, source, items }, null, 1));
    a.download = `nexus-${kind}-${id}-${Date.now()}.json`;
    a.click();
  };
  const mod = location.pathname.match(/^\/([^/]+)\/mods\/(\d+)/);
  const title = document.querySelector('meta[property="og:title"]')?.content;
  const bugs = document.querySelector('#tab-modbugs');
  const pm = location.host === 'forums.nexusmods.com' && location.pathname.match(/^\/messenger\/(\d+)/);

  if (mod && bugs) {
    const head = [...bugs.querySelectorAll('thead th')].map(text);
    save('bugs', mod[2], title, [...bugs.querySelectorAll('tbody tr')].map(tr => {
      const cells = Object.fromEntries([...tr.cells].map((td, i) => [head[i] || `col${i}`, text(td)]));
      const link = tr.querySelector('a[href]');
      const url = link?.href ?? location.href;
      return { id: url, thread: url, text: text(link), status: cells.Status, url, cells };
    }));
  } else if (mod && document.querySelector('li.comment')) {
    const cid = li => li?.id.replace('comment-', '');
    save('posts', mod[2], title, [...document.querySelectorAll('li.comment')].map(li => ({
      id: cid(li),
      thread: cid(li.parentElement.closest('li.comment') ?? li),
      author: text(li.querySelector('.comment-name a')),
      date: new Date(li.querySelector('time[data-date]').dataset.date * 1000).toISOString(),
      text: text(li.querySelector('.comment-content-text')),
      url: `https://www.nexusmods.com/${mod[1]}/mods/${mod[2]}?tab=posts&jump_to_comment=${cid(li)}`,
    })));
  } else if (pm) {
    save('pm', pm[1], text(document.querySelector('h1')), [...document.querySelectorAll('[data-commentid]')].map(el => ({
      id: el.dataset.commentid,
      thread: pm[1],
      author: text(el.querySelector('[itemprop="name"], .cAuthorPane_author, .ipsComment_author')),
      date: el.querySelector('time')?.getAttribute('datetime'),
      text: text(el.querySelector('[data-role="commentContent"]')),
      url: location.href,
    })));
  } else {
    alert("Nexus inbox: open a mod's Posts or Bugs tab, or a PM conversation.");
  }
})();
