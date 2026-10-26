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
/// For each font it writes zh_<FONT>.png and reports the ink-derived fallback scale, the
/// resulting em size, the pixel font's ink height and the fallback's hanzi ink height --
/// those last two must agree -- and how many pixels came out at an intermediate level. A
/// bitmap font rasterised with antialiasing off must produce a purely binary alpha, so the
/// mid-level count is the signal: anything above zero means FreeType smoothed the
/// outlines and the blocky look is gone.
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
	private static readonly string DataDir =
		OS.GetEnvironment("UW_DATA") ?? @"D:\uuchinese\UW1\UW\DATA";
	private static readonly string OutDir =
		OS.GetEnvironment("UW_PROBE_OUT") ?? @"D:\uuchinese\zh_probe";

	private static readonly string[] FontOrder =
	{
		"FONT4X5P", "FONT5X6I", "FONT5X6P", "FONTBIG",
	};

	/// <summary>The sizes the UI actually asks for. All are multiples of DesignSize, so
	/// the .SYS bitmaps rasterise at whole pixels; the fallback should too.</summary>
	private static readonly int[] Sizes = { 32, 48, 64 };

	private const string Text = "深渊的加拉蒙说: Avatar, 你听见了吗? 12345";

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
					continue;
				}
				await RenderFont(placeholder, sys);
			}

			await ScaleSweep(fonts);

			GetTree().Quit(0);
		}
		catch (Exception ex)
		{
			GD.PrintErr(ex.ToString());
			GetTree().Quit(1);
		}
	}

	/// <summary>
	/// Renders the 6-row FONT5X6P at a range of fallback scales so the trade-off can be
	/// seen rather than argued about.
	///
	/// Filling the line box (0.75) leaves the Chinese roughly twice as tall as the English,
	/// because the line box is 12 design units but the ink is only 6 rows. Matching the ink
	/// instead is what SysFontBuilder.FallbackScaleFor now does, and it lands on
	/// 6 * 1500 / (16 * 1300) = 0.4327.
	/// </summary>
	private async System.Threading.Tasks.Task ScaleSweep(Dictionary<string, SysFont> fonts)
	{
		var sys = fonts["FONT5X6P"];
		var (ascent, descent) = SysFontProvider.LegacyMetricsFor("FONT5X6P");

		float[] scales = { 0.75f, 0.60f, 0.50f, 0.433f };
		int size = 64;

		var layer = new Control();
		layer.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(layer);

		var bg = new ColorRect { Color = Colors.Black };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		layer.AddChild(bg);

		float y = 10f;
		foreach (float scale in scales)
		{
			var font = SysFontBuilder.Build(sys, ascent, descent);
			SysFontBuilder.Populate(font, sys, ascent, descent, scale);

			var label = new Label
			{
				Text = string.Format("[{0:0.###}] {1}", scale, Text),
				Position = new Vector2(10f, y),
			};
			label.AddThemeFontOverride("font", font);
			label.AddThemeFontSizeOverride("font_size", size);
			layer.AddChild(label);
			y += size + 8f;
		}

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var img = GetViewport().GetTexture().GetImage();
		if (img != null)
		{
			img.SavePng(Path.Combine(OutDir, "zh_FONT5X6P_sweep.png"));
			GD.Print("[ZhFontProbe] wrote zh_FONT5X6P_sweep.png");
		}
		layer.Free();
	}

	private async System.Threading.Tasks.Task RenderFont(string placeholder, SysFont sys)
	{
		var (ascent, descent) = SysFontProvider.LegacyMetricsFor(placeholder);
		float scale = SysFontBuilder.FallbackScaleFor(sys.Height);
		var font = SysFontBuilder.Build(sys, ascent, descent);
		SysFontBuilder.Populate(font, sys, ascent, descent, scale);

		GD.Print(string.Format(
			"[ZhFontProbe] {0}: rows={1} ascent={2} descent={3} scale={4:0.####} em@64={5:0.###}px pixel-ink@64={6:0.###}px hanzi-ink@64={7:0.###}px",
			placeholder, sys.Height, ascent, descent, scale, scale * 64f,
			sys.Height * 64f / SysFontBuilder.DesignSize,
			scale * 64f * SysFontBuilder.HanziInkUnits / SysFontBuilder.Upem));

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

		layer.Free();
	}
}
