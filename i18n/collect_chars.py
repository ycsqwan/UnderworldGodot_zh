#!/usr/bin/env python3
"""Collect every non-ASCII character the Chinese build can render.

Sources:
  * resources/fonts/translation_zh.json  - the translation table (values only;
    the keys are block_string indices and are never rendered)
  * .cs / .tscn / .tres / .gd files       - Chinese hardcoded in code and scenes

Plus a small margin of punctuation ranges, so a character that only shows up
later still renders instead of falling through to a tofu box.

The whole CJK Unified Ideographs block is deliberately NOT included: it is
20992 code points and would make the subset as big as the original font. Only
characters the build actually renders are kept, so re-run this whenever the
translation table grows and re-subset.

Usage:
    python i18n/collect_chars.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
TABLE = os.path.join(REPO, "resources", "fonts", "translation_zh.json")

# Small ranges that are cheap and easy to need next to CJK text.
MARGIN_RANGES = [
    (0x3000, 0x303F),   # CJK symbols and punctuation  ，。！？：；、《》「」
    (0x3001, 0x3002),   # 、。
    (0x3008, 0x3011),   # 《》「」【】
    (0x3014, 0x3017),   # 〔〕〖〗
    (0xFF00, 0xFFEF),   # Halfwidth / Fullwidth forms
    (0xFF01, 0xFF5E),   # fullwidth ASCII
    (0x2000, 0x206F),   # General punctuation  — ‘ ’ “ ” …
    (0x00B7, 0x00B7),   # middle dot
    (0x2212, 0x2212),   # minus sign
    (0x00D7, 0x00D7),   # multiplication sign
    (0x00F7, 0x00F7),   # division sign
    (0x2103, 0x2103),   # degree celsius
    (0x2109, 0x2109),   # degree fahrenheit
]


def collect_from_table():
    with open(TABLE, encoding="utf-8-sig") as fh:
        data = json.load(fh)
    out = set()
    for value in data.values():
        if isinstance(value, str):
            for ch in value:
                if ord(ch) > 127:
                    out.add(ch)
    return out


def collect_from_sources():
    out = set()
    exts = (".cs", ".tscn", ".gd", ".tres")
    for root, dirs, files in os.walk(REPO):
        dirs[:] = [d for d in dirs if d not in (".git", ".godot", "obj", "bin")]
        for name in files:
            if not name.endswith(exts):
                continue
            try:
                with open(os.path.join(root, name), encoding="utf-8") as fh:
                    text = fh.read()
            except (UnicodeDecodeError, OSError):
                continue
            for ch in text:
                if ord(ch) > 127:
                    out.add(ch)
    return out


def main():
    if not os.path.exists(TABLE):
        sys.exit("translation table not found: " + TABLE)

    from_table = collect_from_table()
    from_src = collect_from_sources()

    chars = set(from_table) | set(from_src)
    for lo, hi in MARGIN_RANGES:
        for cp in range(lo, hi + 1):
            chars.add(chr(cp))

    ordered = sorted(chars)
    out_path = os.path.join(HERE, "needed_chars.txt")
    with open(out_path, "w", encoding="utf-8") as fh:
        fh.write("".join(ordered))

    ideographs = [c for c in ordered if 0x4E00 <= ord(c) <= 0x9FFF]
    print("chars from table   :", len(from_table))
    print("chars from sources :", len(from_src))
    print("total chars kept   :", len(ordered))
    print("  CJK ideographs   :", len(ideographs))
    print("written to         :", out_path)


if __name__ == "__main__":
    main()
