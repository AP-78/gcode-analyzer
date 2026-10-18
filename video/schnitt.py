"""Schneidet das Video aus den Bildern (aufnahme.ps1), der Sprecherspur (sprecher.py) und der Musik.

Aufruf: python video/schnitt.py [musik.mp3]   (ohne Angabe: erste MP3 in input/audio/)
Ergebnis in video/out/: video.mp4, video_ohne_musik.mp4, untertitel.srt, youtube.md, thumbnail.png

Eigene Sprachaufnahmen: 01.wav … 08.wav in video/out/ton/ ersetzen und nur dieses Skript erneut starten.
Die Abschnitte richten sich nach der Länge der Aufnahmen; die Untertitel-Zeiten werden je Abschnitt gestreckt.
"""
import json
import re
import subprocess
import sys
import textwrap
import wave
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFont

HERE = Path(__file__).parent
ROOT = HERE.parent
OUT = HERE / "out"
BILDER = OUT / "bilder"
TON = OUT / "ton"
W, H, FPS = 1920, 1080, 30
VORLAUF, NACHLAUF = 0.3, 0.6  # Stille vor und nach dem Sprecher je Abschnitt
BLENDE = 0.4  # Überblendung zwischen Abschnitten
OUTRO = 6.0
GOLD = (245, 180, 0)
FONT, FONT_BOLD = "C:/Windows/Fonts/segoeui.ttf", "C:/Windows/Fonts/segoeuib.ttf"
PAUSE_SATZ = 0.6  # wie in sprecher.py


def wav_len(path: Path) -> float:
    with wave.open(str(path)) as w:
        return w.getnframes() / w.getframerate()


def img(name: str) -> Image.Image:
    return Image.open(BILDER / f"{name}.png").convert("RGB")


def ease(x: float) -> float:
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


rects = json.loads((BILDER / "rects.json").read_text(encoding="utf-8-sig"))
SW, SH = img("01_uebersicht").size


def box(key: str) -> tuple[int, int, int, int]:
    x, y, w, h = rects[key]
    if x > SW / 2:  # Seitenleiste: Rahmen bis an den rechten Rand
        w = SW - 6 - x
    return max(2, x - 4), max(2, y - 4), min(SW - 3, x + w + 4), min(SH - 3, y + h + 4)


def highlight(im: Image.Image, key: str, dim: bool = True) -> Image.Image:
    """Bereich hervorheben: Rest abdunkeln (optional), goldener Rahmen."""
    x0, y0, x1, y1 = box(key)
    out = ImageEnhance.Brightness(im).enhance(0.45) if dim else im.copy()
    out.paste(im.crop((x0, y0, x1, y1)), (x0, y0))
    ImageDraw.Draw(out).rounded_rectangle([x0, y0, x1, y1], radius=6, outline=GOLD, width=3)
    return out


def view(im: Image.Image, zoom: float = 1.0, cx: float = 0.5, cy: float = 0.5) -> Image.Image:
    """Ausschnitt mit Zoom um (cx, cy) in Bildanteilen, auf 1920x1080 skaliert."""
    w, h = SW / zoom, SH / zoom
    x = min(max(cx * SW - w / 2, 0), SW - w)
    y = min(max(cy * SH - h / 2, 0), SH - h)
    return im.resize((W, H), Image.BICUBIC, box=(x, y, x + w, y + h))


def center(key: str) -> tuple[float, float]:
    x0, y0, x1, y1 = box(key)
    return (x0 + x1) / 2 / SW, (y0 + y1) / 2 / SH


def focus(base: Image.Image, key: str, d: float, zoom: float = 1.3, dim: bool = True):
    """Szene: Hervorhebung blendet ein, Kamera fährt langsam an den Bereich heran."""
    lit = highlight(base, key, dim)
    cx, cy = center(key)

    def frame(t):
        a = ease((t - 0.3) / 0.6)
        z = 1 + (zoom - 1) * ease(t / min(2.0, d))
        return view(Image.blend(base, lit, a), z, 0.5 + (cx - 0.5) * a, 0.5 + (cy - 0.5) * a)
    return frame


def outro_card(lines: list[str]) -> Image.Image:
    card = Image.new("RGB", (W, H), (28, 28, 28))
    d = ImageDraw.Draw(card)
    icon = Image.open(ROOT / "src/GcodeAnalyzer.App/app.ico").convert("RGBA").resize((140, 140), Image.LANCZOS)
    card.paste(icon, (160, 150), icon)
    d.text((340, 160), "G-Code-Analyzer", font=ImageFont.truetype(FONT_BOLD, 80), fill="white")
    d.text((344, 260), "Was der Slicer wirklich druckt", font=ImageFont.truetype(FONT, 40), fill=GOLD)
    y = 400
    for i, line in enumerate(lines):
        big = i < 2  # Download und Blog groß, Namensnennungen klein
        font = ImageFont.truetype(FONT_BOLD if big else FONT, 40 if big else 28)
        d.text((160, y), line, font=font, fill="white" if big else (190, 190, 190))
        y += 64 if big else 44
        if i == 1:
            y += 40
    return card


