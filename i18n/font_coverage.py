#!/usr/bin/env python3
"""Throwaway: which characters does the subsetted WenQuanYi actually cover?

Splits the answer two ways, because they have different consequences:
  * characters used by the translation table  -> would render as tofu in game
  * characters only in hardcoded .cs/.tscn    -> would render as tofu there
"""
import json
import re
import sys

from fontTools.ttLib import TTFont

SUB = "build/WenQuanYiBitmapSong14px-Subset.ttf"
cmap = TTFont(SUB).getBestCmap()
out = []
def w(s=""):
    out.append(s)

with open("i18n/needed_chars.txt", encoding="utf-8") as fh:
    needed = {c for c in fh.read() if ord(c) > 127}
missing_needed = sorted(c for c in needed if ord(c) not in cmap)

# Translation table: only the values are ever rendered.
with open("resources/fonts/translation_zh.json", encoding="utf-8-sig") as fh:
    table = json.load(fh)
table_chars = set()
for v in table.values():
    if isinstance(v, str):
        table_chars |= {c for c in v if ord(c) > 127}
missing_table = sorted(c for c in table_chars if ord(c) not in cmap)

w("subset cmap entries: %d" % len(cmap))
w()
w("needed_chars.txt : %d non-ASCII, %d missing from subset"
  % (len(needed), len(missing_needed)))
w("  missing: %s" % "".join(missing_needed))
w()
w("translation_zh.json: %d distinct non-ASCII, %d missing from subset"
  % (len(table_chars), len(missing_table)))
w("  missing: %s" % "".join(missing_table))

# Where do the missing ones come from? Scan the sources collect_chars.py reads.
src_chars = set()
for path in ("resources/fonts/translation_zh.json",):
    with open(path, encoding="utf-8") as fh:
        src_chars |= {c for c in fh.read() if ord(c) > 127}

cs_chars = set()
import os
for root, _dirs, files in os.walk("src"):
    for name in files:
        if name.endswith(".cs"):
            with open(os.path.join(root, name), encoding="utf-8", errors="replace") as fh:
                cs_chars |= {c for c in fh.read() if ord(c) > 127}
scene_chars = set()
for root, _dirs, files in os.walk("scenes"):
    for name in files:
        if name.endswith((".tscn", ".tres", ".gd")):
            with open(os.path.join(root, name), encoding="utf-8", errors="replace") as fh:
                scene_chars |= {c for c in fh.read() if ord(c) > 127}

w()
w("missing from .cs sources      : %d  %s"
  % (len([c for c in missing_needed if c in cs_chars]),
     "".join(c for c in missing_needed if c in cs_chars)))
w("missing from scenes/.tres/.gd : %d  %s"
  % (len([c for c in missing_needed if c in scene_chars]),
     "".join(c for c in missing_needed if c in scene_chars)))
w("missing from translation table: %d  %s"
  % (len([c for c in missing_needed if c in table_chars]),
     "".join(c for c in missing_needed if c in table_chars)))

with open("build/wqy_coverage.txt", "w", encoding="utf-8") as fh:
    fh.write("\n".join(out))
print("wrote build/wqy_coverage.txt")
