"""產生遊戲音效與背景音樂（全部本機合成，沒有外部素材授權問題）。

python Tools/generate_audio.py   →  Assets/Audio/sfx_*.wav、bgm_loop.wav
- 音效：numpy 合成（鐘聲、木魚、低鼓、上升光效…），44.1 kHz 單聲道
- 背景音樂：寫 MIDI（五聲音階；古箏、尺八、弦樂墊底、太鼓），交給 FluidSynth＋GeneralUser GS 演奏，
  頭尾對齊成可無縫循環的 WAV。GeneralUser GS 授權允許商用。
"""
from __future__ import annotations

import os
import shutil
import struct
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np

sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Audio"
TOOLS = Path.home() / "Desktop" / "claude" / "tools"
# 可用環境變數 FLUIDSYNTH、SF2 指定；沒設就找作者電腦上的位置，再找 PATH
FLUIDSYNTH = Path(os.environ.get("FLUIDSYNTH") or TOOLS / "fluidsynth" / "fs" / "fluidsynth-v2.6.1-win10-x64-cpp11" / "bin" / "fluidsynth.exe")
if not FLUIDSYNTH.exists() and shutil.which("fluidsynth"):
    FLUIDSYNTH = Path(shutil.which("fluidsynth"))
SF2 = Path(os.environ.get("SF2") or TOOLS / "fluidsynth" / "GeneralUser-GS.sf2")
SR = 44100
rng = np.random.default_rng(7)


def write_wav(path: Path, data: np.ndarray, sr: int = SR) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    data = np.asarray(data, dtype=np.float64)
    peak = np.max(np.abs(data)) or 1.0
    data = data / peak * 0.89
    pcm = (np.clip(data, -1, 1) * 32767).astype("<i2")
    channels = 1 if pcm.ndim == 1 else pcm.shape[1]
    with wave.open(str(path), "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())
    print("wrote", path.name, f"{len(pcm) / sr:.2f}s")


def t_axis(sec: float) -> np.ndarray:
    return np.arange(int(sec * SR)) / SR


def env(t: np.ndarray, attack: float, decay: float) -> np.ndarray:
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    return a * np.exp(-np.maximum(t - attack, 0) / decay)


def bell(freq: float, sec: float, decay: float, partials=((1, 1.0), (2.0, 0.5), (3.01, 0.25), (4.2, 0.12))) -> np.ndarray:
    t = t_axis(sec)
    out = np.zeros_like(t)
    for ratio, amp in partials:
        out += amp * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-t / (decay / ratio ** 0.6))
    return out * env(t, 0.002, decay * 2)


def noise(sec: float) -> np.ndarray:
    return rng.standard_normal(int(sec * SR))


def lowpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    # 一階低通，足夠用在打擊聲的去刺
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.zeros_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def place(total: float, *clips: tuple[float, np.ndarray]) -> np.ndarray:
    out = np.zeros(int(total * SR))
    for start, clip in clips:
        i = int(start * SR)
        n = min(len(clip), len(out) - i)
        out[i:i + n] += clip[:n]
    return out


def sfx() -> None:
    # 選取：短促清脆的木魚＋高音
    t = t_axis(0.18)
    wood = np.sin(2 * np.pi * 1180 * t) * env(t, 0.001, 0.035) + 0.5 * np.sin(2 * np.pi * 1770 * t) * env(t, 0.001, 0.02)
    write_wav(OUT / "sfx_select.wav", wood + 0.3 * bell(2350, 0.18, 0.05))

    # 擊退：三音鐘聲上揚＋一點氣音
    kill = place(0.9,
                 (0.0, bell(1046.5, 0.8, 0.22)),
                 (0.05, 0.7 * bell(1568.0, 0.8, 0.2)),
                 (0.10, 0.5 * bell(2093.0, 0.7, 0.18)),
                 (0.0, 0.25 * lowpass(noise(0.12), 3500) * env(t_axis(0.12), 0.002, 0.03)))
    write_wav(OUT / "sfx_kill.wav", kill)

    # 神碑受擊：低頻悶響＋碎石聲
    t = t_axis(0.55)
    f = 110 * np.exp(-t * 6) + 45
    thump = np.sin(2 * np.pi * np.cumsum(f) / SR) * env(t, 0.002, 0.16)
    crack = 0.5 * lowpass(noise(0.55), 1800) * env(t, 0.001, 0.06)
    write_wav(OUT / "sfx_hit.wav", thump + crack)

    # 神碑降臨：上升光效＋鐘聲
    t = t_axis(1.4)
    sweep_f = 300 + 900 * (t / 1.4) ** 1.5
    sweep = 0.35 * np.sin(2 * np.pi * np.cumsum(sweep_f) / SR) * np.clip(t / 0.9, 0, 1) * np.exp(-np.maximum(t - 0.9, 0) / 0.15)
    appear = sweep + place(1.4, (0.85, bell(784.0, 0.55, 0.35)), (0.85, 0.6 * bell(1174.7, 0.55, 0.3)))
    write_wav(OUT / "sfx_appear.wav", appear)

    # 最後 10 秒：每秒一聲木板
    t = t_axis(0.12)
    tick = np.sin(2 * np.pi * 880 * t) * env(t, 0.001, 0.025) + 0.3 * lowpass(noise(0.12), 2500) * env(t, 0.001, 0.01)
    write_wav(OUT / "sfx_tick.wav", tick)

    # 勝利：五聲音階上行琶音（C D E G A C）
    notes = [523.25, 587.33, 659.25, 783.99, 880.0, 1046.5]
    win = place(2.2, *[(i * 0.11, bell(f, 1.4, 0.45)) for i, f in enumerate(notes)],
                (0.66, 0.6 * bell(1318.5, 1.5, 0.6)))
    write_wav(OUT / "sfx_win.wav", win)

    # 失敗：低沉下行（A G E D）＋低鼓
    notes = [440.0, 392.0, 329.63, 293.66]
    t = t_axis(0.8)
    drum = np.sin(2 * np.pi * np.cumsum(70 * np.exp(-t * 4) + 40) / SR) * env(t, 0.002, 0.25)
    lose = place(2.0, *[(i * 0.22, 0.8 * bell(f / 2, 1.2, 0.5, ((1, 1.0), (2.0, 0.35), (3.0, 0.1)))) for i, f in enumerate(notes)],
                 (0.66, drum))
    write_wav(OUT / "sfx_lose.wav", lose)

    # 介面點擊
    t = t_axis(0.09)
    write_wav(OUT / "sfx_ui.wav", np.sin(2 * np.pi * 1500 * t) * env(t, 0.001, 0.018) + 0.4 * bell(3000, 0.09, 0.02))


