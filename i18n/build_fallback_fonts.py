#!/usr/bin/env python3
"""Bake one CJK fallback font per pixel font, with the scale in the font itself.

    python i18n/build_fallback_fonts.py

Why this exists
---------------
`SysFontBuilder` used to match the Chinese fallback's size to each pixel font with a
`FontVariation` transform. A transform scales *outlines only* -- it never touches
advances -- so the hanzi ink shrank to match the English while every hanzi kept its
full-width advance. In the message log (FONT5X6P at size 64) that meant 24px of ink
inside a 59.7px cell, and Chinese read as "one character per two characters" with a
hole after every glyph. Subsetting a font is already a build step here, so the fix
is to bake the scale into the font: raise `unitsPerEm` and every glyph, and its
advance, occupies the same fraction of the em it should.

The em is chosen so that the hanzi ink lands exactly on the pixel font's ink:

    hanzi ink (px) = HanziInkUnits / upem * size
    pixel ink (px) = Height * size / DesignSize

    => upem = HanziInkUnits * DesignSize / Height

Advances come along for free because they are in the same units as the outlines.
The fallback's own ascent/descent are scaled by the same ratio so its line box
stays 1em and remains a sane fallback if the runtime override ever misses.

Inputs and outputs are read/written relative to the repository root.
"""
import io
import json
import os
import shutil
import sys
import tempfile

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.pens.boundsPen import BoundsPen

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SOURCE = os.path.join(REPO, "resources", "fonts", "WenQuanYiBitmapSong14px.ttf")
TABLES = [
    os.path.join(REPO, "resources", "fonts", "translation_zh.json"),
    os.path.join(REPO, "resources", "fonts", "translation_zh_uw2.json"),
]
OUT_DIR = os.path.join(REPO, "resources", "fonts")

# Must match SysFontBuilder.DesignSize / HanziInkUnits.
DESIGN_SIZE = 16
HANZI_INK_UNITS = 1300

# Must match SysFontProvider.Expected[...].Height. The name is only for the log line.
INK_ROWS = [(4, "FONT4X5P"), (6, "FONT5X6P"), (7, "FONT5X6I"), (15, "FONTBIG")]

# Below this the reference size is meaningless; 64 is what the message log uses.
REF_SIZE = 64


def collect_chars():
    """Every code point the translations can put on screen, plus printable ASCII."""
    chars = {chr(c) for c in range(0x20, 0x7F)}
    for path in TABLES:
        if not os.path.exists(path):
            print("  (skipped, not found)", os.path.basename(path))
            continue
        table = json.load(io.open(path, encoding="utf-8-sig"))
        for value in table.values():
            chars.update(value)
    return chars


