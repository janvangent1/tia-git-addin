"""Extract every comment from a TIA Portal software export into one text file.

SCL and STL source contributes // line comments and (* *) block comments.
XML contributes block, network, line, member, and tag comments.
"""

from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as ET
import xml.parsers.expat
from pathlib import Path

SOURCE_SUFFIXES = {".scl", ".awl", ".stl"}
XML_SUFFIX = ".xml"


def extract_source_comments(text: str) -> list[tuple[int, int, str]]:
    """Return (start_line, end_line, comment) for // and nested (* *) comments."""
    comments: list[tuple[int, int, str]] = []
    index = 0
    line = 1
    length = len(text)
    while index < length:
        character = text[index]
        if character == "\n":
            line += 1
            index += 1
            continue
        if character == "'":
            index, line = skip_scl_string(text, index, line)
            continue
        if text.startswith("//", index):
            index += 2
            start = line
            buffer: list[str] = []
            while index < length and text[index] != "\n":
                buffer.append(text[index])
                index += 1
            comment = "".join(buffer).strip()
            if comment:
                comments.append((start, start, comment))
            continue
        if text.startswith("(*", index):
            index += 2
            start = line
            depth = 1
            buffer = []
            while index < length and depth:
                if text[index] == "\n":
                    buffer.append("\n")
                    line += 1
                    index += 1
                    continue
                if text.startswith("(*", index):
                    depth += 1
                    buffer.append("(*")
                    index += 2
                    continue
                if text.startswith("*)", index):
                    depth -= 1
                    if depth:
                        buffer.append("*)")
                    index += 2
                    continue
                buffer.append(text[index])
                index += 1
            comment = "".join(buffer).strip()
            if comment:
                comments.append((start, line, comment))
            continue
        index += 1
    return comments


def skip_scl_string(text: str, index: int, line: int) -> tuple[int, int]:
    index += 1
    length = len(text)
    while index < length:
        if text[index] == "\n":
            line += 1
            index += 1
            continue
        if text[index] == "'":
            if index + 1 < length and text[index + 1] == "'":
                index += 2
                continue
            return index + 1, line
        index += 1
    return index, line


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def direct_named_text(element: ET.Element, name: str) -> str:
    for child in element:
        if local_name(child.tag) != "AttributeList":
            continue
        for item in child:
            if local_name(item.tag) == name:
                return (item.text or "").strip()
    for child in element:
        if local_name(child.tag) == name:
            return (child.text or "").strip()
    return ""


def parse_xml(path: Path) -> tuple[ET.Element, dict[ET.Element, int]] | None:
    """Parse XML and remember each element's opening line. ElementTree drops that."""
    builder = ET.TreeBuilder()
    lines: dict[ET.Element, int] = {}
    parser = xml.parsers.expat.ParserCreate(namespace_separator="}")

    def start(name: str, attrs: dict[str, str]) -> None:
        element = builder.start(name, attrs)
        lines[element] = parser.CurrentLineNumber

    parser.StartElementHandler = start
    parser.EndElementHandler = builder.end
    parser.CharacterDataHandler = builder.data
    try:
        parser.Parse(path.read_bytes(), True)
    except xml.parsers.expat.ExpatError:
        return None
    return builder.close(), lines


def extract_xml_comments(path: Path) -> list[tuple[int, int, str]]:
    parsed = parse_xml(path)
    if parsed is None:
        return []
    root, lines = parsed

    parent = {child: element for element in root.iter() for child in element}
    comments: list[tuple[int, int, str]] = []
    for element in root.iter():
        name = local_name(element.tag)
        line = lines.get(element, 0)
        if name == "LineComment":
            text = "".join(element.itertext()).strip()
            if text:
                comments.append((line, line, text))
            continue
        if name == "Comment":
            parts = comment_parts(element)
            if parts:
                label = context_label(element, parent)
                text = "\n".join(parts)
                if label:
                    text = label + ": " + text
                comments.append((line, line, text))
            continue
        if name == "MultilingualText" and element.get("CompositionName") == "Comment":
            parts = comment_parts(element)
            if parts:
                label = context_label(element, parent)
                text = "\n".join(parts)
                if label:
                    text = label + ": " + text
                comments.append((line, line, text))
    return comments


