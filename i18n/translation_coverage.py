#!/usr/bin/env python3
"""Per-block translation coverage of resources/fonts/translation_zh.json.

    python i18n/translation_coverage.py
"""
import json
import os
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
TABLE = os.path.join(ROOT, "resources", "fonts", "translation_zh.json")


def main():
    with open(TABLE, encoding="utf-8-sig") as fh:
        table = json.load(fh)

    total = len(table)
    translated = 0
    per_block_total = Counter()
    per_block_zh = Counter()
    chars = set()
    empty = []

    for key, value in table.items():
        block = key.split("_")[0]
        per_block_total[block] += 1
        has_cjk = isinstance(value, str) and any(ord(c) > 0x2E80 for c in value)
        if has_cjk:
            translated += 1
            per_block_zh[block] += 1
            chars |= {c for c in value if ord(c) > 127}
        elif value == "":
            empty.append(key)

    print("keys: %d   with CJK: %d (%.1f%%)   empty: %d"
          % (total, translated, 100.0 * translated / max(total, 1), len(empty)))
    print("distinct non-ASCII code points in translated values: %d" % len(chars))
    print()
    print("%-8s %8s %8s %8s" % ("block", "keys", "zh", "pct"))
    for block in sorted(per_block_total):
        t = per_block_total[block]
        z = per_block_zh[block]
        print("%-8s %8d %8d %7.1f%%" % (block, t, z, 100.0 * z / t))
    if empty:
        print()
        print("empty values: %s" % ", ".join(sorted(empty)[:20]))

    def group(label, lo, hi):
        t = sum(per_block_total[b] for b in per_block_total if lo <= int(b) <= hi)
        z = sum(per_block_zh[b] for b in per_block_zh if lo <= int(b) <= hi)
        print("%-28s %8d %8d" % (label, t, z))

    print()
    print("%-28s %8s %8s" % ("group", "keys", "zh"))
    group("001-010  UI / item / misc", 1, 10)
    group("024      debug strings", 24, 24)
    group("3072-3105 subtitles", 3072, 3105)
    group("3585-3999 NPC dialogue", 3585, 3999)
    group("everything else", 0, 999999)

    if "--untranslated" in sys.argv:
        print()
        print("untranslated keys in 001-010:")
        for key in sorted(table):
            block = int(key.split("_")[0])
            if 1 <= block <= 10:
                value = table[key]
                if not (isinstance(value, str) and any(ord(c) > 0x2E80 for c in value)):
                    print("  %s = %r" % (key, value))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
