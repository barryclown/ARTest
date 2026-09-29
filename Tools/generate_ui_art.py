"""產生 UI 與特效用的程序化貼圖（白色為主，在 Unity 裡用 Image／材質顏色上色）。

python Tools/generate_ui_art.py   →  Assets/Art/UI/*.png
全部以 4 倍超取樣繪製再縮小，邊緣平滑；重跑會覆蓋同名檔案。
"""
from __future__ import annotations

import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.stdout.reconfigure(encoding="utf-8")
OUT = Path(__file__).resolve().parents[1] / "Assets" / "Art" / "UI"
SS = 4  # supersampling


def save(img: Image.Image, name: str) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    img.save(OUT / name)
    print("wrote", name, img.size)


def rounded(size: tuple[int, int], radius: int, outline: int = 0) -> Image.Image:
    w, h = size
    big = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(big)
    d.rounded_rectangle((0, 0, w * SS - 1, h * SS - 1), radius=radius * SS, fill=255)
    if outline:
        o = outline * SS
        d.rounded_rectangle((o, o, w * SS - 1 - o, h * SS - 1 - o), radius=max(1, (radius - outline) * SS), fill=0)
    alpha = big.resize((w, h), Image.LANCZOS)
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    img.putalpha(alpha)
    return img


def radial(size: int) -> np.ndarray:
    c = (size - 1) / 2
    y, x = np.mgrid[0:size, 0:size]
    return np.sqrt((x - c) ** 2 + (y - c) ** 2) / c  # 0 中心 → 1 邊緣


def to_rgba(alpha: np.ndarray, rgb=(255, 255, 255)) -> Image.Image:
    a = np.clip(alpha, 0, 1)
    arr = np.zeros(alpha.shape + (4,), dtype=np.uint8)
    arr[..., 0], arr[..., 1], arr[..., 2] = rgb
    arr[..., 3] = (a * 255).round().astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def main() -> None:
    # 卡片底（9-slice，圓角 36）與金框
    save(rounded((128, 128), 36), "card_fill.png")
    save(rounded((128, 128), 36, outline=4), "card_border.png")
    # 膠囊：按鈕、HUD 底條、提示條
    save(rounded((128, 64), 32), "pill.png")
    save(rounded((128, 64), 32, outline=3), "pill_border.png")

    # 分隔線：中間亮、兩端淡出，中央一顆菱形
    w, h = 512, 16
    x = np.linspace(-1, 1, w)
    line = np.clip(1 - np.abs(x) ** 1.6, 0, 1)
    a = np.zeros((h, w))
    a[7:9, :] = line
    img = to_rgba(a)
    d = ImageDraw.Draw(img)
    cx, cy = w // 2, h // 2
    d.polygon([(cx, cy - 7), (cx + 7, cy), (cx, cy + 7), (cx - 7, cy)], fill=(255, 255, 255, 255))
    save(img, "divider.png")

    # 發光圈（選取框）：亮環＋外暈
    n = 256
    r = radial(n)
    ring = np.exp(-((r - 0.78) / 0.055) ** 2)
    halo = 0.45 * np.exp(-((r - 0.78) / 0.16) ** 2)
    inner = 0.18 * np.clip(1 - r / 0.78, 0, 1) ** 2
    a = np.clip(ring + halo + inner, 0, 1) * (r <= 1)
    save(to_rgba(a, (255, 214, 110)), "glow_ring.png")

    # 神碑結界：雙環＋12 個符點＋內部淡光（白色，材質上色）
    n = 512
    r = radial(n)
    y, xg = np.mgrid[0:n, 0:n]
    c = (n - 1) / 2
    ang = np.arctan2(yg := (y - c), xg - c)
    outer = np.exp(-((r - 0.9) / 0.018) ** 2)
    inner_ring = 0.7 * np.exp(-((r - 0.72) / 0.012) ** 2)
    glow = 0.35 * np.exp(-((r - 0.9) / 0.07) ** 2)
    fill = 0.16 * np.clip(1 - r / 0.9, 0, 1) ** 1.5
    dots = np.zeros_like(r)
    for k in range(12):
        t = k / 12 * 2 * math.pi
        px, py = c + 0.81 * c * math.cos(t), c + 0.81 * c * math.sin(t)
        dd = np.sqrt((xg - px) ** 2 + (yg - py) ** 2) / c
        dots += np.exp(-(dd / (0.04 if k % 3 == 0 else 0.026)) ** 2)
    # 內環上的刻度
    ticks = 0.5 * np.exp(-((r - 0.62) / 0.03) ** 2) * (np.cos(ang * 24) > 0.85)
    a = np.clip(outer + inner_ring + glow + fill + dots + ticks, 0, 1) * (r <= 1)
    save(to_rgba(a), "aura.png")

    # 受擊暈影：中間透明、四周漸濃
    n = 512
    r = radial(n)
    a = np.clip((r - 0.45) / 0.75, 0, 1) ** 1.8
    save(to_rgba(a), "vignette.png")

    # 點擊漣漪（錄影示意用）
    n = 256
    r = radial(n)
    a = np.exp(-((r - 0.8) / 0.06) ** 2) + 0.35 * np.clip(1 - r / 0.5, 0, 1)
    save(to_rgba(np.clip(a, 0, 1) * (r <= 1)), "tap_ripple.png")


if __name__ == "__main__":
    main()
