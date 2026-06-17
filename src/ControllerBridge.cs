using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using static SDL2.SDL;

namespace Diapason;

/// <summary>
/// État NORMALISÉ d'une manette (ce qui est envoyé au pad Xbox virtuel = ce que le jeu voit).
/// Rempli par <see cref="ControllerBridge.Sync"/>, lu par l'UI (testeur de manettes).
/// </summary>
internal struct PadState
{
    public bool A, B, X, Y, Back, Guide, Start, L3, R3, LB, RB, Up, Down, Left, Right;
    public short LX, LY, RX, RY;  // -32768..32767 (Y : haut = positif, convention Xbox)
    public byte  LT, RT;          // 0..255
}

/// <summary>
/// Route UNE manette physique (lue + normalisée par SDL) vers UN pad Xbox 360
/// virtuel PERMANENT (= un slot joueur fixe). Le pad n'est NI créé NI détruit ici :
/// il est pré-réservé par Program au démarrage, et peut être réaffecté à chaud
/// (swap P1/P2) en changeant <see cref="Pad"/>. Appeler <see cref="Sync"/> chaque frame.
/// </summary>
internal sealed class ControllerBridge : IDisposable
{
    public string Name { get; }
    public IXbox360Controller? Pad { get; set; } // pad permanent assigné (réaffectable -> swap) ; null = manette inactive
    public bool IsPlayStation { get; }           // -> testeur : afficher ✕◯□△ au lieu de A/B/X/Y

    /// <summary>Dernier état normalisé envoyé au pad (lu par le testeur de manettes).</summary>
    public PadState State;

    private readonly IntPtr _gc;            // SDL_GameController*
    private bool _disposed;

    public ControllerBridge(IntPtr gameController, IXbox360Controller? pad, string name)
    {
        _gc = gameController;
        Pad = pad;
        Name = name;
        IsPlayStation = DetectPlayStation(gameController, name);
    }

    // Détecte une manette PlayStation : type SDL PS5, sinon par le nom (PS4/PS3/DualShock/DualSense).
    private static bool DetectPlayStation(IntPtr gc, string name)
    {
        if (SDL_GameControllerGetType(gc) == SDL_GameControllerType.SDL_CONTROLLER_TYPE_PS5)
            return true;
        var n = (name ?? "").ToLowerInvariant();
        return n.Contains("ps5") || n.Contains("ps4") || n.Contains("ps3")
            || n.Contains("dualsense") || n.Contains("dualshock")
            || n.Contains("playstation") || n.Contains("sony");
    }

    public void Sync()
    {
        if (_disposed) return;
        // Sécurité : ne JAMAIS lire un handle de manette détachée (SDL planterait en natif).
        if (SDL_GameControllerGetAttached(_gc) == SDL_bool.SDL_FALSE) return;

        // Lecture + NORMALISATION. C'est ici que l'inversion A/B des manettes Nintendo est
        // neutralisée : SDL_CONTROLLER_BUTTON_A = toujours le bouton du BAS, peu importe la marque.
        var s = new PadState
        {
            A     = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A),
            B     = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B),
            X     = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X),
            Y     = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y),
            Back  = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK),
            Guide = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE),
            Start = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START),
            L3    = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK),
            R3    = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK),
            LB    = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER),
            RB    = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER),
            Up    = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP),
            Down  = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN),
            Left  = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT),
            Right = Btn(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT),
            LX    = Axis(SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX),
            LY    = InvertAxis(SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY),
            RX    = Axis(SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX),
            RY    = InvertAxis(SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY),
            LT    = Trigger(SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT),
            RT    = Trigger(SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT),
        };

        // Publie l'état pour l'UI (testeur) AVANT d'écrire le pad — struct copié sous le verrou de Program.
        State = s;

        // Manette INACTIVE (aucun pad assigné : « spare » en attente d'affectation via le menu Manettes) :
        // on a lu et publié l'état, mais on n'écrit sur AUCUN pad virtuel.
        var pad = Pad;
        if (pad is null) return;

        pad.SetButtonState(Xbox360Button.A,             s.A);
        pad.SetButtonState(Xbox360Button.B,             s.B);
        pad.SetButtonState(Xbox360Button.X,             s.X);
        pad.SetButtonState(Xbox360Button.Y,             s.Y);
        pad.SetButtonState(Xbox360Button.Back,          s.Back);
        pad.SetButtonState(Xbox360Button.Guide,         s.Guide);
        pad.SetButtonState(Xbox360Button.Start,         s.Start);
        pad.SetButtonState(Xbox360Button.LeftThumb,     s.L3);
        pad.SetButtonState(Xbox360Button.RightThumb,    s.R3);
        pad.SetButtonState(Xbox360Button.LeftShoulder,  s.LB);
        pad.SetButtonState(Xbox360Button.RightShoulder, s.RB);
        pad.SetButtonState(Xbox360Button.Up,            s.Up);
        pad.SetButtonState(Xbox360Button.Down,          s.Down);
        pad.SetButtonState(Xbox360Button.Left,          s.Left);
        pad.SetButtonState(Xbox360Button.Right,         s.Right);

        pad.SetAxisValue(Xbox360Axis.LeftThumbX,  s.LX);
        pad.SetAxisValue(Xbox360Axis.LeftThumbY,  s.LY);
        pad.SetAxisValue(Xbox360Axis.RightThumbX, s.RX);
        pad.SetAxisValue(Xbox360Axis.RightThumbY, s.RY);

        pad.SetSliderValue(Xbox360Slider.LeftTrigger,  s.LT);
        pad.SetSliderValue(Xbox360Slider.RightTrigger, s.RT);

        pad.SubmitReport();
    }

    private bool Btn(SDL_GameControllerButton b) => SDL_GameControllerGetButton(_gc, b) == 1;

    private short Axis(SDL_GameControllerAxis axis) => SDL_GameControllerGetAxis(_gc, axis);

    private short InvertAxis(SDL_GameControllerAxis axis)
    {
        int v = -SDL_GameControllerGetAxis(_gc, axis);
        return (short)Math.Clamp(v, short.MinValue, short.MaxValue); // -(-32768) déborderait sinon
    }

    private byte Trigger(SDL_GameControllerAxis axis)
    {
        var raw = SDL_GameControllerGetAxis(_gc, axis); // 0..32767
        return (byte)(Math.Max(0, (int)raw) >> 7);      // / 128 -> 0..255
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_gc != IntPtr.Zero) SDL_GameControllerClose(_gc);
        // NB : le pad N'est PAS déconnecté ici — il est permanent (géré par Program).
    }
}
