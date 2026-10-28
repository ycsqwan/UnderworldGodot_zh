# Ultima Underworld I & II — 中文化分支（实验性）

> 引擎本身的原版说明见 [README.md](README.md)。本文件只讲中文化。

《Ultima Underworld》是 Origin Systems 于 1992–1993 年发行的 3D 第一人称角色扮演游戏，
玩家以第一人称视角深入地底探索，画面由引擎实时重制。
本仓库是 [hankmorgan/UnderworldGodot](https://github.com/hankmorgan/UnderworldGodot) 的中文化分支，
把引擎读取的英文文本替换为简体中文。

**不修改任何原始游戏文件。** 本分支只新增 `resources/fonts/` 下的译表与字体，
外加少量 UI 适配代码（译表选择、换行）。游戏逻辑与关卡数据一行未改。

> ⚠️ **本分支处于实验阶段。** 译表由脚本生成并经人工抽检，
> 但游戏内并非每一条文本都逐条实测过。若遇到乱码、错位或漏译，
> 请在本仓库提 issue 并附上对应的英文原文。

---

## 现状

| 项目 | 内容 |
|---|---|
| 引擎 | UnderworldGodot（Godot 4.3 重制，MIT） |
| 上游 | [hankmorgan/UnderworldGodot](https://github.com/hankmorgan/UnderworldGodot) |
| 分支 | `chinese-i18n` |
| 远端 | [ycsqwan/UnderworldGodot_zh](https://github.com/ycsqwan/UnderworldGodot_zh) |
| UW1 译表 | `resources/fonts/translation_zh.json` — 7091 键，7031 条中文 |
| UW2 译表 | `resources/fonts/translation_zh_uw2.json` — 9765 键，9686 条中文 |
| 字体 | 文泉驿点阵宋体 14px，4 份烘焙 TTF |
| 换行 | 按像素宽度 + CJK 逐字断点 |

**两张译表是分开的。** UW1 与 UW2 共用键空间但内容不同
（例如 UW1 的 `001_260` 与 UW2 的 `001_276` 都是 `"You see "`，但周边条目错开）。
引擎按当前加载的游戏自动选择对应的表，互不干扰。

---

## 画面示例

<p align="center">
  <img src="docs/images/uw2-dialogue.png" width="600" />
</p>

<p align="center">
  <img src="docs/images/uw2-log.png" width="600" />
</p>

<p align="center">
  <img src="docs/images/uw2-inventory.png" width="600" />
</p>

---

## 运行

中文化不需要额外步骤，直接运行引擎即可：

1. 准备游戏数据：把 UW1 的 `DATA` 目录与 UW2 的 `DATA` 目录放在任意位置。
2. 编辑引擎设置文件 `%APPDATA%\godot\app_userdata\Underworld\settings.json`，
   填入两个数据路径与要加载的游戏：

   ```json
   {
     "pathuw1": "D:\\games\\uw1\\UW",
     "pathuw2": "D:\\games\\uw2\\UW2",
     "gametoload": "UW2",
     "level": 0
   }
   ```

3. 用 Godot 4.3 运行项目：

   ```bash
   Godot_v4.3-stable_mono_win64.exe --path .
   ```

引擎启动时按 `gametoload` 选择译表：`UW1` → `translation_zh.json`，
`UW2` → `translation_zh_uw2.json`。译表缺失时回落英文，不会报错。

---

## 构建

```bash
# 生成 4 份烘焙字体（覆盖 resources/fonts/WenQuanYiBitmapSong14px-ink*.ttf）
python i18n/build_fallback_fonts.py

# 用 Godot 4.3 编辑器导出，或用命令行导出
Godot_v4.3-stable_mono_win64.exe --path . --export-release "Windows Desktop" build/underworld.exe
```

字体烘焙脚本读取 `resources/fonts/WenQuanYiBitmapSong14px.ttf`，
按目标墨迹行数把缩放烘进 `unitsPerEm`，输出 4 份 TTF。
详见 [i18n/README.md](i18n/README.md) 与 [docs/font-architecture.md](docs/font-architecture.md)。

---

## 工作原理

**译表。** `resources/fonts/translation_zh.json`（UW1）与
`resources/fonts/translation_zh_uw2.json`（UW2）是扁平的 `{键: 中文}` 映射。
键为 `区块_序号`（如 `001_276`）。查不到时回落英文原文。

**译表选择。** `src/ui/UnderworldRoot.cs` 按 `UWClass._RES` 决定加载哪张表。

**字体。** `src/loaders/sysfontbuilder.cs` 为 4 个占位字体
（`FONT4X5P` / `FONT5X6I` / `FONT5X6P` / `FONTBIG`）各选一份烘焙 TTF，
按墨迹行数匹配。缩放直接烘在字体的 `unitsPerEm` 里，不再运行时变换轮廓。

**换行。** `src/ui/MessageDisplay.cs` 原先按空格断词，中文整句无空格时永不换行。
现改为按像素宽度断行，并在 CJK 字符间允许逐字断开。

---

## 已知问题

**引擎中仍有 10 处硬编码英文串**，它们不读字符串表，译表修不到，
因此游戏内会零星出现英文（例如 `You see a ... named ...`）：

| 位置 | 硬编码串 |
|---|---|
| `src/npc/npc.cs:239,259,293,297,302` | `You see {name}`、`You see a {mood} {race} named {name}` |
| `src/interaction/look.cs:239` | `You see ` |
| `src/magic/runicmagic.cs:383` | `Not a spell.` |
| `src/interaction/pickup.cs:275`、`src/ui/uimanager_inventory.cs:841` | `Move how many?` |
| `src/objects/shrine.cs:132` | `Chant the mantra` |
| `src/objects/explodingbook.cs:13` | `The book explodes in your face!` |
| `src/audio/MusicStreamPlayer.cs:124` | `Music Producer` |
| `src/loaders/modelloader.cs:80` | Lotus 彩蛋 |
| `src/ui/LaunchMenu.cs:300,307` | 两处错误提示 |

其中只有 `You see ` 在字符串表里有对应键（UW1 `001_260` / UW2 `001_276`，两表均已译），
其余在 `STRINGS.PAK` 中不存在，需要新增中文片段映射才能修复。

**其他：**

- **两表均已基本覆盖** — UW1 为 7031/7091，UW2 为 9686/9765。剩余键全部是刻意保留：
  UW1 的 26 条真言（`002_051`–`002_076`，引擎逐字比对）、`024` 区块的调试串与
  格式串、纯控制码条目，运行时回落英文原文。
- **UW2 咒词保留英文** — 键 `002_071`–`002_076`（INSAHN / FANLO / NO / SUMM RA /
  MU AHM / OM CAH）故意不译，运行时回落英文原文。这是设计如此：
  `src/objects/shrine.cs:155` 用 `GetString(2, 51 + mIndex)` 比对咒词，
  没有 UW2 分支，UW2 神殿咒语比对读的是技能名，本来就是坏的。
- **引擎既有 bug（与翻译无关）** — 上述 `shrine.cs` 的咒词偏移问题，
  本分支未改动。

---

## 字体与许可

本分支的许可分四类：

### 代码

MIT，Copyright 2023 hankmorgan（上游引擎）与本分支作者。
见 [LICENSE.md](LICENSE.md)。MIT 只覆盖代码，不覆盖字体与游戏资产。

### 译文

译表 `translation_zh.json` / `translation_zh_uw2.json` 以
**CC BY-NC-SA 4.0** 发布。译表中的翻译贡献部分归译者所有；
机器生成的部分不主张额外权利。转载或再发布请署名并以相同方式共享。

### 字体

运行时字体为**文泉驿点阵宋体 14px**（`WenQuanYi Bitmap Song 14px`，
v1.0.0-RC1，由 AmusementClub 从 PCF 转换）：

- 许可：**GPL-2.0 + font embedding exception**（不是 OFL）。
  全文见 [resources/fonts/WenQuanYi-LICENSE.txt](resources/fonts/WenQuanYi-LICENSE.txt)。
- 版权：Copyright (c) 2004-2010 The WenQuanYi Project Board of Trustees and Qianqian Fang。
  原始点阵字形由 firefly 制作，上游项目为
  [AmusementClub/Wenquanyi-Bitmap-Song-TTF](https://github.com/AmusementClub/Wenquanyi-Bitmap-Song-TTF)。

**字体不内嵌在引擎可执行文件里**：引擎只引用 `resources/fonts/` 下的 TTF 文件，
由 Godot 在运行时从磁盘加载。因此代码（MIT）与字体（GPL-2.0）是两件独立的作品，
聚合分发不会使代码落入 GPL。`i18n/build_fallback_fonts.py` 生成的 4 份烘焙 TTF
是对该字体的修改，同样以 GPL-2.0 + font embedding exception 发布。

### 其他

`docs/images/` 下的游戏截图仅用于展示中文化效果，
是版权持有方的资产，不属于本分支。

---

## 致谢

- **引擎** — [hankmorgan/UnderworldGodot](https://github.com/hankmorgan/UnderworldGodot)
- **字体** — 文泉驿（WenQuanYi）点阵字形，firefly 制作；
  TTF 版本由 [AmusementClub](https://github.com/AmusementClub/Wenquanyi-Bitmap-Song-TTF) 转换
- **原始游戏** — Origin Systems 的 *Ultima Underworld I & II*