def comment_parts(element: ET.Element) -> list[str]:
    parts: list[str] = []
    for child in element.iter():
        if child is element:
            continue
        name = local_name(child.tag)
        if name not in {"Text", "MultiLanguageText"}:
            continue
        text = (child.text or "").strip()
        if not text:
            continue
        culture = child.get("Lang") or ""
        if not culture:
            for item in child:
                if local_name(item.tag) == "Culture":
                    culture = (item.text or "").strip()
        if culture and culture.lower() not in {"en-us", "en"}:
            parts.append(f"[{culture}] {text}")
        else:
            parts.append(text)
    return parts


def context_label(element: ET.Element, parent: dict[ET.Element, ET.Element]) -> str:
    current: ET.Element | None = element
    while current is not None:
        name = local_name(current.tag)
        if name == "Member" and current.get("Name"):
            return current.get("Name") or ""
        if name.endswith("PlcTag"):
            tag_name = direct_named_text(current, "Name")
            if tag_name:
                return tag_name
        if name.endswith("CompileUnit"):
            title = sibling_title(current)
            return title or "network"
        if name.startswith("SW.Blocks.") or name.endswith("PlcTagTable") or name.endswith("PlcStruct"):
            block_name = direct_named_text(current, "Name")
            if block_name:
                return block_name
        current = parent.get(current)
    return ""


def sibling_title(compile_unit: ET.Element) -> str:
    for child in compile_unit:
        if local_name(child.tag) != "ObjectList":
            continue
        for item in child:
            if local_name(item.tag) == "MultilingualText" and item.get("CompositionName") == "Title":
                parts = comment_parts(item)
                if parts:
                    return parts[0].splitlines()[0]
    return ""


def read_text(path: Path) -> str:
    data = path.read_bytes()
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
    return data.decode("utf-8", errors="replace")


def collect(folder: Path) -> list[tuple[Path, list[tuple[int, int, str]]]]:
    found: list[tuple[Path, list[tuple[int, int, str]]]] = []
    for path in sorted(folder.rglob("*")):
        if not path.is_file():
            continue
        suffix = path.suffix.lower()
        if suffix in SOURCE_SUFFIXES:
            comments = extract_source_comments(read_text(path))
        elif suffix == XML_SUFFIX:
            comments = extract_xml_comments(path)
        else:
            continue
        if comments:
            found.append((path, comments))
    return found


def write_report(folder: Path, found: list[tuple[Path, list[tuple[int, int, str]]]], output: Path) -> int:
    total = 0
    lines: list[str] = []
    for path, comments in found:
        relative = path.relative_to(folder).as_posix()
        lines.append("")
        lines.append("=" * 80)
        lines.append(relative)
        lines.append("=" * 80)
        for start, end, text in comments:
            total += 1
            location = str(start) if start == end or end == 0 else f"{start}-{end}"
            body = text.replace("\r\n", "\n").replace("\r", "\n")
            first, *rest = body.split("\n")
            lines.append(f"{location} | {first}")
            for extra in rest:
                lines.append(f"{' ' * len(location)} | {extra}")
    header = [
        f"Comments from {folder}",
        f"Files with comments: {len(found)}",
        f"Comments: {total}",
    ]
    output.write_text("\n".join(header + lines) + "\n", encoding="utf-8")
    return total


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Write every PLC software comment to a text file.")
    parser.add_argument(
        "folder",
        nargs="?",
        default=r"C:\TiaGit\HCM1500\PLC",
        help="Folder that contains the exported PLC software.",
    )
    parser.add_argument(
        "-o",
        "--output",
        help="Text file to write. Defaults to software-comments.txt next to the folder.",
    )
    args = parser.parse_args(argv)
    folder = Path(args.folder)
    if not folder.is_dir():
        print(f"Folder not found: {folder}", file=sys.stderr)
        return 1
    output = Path(args.output) if args.output else folder.parent / "software-comments.txt"
    found = collect(folder)
    total = write_report(folder, found, output)
    print(f"Wrote {total} comments from {len(found)} files to {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
