"""把 DemoRecording.RenderIcon 渲染的神碑特寫合成 App 圖示。

python Tools/make_icon.py <icon_src.png>
→ Assets/Art/Icon/app_icon.png（1024 全版圖示）、app_icon_bg.png（Android 自適應圖示背景層）
主體縮到 70% 放在中央，落在自適應圖示的安全區內，圓形／方形遮罩都不會切到。
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

sys.stdout.reconfigure(encoding="utf-8")
OUT = Path(__file__).resolve().parents[1] / "Assets" / "Art" / "Icon"
NAVY = (11, 22, 36)
SIZE = 1024


def radial(size: int) -> np.ndarray:
    c = (size - 1) / 2
    y, x = np.mgrid[0:size, 0:size]
    return np.sqrt((x - c) ** 2 + (y - c) ** 2) / c


def main(src: Path) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    r = radial(SIZE)

    # 背景：深藍，中央一點金色光暈
    bg = np.zeros((SIZE, SIZE, 3))
    bg[:] = NAVY
    glow = np.clip(1 - r / 0.75, 0, 1) ** 2.2
    bg += glow[..., None] * np.array([70, 52, 18])
    base = Image.fromarray(np.clip(bg, 0, 255).astype(np.uint8), "RGB")
    Image.new("RGB", (SIZE, SIZE), NAVY).save(OUT / "app_icon_bg.png")

    # 主體：渲染圖縮到 70%，邊緣羽化後貼上
    art = Image.open(src).convert("RGB").resize((int(SIZE * 0.7), int(SIZE * 0.7)), Image.LANCZOS)
    n = art.size[0]
    mask = np.clip((1 - radial(n)) / 0.25, 0, 1)
    mask_img = Image.fromarray((mask * 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(6))
    off = (SIZE - n) // 2
    base.paste(art, (off, off + 20), mask_img)

    base.save(OUT / "app_icon.png")
    print("wrote", OUT / "app_icon.png")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
