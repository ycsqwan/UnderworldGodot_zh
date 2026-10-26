#!/usr/bin/env python3
"""Measure the hanzi ink fraction of the WQY14 fallback so the fallback scale can be
derived from ink height rather than the pixel font's line box.

    python i18n/measure_ink.py

Reads the same translation table the game does and reports, for every distinct CJK
code point the game will actually render, the outline bbox height in font units and as
a fraction of units_per_em. The fallback is drawn at fallbackScale * size pixels of
em, so its hanzi ink is fallbackScale * size * ink_fraction pixels tall. Setting that
equal to the pixel font's ink (Height rows * size / DesignSize) gives

    fallbackScale = Height * upem / (DesignSize * hanzi_ink_units)
"""
import json
import os
import sys
from collections import Counter

from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
FONT = os.path.join(ROOT, "resources", "fonts", "WenQuanYiBitmapSong14px.ttf")
TABLE = os.path.join(ROOT, "resources", "fonts", "translation_zh.json")


def main():
    font = TTFont(FONT)
    upem = font["head"].unitsPerEm
    glyph_set = font.getGlyphSet()
    cmap = font.getBestCmap()
    hmtx = font["hmtx"]
    print("upem=%d glyphs=%d cmap=%d" % (upem, len(font.getGlyphOrder()), len(cmap)))

    with open(TABLE, encoding="utf-8-sig") as fh:
        table = json.load(fh)

    chars = sorted({c for v in table.values() for c in v if ord(c) > 127})
    print("distinct non-ASCII chars in table:", len(chars))

    heights = Counter()
    missing = []
    for ch in chars:
        cp = ord(ch)
        if cp not in cmap:
            missing.append(ch)
            continue
        glyph = glyph_set[cmap[cp]]
        pen = BoundsPen(glyph_set)
        glyph.draw(pen)
        if pen.bounds is None:
            continue
        y_min, y_max = pen.bounds[1], pen.bounds[3]
        heights[y_max - y_min] += 1

    if missing:
        print("missing from font:", "".join(missing))

    if not heights:
        print("FAIL: no measurable glyphs")
        return 1

    total = sum(heights.values())
    ordered = sorted(heights.items())
    cumulative = 0
    median_units = None
    for units, count in ordered:
        cumulative += count
        if median_units is None and cumulative >= total / 2.0:
            median_units = units

    mean_units = sum(u * c for u, c in heights.items()) / float(total)
    print("ink height (font units): min=%d median=%d max=%d mean=%.1f"
          % (ordered[0][0], median_units, ordered[-1][0], mean_units))
    print("as fraction of upem:    median=%.4f mean=%.4f"
          % (median_units / float(upem), mean_units / float(upem)))

    # The common case: how many units is a "full" hanzi?
    top = heights.most_common(8)
    print("most common ink heights:", ", ".join(
        "%d units x%d (%.3f em)" % (u, c, u / float(upem)) for u, c in top))

    # Scale that makes hanzi ink match a pixel font's ink, per row count.
    design = 16
    for rows in (5, 6, 15):
        scale = rows * upem / (float(design) * median_units)
        print("rows=%2d -> fallbackScale=%.4f  (em@64=%.1fpx, hanzi ink=%.1fpx)"
              % (rows, scale, scale * 64, scale * 64 * median_units / float(upem)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
