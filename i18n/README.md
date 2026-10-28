# i18n — CJK font and translation tooling

Tooling that builds the CJK fallback the UI fonts draw their Chinese through, and that
checks the translation tables. The fonts themselves live in
[`../resources/fonts/`](../resources/fonts/); this directory holds the code that produces
and validates them.

## The problem

The engine ships four pixel fonts (`FONT4X5P`, `FONT5X6I`, `FONT5X6P`, `FONTBIG`) that
have no CJK coverage. When a string contains hanzi, `SysFontProvider` falls back to one
of the TTFs in `resources/fonts/`. Matching that fallback to the pixel font used to be
done with a `FontVariation` transform — and that is the bug this directory exists to fix.

A transform scales **outlines only**. It never touches advances. So shrinking the hanzi
to match the English ink height left every hanzi with its full-width advance: 24px of ink
followed by ~36px of empty space, in a log font whose cell is 11px wide. See
[`../docs/font-architecture.md`](../docs/font-architecture.md) for the full history.

## The fix

Subsetting a font is already a build step here, so bake the scale into the font instead:
raise `unitsPerEm` and every glyph — outlines **and** advances — scales with it. The em
is chosen so the hanzi ink lands exactly on the pixel font's ink:

```
hanzi ink (px) = HanziInkUnits / upem * size
pixel ink (px) = Height       / DesignSize * size
=> upem = HanziInkUnits * DesignSize / Height
```

With `HanziInkUnits = 1300` and `DesignSize = 16`:

| ink rows | slot | upem | baked file |
|---|---|---|---|
| 4 | `FONT4X5P` | 5200 | `WenQuanYiBitmapSong14px-ink4.ttf` |
| 6 | `FONT5X6P` | 3467 | `WenQuanYiBitmapSong14px-ink6.ttf` |
| 7 | `FONT5X6I` | 2971 | `WenQuanYiBitmapSong14px-ink7.ttf` |
| 15 | `FONTBIG` | 1387 | `WenQuanYiBitmapSong14px-ink15.ttf` |

The four baked fonts are subsets (2671 glyphs each) of the source font, so they are
small enough to ship while still covering every character the game renders.

## Scripts

| Script | Purpose |
|---|---|
| [`build_fallback_fonts.py`](build_fallback_fonts.py) | **One-stop.** Collects the characters the game renders, subsets the source font to them, rescales `unitsPerEm` per slot, writes the four baked TTFs, and asserts the results. |
| [`font_coverage.py`](font_coverage.py) | Reports which of the game's characters the baked fonts cover. |
| [`check_cjk_fonts.py`](check_cjk_fonts.py) | Verifies the four baked fonts load and rasterise identically to the source. |
| [`measure_ink.py`](measure_ink.py) | Measures the hanzi ink height of a font in font units. |
| [`needed_chars.txt`](needed_chars.txt) | The collected character set, for reference. |
| [`translation_coverage.py`](translation_coverage.py) | Reports translation-table coverage per string block. |
| [`collect_chars.py`](collect_chars.py), [`subset_font.py`](subset_font.py) | **Superseded.** The older two-step pipeline (`collect_chars` → `subset_font`), kept for reference. `build_fallback_fonts.py` does both in one pass. |

### Rebuilding the fonts

```bash
python i18n/build_fallback_fonts.py
```

It prints a table of the numbers the pixel fonts demand and what each baked font
delivers, and fails if any slot's ink misses its target by more than 2% or if advances
come out narrower than the ink. Requires `fontTools`.

## Translation tables

Two tables, one per game, both flat `{key: 中文}` maps keyed `"block_string"`:

| Table | Game | Keys | Translated |
|---|---|---|---|
| `translation_zh.json` | UW1 | 7091 | 7031 |
| `translation_zh_uw2.json` | UW2 | 9765 | 9686 |

The engine picks one based on the game being loaded — the two `STRINGS.PAK` files share
key names but not content, so a single table cannot serve both. `GameStrings.GetString`
falls back to the original English on a miss.

### Things the tables deliberately leave alone

- **Spell words** — keys `002_071`–`002_076` (INSAHN / FANLO / NO / SUMM RA / MU AHM /
  OM CAH) are absent from the UW2 table on purpose, so they fall back to the English
  incantations. `src/objects/shrine.cs` compares against them verbatim.
- **Pure control strings** — entries that contain only escape codes and placeholders
  (`@GS102`, `\1@SS1\0`, `blnkmap.byt`) are passed through unchanged; there is nothing
  to translate.

### Known gaps

- **Both tables are essentially complete.** UW1 is 7031 of 7091 and UW2 is 9686 of
  9765. The leftovers are the deliberate pass-throughs listed above plus the UW1
  spell mantras `002_051`–`002_076`, which are compared verbatim by the engine.
- **Ten hardcoded English strings** in the engine bypass the string table entirely, so
  no table can fix them. They are listed in [`../README.zh-CN.md`](../README.zh-CN.md).

## Terminology

Naming is not centralised; the same word is translated one way in each table. The
decisions below are what the tables follow.

- **The Avatar** — the player character. The tables keep the English "Avatar" where the
  game uses it as a title, and use 「化身」 where it reads as a noun.
- **Runes** — 「符文」. The UW rune stones are 「符文石」; the shrine words are 「咒词」.
- **Mana / health / stamina** — 「法力」/「生命」/「体力」, matching the three bars.
- **The Underworld** — 「地底」 for the place, 「冥界」 reserved for flavour text.
- **Skills** — skill names follow the UW manual's own Chinese names where they exist
  (「潜行」 for Stealth, 「开锁」 for Pick Lock), otherwise a literal rendering.

If you spot an inconsistency, the fix belongs in the table, not in the engine.
