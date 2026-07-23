#!/usr/bin/env python3
"""Compact DiscordChatExporter JSON exports into a token-efficient markdown transcript.

Each export in this folder is a DiscordChatExporter JSON dump: a dict with
guild/channel metadata plus a flat "messages" list. Every message repeats the
full author object (id, nickname, color, isBot, a *complete* roles array with
id/color/position per role, avatarUrl) and reaction entries repeat a full user
object per reactor. That's the bulk of the file size and is almost entirely
noise for reading the conversation itself.

This script:
  1. Prints a structure/stats report for each export (message count, date
     range, author/type breakdown, attachment/embed/reaction counts).
  2. Writes a compact markdown transcript to compact/<channel-name>.md:
     one line per message ("[date time] Author: content"), with attachments,
     embeds and reactions folded into short indented sub-lines, reply
     references resolved to a short quoted snippet, and known-noise bot
     "member lookup card" embeds dropped entirely.

Attachments are NOT copied anywhere -- each compacted line keeps the
relative path DiscordChatExporter already recorded (`<export>.json_Files\\...`),
which is exactly where the real file already lives on disk.

Usage:
    python compact_export.py                  # process every *.json next to this script
    python compact_export.py FILE.json ...    # process only the given file(s)
    python compact_export.py --analyze-only    # print stats, skip writing transcripts
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from datetime import datetime
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
OUT_DIR = SCRIPT_DIR / "compact"

# Field-name signature of the "member lookup card" embeds some server bots
# post (account age / created-on / roles cards). Zero modding content.
BOT_CARD_FIELD_SIGNATURE = {"Account age", "Created on"}

SYSTEM_EVENT_LABELS = {
    "ThreadCreated": "started a thread",
    "ChannelPinnedMessage": "pinned a message",
}
# content strings DiscordChatExporter fills in for the above -- redundant
# once we've already rendered the label, so don't repeat them.
SYSTEM_EVENT_BOILERPLATE = {"Started a thread.", "Pinned a message."}


def load(path: Path) -> dict:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def fmt_ts(ts: str | None) -> str:
    if not ts:
        return "?"
    try:
        return datetime.fromisoformat(ts).strftime("%Y-%m-%d %H:%M")
    except ValueError:
        return ts[:16].replace("T", " ")


def analyze(data: dict, path: Path) -> dict:
    msgs = data["messages"]
    types = Counter(m["type"] for m in msgs)
    authors = Counter((m["author"].get("nickname") or m["author"]["name"]) for m in msgs)
    timestamps = [m["timestamp"] for m in msgs if m.get("timestamp")]
    return {
        "file": path.name,
        "guild": data["guild"]["name"],
        "channel": data["channel"]["name"],
        "category": data["channel"].get("category"),
        "message_count": len(msgs),
        "date_range": (fmt_ts(min(timestamps)), fmt_ts(max(timestamps))) if timestamps else (None, None),
        "type_counts": dict(types),
        "unique_authors": len(authors),
        "top_authors": authors.most_common(5),
        "attachments": sum(len(m["attachments"]) for m in msgs),
        "embeds": sum(len(m["embeds"]) for m in msgs),
        "reactions": sum(len(m["reactions"]) for m in msgs),
        "replies": sum(1 for m in msgs if m.get("reference")),
    }


def is_bot_card(msg: dict) -> bool:
    """Detect templated 'member lookup card' bot embeds -- pure noise."""
    if not msg["author"].get("isBot"):
        return False
    for e in msg.get("embeds", []):
        names = {f["name"] for f in e.get("fields", [])}
        if BOT_CARD_FIELD_SIGNATURE.issubset(names):
            return True
    return False


def render_embed(e: dict) -> str | None:
    title = (e.get("title") or "").strip()
    desc = (e.get("description") or "").strip()
    url = e.get("url")
    head = " — ".join(x for x in (title, desc) if x)
    if url and url not in head:
        head = f"{head} ({url})" if head else url
    parts = [head] if head else []
    for f in e.get("fields", []):
        name = (f.get("name") or "").strip()
        value = (f.get("value") or "").strip().replace("\n", " ")
        if name or value:
            parts.append(f"{name}: {value}" if name else value)
    if e.get("thumbnail") or e.get("images"):
        parts.append("[image]")
    return "; ".join(parts) if parts else None


def render_message(msg: dict, id_lookup: dict[str, tuple[str, str]]) -> list[str]:
    author = msg["author"].get("nickname") or msg["author"]["name"]
    content = (msg.get("content") or "").strip()
    t = msg["type"]

    if t in SYSTEM_EVENT_LABELS:
        text = content if content and content not in SYSTEM_EVENT_BOILERPLATE else ""
        line = f"-- {author} {SYSTEM_EVENT_LABELS[t]}" + (f": {text}" if text else "") + " --"
        return [line]

    prefix = ""
    ref = msg.get("reference")
    if ref and ref.get("messageId") and ref["messageId"] in id_lookup:
        ra, rc = id_lookup[ref["messageId"]]
        snippet = (rc[:60] + "…") if len(rc) > 60 else rc
        prefix = f'[↳ replying to {ra}: "{snippet}"] ' if snippet else f"[↳ replying to {ra}] "
    elif msg.get("forwardedMessage"):
        prefix = "[↳ forwarded message] "

    pin_marker = "\U0001F4CC " if msg.get("isPinned") else ""
    body = f"{prefix}{content}".strip()
    header = f"[{fmt_ts(msg['timestamp'])}] {pin_marker}{author}:"
    lines = [f"{header} {body}" if body else header]

    for a in msg.get("attachments", []):
        lines.append(f"    \U0001F4CE {a['fileName']} -> {a['url']}")
    for e in msg.get("embeds", []):
        rendered = render_embed(e)
        if rendered:
            lines.append(f"    \U0001F517 {rendered}")
    if msg.get("reactions"):
        r = ", ".join(f"{x['emoji']['name']}×{x['count']}" for x in msg["reactions"])
        lines.append(f"    reactions: {r}")
    return lines


def compact_transcript(data: dict) -> str:
    msgs = data["messages"]
    id_lookup = {
        m["id"]: (m["author"].get("nickname") or m["author"]["name"], (m.get("content") or "").strip())
        for m in msgs
    }

    out = [
        f"# {data['guild']['name']} / #{data['channel']['name']}",
    ]
    if data["channel"].get("category"):
        out.append(f"Category: {data['channel']['category']}")
    out.append(f"Messages: {len(msgs)} (source: {data['channel']['id']}.json)")
    out.append("")

    skipped_bot_cards = 0
    for m in msgs:
        if is_bot_card(m):
            skipped_bot_cards += 1
            continue
        out.extend(render_message(m, id_lookup))

    if skipped_bot_cards:
        out.append("")
        out.append(f"[{skipped_bot_cards} bot member-lookup card embed(s) omitted]")

    return "\n".join(out)


def safe_filename(name: str) -> str:
    return "".join(c for c in name if c not in '<>:"/\\|?*')


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("files", nargs="*", help="specific export JSON files (default: all *.json next to this script)")
    ap.add_argument("--analyze-only", action="store_true", help="print stats only, don't write transcripts")
    args = ap.parse_args()

    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass

    files = [Path(f) for f in args.files] if args.files else sorted(SCRIPT_DIR.glob("*.json"))
    if not files:
        print("No JSON export files found.")
        return

    if not args.analyze_only:
        OUT_DIR.mkdir(exist_ok=True)

    report_lines = ["# Discord export structure report", ""]
    for path in files:
        data = load(path)
        report = analyze(data, path)

        print(f"\n=== {path.name} ===")
        for k, v in report.items():
            if k == "file":
                continue
            print(f"  {k}: {v}")

        report_lines.append(f"## {report['channel']} ({report['file']})")
        for k, v in report.items():
            if k in ("file", "channel"):
                continue
            report_lines.append(f"- {k}: {v}")
        report_lines.append("")

        if not args.analyze_only:
            text = compact_transcript(data)
            out_path = OUT_DIR / safe_filename(f"{data['channel']['name']}-{data['channel']['id']}.md")
            out_path.write_text(text, encoding="utf-8")
            orig_size = path.stat().st_size
            new_size = out_path.stat().st_size
            pct = 100 * new_size / orig_size if orig_size else 0
            print(f"  compacted -> compact/{out_path.name} ({orig_size:,} -> {new_size:,} bytes, {pct:.1f}%)")

    if not args.analyze_only:
        (OUT_DIR / "_analysis.md").write_text("\n".join(report_lines), encoding="utf-8")
        print(f"\nStructure report written to compact/_analysis.md")


if __name__ == "__main__":
    main()
