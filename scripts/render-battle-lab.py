#!/usr/bin/env python3
"""Package Godot BattleLab frame captures into audible MP4 studies and a review reel.

Uses Python's standard library and an existing ffmpeg installation. Never deletes captures.
Usage: python3 scripts/render-battle-lab.py /path/to/frames [output-directory]
"""
import array
import html
import math
from pathlib import Path
import random
import subprocess
import sys
import wave

ROOT = Path(__file__).resolve().parents[1]
FPS, RATE, DURATION = 60, 48000, 6.2
TAKES = [
    ("thunderfall", "Tempest Ram · Thunderfall", 1.52),
    ("faultline", "Clockwork Pangolin · Faultline", 1.78),
    ("phantom-rake", "Forest Flicker · Phantom Rake", 1.52),
    ("briar-prison", "Snag · Briar Prison", 1.42),
]


def run(*args):
    subprocess.run([str(a) for a in args], check=True)


def soundtrack(path, kind, contact):
    """Deterministic original synthesis: filtered noise, transients and low percussion."""
    rng = random.Random(1200 + kind)
    samples = array.array("h")
    low = 0.0
    for i in range(round(DURATION * RATE)):
        t = i / RATE
        u = t - contact
        n = rng.uniform(-1, 1)
        low += .075 * (n - low)
        high = n - low
        value = math.sin(t * 2 * math.pi * 44) * .004
        if -.65 < u < 0:
            p = (u + .65) / .65
            if kind == 0:
                value += (.026 * high + .018 * math.sin(2 * math.pi * (300*t + 210*t*t))) * p*p
            elif kind == 1:
                value += low * .22 * p + math.sin(2 * math.pi * 47*t) * p * .024
            elif kind == 2:
                value += high * .075 * math.sin(math.pi*p)**2
            else:
                value += low * .14 * p
        if u >= 0:
            if kind == 0:
                value += high * .72 * math.exp(-u*45) + low * .85 * math.exp(-u*3.8)
                value += math.sin(2*math.pi*(65*u-12*u*u)) * .25 * math.exp(-u*7)
                if u > .19:
                    value += high*.28*math.exp(-(u-.19)*65)
            elif kind == 1:
                value += low * 1.3 * math.exp(-u*2.6)
                value += math.sin(2*math.pi*(61*u-9*u*u))*.34*math.exp(-u*4.5)
                for delay in (.08,.17,.25,.38):
                    if u > delay:
                        value += high*.10*math.exp(-(u-delay)*42)
            elif kind == 2:
                for delay in (0,.085,.17):
                    v = u-delay
                    if v >= 0:
                        value += (high*.34 + math.sin(2*math.pi*190*v)*.09)*math.exp(-v*27)
                value += low*.35*math.exp(-u*6)
            else:
                value += low*.78*math.exp(-u*4)
                for delay in (0,.07,.13,.24):
                    v = u-delay
                    if v >= 0:
                        value += (high*.23+math.sin(2*math.pi*260*v)*.09)*math.exp(-v*52)
        value = math.tanh(value * 1.3) * .78
        samples.append(round(value * 32767))
    if sys.byteorder != "little":
        samples.byteswap()
    with wave.open(str(path), "wb") as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(RATE)
        stream.writeframes(samples.tobytes())


def main():
    frames = Path(sys.argv[1]).resolve()
    output = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else ROOT / "build/battle-lab"
    output.mkdir(parents=True, exist_ok=True)
    videos = []
    for theme in range(2):
        for kind, (slug, title, contact) in enumerate(TAKES):
            name = f"{theme+1}{kind+1}-{slug}"
            source = frames / name
            if not source.exists():
                raise RuntimeError(f"Missing take: {source}")
            count = len(list(source.glob("[0-9][0-9][0-9][0-9].png")))
            expected = round(FPS * DURATION)
            if count != expected:
                raise RuntimeError(f"{name}: expected {expected} frames, found {count}")
            for i in range(expected):
                if not (source / f"{i:04}.png").is_file():
                    raise RuntimeError(f"Missing frame {i} in {source}")
            sound = output / f"{name}.wav"
            soundtrack(sound, kind, contact)
            video = output / f"{name}.mp4"
            run("ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-framerate", FPS,
                "-i", source / "%04d.png", "-i", sound, "-c:v", "libx264", "-preset", "medium",
                "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k",
                "-movflags", "+faststart", "-shortest", video)
            # A readable frame just after contact, after the brief hit-stop.
            run("ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-ss", str(contact+.16),
                "-i", video, "-frames:v", "1", output / f"{name}.jpg")
            videos.append(video)
            print(f"Packaged {name}", flush=True)
    playlist = output / "playlist.txt"
    playlist.write_text("".join(f"file '{p.name}'\n" for p in videos))
    run("ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0",
        "-i", playlist, "-c", "copy", "-movflags", "+faststart", output / "START-HERE-battle-studies.mp4")
    cards=[]
    for theme in range(2):
        cards.append(f'<h2>{"Moonlit Ruins" if theme == 0 else "Ember Vault"}</h2><div class="grid">')
        for kind, (slug,title,_) in enumerate(TAKES):
            name=f"{theme+1}{kind+1}-{slug}"
            cards.append(f'<article><h3>{html.escape(title)}</h3><video controls loop preload="none" poster="{name}.jpg" src="{name}.mp4"></video></article>')
        cards.append('</div>')
    page='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Worklings · Battle Studies</title>
<style>body{background:#0b111b;color:#edf3fc;font:16px system-ui;margin:40px auto;max-width:1180px;padding:0 24px}h1{font-size:42px;letter-spacing:-1.5px}p{color:#adbacb;max-width:800px;line-height:1.6}video{width:100%;border-radius:8px;background:#04070c}h2{margin-top:50px}h3{font-size:16px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:24px}a{color:#8ebfff}small{color:#adbacb}@media(max-width:760px){.grid{grid-template-columns:1fr}}</style>
<h1>Worklings / Battle Studies</h1><p>Four attacks. Two dungeon settings. Native Godot renders with original synthesized sound. Turn sound on, then compare the individual loops below.</p>
<video controls preload="metadata" src="START-HERE-battle-studies.mp4" poster="11-thunderfall.jpg"></video>
<p>Thunderfall: branching discharge and charged ground. Faultline: molten fractures, lifted stone and debris. Phantom Rake: three spectral claw sweeps. Briar Prison: rising thorned roots and falling earth.</p>
'''+''.join(cards)+'''<p>These are standalone visual studies, not changes to the live combat scene. Both cameras are candidates for a larger party layout; four-player readability still needs testing.</p></html>'''
    (output / "index.html").write_text(page)
    print(f"Review: {output / 'index.html'}")


if __name__ == "__main__":
    main()
