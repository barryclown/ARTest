"""萬年龜四種體型的配色貼圖：只取原本手繪貼圖的明暗當細節，每個部位照設計好的色票重新上色。

python Tools/generate_turtle_skins.py
→ Assets/Art/Models/TurtleSkins/<type>_shell.png、<type>_skin.png、<type>_shell_emission.png
（原貼圖不動。眼睛、腳爪用純色材質＋程式產生的虹膜／瞳孔，色票在 Assets/Editor/ShowcaseSceneSetup.cs 的 TurtleTypes）

做法：原貼圖轉成明度 → 三段漸層（暗部／中間調／亮部）上色；原本的黃色點綴另外偵測出來換成各種類的點綴色，
黑龜精的餘燼紋與疾龜的電光紋另外輸出自發光遮罩。
  young 幼龜（小、0.9 m/s）：嫩玉綠，柔黃點綴
  tide  潮龜（中、0.6 m/s）：深海藍，浪花白點綴
  elder 黑龜精（大、0.35 m/s）：墨玉黑，發光的餘燼橙紋
  swift 疾龜（最小、1.3 m/s）：電光紫，發光的青色紋
敵人一律走冷色，點綴避開神碑的金（色相 45–50°）與受擊的紅。
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "Assets" / "Art" / "Models"
OUT = SRC / "TurtleSkins"


def hx(s: str) -> np.ndarray:
    return np.array([int(s[i:i + 2], 16) for i in (1, 3, 5)], dtype=np.float64) / 255


# 每種：殼三段漸層、皮膚三段漸層、點綴色、點綴是否發光
TYPES = {
    "young": {"shell": ("#1F5E4E", "#4FB38E", "#C4F2DA"), "skin": ("#3E7A5C", "#86C99A", "#DDF6E3"), "accent": "#F2D06B", "glow": 0.0},
    "tide":  {"shell": ("#0C2B45", "#2A78A3", "#A6DCEE"), "skin": ("#27566A", "#5C9FB5", "#C4E8F0"), "accent": "#EAF7FB", "glow": 0.0},
    "elder": {"shell": ("#0A1311", "#1F3B34", "#5A7D72"), "skin": ("#161F1D", "#34473F", "#76897F"), "accent": "#FF6A1F", "glow": 1.0},
    "swift": {"shell": ("#1E0E3A", "#643ABC", "#CDB9FF"), "skin": ("#35275E", "#7864B4", "#D9CFF6"), "accent": "#52F2FF", "glow": 0.6},
}
SOURCES = {"shell": "Sphere Normal.png", "skin": "Sphere.001 Normal.png"}


def hue_sat(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.zeros_like(mx)
    m = d > 1e-6
    rm = m & (mx == r)
    gm = m & (mx == g) & ~rm
    bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6
    h[gm] = (b - r)[gm] / d[gm] + 2
    h[bm] = (r - g)[bm] / d[bm] + 4
    return h * 60, np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)


def gradient(t: np.ndarray, stops: tuple[str, str, str]) -> np.ndarray:
    a, b, c = (hx(s) for s in stops)
    t = np.clip(t, 0, 1)[..., None]
    lo = a + (b - a) * np.clip(t / 0.55, 0, 1)
    hi = b + (c - b) * np.clip((t - 0.55) / 0.45, 0, 1)
    return np.where(t < 0.55, lo, hi)


def paint(src: np.ndarray, stops, accent: str) -> tuple[np.ndarray, np.ndarray]:
    lum = src @ np.array([0.3, 0.59, 0.11])
    # 明度中位數（大面積的底色）對到中間調，較暗的斑點／紋路落在暗部，只有少數亮處用到亮部
    med = np.percentile(lum, 50)
    # 拉伸範圍至少 ±0.3，避免大片同色的貼圖被過度拉亮／拉暗
    lo = min(np.percentile(lum, 2), med - 0.3)
    hi = max(np.percentile(lum, 99.5), med + 0.3)
    t = np.where(lum < med,
                 0.55 * (lum - lo) / max(med - lo, 1e-6),
                 0.55 + 0.45 * (lum - med) / max(hi - med, 1e-6))
    rgb = gradient(t, stops)
    # 原本的黃色點綴（色相 25–95°、有飽和度）
    h, s = hue_sat(src)
    acc = np.clip((h - 20) / 10, 0, 1) * np.clip((100 - h) / 10, 0, 1) * np.clip((s - 0.25) / 0.2, 0, 1)
    shade = (0.75 + 0.35 * t)[..., None]
    rgb = rgb * (1 - acc[..., None]) + np.clip(hx(accent) * shade, 0, 1) * acc[..., None]
    return rgb, acc


def save(rgb: np.ndarray, path: Path) -> None:
    Image.fromarray((np.clip(rgb, 0, 1) * 255).round().astype(np.uint8), "RGB").save(path)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*_eye*.png"):
        old.unlink()   # 舊版的眼睛貼圖，改用程式產生的虹膜
    for name, spec in TYPES.items():
        for part, file in SOURCES.items():
            src = np.asarray(Image.open(SRC / file).convert("RGB"), dtype=np.float64) / 255
            rgb, acc = paint(src, spec[part], spec["accent"])
            save(rgb, OUT / f"{name}_{part}.png")
            if part == "shell":
                glow = hx(spec["accent"]) * acc[..., None] * spec["glow"]
                save(glow, OUT / f"{name}_shell_emission.png")
        print("wrote", name)


if __name__ == "__main__":
    main()