def thumbnail() -> Image.Image:
    base = img("01_uebersicht")
    # 3D-Ansicht ohne Seitenleiste, Modell etwas größer
    bg = base.crop((180, 50, 720, 354)).resize((1280, 720), Image.LANCZOS)
    shade = Image.linear_gradient("L").rotate(90).resize((1280, 720))  # links dunkel, rechts hell
    bg = Image.composite(bg, ImageEnhance.Brightness(bg).enhance(0.25), shade)
    d = ImageDraw.Draw(bg)
    icon = Image.open(ROOT / "src/GcodeAnalyzer.App/app.ico").convert("RGBA").resize((96, 96), Image.LANCZOS)
    bg.paste(icon, (60, 60), icon)
    d.text((176, 74), "G-Code-Analyzer", font=ImageFont.truetype(FONT_BOLD, 56), fill=GOLD)
    title = ImageFont.truetype(FONT_BOLD, 104)
    for i, line in enumerate(["Was druckt", "der Slicer", "wirklich?"]):
        d.text((60, 230 + i * 120), line, font=title, fill="white", stroke_width=4, stroke_fill="black")
    return bg


def srt_time(s: float) -> str:
    ms = round(s * 1000)
    return f"{ms // 3600000:02}:{ms // 60000 % 60:02}:{ms // 1000 % 60:02},{ms % 1000:03}"


