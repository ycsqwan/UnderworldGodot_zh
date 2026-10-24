# i18n tooling

Helper scripts for the Chinese localization. Nothing here runs inside the game;
these are build-time tools.

## Font subsetting

`resources/fonts/SourceHanSansCN-Regular.otf` is the full Source Han Sans CN
(8.4 MB, 30926 glyphs). The shipped build only needs the characters that
actually appear in the translated text, so it is subset down to roughly 0.4 MB:

```bash
pip install fonttools

python i18n/collect_chars.py                       # -> i18n/needed_chars.txt
python i18n/subset_font.py i18n/needed_chars.txt \
    resources/fonts/SourceHanSansCN-Regular.otf \
    build/SourceHanSansCN-Subset.otf
```

`collect_chars.py` gathers every non-ASCII character from the translation table
plus any Chinese hardcoded in `.cs`/`.tscn`/`.tres`/`.gd` files, then adds a
small margin of punctuation ranges. It deliberately does **not** include the
whole CJK Unified Ideographs block — that alone is 20992 code points and would
make the subset as large as the original font.

`subset_font.py` keeps the name table (copyright and the OFL licence text), the
glyph order and the CFF charstrings, and verifies afterwards that no code point
the source font actually covers was lost. It exits non-zero if the subset is
incomplete.

Re-run both whenever `translation_zh.json` grows. A character that shows up in
a translation added after the last subsetting run will render as a tofu box,
because the fallback font has no glyph for it.

The full font stays tracked in the repository because the remaining strings are
still being translated; once translation is complete it can be replaced by the
subset (and the history rewritten) to shrink the repository.

## Runtime wiring

The translation table and the Chinese font are read from the folder that holds
`Underworld.exe`, not from inside the `.pck`. That keeps the patch package free
of a multi-megabyte replacement pack and lets users swap either file.

| File | Looked up as | Fallback |
| --- | --- | --- |
| `uw_zh.txt` | `uw_zh.txt`, `translation_zh.txt` | original English |
| Chinese font | `SourceHanSansCN-Subset.otf`, `SourceHanSansCN-Regular.otf` | no fallback attached |

Both lookups can be overridden with environment variables:

* `UW_ZH_TSV` – absolute path to the translation table
* `UW_ZH_FONT` – absolute path to the Chinese font
