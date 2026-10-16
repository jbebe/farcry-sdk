// Captures the open Nexus page (a mod's Posts or Bugs tab, or a PM conversation) to Downloads/nexus-*.json.
(() => {
  const text = el => el?.innerText.trim() ?? '';
  const save = (kind, id, source, items) => {
    const a = document.createElement('a');
    a.href = 'data:application/json,' + encodeURIComponent(JSON.stringify({ kind, source, items }, null, 1));
    a.download = `nexus-${kind}-${id}-${Date.now()}.json`;
    a.click();
  };
  // A mod page comment, in the Posts tab or in an expanded bug report.
  const comment = li => {
    const time = li.querySelector('time');
    return {
      id: li.id.replace(/\D+/, ''),
      author: text(li.querySelector('.comment-name')),
      date: time.dataset.date ? new Date(time.dataset.date * 1000).toISOString() : time.getAttribute('datetime'),
      text: text(li.querySelector('.comment-content-text') ?? li.querySelector('.comment-content')).replace(text(time), '').trim(),
    };
  };
  const mod = location.pathname.match(/^\/([^/]+)\/mods\/(\d+)/);
  const page = mod && `https://www.nexusmods.com/${mod[1]}/mods/${mod[2]}`;
  const title = document.querySelector('meta[property="og:title"]')?.content;
  const tab = document.querySelector('a.selected[href*="?tab="]')?.href.match(/tab=(\w+)/)[1];
  const pm = location.host === 'forums.nexusmods.com' && location.pathname.match(/^\/messenger\/(\d+)/);

  if (mod && tab === 'bugs') {
    save('bugs', mod[2], title, [...document.querySelectorAll('tr.mod-issue-row')].flatMap(tr => {
      const issue = {
        thread: tr.dataset.issueId,
        title: text(tr.querySelector('.issue-title')),
        status: text(tr.querySelector('.table-bug-status')),
        url: `${page}?tab=bugs#issue_${tr.dataset.issueId}`,
      };
      const replies = [...tr.querySelectorAll('li.comment')].map(li => ({ ...issue, ...comment(li) }));
      return replies.length ? replies : [{ ...issue, id: issue.thread }];
    }));
  } else if (mod && tab === 'posts') {
    save('posts', mod[2], title, [...document.querySelectorAll('li.comment')].map(li => {
      const c = comment(li);
      const root = li.parentElement.closest('li.comment');
      return { ...c, thread: root ? comment(root).id : c.id, url: `${page}?tab=posts&jump_to_comment=${c.id}` };
    }));
  } else if (pm) {
    save('pm', pm[1], document.title.split(' - ')[0], [...document.querySelectorAll('[data-commentid]')].map(el => ({
      id: el.dataset.commentid,
      thread: pm[1],
      author: text(el.querySelector('.cAuthorPane_author, .ipsComment_author')).split('\n')[0].trim(),
      date: el.querySelector('time')?.getAttribute('datetime'),
      text: text(el.querySelector('[data-role="commentContent"]')),
      url: location.href,
    })));
  } else {
    alert("Nexus inbox: open a mod's Posts or Bugs tab, or a PM conversation.");
  }
})();
