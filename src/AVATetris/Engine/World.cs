namespace AVATetris.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>
/// The controls: left or right, turn clockwise, turn back, soft drop (fall faster while held) and hard drop (straight
/// down, at once).
/// </summary>
public readonly record struct Controls(int Dx = 0, bool Turn = false, bool Back = false, bool Soft = false, bool Hard = false);

/// <summary>
/// Everything in the game that moves or changes, kept together so it can be copied whole: the stack, the piece
/// falling and the next one, the score, lines and level, and the timers.
/// </summary>
public sealed class World
{
    public Stack Stack = new();
    public Piece? Falling;
    public Kind Next;
    /// <summary>The bag the pieces are dealt from: each of the seven once, in a shuffled order, then a new bag.</summary>
    public List<Kind> Bag = [];
    public Dice Dice = new(1);
    public int Score, Lines, Level, StartLevel;
    /// <summary>How many of each piece have come.</summary>
    public int[] Stats = new int[Shapes.Count];
    /// <summary>Counts every piece dealt: a new number is a new piece.</summary>
    public int Dealt;
    public int Tetrises;

    /// <summary>Frames since the piece last fell a row; how long left or right has been held, and which way.</summary>
    public int Fall, Held, HeldDir;
    public bool TurnPrev, BackPrev, HardPrev;
    /// <summary>The rows being cleared, and how far through it is; the pause before the next piece comes.</summary>
    public List<int> Clearing = [];
    public double ClearTime, Entry;
    /// <summary>The well filled to the top: the game is over.</summary>
    public bool Over;
    public double Clock;
    /// <summary>The last clear (1 to 4 rows) and when, for the word that shows.</summary>
    public int LastClear;
    public double LastClearAt = -10;
    public List<Sfx> Heard = [];

    public World Copy() => new()
    {
        Stack = Stack.Copy(), Falling = Falling, Next = Next, Bag = [.. Bag], Dice = Dice, Score = Score, Lines = Lines, Level = Level,
        StartLevel = StartLevel, Stats = [.. Stats], Dealt = Dealt, Tetrises = Tetrises, Fall = Fall, Held = Held, HeldDir = HeldDir,
        TurnPrev = TurnPrev, BackPrev = BackPrev, HardPrev = HardPrev, Clearing = [.. Clearing], ClearTime = ClearTime, Entry = Entry,
        Over = Over, Clock = Clock, LastClear = LastClear, LastClearAt = LastClearAt,
    };

    /// <summary>Where the falling piece would land.</summary>
    public Piece? Ghost => Falling is { } p ? Stack.Drop(p) : null;
}
