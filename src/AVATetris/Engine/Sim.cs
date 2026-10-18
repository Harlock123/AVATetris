namespace AVATetris.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{
    /// <summary>Frames per row at each level, as the 1989 home version had them: from 48 at level 0 to one at 29.</summary>
    private static readonly int[] Speeds = [48, 43, 38, 33, 28, 23, 18, 13, 8, 6, 5, 5, 5, 4, 4, 4, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1];

    public static int FramesPerRow(int level) => Speeds[Math.Clamp(level, 0, Speeds.Length - 1)];

    /// <summary>Holding left or right: one square at once, then after a moment one every couple of frames.</summary>
    public const int RepeatDelay = 10, RepeatEvery = 2;
    /// <summary>Soft drop: a row every two frames at most.</summary>
    public const int SoftFrames = 2;
    public const double ClearSeconds = 0.4, EntrySeconds = 0.15;
    public const int MaxLevel = 29;
    /// <summary>Points for one to four rows at once, times (level + 1).</summary>
    public static readonly int[] ClearPoints = [0, 40, 100, 300, 1200];

    public static void Start(World w, int level = 0, int seed = 1)
    {
        w.Stack = new Stack();
        w.Dice = new Dice(seed);
        w.Bag.Clear();
        w.StartLevel = w.Level = Math.Clamp(level, 0, 9);
        w.Next = Deal(w);
        Spawn(w);
    }

    private static Kind Deal(World w)
    {
        if (w.Bag.Count == 0)
        {
            w.Bag.AddRange(Enum.GetValues<Kind>());
            for (int i = w.Bag.Count - 1; i > 0; i--)
            {
                int j = w.Dice.Next(i + 1);
                (w.Bag[i], w.Bag[j]) = (w.Bag[j], w.Bag[i]);
            }
        }
        var k = w.Bag[0];
        w.Bag.RemoveAt(0);
        return k;
    }

    /// <summary>The next piece in at the top; if there's no room for it, the game is over.</summary>
    public static void Spawn(World w)
    {
        var p = Piece.Spawn(w.Next);
        w.Next = Deal(w);
        w.Stats[(int)p.K]++;
        w.Dealt++;
        w.Fall = 0;
        if (!w.Stack.Fits(p))
        {
            w.Falling = null;
            w.Over = true;
            return;
        }
        w.Falling = p;
    }

    public static void Step(World w, Controls c)
    {
        if (w.Over) return;
        w.Clock += Field.Step;
        bool turn = c.Turn && !w.TurnPrev, back = c.Back && !w.BackPrev, hard = c.Hard && !w.HardPrev;
        w.TurnPrev = c.Turn;
        w.BackPrev = c.Back;
        w.HardPrev = c.Hard;
        int dx = Math.Sign(c.Dx);
        // Left or right held: moves once, waits, then repeats.
        bool move = false;
        if (dx == 0) { w.HeldDir = 0; w.Held = 0; }
        else if (dx != w.HeldDir) { w.HeldDir = dx; w.Held = 0; move = true; }
        else if (++w.Held >= RepeatDelay && (w.Held - RepeatDelay) % RepeatEvery == 0) move = true;

        if (w.Clearing.Count > 0)
        {
            w.ClearTime += Field.Step;
            if (w.ClearTime < ClearSeconds) return;
            w.Stack.Remove(w.Clearing);
            w.Clearing.Clear();
            w.Entry = EntrySeconds;
        }
        if (w.Falling is not { } p)
        {
            w.Entry -= Field.Step;
            if (w.Entry > 0) return;
            Spawn(w);
            if (w.Falling is not { } np) return;
            p = np;
        }

        if (turn || back)
        {
            if (w.Stack.Turn(p, turn ? 1 : -1) is { } t && t != p) { p = t; w.Heard.Add(Sfx.Turn); }
        }
        if (move && w.Stack.Fits(p.Moved(dx, 0)))
        {
            p = p.Moved(dx, 0);
            w.Heard.Add(Sfx.Move);
        }
        if (hard)
        {
            var d = w.Stack.Drop(p);
            w.Score += 2 * (d.Y - p.Y);
            Lock(w, d);
            return;
        }
        int every = FramesPerRow(w.Level);
        bool soft = c.Soft && every > SoftFrames;
        if (soft) every = SoftFrames;
        if (++w.Fall >= every)
        {
            w.Fall = 0;
            if (w.Stack.Fits(p.Moved(0, 1)))
            {
                p = p.Moved(0, 1);
                if (soft) w.Score++;
            }
            else
            {
                Lock(w, p);
                return;
            }
        }
        w.Falling = p;
    }

    /// <summary>The piece settles; full rows start clearing and score.</summary>
    public static void Lock(World w, Piece p)
    {
        w.Falling = null;
        w.Entry = EntrySeconds;
        if (!w.Stack.Settle(p))
        {
            w.Over = true;
            return;
        }
        var rows = w.Stack.FullRows();
        if (rows.Count == 0)
        {
            w.Heard.Add(Sfx.Land);
            return;
        }
        w.Clearing = rows;
        w.ClearTime = 0;
        w.Score += ClearPoints[rows.Count] * (w.Level + 1);
        w.LastClear = rows.Count;
        w.LastClearAt = w.Clock;
        int before = w.Level;
        w.Lines += rows.Count;
        w.Level = Math.Min(MaxLevel, Math.Max(w.StartLevel, w.Lines / 10));
        if (rows.Count == 4)
        {
            w.Tetrises++;
            w.Heard.Add(Sfx.Tetris);
            w.Heard.Add(Sfx.SayTetris);
        }
        else w.Heard.Add(Sfx.Line);
        if (w.Level != before) w.Heard.Add(Sfx.LevelUp);
    }
}
