namespace AVATetris.Engine;

/// <summary>The screen and the well: ten columns by twenty rows, in 32-pixel squares, in the middle of the screen.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int Cols = 10, Rows = 20;
    public const double Cell = 32;
    /// <summary>The well's top left corner on screen.</summary>
    public const double X0 = 352, Y0 = 72;
}

/// <summary>The seven pieces, each four squares.</summary>
public enum Kind { I, O, T, S, Z, J, L }

/// <summary>
/// The pieces' shapes in each of their four turns. Each sits in a box (four squares across for the I, two for the O,
/// three for the rest) and turns about the box's middle, clockwise, down the screen being +y.
/// </summary>
public static class Shapes
{
    public const int Count = 7;

    private static readonly (int X, int Y)[][] Flat =
    [
        [(0, 1), (1, 1), (2, 1), (3, 1)],
        [(0, 0), (1, 0), (0, 1), (1, 1)],
        [(1, 0), (0, 1), (1, 1), (2, 1)],
        [(1, 0), (2, 0), (0, 1), (1, 1)],
        [(0, 0), (1, 0), (1, 1), (2, 1)],
        [(0, 0), (0, 1), (1, 1), (2, 1)],
        [(2, 0), (0, 1), (1, 1), (2, 1)],
    ];

    /// <summary>[kind][turn]: the four squares, within the piece's box.</summary>
    public static readonly (int X, int Y)[][][] Cells = Build();

    public static int Box(Kind k) => k switch { Kind.I => 4, Kind.O => 2, _ => 3 };

    private static (int X, int Y)[][][] Build()
    {
        var all = new (int, int)[Count][][];
        for (int k = 0; k < Count; k++)
        {
            int n = Box((Kind)k);
            all[k] = new (int, int)[4][];
            all[k][0] = Flat[k];
            for (int r = 1; r < 4; r++)
                all[k][r] = all[k][r - 1].Select(c => (n - 1 - c.Item2, c.Item1)).ToArray();
        }
        return all;
    }

    /// <summary>Each piece's colour, as the modern games have them.</summary>
    public static readonly uint[] Colour = [0xFF30E0F0, 0xFFF8D820, 0xFFB040F0, 0xFF40E040, 0xFFF04040, 0xFF3060F0, 0xFFF89020];

    /// <summary>
    /// The nudges tried when a turn doesn't fit where the piece is: none, a square either way, two either way (the long
    /// I needs it against a wall), and up one (off the floor or the stack).
    /// </summary>
    public static readonly (int X, int Y)[] Kicks = [(0, 0), (-1, 0), (1, 0), (-2, 0), (2, 0), (0, -1)];
}

/// <summary>A piece in the well: which, which way turned, and where its box's top left corner is.</summary>
public readonly record struct Piece(Kind K, int Rot, int X, int Y)
{
    public (int X, int Y)[] Squares
    {
        get
        {
            var cells = Shapes.Cells[(int)K][Rot];
            var sq = new (int X, int Y)[4];
            for (int i = 0; i < 4; i++) sq[i] = (X + cells[i].X, Y + cells[i].Y);
            return sq;
        }
    }
    public Piece Moved(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>A new piece at the top of the well, its box in the middle.</summary>
    public static Piece Spawn(Kind k) => new(k, 0, k == Kind.O ? 4 : 3, k == Kind.I ? -1 : 0);
}

/// <summary>The settled squares: 0 is empty, 1 to 7 a square of a piece (its kind + 1), 8 the grey of a lost game.</summary>
public sealed class Stack
{
    public const byte Grey = 8;
    public readonly byte[,] Sq = new byte[Field.Cols, Field.Rows];

    public Stack Copy()
    {
        var s = new Stack();
        Array.Copy(Sq, s.Sq, Sq.Length);
        return s;
    }

    public byte this[int x, int y] => Sq[x, y];

    /// <summary>Whether a square is free: inside the walls and above the floor, and not settled. Above the well is free.</summary>
    public bool Free(int x, int y) => x >= 0 && x < Field.Cols && y < Field.Rows && (y < 0 || Sq[x, y] == 0);

    public bool Fits(Piece p)
    {
        foreach (var (x, y) in p.Squares)
            if (!Free(x, y)) return false;
        return true;
    }

    /// <summary>As far down as the piece will go from where it is.</summary>
    public Piece Drop(Piece p)
    {
        while (Fits(p.Moved(0, 1))) p = p.Moved(0, 1);
        return p;
    }

    /// <summary>Turns the piece (+1 clockwise, -1 back), nudging it if it has to; null if it can't turn.</summary>
    public Piece? Turn(Piece p, int dir)
    {
        if (p.K == Kind.O) return p;
        var t = p with { Rot = (p.Rot + dir + 4) % 4 };
        foreach (var (kx, ky) in Shapes.Kicks)
        {
            if (p.K != Kind.I && Math.Abs(kx) == 2) continue;
            var k = t.Moved(kx, ky);
            if (Fits(k)) return k;
        }
        return null;
    }

    /// <summary>Settles the piece; false if any of it is still above the well (the game is lost).</summary>
    public bool Settle(Piece p)
    {
        bool inside = true;
        foreach (var (x, y) in p.Squares)
        {
            if (y < 0) { inside = false; continue; }
            Sq[x, y] = (byte)((int)p.K + 1);
        }
        return inside;
    }

    public bool Full(int y)
    {
        for (int x = 0; x < Field.Cols; x++)
            if (Sq[x, y] == 0) return false;
        return true;
    }

    public List<int> FullRows()
    {
        var rows = new List<int>();
        for (int y = 0; y < Field.Rows; y++)
            if (Full(y)) rows.Add(y);
        return rows;
    }

    /// <summary>Takes the rows out; everything above comes down.</summary>
    public void Remove(IReadOnlyCollection<int> rows)
    {
        int to = Field.Rows - 1;
        for (int y = Field.Rows - 1; y >= 0; y--)
        {
            if (rows.Contains(y)) continue;
            for (int x = 0; x < Field.Cols; x++) Sq[x, to] = Sq[x, y];
            to--;
        }
        for (; to >= 0; to--)
            for (int x = 0; x < Field.Cols; x++) Sq[x, to] = 0;
    }

    /// <summary>The top of the stack: the first row with anything in it (Rows if empty).</summary>
    public int Top()
    {
        for (int y = 0; y < Field.Rows; y++)
            for (int x = 0; x < Field.Cols; x++)
                if (Sq[x, y] != 0) return y;
        return Field.Rows;
    }

    public bool Empty => Top() == Field.Rows;
}
