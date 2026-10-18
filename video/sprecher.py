"""Erzeugt die Sprecherspur aus transkript.md mit Piper (Stimme Thorsten).

Aufruf: python video/sprecher.py  ->  video/out/ton/01.wav, 02.wav, ... (ein WAV pro Abschnitt)
"""
import json
import re
import urllib.request
import wave
from pathlib import Path

from piper import PiperVoice

HERE = Path(__file__).parent
OUT = HERE / "out"
VOICE = OUT / "stimme" / "de_DE-thorsten-high.onnx"
VOICE_URL = "https://huggingface.co/rhasspy/piper-voices/resolve/main/de/de_DE/thorsten/high/"

# Englische Begriffe in Lautschrift (espeak-IPA, nur Laute, die die deutsche Stimme kennt).
# Längere Schreibweisen zuerst, damit Komposita vor ihren Teilen ersetzt werden.
AUSSPRACHE = {
    "G-Code-Analyzer": "dʒˈiːkoʊt ˈɛnɛlaɪzɜ",
    "G-Code-Dateien": "dʒˈiːkoʊt daːtˈaɪən",
    "G-Code": "dʒˈiːkoʊt",
    "Windows-Programm": "vˈɪndoːs pɾoɡɾˈam",
    "OrcaSlicer": "ˈɔːkɑ slˈaɪsɜ",
    "PrusaSlicer": "pɾˈuːzɑ slˈaɪsɜ",
    "Bambu Studio": "bˈambuː stjˈuːdɪoː",
    "Slicers": "slˈaɪsɜs",
    "Slicer": "slˈaɪsɜ",
    "Layer-Regler": "lˈeɪɜ ɾˈeːɡlɜ",
    "Layerhöhe": "lˈeɪɜhˌøːə",
    "Layerzeit": "lˈeɪɜtsˌaɪt",
    "Layern": "lˈeɪɜn",
    "Layer": "lˈeɪɜ",
    "Infill": "ˈɪnfɪl",
    "Support": "səpˈɔːt",
    "Cura": "kjˈʊɾɑ",
    "GitHub": "ɡˈɪthap",
    "Download": "dˈaʊnloːt",
    "Internet": "ˈɪntɜnˌɛt",
    "App": "ˈɛp",
    "Link": "lˈɪŋk",
}
# Pausen in Sekunden: nach Satzende und nach Komma/Doppelpunkt (Piper selbst pausiert dort kaum).
PAUSE_SATZ = 0.6
PAUSE_KOMMA = 0.25
ZAHLEN = {"0,8": "null Komma acht", "0,24": "null Komma zwei vier", "3D": "drei De"}


def for_voice(text: str) -> str:
    for k, v in ZAHLEN.items():
        text = text.replace(k, v)
    pattern = "|".join(re.escape(k) for k in AUSSPRACHE)
    return re.sub(rf"(?<!\w)({pattern})(?!\w)", lambda m: f"[[ {AUSSPRACHE[m.group(1)]} ]]", text)


def sections(md: str) -> list[str]:
    """Gesprochener Text je '## '-Abschnitt; Bildhinweise [..] und das Schlussbild fallen weg."""
    result = []
    for block in re.split(r"^## ", md, flags=re.M)[1:]:
        if block.startswith("Schlussbild"):
            break
        lines = [l for l in block.splitlines()[1:] if l.strip() and not l.startswith("[")]
        result.append(" ".join(lines))
    return result


def load_voice() -> PiperVoice:
    VOICE.parent.mkdir(parents=True, exist_ok=True)
    for suffix in ("", ".json"):
        target = VOICE.with_name(VOICE.name + suffix)
        if not target.exists():
            urllib.request.urlretrieve(VOICE_URL + target.name, target)
    return PiperVoice.load(str(VOICE))


def speak(voice: PiperVoice, text: str, path: Path) -> list[dict]:
    """Schreibt das WAV und gibt je Teilsatz Text, Start und Ende (Sekunden) zurück, für die Untertitel."""
    path.parent.mkdir(parents=True, exist_ok=True)
    rate = voice.config.sample_rate
    audio = bytearray()
    cues = []
    # Teilsätze einzeln sprechen, damit nach jedem Satzzeichen eine echte Pause entsteht.
    for part in re.split(r"(?<=[,:;.!?])\s+", text):
        start = len(audio) / 2 / rate
        for chunk in voice.synthesize(for_voice(part)):
            audio += chunk.audio_int16_bytes
        cues.append({"text": part, "start": round(start, 3), "end": round(len(audio) / 2 / rate, 3)})
        audio += bytes(2 * int(rate * (PAUSE_KOMMA if part[-1] in ",:;" else PAUSE_SATZ)))
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(rate)
        wav.writeframes(audio)
    return cues


if __name__ == "__main__":
    voice = load_voice()
    timings = {}
    for i, text in enumerate(sections((HERE / "transkript.md").read_text(encoding="utf-8")), 1):
        timings[f"{i:02}"] = speak(voice, text, OUT / "ton" / f"{i:02}.wav")
        print(f"{i:02}: {timings[f'{i:02}'][-1]['end']:.1f} s  {text[:50]}")
    (OUT / "ton" / "zeiten.json").write_text(json.dumps(timings, ensure_ascii=False, indent=1), encoding="utf-8")
