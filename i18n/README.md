# Chinese localization — tooling and status

This directory holds the scripts behind the Simplified Chinese localization. The
localization lives **in the engine**, not in a separate mod: the translation table is
read by `GameStrings.GetString` at runtime, and the CJK glyphs come from a fallback font
that the four `.SYS` UI fonts already know how to call. See `docs/font-architecture.md`
for the font side and the root `README.md` for the fork as a whole.

## The translation table

`resources/fonts/translation_zh.json` maps `"block_string"` to Chinese, where `block` is
the three-digit `StringTable` block and `string` the three-digit string number, both
zero-padded — `"001_002"` is block 1, string 2. The file is UTF-8 **with a BOM**, so read
it with `utf-8-sig`.

`GameStrings.GetString` looks the key up and returns the Chinese on a hit. A miss falls
through to the original `StringTable` entry, so an untranslated string still renders in
English and the game stays playable.

The table is keyed on the string the engine actually asks for, so the same line in
different releases is two keys: `001_003` and `001_139` are the same text.

### Deliberately untranslated

- `002_051`–`002_076` — the 26 shrine words. `shrine.cs` compares the player's input
  against `GetString(2, 51 + i).ToUpper()`, so translating them would break the shrines.
  `collect_chars.py` keeps them out of the subset for the same reason.
- Anything the engine compares or parses rather than displays.

## The CJK fallback font

`resources/fonts/WenQuanYiBitmapSong14px.ttf` — WenQuanYi Bitmap Song, 14px cut. It is the
only CJK font that both covers the translation table and rasterises as a bitmap rather
than as greyscale. See `docs/font-architecture.md` for why that matters.

`SysFontBuilder.Populate` adds it to every `.SYS` font's `FontFallback` list and builds a
`FontVariation` over it that carries three things:

1. **The pixel font's metrics.** The fallback's own ascent/descent (1200/-300) would
   double the line height at these sizes, so the variation is pinned to the pixel font's
   line box instead.
2. **A scale chosen so the fallback's hanzi ink matches the pixel font's ink.** The pixel
   fonts draw 4–15 rows of ink inside a line box that is often twice as tall, so a scale
   derived from the line box draws the Chinese 1.6–1.9x too tall. The scale is
   `inkRows * upem / (DesignSize * hanziInkUnits)` — see `FallbackScaleFor`.
3. **Godot's rasterisation parameters** — antialiasing off, hinting off, subpixel
   positioning off — which is what keeps the outlines binary rather than greyscale.

The `.import` file beside the `.ttf` has to agree: `antialiasing=0`, `hinting=0`,
`subpixel_positioning=0`, and `disable_embedded_bitmaps=true`. Godot does not rewrite
those when it re-imports, so edit the `.import` by hand if they ever change.

### Subsetting

The full font is 5.6 MB and 22825 glyphs; the translation table needs 1483 distinct
non-ASCII code points. `subset_font.py` cuts it to those plus ASCII and the punctuation
the UI uses, which lands at 1625 glyphs / 337 KB.

```sh
python i18n/collect_chars.py                          # writes i18n/needed_chars.txt
python i18n/subset_font.py --help
python i18n/font_coverage.py                             # coverage -> build/wqy_coverage.txt
python i18n/translation_coverage.py --untranslated               # per-block translation report
python i18n/measure_ink.py                           # hanzi ink height, in font units
```

`collect_chars.py` reads the translation table, the `.cs` sources and the `.tscn`/`.tres`
files. `measure_ink.py` produces the `HanziInkUnits` constant that
`SysFontBuilder.FallbackScaleFor` is built on — the two must stay in step if the fallback
font is ever replaced.

## Status

Numbers below are from `translation_coverage.py` over `translation_zh.json`: **7091 keys, 1872 with
Chinese (26.4%), 27 empty values, 1483 distinct non-ASCII code points.**

| Group | Keys | Translated |
|---|---|---|
| `001`–`010` — UI, items, misc | 1778 | 1750 (98.4%) |
| `3072`–`3105` — subtitles | 122 | 122 (100%) |
| `024` — debug strings | 101 | 0 |
| `3585`–`3999` — NPC dialogue | 5090 | 0 |

Translated: the whole of `001` except `001_000` (`"Hey, its all the game strings"`, a
placeholder the engine never asks for) and `001_170` (`"……"`); all of `003`–`010`; the
subtitle blocks; and `002` apart from the shrine words.

Not translated: `024` entirely, and all 5090 NPC dialogue lines in `3585`–`3999`.

Terminology is not yet consistent — 地精 and 哥布林 are both in use for "goblin" and one
has to be picked.

## Verifying the font

`tests/godot/ZhFontProbe.cs` renders all four `.SYS` fonts with their Chinese fallback,
prints the ink-derived scale, the resulting em size, the pixel font's ink height and the
fallback's hanzi ink height -- which must agree -- and counts pixels that came out at an
intermediate level, which must be zero. It needs a real rendering driver, so it cannot run
headless:

```sh
Godot_v4.3-stable_mono_win64.exe --path . res://tests/godot/zh_font_probe.tscn
```

`UW_DATA` and `UW_PROBE_OUT` override the data and output directories.
