namespace AVATetris.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. For each new piece it tries every way the piece can be
/// turned and every column it can be slid to, drops it there in its head, and scores the stack it would leave (the
/// way Pierre Dellacherie's player did, with El-Ashi's weights): low landings and cleared rows are good; holes,
/// ragged edges and deep wells are bad. While the stack is low it keeps the right-hand column clear for the long I,
/// to take four rows at once. Then it turns the piece, slides it across, and drops it.
/// </summary>
public sealed class Pilot
{
    private int _for = -1;
    private int _turns, _col;
    private bool _pressed;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _for = -1;
        _pressed = false;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        if (w.Falling is not { } p || w.Over) { _pressed = false; return default; }
        if (_for != w.Dealt)
        {
            _for = w.Dealt;
            (_turns, _col) = Choose(w.Stack, p, w.Next);
            Doing = $"{_turns} turns, to column {_col}";
        }
        // Buttons work on being pressed, so let go between presses.
        if (_pressed) { _pressed = false; return default; }
        if (_turns > 0)
        {
            _pressed = true;
            _turns--;
            return new Controls(Turn: true);
        }
        if (p.X != _col)
        {
            // Tap across one square a press: no waiting for the repeat.
            _pressed = true;
            return new Controls(Math.Sign(_col - p.X));
        }
        _pressed = true;
        return new Controls(Hard: true);
    }

    /// <summary>
    /// The best place for the piece: (how many clockwise turns, which column for its box). Each choice is scored with the
    /// next piece's best place after it.
    /// </summary>
    public static (int Turns, int Col) Choose(Stack s, Piece p, Kind? next = null)
    {
        double best = double.NegativeInfinity;
        (int, int) pick = (0, p.X);
        bool build = Building(s);
        foreach (var (turns, col, landed) in Places(s, p))
        {
            var after = s.Copy();
            double score = Value(after, landed, build);
            if (double.IsNegativeInfinity(score)) continue;
            if (next is { } n)
            {
                double follow = double.NegativeInfinity;
                var np = Piece.Spawn(n);
                if (after.Fits(np))
                    foreach (var (_, _, l2) in Places(after, np))
                        follow = Math.Max(follow, Value(after.Copy(), l2, build));
                score += follow;
            }
            if (score > best) { best = score; pick = (turns, col); }
        }
        return pick;
    }

    /// <summary>Every place the piece can get to: turned at the top, slid across as far as it will go, dropped.</summary>
    public static IEnumerable<(int Turns, int Col, Piece Landed)> Places(Stack s, Piece p)
    {
        var seen = new HashSet<(int, int)>();
        var t = p;
        for (int turns = 0; turns < 4; turns++)
        {
            if (turns > 0)
            {
                if (s.Turn(t, 1) is not { } n) break;
                t = n;
            }
            if (p.K == Kind.O && turns > 0) break;
            foreach (int dir in new[] { -1, 1 })
            {
                var m = t;
                while (true)
                {
                    if (seen.Add((m.Rot, m.X))) yield return (turns, m.X, s.Drop(m));
                    if (!s.Fits(m.Moved(dir, 0))) break;
                    m = m.Moved(dir, 0);
                }
            }
        }
    }

    /// <summary>The column kept clear for the long I while the stack is low.</summary>
    public const int WellCol = Field.Cols - 1;

    /// <summary>
    /// While the stack is low and has no holes, it's worth building it up beside an empty column and waiting for the
    /// long I to take four rows at once.
    /// </summary>
    public static bool Building(Stack s) => s.Top() > 9 && Holes(s) == 0;

    /// <summary>Settles the piece in the stack (which it changes) and scores what's left.</summary>
    public static double Value(Stack s, Piece landed, bool build = false)
    {
        if (!s.Settle(landed)) return double.NegativeInfinity;
        var sq = landed.Squares;
        double landing = Field.Rows - sq.Average(c => c.Y);
        var rows = s.FullRows();
        int used = rows.Count(r => sq.Any(c => c.Y == r));
        int eliminated = rows.Count * used;
        if (rows.Count > 0) s.Remove(rows);
        double v = -4.500158825082766 * landing + 3.4181268101392694 * eliminated - 3.2178882868487753 * RowTransitions(s)
                   - 9.348695305445199 * ColTransitions(s) - 7.899265427351652 * Holes(s) - 3.3855972247263626 * Wells(s, build ? WellCol : -1);
        if (build)
        {
            if (rows.Count == 4) v += 60;
            else
            {
                v -= 8 * rows.Count;
                v -= 12 * sq.Count(c => c.X == WellCol);
            }
        }
        return v;
    }

    private static bool Solid(Stack s, int x, int y) => x < 0 || x >= Field.Cols || y >= Field.Rows || y >= 0 && s[x, y] != 0;

    public static int RowTransitions(Stack s)
    {
        int n = 0;
        for (int y = 0; y < Field.Rows; y++)
            for (int x = 0; x <= Field.Cols; x++)
                if (Solid(s, x - 1, y) != Solid(s, x, y)) n++;
        return n;
    }

    public static int ColTransitions(Stack s)
    {
        int n = 0;
        for (int x = 0; x < Field.Cols; x++)
            for (int y = 0; y < Field.Rows; y++)
                if (Solid(s, x, y) != Solid(s, x, y + 1)) n++;
        return n;
    }

    /// <summary>Empty squares with something above them.</summary>
    public static int Holes(Stack s)
    {
        int n = 0;
        for (int x = 0; x < Field.Cols; x++)
        {
            bool roof = false;
            for (int y = 0; y < Field.Rows; y++)
            {
                if (s[x, y] != 0) roof = true;
                else if (roof) n++;
            }
        }
        return n;
    }

    /// <summary>Wells: empty squares with both sides filled, counted 1 + 2 + 3... down each well.</summary>
    public static int Wells(Stack s, int skip = -1)
    {
        int n = 0;
        for (int x = 0; x < Field.Cols; x++)
        {
            if (x == skip) continue;
            int depth = 0;
            for (int y = 0; y < Field.Rows; y++)
            {
                if (s[x, y] == 0 && Solid(s, x - 1, y) && Solid(s, x + 1, y)) n += ++depth;
                else depth = 0;
            }
        }
        return n;
    }
}
