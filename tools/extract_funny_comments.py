"""Pick the joking asides out of software-comments.txt."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

FUNNY = re.compile(
    r"wtf"
    r"|no idea"
    r"|why alarm"
    r"|obsolete junk"
    r"|someone intelligent"
    r"|still required \?"
    r"|does not exist"
    r"|bricolage (buydtsienne|kris|bornemienne|anti-nikske|!\?|!)"
    r"|kurt's bricolage"
    r"|bricolage to avoid"
    r"|klaasje|jantje"
    r"|has not frequency"
    r"|wherever it is"
    r"|you have no idea where"
    r"|get stuck \?\?\?"
    r"|fake pointer"
    r"|25 years"
    r"|dirty patch"
    r"|kind of rounding"
    r"|demands a db to opened"
    r"|try to get by"
    r"|unflipped position.{0,40}\?\?\?\?"
    r"|how to disable p factor"
    r"|why edge triggered"
    r"|cam track signal \?\?\?\?",
    re.IGNORECASE,
)


def parse_comments(path: Path) -> list[tuple[str, str, str]]:
    current = ""
    entries: list[tuple[str, str, str]] = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        if not raw.strip() or raw.startswith("=") or raw.startswith("Comments ") or raw.startswith("Files "):
            continue
        if " | " not in raw:
            current = raw.strip()
            continue
        location, text = raw.split(" | ", 1)
        location = location.strip()
        if not location and entries and entries[-1][0] == current:
            file_name, previous_location, previous = entries[-1]
            entries[-1] = (file_name, previous_location, previous + "\n" + text.strip())
            continue
        entries.append((current, location, text.strip()))
    return entries


def interesting(entries: list[tuple[str, str, str]]) -> list[tuple[str, str, str]]:
    chosen: list[tuple[str, str, str]] = []
    seen: set[str] = set()
    for index, (file_name, location, text) in enumerate(entries):
        if not FUNNY.search(text):
            continue
        if len(text) > 280 and ("END_IF" in text or "END_REGION" in text):
            continue
        body = text
        if body[:1].islower() and index > 0 and entries[index - 1][0] == file_name:
            body = entries[index - 1][2] + " " + body
        if index + 1 < len(entries) and entries[index + 1][0] == file_name:
            nxt = entries[index + 1][2]
            if nxt[:1].islower():
                body = body + " " + nxt
        key = re.sub(r"\s+", " ", body).strip().lower()
        key = re.sub(r"\$\$\$\s*lds\s*-\s*\d+\s*-\s*", "", key)
        if key in seen:
            continue
        seen.add(key)
        chosen.append((file_name, location, re.sub(r"\s+", " ", body).strip()))
    return chosen


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Write the funny software comments to a text file.")
    parser.add_argument(
        "comments",
        nargs="?",
        default=r"C:\TiaGit\HCM1500\software-comments.txt",
    )
    parser.add_argument(
        "-o",
        "--output",
        default=r"C:\TiaGit\HCM1500\funny-comments.txt",
    )
    args = parser.parse_args(argv)
    source = Path(args.comments)
    if not source.is_file():
        print(f"Comment file not found: {source}", file=sys.stderr)
        return 1
    chosen = interesting(parse_comments(source))
    lines = [f"Funny comments from {source}", f"Comments: {len(chosen)}", ""]
    for file_name, location, text in chosen:
        lines.append(f"{file_name}:{location}")
        lines.append(text)
        lines.append("")
    output = Path(args.output)
    output.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {len(chosen)} comments to {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
