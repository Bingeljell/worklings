#!/usr/bin/env python3
"""Package three native Godot aura comparisons and nine portrait loops. No dependencies beyond ffmpeg."""
from pathlib import Path
import html
import json
import subprocess
import sys

MODELS = [
    ("tempest_ram", "Tempest Ram", ["Horn Static", "Storm Veins", "Storm Mantle"]),
    ("clockwork_pangolin", "Clockwork Pangolin", ["Orbiting Inscriptions", "Runic Orrery", "Shell Sigils"]),
    ("snag", "Snag", ["Earth Motes", "Dust Puffs", "Golden Spores"]),
]


def run(*args):
    subprocess.run([str(a) for a in args], check=True)


def main():
    global MODELS
    if "--internal" in sys.argv:
        MODELS = [
            ("tempest_ram", "Tempest Ram", ["Resting Current", "Living Lightning", "Surging Storm"]),
            ("clockwork_pangolin", "Clockwork Pangolin", ["Runic Embers", "Awakened Core", "Breathing Energy"]),
        ]
    # Mirrors WORKLINGS_AURA_SELECT on the capture side: packaging one creature's
    # frames should not require the other two to have been captured.
    if "--only" in sys.argv:
        wanted = sys.argv[sys.argv.index("--only") + 1]
        MODELS = [m for m in MODELS if m[0] == wanted]
        if not MODELS:
            raise SystemExit(f"--only {wanted}: no such model")
    positional = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--only" in sys.argv:
        positional.remove(wanted)
    source, output = (Path(p).resolve() for p in positional[:2])
    output.mkdir(parents=True, exist_ok=True)
    cards=[]
    for slug, title, variants in MODELS:
        directory=source/slug
        for frame in range(240):
            if not (directory/f"{frame:04}.png").is_file():
                raise RuntimeError(f"Missing {slug} frame {frame}")
        target=output/f"{slug}-comparison.mp4"
        run("ffmpeg","-hide_banner","-loglevel","error","-y","-framerate","30","-i",directory/"%04d.png",
            "-frames:v","240","-c:v","libx264","-preset","medium","-crf","17","-pix_fmt","yuv420p","-movflags","+faststart",target)
        run("ffmpeg","-hide_banner","-loglevel","error","-y","-ss","2","-i",target,"-frames:v","1",output/f"{slug}.png")
        links=[]
        for index,variant in enumerate(variants):
            name=f"{slug}-{index+1}.mp4"
            run("ffmpeg","-hide_banner","-loglevel","error","-y","-i",target,"-vf",f"crop=510:752:{22+526*index}:96",
                "-c:v","libx264","-crf","17","-pix_fmt","yuv420p","-movflags","+faststart",output/name)
            links.append(f'<a href="{name}">{index+1}. {html.escape(variant)}</a>')
        cards.append(f'<section><h2>{title}</h2><video controls loop preload="none" poster="{slug}.png" src="{target.name}"></video><p>'+" &nbsp; · &nbsp; ".join(links)+"</p></section>")
        print(f"Packaged {slug}: comparison + 3 portraits",flush=True)
    playlist=output/"playlist.txt"
    playlist.write_text("".join(f"file '{slug}-comparison.mp4'\n" for slug,_,_ in MODELS))
    run("ffmpeg","-hide_banner","-loglevel","error","-y","-f","concat","-safe","0","-i",playlist,"-c","copy",
        "-movflags","+faststart",output/"START-HERE-aura-studies.mp4")
    for path in sorted(output.glob("*.mp4")):
        info=json.loads(subprocess.check_output(["ffprobe","-v","error","-show_streams","-of","json",str(path)]))
        stream=next(s for s in info["streams"] if s["codec_type"]=="video")
        assert stream["r_frame_rate"]=="30/1",path
        assert int(stream["nb_frames"])==(240*len(MODELS) if path.name.startswith("START") else 240),path
    run("ffmpeg","-v","error","-i",output/"START-HERE-aura-studies.mp4","-f","null","-")
    (output/"index.html").write_text('''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Worklings · Model Auras</title>
<style>body{background:#0b1018;color:#edf4ff;font:16px system-ui;max-width:1400px;margin:40px auto;padding:0 24px}h1{font-size:40px;letter-spacing:-1px}p{color:#aabbcf;line-height:1.6}a{color:#8fc5ff}video{width:100%;background:#05080e;border-radius:10px}section{margin-top:48px}</style>
<h1>Worklings / Ambient Model Studies</h1><p>Three variations per creature. Real Godot models and animated effects, shown under identical lighting. These ambient studies are silent.</p>
<video controls preload="metadata" src="START-HERE-aura-studies.mp4" poster="tempest_ram.png"></video>
'''+''.join(cards)+'''<p>The internal energy pass uses a live skinned material overlay; the Ram arcs and original orbit pass use cached idle poses. They are isolated from the dungeon implementation. See <a href="HANDOVER.md">the handover</a> for reuse and limitations.</p></html>''')
    doc=Path(__file__).resolve().parents[1]/"docs/engineering/model-aura-studies.md"
    if doc.exists():
        (output/"HANDOVER.md").write_text(doc.read_text())
    print(f"Validated all clips. Review: {output/'index.html'}")


if __name__=="__main__":
    main()
