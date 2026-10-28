# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A falling-block puzzle game in the style of Alexey Pajitnov's 1984 Tetris, in C# on .NET 10 and Avalonia 12, drawn
through Avalonia's `DrawingContext`. Seven pieces from shuffled bags, a 10 x 20 well, and level speeds after the 1989 home version. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code, and
the sounds, music and voice are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVATetris.slnx                       # the SDK version is in global.json (10.0.400 or later)
dotnet test AVATetris.slnx                        # every test runs headless: no window opens
dotnet run --project src/AVATetris                # play it; --no-audio for silence
TETRIS_SHOTS=$PWD/Images dotnet test tests/AVATetris.Tests --filter ScreenshotTests   # refresh the README's pictures
TETRIS_PROBE=1 dotnet test tests/AVATetris.Tests --filter PilotProbe --logger "console;verbosity=detailed"
                                                  # how the computer player does (TETRIS_PROBE=lazy: just drops)
python3 tools/make_sounds.py                      # rebuild src/AVATetris/Sounds (standard-library Python only)
```

`tools/speech.py` is a small formant speech synthesiser that `make_sounds.py` imports for the voice.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVATetris/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, initials, high scores), the `Sfx` list and the
    fixed-step loop.
  - `Well.cs` has the measures (`Field`), the seven piece kinds, their `Shapes` and wall kicks, `Piece`, and the
    `Stack` of settled squares.
  - `World.cs` holds a game's state (stack, falling and next piece, bag, score, lines, level), `Controls` and the
    `Dice` generator.
  - `Sim.cs` steps the world: gravity per level, sliding with auto-repeat, soft and hard drops, clearing rows and
    scoring.
  - `Pilot.cs` is the computer player (a Dellacherie-style evaluator), used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVATetris/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024 x 768 picture scaled to the
  window, the well in the middle with the score and the next piece either side, and runs the 60 Hz timer. Ctrl+S
  saves a screenshot to the Pictures folder.
- `src/AVATetris/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVATetris.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay and screenshot tests, and the pilot
  probe.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Field.Step`) from an accumulator, independent of
  the frame rate, so tests can play deterministically. Draw random numbers from the world's seeded `Dice`
  (`w.Dice`, seeded in `Sim.Start`; tests fix it with `GameEngine.Seed`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** `Controls` has no up/down axis: up turns the piece, down is a soft drop,
  and D-pad up (or Space) is a hard drop. Test new controls through `InputHandler`, not by setting `Controls`
  directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVATetris`); tests load the
  table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change the rules or the pilot, run the probe above to see how the computer player does.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release; either then asks the portfolio site to rebuild.
