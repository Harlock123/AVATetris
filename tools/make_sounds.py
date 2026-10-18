#!/usr/bin/env python3
"""Synthesises AVATetris's sound effects and tunes into ../src/AVATetris/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices. The music in play
is Korobeiniki, the Russian folk song (1861), quicker when the stack nears the top; the title's balalaika dance is a new
composition. The two words are spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVATetris", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def korobeiniki():
    """Korobeiniki (the Russian folk song, 1861): the two halves of the tune, and a bass line under them."""
    q, e, d = 1, 0.5, 1.5
    a = [("E5", q), ("B4", e), ("C5", e), ("D5", q), ("C5", e), ("B4", e),
         ("A4", q), ("A4", e), ("C5", e), ("E5", q), ("D5", e), ("C5", e),
         ("B4", d), ("C5", e), ("D5", q), ("E5", q),
         ("C5", q), ("A4", q), ("A4", q), ("-", q)]
    b = [("D5", d), ("F5", e), ("A5", q), ("G5", e), ("F5", e),
         ("E5", d), ("C5", e), ("E5", q), ("D5", e), ("C5", e),
         ("B4", q), ("B4", e), ("C5", e), ("D5", q), ("E5", q),
         ("C5", q), ("A4", q), ("A4", q), ("-", q)]

    def bar(lo, hi, n=4):
        return [(lo, e), (hi, e)] * n

    ba = bar("E2", "E3") + bar("A2", "A3") + bar("G#2", "G#3", 2) + bar("E2", "E3", 2) + bar("A2", "A3")
    bb = bar("D2", "D3") + bar("C2", "C3") + bar("B1", "B2", 2) + bar("G#2", "G#3", 2) + bar("A2", "A3")
    return a + a + b + a, ba + ba + bb + ba


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The piece.
    write("Move.wav", tone(0.025, 1200, 1100, "square", attack=0.001, release=1.0, gain=0.5), 0.1)
    write("Turn.wav", cat(tone(0.02, 700, kind="square", release=0.4, gain=0.5), tone(0.03, 1050, kind="square", release=0.8, gain=0.5)), 0.12)
    write("Land.wav", mix(tone(0.08, 220, 110, "square", attack=0.001, release=1.0, gain=0.6), tone(0.05, 900, 300, "noise", attack=0.001, release=1.0, gain=0.4)), 0.22)
    write("Pick.wav", tone(0.04, hz("A5"), kind="pulse", release=0.6), 0.15)
    # Rows going: a rising sweep; four at once, a bigger one and a chord.
    write("Line.wav", cat(tone(0.18, 300, 1500, "square", attack=0.002, release=0.3, gain=0.5), tone(0.12, 1500, 900, "noise", release=1.0, gain=0.3)), 0.24)
    write("Tetris.wav", cat(*[tone(0.05, hz(n), kind="pulse", release=0.3) for n in ["A4", "C5", "E5", "A5", "C6", "E6"]],
                            mix(tone(0.5, hz("A5"), kind="pulse", vibrato=0.01, release=0.6), tone(0.5, hz("E6"), kind="square", release=0.6, gain=0.5))), 0.3)
    write("LevelUp.wav", cat(*[tone(0.07, hz(n), kind="square", release=0.4) for n in ["E5", "G#5", "B5", "E6"]]), 0.22)
    write("Topped.wav", cat(*[tone(0.05, 900 - i * 30, kind="square", release=0.3, gain=0.6) for i in range(24)], tone(0.4, 80, 40, "sine", release=0.9)), 0.34)
    # The voice: four rows at once, and a new level.
    for name, words, pitch in [("SayTetris", "T EH1 T R IH S !", 120), ("SayLevel", "L EH1 V AH L , AH1 P !", 112)]:
        write(name + ".wav", mix(say(words, rate=RATE, pitch=pitch, speed=1.0, size=1.0), [s * 0.3 for s in say(words, rate=RATE, pitch=pitch * 1.5, speed=1.0, size=1.0, seed=11)]), 0.5)

    # In play: Korobeiniki; faster when the stack is near the top.
    lead, bass = korobeiniki()
    write("Music.wav", tune(144, lead, bass, kind="pulse"), 0.26)
    write("MusicFast.wav", tune(196, lead, bass, kind="square"), 0.26)
    # The title: a tune of its own, a balalaika dance in A minor that quickens.
    q = 1
    dance = [("A4", q), ("C5", q), ("E5", q), ("E5", q), ("D5", q), ("C5", q), ("B4", q), ("B4", q),
             ("G4", q), ("B4", q), ("D5", q), ("D5", q), ("C5", q), ("B4", q), ("A4", 2),
             ("E5", q), ("A5", q), ("A5", q), ("G5", q), ("F5", q), ("E5", q), ("D5", q), ("D5", q),
             ("C5", q), ("E5", q), ("D5", q), ("B4", q), ("A4", 2), ("-", 2)]
    # Balalaika: each note picked fast, again and again.
    trem = [(n, 0.25) for n, b in dance for _ in range(int(b * 4))]
    low = [("A2", 1), ("E3", 1)] * 4 + [("G2", 1), ("D3", 1)] * 2 + [("A2", 1), ("E3", 1)] * 2 + \
          [("A2", 1), ("E3", 1)] * 2 + [("D3", 1), ("A2", 1)] * 2 + [("E2", 1), ("B2", 1)] * 2 + [("A2", 2), ("-", 2)]
    write("TitleTune.wav", cat(tune(120, trem, low, kind="pulse"), tune(150, trem, low, kind="pulse"), silence(1.0)), 0.3)
    write("Victory.wav", tune(150, [("E5", 0.5), ("A5", 0.5), ("C6", 0.5), ("E6", 1.5), ("D6", 0.5), ("C6", 0.5), ("B5", 0.5), ("A5", 3)],
                              [("A3", 1), ("E3", 1), ("F3", 1), ("D3", 1), ("E3", 1), ("A2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("A4", 1), ("E4", 1), ("A4", 3)], [("A2", 2), ("F2", 2), ("A1", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
