using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVATetris.Engine;

namespace AVATetris.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVATetris/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double C = Field.Cell;

    /// <summary>The sky behind each level, from a winter dawn round to a white night.</summary>
    private static readonly (uint Top, uint Low, uint Domes)[] Skies =
    [
        (0xFF101838, 0xFF384878, 0xFF20203A), (0xFF1A1030, 0xFF6A3050, 0xFF2A1020), (0xFF0A2020, 0xFF2A6060, 0xFF102828),
        (0xFF201808, 0xFF805A20, 0xFF302010), (0xFF081028, 0xFF2050A0, 0xFF101830), (0xFF280810, 0xFF902838, 0xFF300A10),
        (0xFF101808, 0xFF4A7030, 0xFF182010), (0xFF181028, 0xFF5A4AA0, 0xFF1A1430), (0xFF202020, 0xFF707080, 0xFF282830),
        (0xFF061020, 0xFF103050, 0xFF081018),
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawBackdrop(dc, 4); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawBackdrop(dc, 7); DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawBackdrop(dc, g.World.Level);
                DrawWell(dc, g.World);
                DrawPanels(dc, g.World);
                if (g.Message is { } msg)
                {
                    dc.FillRectangle(Brush(0xC0000000), new Rect(Field.X0, Field.Y0 + 260, Field.Cols * C, 70));
                    Text(dc, msg, new Point(Field.X0 + Field.Cols * C / 2, Field.Y0 + 295), 52, Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xF0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 16), 22, Yellow, center: true);
    }

    // ───────────────────────────── the backdrop ─────────────────────────────

    /// <summary>A sky that changes with the level, stars, and a skyline of onion domes along the bottom.</summary>
    private void DrawBackdrop(DrawingContext dc, int level)
    {
        var sky = Skies[level % Skies.Length];
        const int bands = 24;
        for (int i = 0; i < bands; i++)
            dc.FillRectangle(Brush(Blend(sky.Top, sky.Low, i / (double)(bands - 1))), new Rect(0, i * Field.Height / bands, Field.Width, Field.Height / bands + 1));
        for (int k = 0; k < 70; k++)
        {
            double tw = 0.5 + 0.5 * Math.Sin(_clock * (1 + k % 5) + k);
            dc.FillEllipse(Hash(k, 1, level) % 1024, Hash(k, 2, level) % 480, 0.8 + Hash(k, 3, level) % 2 * 0.7, Alpha(0xFFE8E8FF, 0.3 + 0.6 * tw));
        }
        DrawSkyline(dc, 768, sky.Domes, level);
    }

    private static uint Blend(uint a, uint b, double t)
    {
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>Towers with onion domes, of a cathedral on a square, the way the old box art had it.</summary>
    private static void DrawSkyline(DrawingContext dc, double ground, uint colour, int salt)
    {
        uint lit = Blend(colour, 0xFFFFD070, 0.25);
        double x = -20;
        int i = 0;
        while (x < Field.Width)
        {
            uint h = Hash(i, 7, salt);
            double w = 40 + h % 50, tall = 90 + h / 7 % 160;
            dc.FillRectangle(Brush(colour), new Rect(x, ground - tall, w, tall));
            // Little windows.
            for (int r = 0; r < tall / 30 - 1; r++)
                if (Hash(i, r, salt + 3) % 3 == 0) dc.FillRectangle(Brush(lit), new Rect(x + w / 2 - 3, ground - tall + 16 + r * 30, 6, 12), 3);
            // The drum, and the onion on it.
            double cx = x + w / 2, dw = w * 0.32, top = ground - tall;
            dc.FillRectangle(Brush(colour), new Rect(cx - dw * 0.7, top - 24, dw * 1.4, 26));
            Onion(dc, cx, top - 24, dw, colour);
            x += w + 6 + h / 11 % 30;
            i++;
        }
    }

    private static void Onion(DrawingContext dc, double cx, double baseY, double r, uint colour)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx - r * 0.7, baseY), true);
            c.CubicBezierTo(new Point(cx - r * 1.5, baseY - r * 0.9), new Point(cx - r * 0.2, baseY - r * 1.5), new Point(cx, baseY - r * 2.4));
            c.CubicBezierTo(new Point(cx + r * 0.2, baseY - r * 1.5), new Point(cx + r * 1.5, baseY - r * 0.9), new Point(cx + r * 0.7, baseY));
            c.EndFigure(true);
        }
        dc.DrawGeometry(Brush(colour), null, g);
        dc.DrawLine(Pen(colour, 2), new Point(cx, baseY - r * 2.4), new Point(cx, baseY - r * 2.4 - 14));
        dc.DrawLine(Pen(colour, 2), new Point(cx - 5, baseY - r * 2.4 - 9), new Point(cx + 5, baseY - r * 2.4 - 9));
    }

    // ───────────────────────────── the well ─────────────────────────────

    private void DrawWell(DrawingContext dc, World w)
    {
        double x0 = Field.X0, y0 = Field.Y0, ww = Field.Cols * C, wh = Field.Rows * C;
        // The frame: stone blocks.
        dc.FillRectangle(Brush(0xFF6A6A78), new Rect(x0 - 16, y0 - 8, ww + 32, wh + 24));
        for (int r = 0; r < (wh + 24) / 24; r++)
            for (int side = 0; side < 2; side++)
            {
                double bx = side == 0 ? x0 - 16 : x0 + ww, by = y0 - 8 + r * 24;
                dc.FillRectangle(Brush(0xFF8A8A98), new Rect(bx + 1, by + 1, 14, 22), 2);
            }
        for (int k = 0; k < (ww + 32) / 32; k++)
            dc.FillRectangle(Brush(0xFF8A8A98), new Rect(x0 - 16 + k * 32 + 1, y0 + wh + 1, 30, 14), 2);
        dc.FillRectangle(Brush(0xF0080810), new Rect(x0, y0, ww, wh));
        for (int x = 1; x < Field.Cols; x++) dc.DrawLine(Pen(0xFF141828, 1), new Point(x0 + x * C, y0), new Point(x0 + x * C, y0 + wh));
        for (int y = 1; y < Field.Rows; y++) dc.DrawLine(Pen(0xFF141828, 1), new Point(x0, y0 + y * C), new Point(x0 + ww, y0 + y * C));

        // The stack; rows being cleared flash, then wipe out from the middle.
        double clear = w.Clearing.Count > 0 ? w.ClearTime / Sim.ClearSeconds : 0;
        for (int y = 0; y < Field.Rows; y++)
        {
            bool clearing = w.Clearing.Contains(y);
            for (int x = 0; x < Field.Cols; x++)
            {
                byte sq = w.Stack[x, y];
                if (sq == 0) continue;
                if (clearing)
                {
                    int gone = (int)(clear * 6);
                    if (Math.Abs(x - 4.5) < gone) continue;
                    if ((int)(clear * 14) % 2 == 0 && w.LastClear == 4) { dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(x0 + x * C, y0 + y * C, C, C)); continue; }
                }
                Block(dc, x0 + x * C, y0 + y * C, sq == Stack.Grey ? 0xFF8A8A90 : Shapes.Colour[sq - 1]);
            }
        }
        if (w.Falling is { } p && !w.Over)
        {
            if (w.Ghost is { } gh && gh.Y > p.Y)
                foreach (var (x, y) in gh.Squares)
                    if (y >= 0) dc.DrawRectangle(Brush(Alpha(Shapes.Colour[(int)p.K], 0.18)), Pen(Alpha(Shapes.Colour[(int)p.K], 0.6), 2), new Rect(x0 + x * C + 2, y0 + y * C + 2, C - 4, C - 4));
            foreach (var (x, y) in p.Squares)
                if (y >= 0) Block(dc, x0 + x * C, y0 + y * C, Shapes.Colour[(int)p.K]);
        }
        // The word for a big clear.
        double since = w.Clock - w.LastClearAt;
        if (since < 1.2 && w.LastClear > 0)
        {
            string word = w.LastClear switch { 1 => "SINGLE", 2 => "DOUBLE", 3 => "TRIPLE", _ => "TETRIS!" };
            uint colour = w.LastClear == 4 ? ((int)(_clock * 10) % 2 == 0 ? Yellow : Cyan) : White;
            Text(dc, word, new Point(x0 + ww / 2, y0 + 200 - since * 40), w.LastClear == 4 ? 64 : 40, Alpha(colour, 1.4 - since), center: true);
        }
    }

    /// <summary>One square of a piece: its colour, lit along the top and left, shaded along the bottom and right.</summary>
    private static void Block(DrawingContext dc, double x, double y, uint colour, double size = C)
    {
        double e = Math.Max(2, size / 8);
        dc.FillRectangle(Brush(colour), new Rect(x, y, size, size));
        Poly(dc, Blend(colour, 0xFFFFFFFF, 0.5), null, 0, new Point(x, y), new Point(x + size, y), new Point(x + size - e, y + e), new Point(x + e, y + e), new Point(x + e, y + size - e), new Point(x, y + size));
        Poly(dc, Blend(colour, 0xFF000000, 0.45), null, 0, new Point(x + size, y), new Point(x + size, y + size), new Point(x, y + size), new Point(x + e, y + size - e), new Point(x + size - e, y + size - e), new Point(x + size - e, y + e));
        dc.FillRectangle(Brush(Blend(colour, 0xFFFFFFFF, 0.25)), new Rect(x + e + 2, y + e + 2, size / 5, size / 5));
    }

    /// <summary>A piece drawn on its own (for the next box and the tally), centred on a point.</summary>
    private static void Mini(DrawingContext dc, Kind k, double cx, double cy, double size)
    {
        var sq = Shapes.Cells[(int)k][0];
        double minX = sq.Min(c => c.X), maxX = sq.Max(c => c.X), minY = sq.Min(c => c.Y), maxY = sq.Max(c => c.Y);
        double ox = cx - (minX + maxX + 1) * size / 2, oy = cy - (minY + maxY + 1) * size / 2;
        foreach (var (x, y) in sq) Block(dc, ox + x * size, oy + y * size, Shapes.Colour[(int)k], size);
    }

    // ───────────────────────────── the panels ─────────────────────────────

    private void Panel(DrawingContext dc, Rect r, string title)
    {
        dc.FillRectangle(Brush(0xD0080A18), r, 8);
        dc.DrawRectangle(null, Pen(0xFF8A8A98, 3), r, 8);
        Text(dc, title, new Point(r.X + r.Width / 2, r.Y + 20), 28, Dim, center: true);
    }

    private void DrawPanels(DrawingContext dc, World w)
    {
        var g = Engine;
        // The score.
        Panel(dc, new Rect(40, 72, 270, 200), "SCORE");
        Text(dc, $"{g.Score:000000}", new Point(175, 120), 56, White, center: true);
        Text(dc, "TOP", new Point(70, 172), 26, Dim);
        Text(dc, $"{Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(280, 172), 30, Yellow, right: true);
        Text(dc, "LINES", new Point(70, 208), 26, Dim);
        Text(dc, $"{w.Lines:000}", new Point(280, 208), 30, White, right: true);
        Text(dc, "LEVEL", new Point(70, 244), 26, Dim);
        Text(dc, $"{w.Level:00}", new Point(280, 244), 30, Cyan, right: true);
        // How many of each piece have come.
        Panel(dc, new Rect(40, 292, 270, 420), "STATISTICS");
        for (int k = 0; k < Shapes.Count; k++)
        {
            double y = 350 + k * 52;
            Mini(dc, (Kind)k, 110, y, 16);
            Text(dc, $"{w.Stats[k]:000}", new Point(270, y), 34, Shapes.Colour[k], right: true);
        }
        // The next piece.
        Panel(dc, new Rect(714, 72, 270, 170), "NEXT");
        Mini(dc, w.Next, 849, 165, 30);
        // Fours, and how fast it's falling.
        Panel(dc, new Rect(714, 262, 270, 130), "TETRISES");
        Text(dc, $"{w.Tetrises}", new Point(849, 330), 52, Yellow, center: true);
        Panel(dc, new Rect(714, 412, 270, 130), "SPEED");
        double rate = 60.0 / Sim.FramesPerRow(w.Level);
        Text(dc, $"{rate:0.0} ROWS/S", new Point(849, 480), 34, White, center: true);
        // Danger: the stack near the top.
        if (w.Stack.Top() < GameEngine.DangerRow && g.Phase == Phase.Play && (int)(_clock * 4) % 2 == 0)
            Text(dc, "DANGER!", new Point(849, 590), 52, Red, center: true);
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawBackdrop(dc, 0);
        // Pieces tumbling down behind the title.
        for (int k = 0; k < 9; k++)
        {
            double speed = 40 + Hash(k, 1, 9) % 60, x = 60 + Hash(k, 2, 9) % 900;
            double y = (_clock * speed + Hash(k, 3, 9) % 700) % 860 - 90;
            Mini(dc, (Kind)(k % 7), x, y, 22);
        }
        string title = "AVA TETRIS";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 30 + 30 + i * 60, ty = 120 + Math.Sin(_clock * 2.4 + i * 0.45) * 5;
            uint[] colours = Shapes.Colour;
            Text(dc, title[i].ToString(), new Point(tx, ty), 120, colours[i % colours.Length], center: true);
        }
        Text(dc, "AFTER ALEXEY PAJITNOV'S 1984 GAME", new Point(cx, 196), 26, White, center: true);
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 360, 236, 720, 104), 10);
        Text(dc, "THE PIECES FALL. TURN THEM AND SLIDE THEM SO THEY FILL", new Point(cx, 258), 26, White, center: true);
        Text(dc, "WHOLE ROWS, AND THE ROWS VANISH. FOUR AT ONCE SCORES MOST.", new Point(cx, 286), 26, White, center: true);
        Text(dc, "LET THE STACK REACH THE TOP AND THE GAME IS OVER.", new Point(cx, 314), 26, White, center: true);
        // The level to start at.
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 200, 368, 400, 64), 10);
        Text(dc, "START LEVEL", new Point(cx, 386), 24, Dim, center: true);
        Text(dc, $"<  {Engine.StartLevel}  >", new Point(cx, 414), 38, Cyan, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 470), 44, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 700, Field.Width, 68));
        Text(dc, "LEFT/RIGHT: SLIDE   UP OR X: TURN   Z: TURN BACK   DOWN: FASTER   SPACE: DROP   P: PAUSE   M: SOUND", new Point(cx, 718), 19, 0xFFB0A0D0, center: true);
        Text(dc, "MUSIC: KOROBEINIKI (RUSSIAN FOLK SONG, 1861), AND A TUNE OF ITS OWN", new Point(cx, 746), 18, Dim, center: true);
    }

    /// <summary>Game over: the well greyed out, and the score; a rocket goes up for a big one.</summary>
    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2, t = g.StateTime;
        DrawBackdrop(dc, 9);
        if (g.Score >= 30000)
        {
            // A rocket from behind the domes.
            double y = 700 - Math.Max(0, t - 0.6) * 160;
            double rx = cx + 260;
            Poly(dc, 0xFFE0E0E8, 0xFF606070, 2, new Point(rx, y - 120), new Point(rx + 18, y - 80), new Point(rx + 18, y), new Point(rx - 18, y), new Point(rx - 18, y - 80));
            Poly(dc, 0xFFC03030, null, 0, new Point(rx - 18, y - 10), new Point(rx - 34, y + 14), new Point(rx - 18, y + 4));
            Poly(dc, 0xFFC03030, null, 0, new Point(rx + 18, y - 10), new Point(rx + 34, y + 14), new Point(rx + 18, y + 4));
            for (int k = 0; k < 6; k++)
                dc.FillEllipse(rx + Math.Sin(_clock * 30 + k) * 6, y + 14 + k * 12, 12 - k * 1.6, Alpha(k < 2 ? 0xFFFFF0A0 : 0xFFFF8020, 1 - k / 7.0));
            DrawSkyline(dc, 768, Skies[9].Domes, 9);
        }
        Text(dc, "GAME OVER", new Point(cx, 220), 110, (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 320), 46, White, center: true);
        Text(dc, $"{g.World.Lines} LINES  -  LEVEL {g.World.Level}  -  {g.World.Tetrises} TETRISES", new Point(cx, 372), 30, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 330, 70, 660, 640), 12);
        Text(dc, "THE BEST STACKERS", new Point(cx, 120), 64, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   LINES", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 330, 140, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
