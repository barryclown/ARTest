"""神碑正面的浮雕描金貼圖：碑額雲紋、中央直書「定海神碑」、碑腳海浪紋、雙線邊框。

python Tools/generate_stele_art.py
→ Assets/Art/Stele/stele_face.png（描金顏色＋透明度，材質用 Cutout）
  Assets/Art/Stele/stele_face_normal.png（刻痕法線，讓筆畫邊緣吃光）
字型：霞鶩文楷 TC Medium（SIL OFL 1.1），可用環境變數 STELE_FONT 指定其他字型檔。
字直接畫進貼圖，遊戲裡不需要打包字型檔。
"""
from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.stdout.reconfigure(encoding="utf-8")
OUT = Path(__file__).resolve().parents[1] / "Assets" / "Art" / "Stele"
FONT = Path(os.environ.get("STELE_FONT") or
            Path.home() / "Desktop" / "claude" / "fonts" / "繁中" / "霞鶩文楷TC" / "LXGWWenKaiTC-Medium.ttf")
TITLE = "定海神碑"

W, H = 1024, 1512          # 碑面寬高比 0.63 : 0.93
SS = 2                     # 超取樣
GOLD_HI = np.array([242, 212, 128]) / 255
GOLD_LO = np.array([168, 118, 42]) / 255


def find_font() -> Path:
    if FONT.exists():
        return FONT
    folder = FONT.parent
    for f in sorted(folder.glob("*.ttf")):
        if "Medium" in f.name or "Bold" in f.name:
            return f
    raise SystemExit(f"找不到字型：{FONT}")


def draw_mask() -> Image.Image:
    w, h = W * SS, H * SS
    img = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(img)
    u = w / 100  # 以寬度的 1% 為單位

    # 雙線邊框
    d.rounded_rectangle((3.5 * u, 3.5 * u, w - 3.5 * u, h - 3.5 * u), radius=5 * u, outline=255, width=int(1.3 * u))
    d.rounded_rectangle((6.5 * u, 6.5 * u, w - 6.5 * u, h - 6.5 * u), radius=3.5 * u, outline=255, width=int(0.55 * u))

    # 四角卷雲
    def scroll(cx: float, cy: float, r: float, flip_x: int, flip_y: int) -> None:
        pts = []
        for i in range(90):
            t = i / 89 * 2.6 * math.pi
            rr = r * (1 - t / (2.9 * math.pi))
            pts.append((cx + flip_x * rr * math.cos(t), cy + flip_y * rr * math.sin(t)))
        d.line(pts, fill=255, width=int(0.7 * u), joint="curve")

    for fx, fy, x, y in ((1, 1, 13, 13), (-1, 1, 87, 13), (1, -1, 13, 100 * h / w - 13), (-1, -1, 87, 100 * h / w - 13)):
        scroll(x * u, y * u, 4.2 * u, fx, fy)

    # 碑額：寶珠居中，兩側卷雲對稱展開，下方一道橫線
    top = 0.155 * h
    cx = w / 2
    d.ellipse((cx - 4.2 * u, top - 4.2 * u, cx + 4.2 * u, top + 4.2 * u), outline=255, width=int(0.8 * u))
    d.ellipse((cx - 2.2 * u, top - 2.2 * u, cx + 2.2 * u, top + 2.2 * u), fill=255)
    for side in (-1, 1):
        # 從寶珠往外的 S 形雲帶，末端捲成漩渦
        pts = []
        for i in range(60):
            k = i / 59
            x = cx + side * (5.5 * u + k * 22 * u)
            y = top + 2.6 * u * math.sin(k * 2 * math.pi) * (1 - 0.3 * k)
            pts.append((x, y))
        d.line(pts, fill=255, width=int(0.75 * u), joint="curve")
        scroll(cx + side * 30.5 * u, top - 0.3 * u, 3.2 * u, side, 1)
        scroll(cx + side * 16 * u, top + 3.6 * u, 1.8 * u, -side, -1)
    d.line((12 * u, 0.235 * h, w - 12 * u, 0.235 * h), fill=255, width=int(0.6 * u))

    # 碑腳：海水紋（層疊半圓）＋上緣橫線
    base_top = 0.79 * h
    d.line((12 * u, base_top, w - 12 * u, base_top), fill=255, width=int(0.6 * u))
    rw = 9 * u
    for row in range(3):
        y = base_top + (3.2 + row * 3.4) * u
        off = (row % 2) * rw / 2
        x = 12.5 * u + off
        while x <= w - 12.5 * u:
            for k in (1.0, 0.68, 0.36):
                r = rw / 2 * k
                d.arc((x - r, y - r, x + r, y + r), 180, 360, fill=255, width=int(0.55 * u))
            x += rw

    # 題字外框（直書牌位）
    box = (w / 2 - 13 * u, 0.27 * h, w / 2 + 13 * u, 0.755 * h)
    d.rounded_rectangle(box, radius=4 * u, outline=255, width=int(0.8 * u))
    d.rounded_rectangle((box[0] + 1.8 * u, box[1] + 1.8 * u, box[2] - 1.8 * u, box[3] - 1.8 * u), radius=3 * u, outline=255, width=int(0.35 * u))

    # 直書「定海神碑」
    font = ImageFont.truetype(str(find_font()), int(19 * u))
    slot = (box[3] - box[1] - 6 * u) / len(TITLE)
    for i, ch in enumerate(TITLE):
        cy = box[1] + 3 * u + slot * (i + 0.5)
        l, t, r, b = d.textbbox((0, 0), ch, font=font)
        d.text((w / 2 - (l + r) / 2, cy - (t + b) / 2), ch, font=font, fill=255, stroke_width=int(0.35 * u), stroke_fill=255)

    return img.resize((W, H), Image.LANCZOS)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    mask = draw_mask()
    m = np.asarray(mask, dtype=np.float64) / 255

    # 描金：筆畫中心亮、邊緣暗，模擬刻槽裡貼金
    inner = np.asarray(mask.filter(ImageFilter.GaussianBlur(3)), dtype=np.float64) / 255
    shade = np.clip((inner - 0.35) / 0.65, 0, 1)
    sheen = 0.5 + 0.5 * np.linspace(1, 0, H)[:, None]            # 上亮下暗
    t = np.clip(shade * 0.8 + sheen * 0.2, 0, 1)[..., None]
    rgb = GOLD_LO + (GOLD_HI - GOLD_LO) * t
    rgba = np.dstack([rgb, np.clip(m * 1.15, 0, 1)])
    Image.fromarray((rgba * 255).round().astype(np.uint8), "RGBA").save(OUT / "stele_face.png")

    # 刻痕法線：筆畫往內凹（高度 = -模糊遮罩），Unity 用 OpenGL 慣例（+Y 朝上）
    height = -np.asarray(mask.filter(ImageFilter.GaussianBlur(2.2)), dtype=np.float64) / 255
    gy, gx = np.gradient(height)
    k = 6.0
    n = np.dstack([-gx * k, gy * k, np.ones_like(height)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    Image.fromarray(((n * 0.5 + 0.5) * 255).round().astype(np.uint8), "RGB").save(OUT / "stele_face_normal.png")
    print("wrote", OUT / "stele_face.png", OUT / "stele_face_normal.png", "font:", find_font().name)


if __name__ == "__main__":
    main()
