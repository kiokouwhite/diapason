using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;

namespace Diapason;

/// <summary>
/// Fenêtre invisible : sert (1) de réceptacle pour le raccourci clavier GLOBAL
/// (Ctrl+Alt+S, qui marche même quand un jeu est au premier plan), et (2) de cible
/// d'Invoke pour mettre à jour l'UI depuis le thread de polling.
/// </summary>
internal sealed class TrayForm : Form
{
    private const int  WM_HOTKEY  = 0x0312;
    private const int  HOTKEY_ID  = 0xB001;
    private const uint MOD_ALT    = 0x0001;
    private const uint MOD_CONTROL= 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_S       = 0x53;

    private readonly Action _onHotkey;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public TrayForm(Action onHotkey)
    {
        _onHotkey = onHotkey;
        ShowInTaskbar   = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition   = FormStartPosition.Manual;
        Location        = new Point(-32000, -32000);   // hors écran
        Size            = new Size(1, 1);
    }

    // Ne jamais afficher cette fenêtre.
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Ctrl+Alt+S global -> swap P1/P2, même en plein jeu.
        RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_S);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            _onHotkey();
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { /* ignore */ } }
        base.Dispose(disposing);
    }
}