def measure_ink(font, chars):
    """Median hanzi ink height in font units, over the characters we actually ship."""
    cmap = font.getBestCmap()
    glyphs = font.getGlyphSet()
    heights = []
    for ch in chars:
        if ord(ch) < 0x2E80:            # skip ASCII/punctuation: not square hanzi
            continue
        name = cmap.get(ord(ch))
        if name is None:
            continue
        pen = BoundsPen(glyphs)
        glyphs[name].draw(pen)
        if pen.bounds:
            heights.append(pen.bounds[3] - pen.bounds[1])
    if not heights:
        return None, 0
    heights.sort()
    return heights[len(heights) // 2], len(heights)


def build_subset(chars, out_path):
    opts = subset.Options()
    opts.layout_features = ["*"]
    opts.name_IDs = ["*"]
    opts.name_legacy = True
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    opts.recalc_bounds = True
    opts.drop_tables = []
    opts.ignore_missing_unicodes = True
    opts.glyph_names = False
    # Hinting instructions are tied to the original ppem grid and every consumer of
    # these fonts rasterises with hinting off. Dropping them also takes a large slice
    # out of the file.
    opts.hinting = False

    unicodes = sorted({ord(c) for c in chars})
    font = subset.load_font(SOURCE, opts)
    subsetter = subset.Subsetter(options=opts)
    subsetter.populate(unicodes=unicodes)
    subsetter.subset(font)
    subset.save_font(font, out_path, opts)
    return unicodes


def rescale(src_path, dst_path, upem):
    """Move the em square, leaving outlines and advances where they are.

    Growing unitsPerEm without touching the glyphs is exactly the operation we want:
    the glyphs keep their unit values, so they occupy a smaller fraction of the em and
    render smaller at the same font size -- advances included, which is the whole point.
    """
    font = TTFont(src_path)
    old_upem = font["head"].unitsPerEm
    ratio = upem / float(old_upem)

    font["head"].unitsPerEm = upem

    # Keep the line box at 1em so the font is still self-consistent. The runtime pins
    # ascent/descent per size anyway; this only matters if that ever fails.
    for tag in ("hhea", "OS/2"):
        if tag not in font:
            continue
        table = font[tag]
        for attr in ("ascent", "descent", "lineGap",
                     "sTypoAscender", "sTypoDescender", "sTypoLineGap",
                     "usWinAscent", "usWinDescent"):
            if hasattr(table, attr):
                setattr(table, attr, int(round(getattr(table, attr) * ratio)))

    font.save(dst_path)
    return old_upem, ratio


def verify(path, height, chars):
    """Report the numbers the pixel font demands and what the baked font delivers."""
    font = TTFont(path)
    upem = font["head"].unitsPerEm
    cmap = font.getBestCmap()
    hmtx = font["hmtx"]
    glyphs = font.getGlyphSet()

    ink_units = []
    adv_units = []
    for ch in chars:
        if ord(ch) < 0x2E80:
            continue
        name = cmap.get(ord(ch))
        if name is None:
            continue
        pen = BoundsPen(glyphs)
        glyphs[name].draw(pen)
        if pen.bounds:
            ink_units.append(pen.bounds[3] - pen.bounds[1])
            adv_units.append(hmtx[name][0])
    if not ink_units:
        return None
    ink_units.sort()
    adv_units.sort()
    ink_med = ink_units[len(ink_units) // 2]
    adv_med = adv_units[len(adv_units) // 2]

    ink_px = ink_med / upem * REF_SIZE
    adv_px = adv_med / upem * REF_SIZE
    want_px = height * REF_SIZE / DESIGN_SIZE
    return {
        "upem": upem,
        "ink_px": ink_px,
        "adv_px": adv_px,
        "want_px": want_px,
        "err": abs(ink_px - want_px) / want_px,
        "size": os.path.getsize(path),
    }


def main():
    if not os.path.exists(SOURCE):
        sys.stderr.write("missing source font: %s\n" % SOURCE)
        return 1

    chars = collect_chars()
    print("code points needed: %d (%d non-ASCII)"
          % (len(chars), sum(1 for c in chars if ord(c) > 127)))

    src = TTFont(SOURCE)
    ink_units, sample = measure_ink(src, chars)
    print("source: upem=%d hanzi ink=%s units (median over %d glyphs)"
          % (src["head"].unitsPerEm, ink_units, sample))
    if ink_units is None:
        sys.stderr.write("FAIL: could not measure any hanzi ink\n")
        return 1
    if abs(ink_units - HANZI_INK_UNITS) > 8:
        sys.stderr.write(
            "WARNING: SysFontBuilder.HanziInkUnits is %d but this font measures %d; "
            "the baked sizes will be off until they are put back in step.\n"
            % (HANZI_INK_UNITS, ink_units))
    src.close()

    tmpdir = tempfile.mkdtemp(prefix="wqy-subset-")
    subset_path = os.path.join(tmpdir, "subset.ttf")
    try:
        unicodes = build_subset(chars, subset_path)
        print("subset: %d code points -> %d bytes"
              % (len(unicodes), os.path.getsize(subset_path)))

        print()
        print("%-9s %-6s %-6s %-7s %-8s %-8s %-8s %s"
              % ("font", "height", "upem", "ink px", "adv px", "want px", "err", "bytes"))
        worst = 0.0
        for height, name in INK_ROWS:
            upem = int(round(HANZI_INK_UNITS * DESIGN_SIZE / float(height)))
            out_path = os.path.join(OUT_DIR, "WenQuanYiBitmapSong14px-ink%d.ttf" % height)
            rescale(subset_path, out_path, upem)
            v = verify(out_path, height, chars)
            if v is None:
                sys.stderr.write("FAIL: %s has no measurable ink\n" % out_path)
                return 1
            worst = max(worst, v["err"])
            print("%-9s %-6d %-6d %-7.2f %-8.2f %-8.2f %-7.2f%% %d"
                  % (name, height, v["upem"], v["ink_px"], v["adv_px"],
                     v["want_px"], v["err"] * 100.0, v["size"]))
            if v["adv_px"] < v["ink_px"] * 0.98:
                sys.stderr.write(
                    "FAIL: %s advances (%.2f) are narrower than its ink (%.2f); "
                    "hanzi would collide\n" % (name, v["adv_px"], v["ink_px"]))
                return 1

        print()
        if worst > 0.02:
            sys.stderr.write("FAIL: worst ink error %.1f%% exceeds 2%%\n" % (worst * 100))
            return 1
        print("ok: worst ink error %.2f%%" % (worst * 100.0))
        return 0
    finally:
        shutil.rmtree(tmpdir, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
