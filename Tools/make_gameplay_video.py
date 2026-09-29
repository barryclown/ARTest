"""把 DemoRecording 測試錄下的影格與音效時間表合成有聲 MP4。

1. 錄影（Unity 批次模式跑 PlayMode 測試，約 3–5 分鐘）：
   Unity.exe -batchmode -projectPath <專案> -runTests -testPlatform PlayMode
             -testFilter DemoRecording -recordDir <輸出資料夾>
2. 合成：python Tools/make_gameplay_video.py <輸出資料夾> <成品.mp4>

音軌：背景音樂從第 0 秒循環（結算時壓低），音效依 events.tsv 的時間點疊上去。
畫面右下角加註「Unity 編輯器自動試玩・非手機實拍」。
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np

sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(__file__).resolve().parents[1]
AUDIO = ROOT / "Assets" / "Audio"
# 可用環境變數 FFMPEG 指定；沒設就找作者電腦上的位置，再找 PATH
FFMPEG = Path(os.environ.get("FFMPEG") or Path.home() / "Desktop" / "claude" / "tools" / "ffmpeg" / "ffmpeg-8.1.2-essentials_build" / "bin" / "ffmpeg.exe")
if not FFMPEG.exists() and shutil.which("ffmpeg"):
    FFMPEG = Path(shutil.which("ffmpeg"))
FONT = ROOT / "Assets" / "Fonts" / "NotoSansTC-Regular.ttf"
SR = 44100

SFX = {"Select": ("sfx_select", 0.7), "Kill": ("sfx_kill", 1.0), "Hit": ("sfx_hit", 1.0), "Appear": ("sfx_appear", 1.0),
       "Tick": ("sfx_tick", 0.6), "Win": ("sfx_win", 1.0), "Lose": ("sfx_lose", 1.0), "Ui": ("sfx_ui", 0.6)}
SFX_VOLUME = 0.9
BGM_VOLUME = 0.35
BGM_DUCK = 0.4


def read_wav(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as w:
        ch = w.getnchannels()
        data = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768
    data = data.reshape(-1, ch)
    return data if ch == 2 else np.repeat(data, 2, axis=1)


def main(rec_dir: Path, out: Path) -> None:
    frames, fps, _ = (rec_dir / "frames.txt").read_text().split("\t")
    frames, fps = int(frames), int(fps)
    duration = frames / fps
    events = [(float(t), name) for t, name in (line.split("\t") for line in (rec_dir / "events.tsv").read_text(encoding="utf-8").splitlines() if line)]

    n = int(duration * SR)
    mix = np.zeros((n, 2))

    # 背景音樂：循環鋪滿，duck/unduck 之間壓低（0.6 秒內滑到目標音量）
    bgm = read_wav(AUDIO / "bgm_loop.wav")
    reps = int(np.ceil(n / len(bgm)))
    bed = np.tile(bgm, (reps, 1))[:n]
    gain = np.full(n, BGM_VOLUME)
    level = BGM_VOLUME
    last = 0
    for t, name in sorted(events):
        if name not in ("duck", "unduck"):
            continue
        i = min(int(t * SR), n)
        gain[last:i] = level
        target = BGM_VOLUME * (BGM_DUCK if name == "duck" else 1.0)
        ramp = min(int(0.6 * SR), n - i)
        gain[i:i + ramp] = np.linspace(level, target, ramp)
        level, last = target, i + ramp
    gain[last:] = level
    mix += bed * gain[:, None]

    cache: dict[str, np.ndarray] = {}
    for t, name in events:
        if name not in SFX:
            continue
        file, vol = SFX[name]
        clip = cache.setdefault(file, read_wav(AUDIO / f"{file}.wav"))
        i = int(t * SR)
        k = min(len(clip), n - i)
        if k > 0:
            mix[i:i + k] += clip[:k] * vol * SFX_VOLUME

    peak = np.max(np.abs(mix)) or 1
    if peak > 0.95:
        mix *= 0.95 / peak
    wav = rec_dir / "mix.wav"
    with wave.open(str(wav), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((mix * 32767).astype("<i2").tobytes())

    font = str(FONT).replace("\\", "/").replace(":", "\\:")
    label = "Unity 編輯器自動試玩・非手機實拍"
    vf = (f"drawtext=fontfile='{font}':text='{label}':fontcolor=white@0.55:fontsize=26:"
          f"x=w-tw-28:y=h-th-24:box=1:boxcolor=black@0.25:boxborderw=8")
    cmd = [str(FFMPEG), "-y", "-hide_banner", "-loglevel", "error",
           "-framerate", str(fps), "-i", str(rec_dir / "frames" / "f_%05d.jpg"),
           "-i", str(wav), "-vf", vf,
           "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-pix_fmt", "yuv420p",
           "-c:a", "aac", "-b:a", "160k", "-af", "loudnorm=I=-16:TP=-1.5",
           "-shortest", "-movflags", "+faststart", str(out)]
    subprocess.run(cmd, check=True)
    print(f"wrote {out}  ({duration:.1f}s, {frames} frames, {len(events)} events)")


if __name__ == "__main__":
    main(Path(sys.argv[1]), Path(sys.argv[2]))
