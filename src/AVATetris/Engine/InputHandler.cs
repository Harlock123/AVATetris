using Avalonia.Input;

namespace AVATetris.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: slide, turn either way, and drop.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// Left and right (or A and D) to slide; Up or X to turn clockwise, Z or Ctrl to turn back; Down (or S) to drop
    /// faster; Space to drop at once. On a gamepad: the stick or D-pad, A to turn, B to turn back, X or D-pad up to drop.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            bool turn = Any(Key.Up, Key.X, Key.W, Key.NumPad8) || pad.ButtonA;
            bool back = Any(Key.Z, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonB;
            bool soft = Any(Key.Down, Key.S, Key.NumPad2) || pad.DPadDown || pad.LeftStickY > 0.5f;
            bool hard = Any(Key.Space) || pad.ButtonX || pad.DPadUp;
            return new Controls(dx, turn, back, soft, hard);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