# ------------------- 背景音樂（MIDI） -------------------

TPB = 480
BPM = 84


def vlq(n: int) -> bytes:
    out = [n & 0x7F]
    n >>= 7
    while n:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    return bytes(reversed(out))


def write_midi(path: Path, events: list[tuple[int, bytes]]) -> None:
    events.sort(key=lambda e: (e[0], e[1][0] & 0xF0 != 0x80))  # 同時間先 note_off
    track = bytearray()
    tempo = int(60_000_000 / BPM)
    track += vlq(0) + b"\xff\x51\x03" + tempo.to_bytes(3, "big")
    last = 0
    for tick, msg in events:
        track += vlq(tick - last) + msg
        last = tick
    track += vlq(0) + b"\xff\x2f\x00"
    data = b"MThd" + struct.pack(">IHHH", 6, 0, 1, TPB) + b"MTrk" + struct.pack(">I", len(track)) + track
    path.write_bytes(data)


def bgm() -> None:
    beat = TPB
    bar = beat * 4
    bars = 16
    ev: list[tuple[int, bytes]] = []

    def prog(ch: int, program: int, vol: int, rev: int = 70) -> None:
        ev.append((0, bytes([0xC0 | ch, program])))
        ev.append((0, bytes([0xB0 | ch, 7, vol])))
        ev.append((0, bytes([0xB0 | ch, 91, rev])))

    def note(ch: int, start: int, dur: int, pitch: int, vel: int) -> None:
        ev.append((start, bytes([0x90 | ch, pitch, vel])))
        ev.append((start + dur, bytes([0x80 | ch, pitch, 0])))

    prog(0, 107, 96)   # 古箏
    prog(1, 77, 88)    # 尺八
    prog(2, 48, 70)    # 弦樂墊底
    prog(3, 116, 100)  # 太鼓
    prog(4, 32, 84)    # 原聲貝斯

    # D 羽調式五聲：D F G A C
    chords = [[50, 57, 62], [48, 55, 60], [46, 53, 58], [45, 52, 57]]  # Dm  C  Bb  Am
    roots = [38, 36, 34, 33]
    for b in range(bars):
        c = chords[b % 4]
        start = b * bar
        for p in c:
            note(2, start, bar, p, 58)
        note(4, start, beat * 2, roots[b % 4], 80)
        note(4, start + beat * 2, beat * 2, roots[b % 4] + 7, 70)
        # 太鼓：第 1、3 拍重，4 拍後半輕
        note(3, start, beat // 2, 60, 96)
        note(3, start + beat * 2, beat // 2, 60, 80)
        note(3, start + beat * 3 + beat // 2, beat // 4, 60, 56)
        # 古箏分解和弦（八分音符）
        arp = [c[0] + 12, c[1] + 12, c[2] + 12, c[1] + 24, c[2] + 12, c[1] + 12, c[0] + 24, c[2] + 12]
        for i, p in enumerate(arp):
            note(0, start + i * beat // 2, beat // 2, p, 70 if i % 2 else 84)

    # 尺八旋律：第 5–12 小節，五聲音階長音
    melody = [
        (4, 0, 2, 74), (4, 2, 1, 77), (4, 3, 1, 74), (5, 0, 3, 72), (5, 3, 1, 69),
        (6, 0, 2, 70), (6, 2, 2, 72), (7, 0, 4, 69),
        (8, 0, 2, 74), (8, 2, 1, 77), (8, 3, 1, 79), (9, 0, 3, 81), (9, 3, 1, 79),
        (10, 0, 2, 77), (10, 2, 2, 74), (11, 0, 4, 74),
    ]
    for b, beat_in_bar, length, pitch in melody:
        note(1, b * bar + beat_in_bar * beat, length * beat - 30, pitch, 78)

    mid = OUT / "bgm_loop.mid"
    OUT.mkdir(parents=True, exist_ok=True)
    write_midi(mid, ev)

    raw = OUT / "_bgm_raw.wav"
    subprocess.run([str(FLUIDSYNTH), "-ni", "-q", "-g", "0.6", "-r", str(SR), "-F", str(raw), str(SF2), str(mid)], check=True)

    with wave.open(str(raw), "rb") as w:
        ch = w.getnchannels()
        frames = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768
    frames = frames.reshape(-1, ch)

    # 無縫循環：長度剛好 16 小節，尾巴的殘響疊回開頭
    loop_len = int(bars * 4 * 60 / BPM * SR)
    body = frames[:loop_len].copy()
    tail = frames[loop_len:]
    n = min(len(tail), loop_len)
    body[:n] += tail[:n]
    write_wav(OUT / "bgm_loop.wav", body)
    raw.unlink()
    mid.unlink()


if __name__ == "__main__":
    sfx()
    bgm()
