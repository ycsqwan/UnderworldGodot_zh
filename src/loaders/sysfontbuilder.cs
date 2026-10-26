using Godot;

namespace Underworld
{
    /// <summary>
    /// Turns a parsed SysFont into a Godot FontFile.
    ///
    /// Kept apart from SysFont so the parser stays Godot-free and testable in the
    /// headless suite. Everything here needs the engine, so it is covered by the
    /// render test in tests/godot instead.
    /// </summary>
    public static class SysFontBuilder
    {
        /// <summary>
        /// The design size the committed TTFs behave as: 1024 units per em at 64
        /// units per source pixel. A font size of S therefore renders S/16 source
        /// pixels. Using the .SYS glyph height here instead makes every size wrong.
        /// </summary>
        public const int DesignSize = 16;

        /// <summary>
        /// WenQuanYi Bitmap Song 14px has 1500 units of em and its hanzi fill 1300 of them:
        /// the median outline bbox height over the 1483 code points the translation table
        /// uses, measured by i18n/measure_ink.py. Keep these in step if the fallback font is
        /// ever replaced.
        /// </summary>
        public const int Upem = 1500;

        public const int HanziInkUnits = 1300;

        public static FontFile Build(SysFont font, float ascent, float descent)
        {
            var f = new FontFile();
            Populate(f, font, ascent, descent);
            return f;
        }

        /// <summary>
        /// Fills an existing FontFile. The scene holds references to four placeholder
        /// instances, and the switch-over fills those rather than replacing them, because
        /// every theme override already points at them.
        /// </summary>
        /// <param name="ascent">
        /// Rows from the top of the line box to the baseline, in design-size units. NOT
        /// derived from the font: the .SYS header has no baseline field, so the caller
        /// supplies it. See SysFontProvider.LegacyMetricsFor for why the values are what
        /// they are.
        /// </param>
        /// <param name="descent">Rows below the baseline, same units.</param>
        /// <summary>
        /// The scale that makes the Chinese fallback's hanzi ink match the pixel font's ink.
        ///
        /// The pixel font draws inkRows rows at size/DesignSize pixels per row, so its ink is
        /// inkRows * size / DesignSize tall. The fallback is drawn at fallbackScale * size pixels
        /// of em, and its hanzi fill only HanziInkUnits/Upem of that, so its ink is
        /// fallbackScale * size * HanziInkUnits / Upem tall. Setting the two equal gives
        ///
        ///     fallbackScale = inkRows * Upem / (DesignSize * HanziInkUnits)
        ///
        /// The TextServer override further down pins the fallback's ascent/descent to the pixel
        /// font's line box, so this scale never has to make the fallback fit that box; it only
        /// decides how tall the hanzi sit next to the English. A FontVariation's transform scales
        /// outlines only and never metrics, so the two concerns are independent.
        ///
        /// An earlier revision derived this from the line box instead, (ascent + descent) /
        /// DesignSize, which is wrong for the three small fonts: their line box is 12 design units
        /// but their ink is only 4-7 rows, so the rule drew the Chinese 1.6-1.9x taller than the
        /// English. FONTBIG is the one case where the two rules agree, because its line box
        /// (15 units) equals its ink (15 rows).
        /// </summary>
        public static float FallbackScaleFor(int inkRows)
        {
            return inkRows * Upem / (float)(DesignSize * HanziInkUnits);
        }

        public static void Populate(FontFile f, SysFont font, float ascent, float descent, float fallbackScale = -1f)
        {
            if (fallbackScale < 0f)
            {
                fallbackScale = FallbackScaleFor(font.Height);
            }

            int cell = font.CellWidth;
            var img = Image.CreateEmpty(cell * font.GlyphCount, font.Height, false, Image.Format.Rgba8);
            img.Fill(new Color(0, 0, 0, 0));

            // Vertical metrics come from the caller, not from the glyph height. Deriving
            // them from Height was wrong and shipped a real regression: it gave FONT5X6I a
            // line box of 28 screen pixels at font size 64 where the converted TTF gave 47,
            // moving every baseline up by 19 pixels and making multi-line labels drift.
            f.SetCacheAscent(0, DesignSize, ascent);
            f.SetCacheDescent(0, DesignSize, descent);

            var key = new Vector2I(DesignSize, 0);

            for (int c = 0; c < font.GlyphCount; c++)
            {
                for (int r = 0; r < font.Height; r++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        if (font.PixelAt(c, r, x))
                        {
                            img.SetPixel(c * cell + x, r, new Color(1, 1, 1, 1));
                        }
                    }
                }

                // Advance from the declared width byte, never from the ink.
                f.SetGlyphAdvance(0, DesignSize, c, new Vector2(font.AdvanceOf(c), 0));
                // The glyph's bottom row sits `descent` rows below the baseline, so its top
                // belongs Height - descent above it. The old -(Height - 1) assumed exactly
                // one descender row: true for the three small fonts, wrong for FONTBIG,
                // which has four and rendered 3 source pixels (12 at size 64) too high on
                // save slot labels, chargen, subtitles and the automap number.
                f.SetGlyphOffset(0, key, c, new Vector2(0, -(font.Height - descent)));
                f.SetGlyphSize(0, key, c, new Vector2(cell, font.Height));
                f.SetGlyphUVRect(0, key, c, new Rect2(c * cell, 0, cell, font.Height));
                f.SetGlyphTextureIdx(0, key, c, 0);
            }

