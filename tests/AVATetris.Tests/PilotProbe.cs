using AVATetris.Engine;

namespace AVATetris.Tests;

/// <summary>How the computer player gets on (set TETRIS_PROBE to run it, for tuning; TETRIS_PROBE=lazy just drops).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_player_does()
    {
        if (Environment.GetEnvironmentVariable("TETRIS_PROBE") is not { } mode) return;
        foreach (int level in new[] { 0, 9, 15, 19, 29 })
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "tetris-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true, Seed = 7 + level };
            g.Go(Math.Min(level, 9));
            g.World.Level = level;
            g.World.StartLevel = level;
            int t = 0, minutes = 20;
            for (; t < 60 * 60 * minutes && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Hard: t % 2 == 0) : g.Pilot.Decide(g);
                g.Tick(c);
            }
            var w = g.World;
            output.WriteLine($"level {level}: {(g.State == GameState.Playing ? "still going" : "topped out")} after {t / 60}s; {w.Lines} lines, {w.Tetrises} tetrises, " +
                             $"level now {w.Level}, score {g.Score}, {w.Dealt} pieces; {sw.ElapsedMilliseconds} ms");
        }
    }
}
