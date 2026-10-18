using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVATetris.Engine;
using AVATetris.Views;
using Controls = AVATetris.Engine.Controls;

namespace AVATetris.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set TETRIS_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avatetris-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("TETRIS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVATetris-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 300)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 48230, 61);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 10);
        for (int i = 0; i < 60 * 600 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Playing()
    {
        var (w, c, g) = Open();
        g.Seed = 3;
        g.Go();
        Assert.True(PlayUntil(g, c, g => g.World.Lines >= 6 && g.World.Stack.Top() < 15 && g.World.Falling is { Y: > 6 }));
        Shot(w, c, "playing");
    }

    [AvaloniaFact]
    public void Four_rows_at_once()
    {
        var (w, c, g) = Open();
        g.Seed = 5;
        g.Go(4);
        Assert.True(PlayUntil(g, c, g => g.World.Clearing.Count == 4 && g.World.ClearTime > 0.12, 900));
        Shot(w, c, "tetris");
    }

    [AvaloniaFact]
    public void Higher_up()
    {
        var (w, c, g) = Open();
        g.Seed = 9;
        g.Go(7);
        Assert.True(PlayUntil(g, c, g => g.World.Lines >= 4 && g.World.Falling is { Y: > 4 } && g.World.Stack.Top() < 16));
        Shot(w, c, "level-7");
    }

    /// <summary>A stack with a few rows left at the top, and the game lost.</summary>
    [AvaloniaFact]
    public void Danger_and_game_over()
    {
        var (w, c, g) = Open();
        g.Seed = 11;
        g.Go(3);
        var s = g.World.Stack;
        for (int y = 5; y < Field.Rows; y++)
            for (int x = 0; x < Field.Cols; x++)
                if ((x * 7 + y * 3) % 10 != 0 && !(y < 9 && x > 6)) s.Sq[x, y] = (byte)(1 + (x + y) % 7);
        for (int i = 0; i < 40; i++) { g.Tick(default); c.Advance(0.05); }
        Shot(w, c, "danger");
        for (int i = 0; i < 60 * 30 && g.Phase != Phase.Topped; i++) { g.Tick(new Controls(Hard: i % 2 == 0)); if (i % 3 == 0) c.Advance(0.05); }
        for (int i = 0; i < 70; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(Phase.Topped, g.Phase);
        Shot(w, c, "topped");
    }

    [AvaloniaFact]
    public void A_rocket_for_a_big_score()
    {
        var (w, c, g) = Open();
        g.Go(9);
        g.World.Score = 61450;
        g.World.Lines = 112;
        g.World.Tetrises = 19;
        g.World.Level = 11;
        g.World.Over = true;
        for (int i = 0; i < 60 * 6 && !(g.State == GameState.GameOver && g.StateTime > 3); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}
