using System;
using System.Collections.Generic;
using System.IO;

using Godot;

using Underworld;

/// <summary>
/// Throwaway verification probe.
///
/// Renders every placeholder font through the PRODUCTION path — SysFontProvider load,
/// SysFontBuilder.Build, SysFontBuilder.Populate — which is what UnderworldRoot does at
/// boot, so the Chinese fallback is checked in the fonts it will actually appear next to
/// rather than in isolation.
///
/// For each font it writes zh_&lt;FONT&gt;.png and reports four things:
///
///   ink@64      the pixel font's ink height at font size 64, from its row count
///   hanzi@64    the fallback's median hanzi ink height, measured off the glyphs
///   adv@64      the fallback's hanzi advance, measured by shaping ten of them
///   mid%        pixels that came out at an intermediate alpha level
///
/// adv must track ink. This is the regression that made the message log unreadable: a
/// FontVariation transform scaled the outlines but not the advances, so hanzi drew at 24px
/// of ink inside a 59.7px cell and Chinese read as one character per two. The check below
/// fails the probe if the advance drifts outside a narrow band around the ink.
///
/// mid must be zero. A bitmap font rasterised with antialiasing off produces purely binary
/// alpha, so anything above zero means FreeType smoothed the outlines and the blocky look
/// is gone.
///
/// Run it with a real rendering driver, because headless returns dummy values:
///
///     Godot_v4.3-stable_mono_win64.exe --path . res://tests/godot/zh_font_probe.tscn
///
/// Data and output directories default to the layout this was written against; override
/// them with UW_DATA and UW_PROBE_OUT.
/// </summary>
public partial class ZhFontProbe : Control
{
	// Godot's OS.GetEnvironment returns "" for an unset variable, not null, so these cannot
	// use the ?? operator.
	private static string EnvOr(string name, string fallback)
	{
		string value = OS.GetEnvironment(name);
		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}

	private static readonly string DataDir =
		EnvOr("UW_DATA", @"D:\uuchinese\UW1\UW\DATA");
	private static readonly string OutDir =
		EnvOr("UW_PROBE_OUT", @"D:\uuchinese\zh_probe");

	private static readonly string[] FontOrder =
	{
		"FONT4X5P", "FONT5X6I", "FONT5X6P", "FONTBIG",
	};

	/// <summary>The sizes the UI actually asks for. All are multiples of DesignSize, so
	/// the .SYS bitmaps rasterise at whole pixels; the fallback should too.</summary>
	private static readonly int[] Sizes = { 32, 48, 64 };

	/// <summary>The size the message log uses, and the one that exposed the advance bug.</summary>
	private const int ReferenceSize = 64;

	/// <summary>Ten hanzi, so shaping divides out side bearings instead of measuring one.</summary>
	private const string HanziRun = "深深深深深深深深深深";

	private const string Text = "深渊的加拉蒙说: Avatar, 你听见了吗? 12345";

	private int _failures;

	public override void _Ready()
	{
		Callable.From(RunAll).CallDeferred();
	}

	private async void RunAll()
	{
		try
		{
			if (!SysFontProvider.TryLoadAll(DataDir, out var fonts, out string err))
			{
				GD.PushError("[ZhFontProbe] load failed: " + err);
				GetTree().Quit(1);
				return;
			}
			GD.Print("[ZhFontProbe] loaded " + fonts.Count + " .SYS fonts");

			Directory.CreateDirectory(OutDir);

			foreach (string placeholder in FontOrder)
			{
				if (!fonts.TryGetValue(placeholder, out SysFont sys))
				{
					GD.PushError("[ZhFontProbe] " + placeholder + " did not load");
					_failures++;
					continue;
				}
				await RenderFont(placeholder, sys);
			}

			if (_failures > 0)
			{
				GD.PushError("[ZhFontProbe] " + _failures + " check(s) failed");
				GetTree().Quit(1);
				return;
			}

			GD.Print("[ZhFontProbe] all checks passed");
			GetTree().Quit(0);
		}
		catch (Exception ex)
		{
			GD.PrintErr(ex.ToString());
			GetTree().Quit(1);
		}
	}

	private async System.Threading.Tasks.Task RenderFont(string placeholder, SysFont sys)
	{
		var (ascent, descent) = SysFontProvider.LegacyMetricsFor(placeholder);
		var font = SysFontBuilder.Build(sys, ascent, descent);

		float pixelInk = sys.Height * (float)ReferenceSize / SysFontBuilder.DesignSize;
		float adv = font.GetStringSize(HanziRun, HorizontalAlignment.Left, -1, ReferenceSize).X
			/ HanziRun.Length;

		GD.Print(string.Format(
			"[ZhFontProbe] {0}: rows={1} ascent={2} descent={3} pixel-ink@64={4:0.###}px adv@64={5:0.###}px",
			placeholder, sys.Height, ascent, descent, pixelInk, adv));

		// The advance has to sit just above the ink: a hair wider so hanzi do not touch, but
		// nowhere near the ~2.5x the broken transform produced.
		float low = pixelInk * 0.98f;
		float high = pixelInk * 1.25f;
		if (adv < low || adv > high)
		{
			GD.PushError(string.Format(
				"[ZhFontProbe] {0}: hanzi advance {1:0.###}px at size {2} is outside " +
				"{3:0.###}-{4:0.###}px for {5:0.###}px of ink; " +
				"the baked fallback is wrong or missing",
				placeholder, adv, ReferenceSize, low, high, pixelInk));
			_failures++;
		}

		var layer = new Control();
		layer.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(layer);

		var bg = new ColorRect { Color = Colors.Black };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		layer.AddChild(bg);

		float y = 10f;
		foreach (int size in Sizes)
		{
			var label = new Label
			{
				Text = "[" + size + "] " + Text,
				Position = new Vector2(10f, y),
			};
			label.AddThemeFontOverride("font", font);
			label.AddThemeFontSizeOverride("font_size", size);
			layer.AddChild(label);
			y += size + 8f;
		}

		// Let the labels lay out and the viewport present before reading pixels back.
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var img = GetViewport().GetTexture().GetImage();
		if (img == null)
		{
			GD.PushError("[ZhFontProbe] " + placeholder + ": viewport readback returned nothing");
			layer.Free();
			_failures++;
			return;
		}

		int ink = 0;
		int mid = 0;
		int w = img.GetWidth();
		int h = img.GetHeight();
		for (int py = 0; py < h; py++)
		{
			for (int px = 0; px < w; px++)
			{
				float v = img.GetPixel(px, py).R;
				if (v > 0.5f)
				{
					ink++;
				}
				else if (v > 0.0f)
				{
					mid++;
				}
			}
		}

		string path = Path.Combine(OutDir, "zh_" + placeholder + ".png");
		img.SavePng(path);
		GD.Print(string.Format(
			"[ZhFontProbe]   wrote {0}  ink={1} mid={2} mid%={3:0.###}",
			path, ink, mid, 100.0 * mid / Math.Max(ink, 1)));

		if (mid > 0)
		{
			GD.PushError(string.Format(
				"[ZhFontProbe] {0}: {1} pixels are antialiased; the bitmap look is gone",
				placeholder, mid));
			_failures++;
		}

		layer.Free();
	}
}