def main():
    md = (HERE / "transkript.md").read_text(encoding="utf-8")
    titles = re.findall(r"^## \d+\. (.+)$", md, flags=re.M)
    credits = [l[2:] for l in md.split("## Schlussbild", 1)[1].splitlines() if l.startswith("- ")]
    timings = json.loads((TON / "zeiten.json").read_text(encoding="utf-8"))
    music = Path(sys.argv[1]) if len(sys.argv) > 1 else sorted((ROOT / "input/audio").glob("*.mp3"))[0]

    wavs = [TON / f"{i:02}.wav" for i in range(1, len(titles) + 1)]
    # YouTube-Kapitel müssen mindestens 10 s lang sein.
    durs = [max(10.5, VORLAUF + wav_len(w) + NACHLAUF) for w in wavs]
    starts = [sum(durs[:i]) for i in range(len(durs))]
    total = sum(durs) + OUTRO

    # Untertitel: Teilsätze zu Zeilen bis ca. 70 Zeichen zusammenfassen, je Abschnitt auf die WAV-Länge gestreckt.
    srt, n = [], 0
    for i, key in enumerate(sorted(timings)):
        cues = timings[key]
        scale = wav_len(wavs[i]) / (cues[-1]["end"] + PAUSE_SATZ)
        group = []
        for c in cues + [None]:
            text = " ".join(g["text"] for g in group)
            if group and (c is None or text[-1] in ".!?" or len(text) + len(c["text"]) > 70):
                n += 1
                t0 = starts[i] + VORLAUF + group[0]["start"] * scale
                t1 = starts[i] + VORLAUF + group[-1]["end"] * scale
                srt.append(f"{n}\n{srt_time(t0)} --> {srt_time(t1)}\n{textwrap.fill(text, 42)}\n")
                group = []
            if c:
                group.append(c)
    (OUT / "untertitel.srt").write_text("\n".join(srt), encoding="utf-8")

    # Szenen, je Abschnitt eine Funktion t -> Bild
    base = img("01_uebersicht")
    switch = next(c["start"] for c in timings["02"] if c["text"].startswith("Jede weitere"))
    switch = VORLAUF + switch * wav_len(wavs[1]) / (timings["02"][-1]["end"] + PAUSE_SATZ)
    fav1, fav2 = focus(base, "favoriten", durs[1]), focus(img("02_favorit"), "favoriten_neu", durs[1])
    layers = [img(p.stem) for p in sorted(BILDER.glob("03_layer_*.png"))]
    modes = [highlight(img(f"06_modus_{k}"), "einfaerbung", dim=False) for k in range(1, 5)]

    def s2(t):
        if t < switch:
            return fav1(t)
        a = ease((t - switch) / 0.3)
        return Image.blend(fav1(t), fav2(t), a) if a < 1 else fav2(t)

    def s3(t):
        k = min(len(layers) - 1, int(t / max(0.1, durs[2] - 1.0) * len(layers)))
        return view(layers[k], 1.15, 0.35, 0.4)

    def s6(t):
        k, rest = divmod(t / (durs[5] / 4), 1)
        k = min(int(k), 3)
        a = ease(rest * durs[5] / 4 / 0.3) if k > 0 else 1
        return view(Image.blend(modes[k - 1], modes[k], a) if a < 1 else modes[k], 1.1, 0.35, 0.4)

    scenes = [
        lambda t: view(base, 1 + 0.12 * t / durs[0], 0.35, 0.4),
        s2,
        s3,
        focus(img("04_waende"), "waende", durs[3]),
        focus(img("05_auswertung"), "infill_support", durs[4]),
        s6,
        focus(img("07_diagramm"), "diagramm", durs[6], zoom=1.1),
        lambda t: view(base, 1.12 - 0.12 * t / durs[7], 0.35, 0.4),
    ]
    card = outro_card(credits)

    ff = subprocess.Popen(["ffmpeg", "-y", "-v", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
                           "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "medium", "-crf", "18",
                           "-pix_fmt", "yuv420p", str(OUT / "bild.mp4")], stdin=subprocess.PIPE)
    prev = None
    for i, (scene, d) in enumerate(zip(scenes + [lambda t: card], durs + [OUTRO])):
        for f in range(round(d * FPS)):
            t = f / FPS
            frame = scene(t)
            if prev is not None and t < BLENDE:
                frame = Image.blend(prev, frame, ease(t / BLENDE))
            ff.stdin.write(frame.tobytes())
        prev = frame
        print(f"Szene {i + 1}: {d:.1f} s", flush=True)
    ff.stdin.close()
    ff.wait()

    # Ton: Sprecher an die Abschnittsstarts, Musik leise darunter und beim Sprechen weiter abgesenkt.
    inputs, delays = [], []
    for i, w in enumerate(wavs):
        inputs += ["-i", str(w)]
        ms = round((starts[i] + VORLAUF) * 1000)
        delays.append(f"[{i}]aresample=48000,adelay={ms}|{ms}[v{i}]")
    mix = "".join(f"[v{i}]" for i in range(len(wavs)))
    voice = ";".join(delays) + f";{mix}amix=inputs={len(wavs)}:normalize=0,apad,atrim=0:{total:.3f}"
    m = len(wavs)
    common = ["ffmpeg", "-y", "-v", "error", "-i", str(OUT / "bild.mp4")]
    shift = lambda s: re.sub(r"\[(\d+)\]", lambda g: f"[{int(g.group(1)) + 1}]", s)  # Eingang 0 ist das Bild
    subprocess.run(common + inputs + ["-filter_complex", shift(voice) + ",loudnorm=I=-16:TP=-1.5,aresample=48000[a]",
                   "-map", "0:v", "-map", "[a]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
                   str(OUT / "video_ohne_musik.mp4")], check=True)
    music_f = (f"[{m + 1}]aresample=48000,volume=0.35,afade=t=in:d=1,afade=t=out:st={total - 3:.3f}:d=3,"
               f"atrim=0:{total:.3f}[mu];[vo]asplit[vo1][vo2];[mu][vo1]sidechaincompress=threshold=0.03:ratio=6:"
               f"attack=20:release=500[duck];[vo2][duck]amix=inputs=2:normalize=0,loudnorm=I=-16:TP=-1.5,aresample=48000[a]")
    subprocess.run(common + inputs + ["-i", str(music), "-filter_complex", shift(voice) + "[vo];" + music_f,
                   "-map", "0:v", "-map", "[a]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
                   str(OUT / "video.mp4")], check=True)
    (OUT / "bild.mp4").unlink()

    thumbnail().save(OUT / "thumbnail.png")

    chapters = "\n".join(f"{int(s // 60)}:{int(s % 60):02} {t}" for s, t in zip(starts, titles))
    links = "\n".join(credits[2:])  # Download und Blog stehen schon oben
    (OUT / "youtube.md").write_text(f"""# Titel

G-Code-Analyzer: Was der Slicer wirklich druckt (kostenlos, Windows)

# Beschreibung

Der G-Code-Analyzer zeigt fertige G-Code-Dateien in 3D und prüft, ob Wände, Schalenschichten, Infill und Support so gedruckt werden wie eingestellt. Kostenlos, ohne Installation, offline. Für OrcaSlicer, Bambu Studio und PrusaSlicer (Cura: Grundwerte).

Download: https://github.com/AP-78/gcode-analyzer/releases/latest
Blogbeitrag: https://ap78.de/g-code-analyzer-was-der-slicer-wirklich-druckt/

Kapitel:
{chapters}

{links}
Modell auf Printables: https://www.printables.com/model/361676-playstation-icons-lights-lamp-led-remix

# Tags

G-Code, 3D-Druck, OrcaSlicer, Bambu Studio, PrusaSlicer, Slicer, Wandstärke, Infill, Schalenschichten, Support, G-Code Viewer, Windows, kostenlos
""", encoding="utf-8")
    print(f"Fertig: {total:.1f} s ({int(total // 60)}:{int(total % 60):02})")


if __name__ == "__main__":
    main()