            f.SetTextureImage(0, key, 0, img);
            f.FixedSize = DesignSize;
            f.FixedSizeScaleMode = TextServer.FixedSizeScaleMode.Enabled;

            // A gap must be a visible gap, not a silent substitution from a system
            // font, or both the validation and the render test's oracle are useless.
            f.AllowSystemFallback = false;

            // Add Chinese fallback font for translation support.
            //
            // WenQuanYi Bitmap Song is a bitmap font: its outlines are staircases on a
            // 1500-unit grid that maps to a 15px em, so at an integer multiple of 15px it
            // rasterises to hard blocks like the .SYS fonts it sits next to. Source Han Sans
            // is an outline font and can only ever look smooth, which is why it read as a
            // different medium rather than a translation of the same one.
            string fallbackPath = "res://resources/fonts/WenQuanYiBitmapSong14px.ttf";
            if (Godot.ResourceLoader.Exists(fallbackPath))
            {
                var fallbackFont = Godot.ResourceLoader.Load<Godot.Font>(fallbackPath);
                if (fallbackFont != null)
                {
                    // The blocky look survives only if FreeType is forbidden from resampling
                    // the outlines. Gray antialiasing turns each step into a gray ramp, and
                    // hinting/subpixel positioning nudge the grid off whole pixels; both
                    // destroy the effect even though the outlines themselves are unchanged.
                    // The .import file sets the same values, so this is a belt-and-braces
                    // copy rather than the only line of defence.
                    if (fallbackFont is FontFile fallbackFile)
                    {
                        fallbackFile.Antialiasing = TextServer.FontAntialiasing.None;
                        fallbackFile.Hinting = TextServer.Hinting.None;
                        fallbackFile.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
                    }

                    // Wrap in FontVariation to shrink outline glyphs so they
                    // visually match the pixel font (~16 units). At default
                    // size Chinese dwarfs English and breaks log rendering.
                    var variation = new FontVariation();
                    variation.SetBaseFont(fallbackFont);
                    variation.SetVariationTransform(Transform2D.Identity.Scaled(new Vector2(fallbackScale, fallbackScale)));

                    // The transform scales outlines, not metrics. Source Han Sans declares a
                    // ~1.45em line box against these fonts' 12/16em, and Godot sizes a line
                    // from the tallest font on it, so every line holding a Chinese glyph came
                    // out ~2x too tall. At font size 64 the message log is 140px and should
                    // show all five of its buffered lines; only one fitted, the newest fell
                    // out of the box, and the log read as lagging several messages behind.
                    // Shrinking fallbackScale never helped, because it never touched metrics.
                    // FontVariation exposes no metric override and ignores the base FontFile's
                    // cache ascent, so the only lever is the TextServer, which keys ascent by
                    // the requested size. Lay the override down per size; past the range a
                    // font simply keeps its natural metrics.
                    const int MinScaledSize = 4;
                    const int MaxScaledSize = 256;
                    TextServer textServer = TextServerManager.GetPrimaryInterface();
                    foreach (Rid rid in variation.GetRids())
                    {
                        for (int size = MinScaledSize; size <= MaxScaledSize; size++)
                        {
                            textServer.FontSetAscent(rid, size, ascent * size / DesignSize);
                            textServer.FontSetDescent(rid, size, descent * size / DesignSize);
                        }
                    }

                    f.Fallbacks = new Godot.Collections.Array<Godot.Font> { variation };
                }
            }
        }
    }
}
