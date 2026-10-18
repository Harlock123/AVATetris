using Avalonia.Input;
using AVATetris.Engine;

namespace AVATetris.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avatetris-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine(int seed = 1) => new(HighScoreTable.Load(TempFolder())) { Silent = true, Seed = seed };

    private static World Fresh(int level = 0, int seed = 1)
    {
        var w = new World();
        Sim.Start(w, level, seed);
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    /// <summary>Fills a row but for the given columns.</summary>
    private static void Row(Stack s, int y, params int[] gaps)
    {
        for (int x = 0; x < Field.Cols; x++) s.Sq[x, y] = gaps.Contains(x) ? (byte)0 : (byte)3;
    }

    // ───────────────────────────── the pieces ─────────────────────────────

    [Fact]
    public void Every_piece_is_four_squares_in_every_turn_and_four_turns_come_round()
    {
        foreach (var k in Enum.GetValues<Kind>())
        {
            for (int r = 0; r < 4; r++)
            {
                var sq = Shapes.Cells[(int)k][r];
                Assert.Equal(4, sq.Distinct().Count());
                Assert.All(sq, c => Assert.InRange(c.X, 0, Shapes.Box(k) - 1));
            }
            int distinct = Enumerable.Range(0, 4).Select(r => string.Join(",", Shapes.Cells[(int)k][r].OrderBy(c => c).Select(c => c.ToString()))).Distinct().Count();
            Assert.Equal(k == Kind.O ? 1 : 4, distinct);
        }
    }

    [Fact]
    public void Pieces_come_in_bags_of_all_seven()
    {
        var w = Fresh(seed: 42);
        var dealt = new List<Kind> { w.Falling!.Value.K };
        for (int i = 0; i < 27; i++)
        {
            Sim.Lock(w, new Piece(Kind.O, 0, 0, 0));
            w.Stack = new Stack();
            w.Clearing.Clear();
            Sim.Spawn(w);
            dealt.Add(w.Falling!.Value.K);
        }
        for (int b = 0; b < 4; b++) Assert.Equal(7, dealt.Skip(b * 7).Take(7).Distinct().Count());
        Assert.Equal(28, w.Stats.Sum());
    }

    [Fact]
    public void A_new_piece_comes_in_at_the_top_in_the_middle()
    {
        foreach (var k in Enum.GetValues<Kind>())
        {
            var p = Piece.Spawn(k);
            Assert.All(p.Squares, c => Assert.InRange(c.X, 3, 6));
            Assert.Equal(0, p.Squares.Min(c => c.Y));
        }
    }

    // ───────────────────────────── moving and turning ─────────────────────────────

    [Fact]
    public void Left_moves_one_square_then_repeats_after_a_moment_and_stops_at_the_wall()
    {
        var w = Fresh(seed: 3);
        w.Falling = Piece.Spawn(Kind.T);
        Sim.Step(w, new Controls(-1));
        Assert.Equal(2, w.Falling!.Value.X);
        Run(w, Sim.RepeatDelay - 2, new Controls(-1));
        Assert.Equal(2, w.Falling!.Value.X);
        Run(w, 2, new Controls(-1));
        Assert.Equal(1, w.Falling!.Value.X);
        Run(w, 20, new Controls(-1));
        Assert.Equal(0, w.Falling!.Value.Squares.Min(c => c.X));
    }

    [Fact]
    public void Turning_goes_clockwise_on_each_press_and_back_with_the_other_button()
    {
        var w = Fresh();
        w.Falling = Piece.Spawn(Kind.T).Moved(0, 5);
        Sim.Step(w, new Controls(Turn: true));
        Assert.Equal(1, w.Falling!.Value.Rot);
        Sim.Step(w, new Controls(Turn: true));
        Assert.Equal(1, w.Falling!.Value.Rot);
        Sim.Step(w, default);
        Sim.Step(w, new Controls(Turn: true));
        Assert.Equal(2, w.Falling!.Value.Rot);
        Sim.Step(w, default);
        Sim.Step(w, new Controls(Back: true));
        Assert.Equal(1, w.Falling!.Value.Rot);
    }

    [Fact]
    public void A_long_piece_against_the_wall_is_nudged_off_it_to_turn()
    {
        var s = new Stack();
        // Upright, hard against the left wall.
        var p = new Piece(Kind.I, 3, -1, 5);
        Assert.True(s.Fits(p));
        Assert.Equal(0, p.Squares.Min(c => c.X));
        var t = s.Turn(p, 1);
        Assert.NotNull(t);
        Assert.Equal(0, t!.Value.Rot);
        Assert.True(s.Fits(t.Value));
        Assert.Equal(0, t.Value.Squares.Min(c => c.X));
    }

    [Fact]
    public void A_piece_with_no_room_to_turn_stays_as_it_is()
    {
        var s = new Stack();
        for (int y = 0; y < Field.Rows; y++)
            for (int x = 0; x < Field.Cols; x++)
                if (x != 4) s.Sq[x, y] = 1;
        var p = new Piece(Kind.I, 1, 2, 8);
        Assert.True(s.Fits(p));
        Assert.Null(s.Turn(p, 1));
    }

    // ───────────────────────────── falling and settling ─────────────────────────────

    [Fact]
    public void Pieces_fall_faster_level_by_level()
    {
        var w = Fresh(0);
        int y = w.Falling!.Value.Y;
        Run(w, 47);
        Assert.Equal(y, w.Falling!.Value.Y);
        Run(w, 1);
        Assert.Equal(y + 1, w.Falling!.Value.Y);
        Assert.Equal(48, Sim.FramesPerRow(0));
        Assert.Equal(6, Sim.FramesPerRow(9));
        Assert.Equal(1, Sim.FramesPerRow(29));
        for (int l = 1; l <= 29; l++) Assert.True(Sim.FramesPerRow(l) <= Sim.FramesPerRow(l - 1));
    }

    [Fact]
    public void Soft_drop_falls_a_row_every_two_frames_for_a_point_a_row()
    {
        var w = Fresh(0);
        int y = w.Falling!.Value.Y;
        Run(w, 20, new Controls(Soft: true));
        Assert.Equal(y + 10, w.Falling!.Value.Y);
        Assert.Equal(10, w.Score);
    }

    [Fact]
    public void Hard_drop_lands_at_once_for_two_points_a_row_and_the_next_piece_follows()
    {
        var w = Fresh(0);
        var p = w.Falling!.Value;
        var next = w.Next;
        int rows = w.Stack.Drop(p).Y - p.Y;
        Sim.Step(w, new Controls(Hard: true));
        Assert.Null(w.Falling);
        Assert.Equal(2 * rows, w.Score);
        Assert.False(w.Stack.Empty);
        Run(w, (int)(Sim.EntrySeconds * 60) + 1);
        Assert.Equal(next, w.Falling!.Value.K);
    }

    [Fact]
    public void A_piece_settles_when_it_cannot_fall_any_further()
    {
        var w = Fresh(9);
        var p = w.Falling!.Value;
        var landed = w.Stack.Drop(p);
        Run(w, 6 * (landed.Y - p.Y) + 6);
        Assert.Null(w.Falling);
        foreach (var (x, y) in landed.Squares) Assert.Equal((int)p.K + 1, w.Stack[x, y]);
    }

    // ───────────────────────────── rows ─────────────────────────────

    [Fact]
    public void A_full_row_flashes_then_goes_and_everything_above_comes_down()
    {
        var w = Fresh(2);
        Row(w.Stack, 19, 4, 5);
        w.Stack.Sq[0, 18] = 5;
        Sim.Lock(w, new Piece(Kind.O, 0, 4, 18));
        Assert.Equal([19], w.Clearing);
        Assert.Equal(40 * 3, w.Score);
        Assert.Equal(1, w.Lines);
        Assert.Contains(Sfx.Line, w.Heard);
        Run(w, (int)(Sim.ClearSeconds * 60) + 2);
        Assert.Empty(w.Clearing);
        Assert.Equal(5, w.Stack[0, 19]);
        Assert.Equal((int)Kind.O + 1, w.Stack[4, 19]);
        Assert.Equal(0, w.Stack[1, 19]);
        Assert.Equal(19, w.Stack.Top());
    }

    [Fact]
    public void Four_rows_at_once_score_most()
    {
        var w = Fresh(1);
        for (int y = 16; y < 20; y++) Row(w.Stack, y, 9);
        Sim.Lock(w, new Piece(Kind.I, 1, 7, 16));
        Assert.Equal(4, w.Clearing.Count);
        Assert.Equal(1200 * 2, w.Score);
        Assert.Equal(1, w.Tetrises);
        Assert.Contains(Sfx.Tetris, w.Heard);
        Assert.Contains(Sfx.SayTetris, w.Heard);
        Run(w, 40);
        Assert.True(w.Stack.Empty);
    }

    [Fact]
    public void Every_ten_lines_is_a_level_but_a_game_started_higher_stays_there_until_it_catches_up()
    {
        var w = Fresh(0);
        w.Lines = 9;
        Row(w.Stack, 19, 9);
        Sim.Lock(w, new Piece(Kind.I, 1, 7, 16));
        Assert.Equal(1, w.Level);
        Assert.Contains(Sfx.LevelUp, w.Heard);

        var h = Fresh(5);
        h.Lines = 30;
        Row(h.Stack, 19, 9);
        Sim.Lock(h, new Piece(Kind.I, 1, 7, 16));
        Assert.Equal(5, h.Level);
        h.Lines = 59;
        Run(h, 40);
        Row(h.Stack, 19, 9);
        Sim.Lock(h, new Piece(Kind.I, 1, 7, 16));
        Assert.Equal(6, h.Level);
    }

    // ───────────────────────────── the end ─────────────────────────────

    [Fact]
    public void No_room_for_the_next_piece_and_the_game_is_over()
    {
        var w = Fresh();
        for (int y = 0; y < Field.Rows; y++) w.Stack.Sq[4, y] = w.Stack.Sq[5, y] = 2;
        Sim.Spawn(w);
        Assert.True(w.Over);
        Assert.Null(w.Falling);
    }

    [Fact]
    public void A_piece_settling_above_the_well_ends_the_game()
    {
        Assert.False(new Stack().Settle(new Piece(Kind.I, 1, 3, -2)));
        var v = Fresh();
        Sim.Lock(v, new Piece(Kind.I, 1, 3, -2));
        Assert.True(v.Over);
    }

    [Fact]
    public void Topping_out_greys_the_well_then_asks_for_initials_for_a_good_score()
    {
        var g = Engine();
        g.Go();
        g.World.Score = 50000;
        for (int y = 0; y < Field.Rows; y++) g.World.Stack.Sq[0, y] = 1;
        g.World.Over = true;
        g.Tick(default);
        Assert.Equal(Phase.Topped, g.Phase);
        g.Run(60 * 3);
        Assert.Equal(Stack.Grey, g.World.Stack[5, 19]);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.OverSeconds) + 2);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('A');
        g.Initials.Type('B');
        g.Initials.Type('C');
        g.Initials.Finish();
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(0, g.NewEntryIndex);
        Assert.Equal("ABC", g.HighScores.Entries[0].Initials);
    }

    // ───────────────────────────── the game around it ─────────────────────────────

    [Fact]
    public void The_title_chooses_the_starting_level()
    {
        var g = Engine();
        g.Tick(new Controls(1));
        g.Tick(default);
        g.Tick(new Controls(1));
        g.Tick(new Controls(1));
        Assert.Equal(2, g.StartLevel);
        for (int i = 0; i < 20; i++) { g.Tick(new Controls(1)); g.Tick(default); }
        Assert.Equal(9, g.StartLevel);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(9, g.World.Level);
    }

    [Fact]
    public void Left_alone_the_title_shows_the_computer_playing_then_the_high_scores()
    {
        var g = Engine();
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run(60 * 20);
        Assert.True(g.World.Dealt > 10);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * (GameEngine.AttractSeconds - 20)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Pause_stops_everything()
    {
        var g = Engine();
        g.Go();
        g.Run(30);
        var p = g.World.Falling;
        g.Pause();
        g.Run(200);
        Assert.Equal(p, g.World.Falling);
        g.Resume();
        g.Run(60);
        Assert.NotEqual(p, g.World.Falling);
    }

    [Fact]
    public void The_music_hurries_when_the_stack_is_near_the_top()
    {
        var g = Engine();
        var heard = new List<Sfx>();
        g.Silent = false;
        g.Sound += heard.Add;
        g.Go();
        g.Tick(default);
        Assert.Contains(Sfx.Music, heard);
        for (int y = GameEngine.DangerRow - 1; y < Field.Rows; y++) g.World.Stack.Sq[0, y] = 1;
        g.Tick(default);
        Assert.Contains(Sfx.MusicFast, heard);
    }

    [Fact]
    public void A_copy_of_the_world_goes_its_own_way()
    {
        var w = Fresh();
        var c = w.Copy();
        Run(c, 200, new Controls(Soft: true));
        Assert.True(w.Stack.Empty);
        Assert.Equal(0, w.Score);
        Assert.NotEqual(w.Score, c.Score);
    }

    [Fact]
    public void The_keys_reach_the_controls()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyUp(Key.Right);
        input.OnKeyDown(Key.Up);
        Assert.True(input.Player.Turn);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Z);
        Assert.True(input.Player.Back);
        input.OnKeyUp(Key.Z);
        input.OnKeyDown(Key.Down);
        Assert.True(input.Player.Soft);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Hard);
        input.OnKeyUp(Key.Space);
        Assert.Equal(default, input.Player);
        input.Dispose();

        // Through to the game: Left slides the piece.
        var g = Engine();
        g.Go();
        int x = g.World.Falling!.Value.X;
        g.Tick(new Controls(-1));
        Assert.Equal(x - 1, g.World.Falling!.Value.X);
    }

    // ───────────────────────────── the computer player ─────────────────────────────

    [Fact]
    public void The_computer_drops_the_long_piece_into_the_gap()
    {
        var s = new Stack();
        for (int y = 16; y < 20; y++) Row(s, y, 6);
        var (turns, col) = Pilot.Choose(s, Piece.Spawn(Kind.I));
        var p = Piece.Spawn(Kind.I);
        for (int i = 0; i < turns; i++) p = s.Turn(p, 1)!.Value;
        p = s.Drop(p with { X = col });
        Assert.All(p.Squares, c => Assert.Equal(6, c.X));
    }

    [Fact]
    public void The_computer_avoids_making_holes()
    {
        var s = new Stack();
        Row(s, 19, 0, 1, 2, 3);
        Row(s, 18, 0, 1, 2, 3, 7, 8, 9);
        var (turns, col) = Pilot.Choose(s, Piece.Spawn(Kind.O));
        var p = s.Drop(Piece.Spawn(Kind.O) with { X = col });
        var after = s.Copy();
        after.Settle(p);
        Assert.Equal(0, Pilot.Holes(after));
        Assert.Equal(0, turns);
    }

    [Fact]
    public void The_computer_clears_lines_for_minutes_and_just_dropping_tops_out_with_none()
    {
        var g = Engine(4);
        g.Go();
        g.Run(1);
        for (int t = 0; t < 60 * 120; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.World.Lines >= 30, $"only {g.World.Lines} lines");
        Assert.True(g.World.Tetrises >= 1);

        var lazy = Engine(4);
        lazy.Go();
        for (int t = 0; t < 60 * 120 && lazy.State == GameState.Playing; t++) lazy.Tick(new Controls(Hard: t % 2 == 0));
        Assert.NotEqual(GameState.Playing, lazy.State);
        Assert.True(lazy.World.Lines <= 2);
    }
}
