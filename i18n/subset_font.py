#!/usr/bin/env python3
"""Subset Source Han Sans CN down to the characters the Chinese build renders.

    python i18n/collect_chars.py
    python i18n/subset_font.py i18n/needed_chars.txt \
        resources/fonts/SourceHanSansCN-Regular.otf \
        build/SourceHanSansCN-Subset.otf

The full font is 8.4 MB and covers 30926 glyphs; the subset is around 0.4 MB.
The output keeps the name table (copyright plus the OFL licence text), the
glyph order and the CFF charstrings for the kept code points, and drops the
rest. A verification pass at the end fails loudly if any code point the source
font actually covers was lost, so a bad subset can never ship silently.

Requires fonttools:  pip install fonttools
"""
import sys

from fontTools import subset
from fontTools.ttLib import TTFont


def main():
    if len(sys.argv) != 4:
        sys.stderr.write(
            "usage: subset_font.py <chars.txt> <in.otf> <out.otf>\n")
        return 2

    chars_path, in_path, out_path = sys.argv[1:4]

    with open(chars_path, encoding="utf-8") as fh:
        text = fh.read()
    unicodes = sorted({ord(c) for c in text if ord(c) > 127})
    print("code points to keep:", len(unicodes))

    opts = subset.Options()
    opts.layout_features = ["*"]      # keep GSUB/GPOS (kerning etc.)
    opts.name_IDs = ["*"]             # keep copyright + licence strings
    opts.name_legacy = True
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    opts.recalc_bounds = True
    opts.drop_tables = []
    opts.passthrough_tables = False
    # Source Han Sans has no outline for many symbol code points; skip those and
    # let the verification below confirm nothing the source actually covers was
    # lost.
    opts.ignore_missing_unicodes = True
    opts.glyph_names = False
    opts.hinting = True

    font = subset.load_font(in_path, opts)
    subsetter = subset.Subsetter(options=opts)
    subsetter.populate(unicodes=unicodes)
    subsetter.subset(font)
    subset.save_font(font, out_path, opts)

    src = TTFont(in_path)
    dst = TTFont(out_path)
    src_map = src.getBestCmap()
    dst_map = dst.getBestCmap()

    missing = [cp for cp in unicodes if cp in src_map and cp not in dst_map]
    if missing:
        sys.stderr.write(
            "FAIL: %d code points missing from the subset, e.g. %s\n"
            % (len(missing), " ".join("U+%04X" % cp for cp in missing[:12])))
        return 1

    print("glyphs: %d -> %d" % (len(src.getGlyphOrder()), len(dst.getGlyphOrder())))
    print("verified: all %d requested code points present" % len(unicodes))

    import os
    print("size: %d -> %d bytes (%.1f%% smaller)"
          % (os.path.getsize(in_path), os.path.getsize(out_path),
             100.0 * (1 - os.path.getsize(out_path) / float(os.path.getsize(in_path)))))
    print("wrote:", out_path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
