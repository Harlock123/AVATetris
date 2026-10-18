namespace AVATetris.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>A moment before the first piece.</summary>
    Start,
    Play,
    /// <summary>The well filled to the top: grey comes down over the stack.</summary>
    Topped,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Move, Turn, Land, Line, Tetris, LevelUp, Topped, Pick,
    SayTetris, SayLevel,
    Music, MusicFast, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 1.4, ToppedSeconds = 1.6, OverSeconds = 4;
    /// <summary>The stack this high (rows from the top) and the music hurries.</summary>
    public const int DangerRow = 6;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true;
    private int _music, _dxPrev, _seed = Environment.TickCount;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    /// <summary>The level a new game starts at, chosen on the title screen (0 to 9).</summary>
    public int StartLevel { get; set; }
    /// <summary>Same pieces every game, for tests.</summary>
    public int? Seed { get; set; }
    public int Score => World.Score;
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        int dx = Math.Sign(c.Dx);
        bool dxPressed = dx != 0 && dx != _dxPrev;
        _dxPrev = dx;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (dxPressed)
                {
                    StartLevel = Math.Clamp(StartLevel + dx, 0, 9);
                    _idle = 0;
                    Play(Sfx.Pick);
                }
                if (startPressed || c.Hard && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(0); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, World.Lines);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame(int level = 0)
    {
        World = new World();
        Sim.Start(World, level, Seed ?? _seed++);
        SetPhase(Phase.Start);
        Message = $"LEVEL {level}";
        MessageTime = StartSeconds;
        Pilot.Reset();
    }

    public void StartGame()
    {
        NewGame(StartLevel);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        // The demonstration starts a few levels up, where it's quicker to watch.
        NewGame(5);
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(0);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(0);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
    }

    /// <summary>0 is quiet, 1 the tune, 2 the tune hurrying (the stack is high).</summary>
    private void Music(int which)
    {
        if (which == _music) return;
        _music = which;
        Play(which switch { 1 => Sfx.Music, 2 => Sfx.MusicFast, _ => Sfx.MusicStop });
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
            {
                int level = w.Level;
                Sim.Step(w, c);
                Music(w.Stack.Top() < DangerRow ? 2 : 1);
                if (w.Level != level)
                {
                    Message = $"LEVEL {w.Level}";
                    MessageTime = 1.6;
                    Play(Sfx.SayLevel);
                }
                if (w.Over)
                {
                    SetPhase(Phase.Topped);
                    Music(0);
                    Play(Sfx.Topped);
                }
                break;
            }
            case Phase.Topped:
                // Grey comes down the well a row at a time.
                int rows = Math.Min(Field.Rows, (int)(PhaseTime / ToppedSeconds * Field.Rows));
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < Field.Cols; x++)
                        w.Stack.Sq[x, y] = Stack.Grey;
                if (PhaseTime > ToppedSeconds + 0.4)
                {
                    // A fanfare for the best score yet; otherwise the sad tune.
                    Play(State == GameState.Playing && Score > HighScores.TopScore ? Sfx.Victory : Sfx.GameOver);
                    if (State == GameState.Attract) { ShowHighScores(-1); return; }
                    State = GameState.GameOver;
                    StateTime = 0;
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a game, past the opening moment.</summary>
    public void Go(int level = 0)
    {
        NewGame(level);
        State = GameState.Playing;
        SetPhase(Phase.Play);
        Message = null;
    }
}
