using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Diapason;

/// <summary>
/// Fenêtre principale = TESTEUR de manettes. Affiche P1 et P2 avec l'état NORMALISÉ
/// (ce que le jeu reçoit en Xbox) : boutons qui s'allument, sticks, gâchettes. Permet de
/// vérifier d'un coup d'œil qu'une manette répond et qu'aucun bouton n'est inversé.
/// Fermer la fenêtre (croix) la réduit dans la zone de notification ; Diapason continue.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly PadPanel[] _pads;
    private readonly Label[] _names;
    private readonly Label _status = new();
    private readonly Label _xinput = new();
    private readonly ToolTip _tip = new();

    public MainForm()
    {
        Text = "Diapason";
        BackColor = Color.FromArgb(21, 15, 36);   // prune sombre (thème logo)
        ForeColor = Color.FromArgb(200, 187, 230);
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Program.LoadAppIcon(32); } catch { /* ignore */ }

        // Barre de menus en haut (Outils / Manettes / Aide).
        var menu = BuildMenu();
        Controls.Add(menu);
        MainMenuStrip = menu;
        const int menuH = 24;

        // Grille de N manettes (max 2 colonnes), fenêtre dimensionnée selon le nombre de slots.
        var n = Program.SlotCount;
        const int padW = 360, padH = 330, nameH = 24, mx = 12, gapX = 16, gapY = 8, top = 38;
        const int rowH = nameH + padH + gapY;
        var cols = Math.Min(n, 2);
        var rows = (n + cols - 1) / cols;
        var width = mx + cols * padW + (cols - 1) * gapX + mx;
        var contentTop = menuH + top;
        var btnY = contentTop + rows * rowH;
        const int xinfoH = 48;   // bande basse : manettes Xbox détectées (lues directement, hors P1/P2)
        ClientSize = new Size(width, btnY + 44 + xinfoH);

        _status.SetBounds(0, menuH, width, 30);
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        Controls.Add(_status);

        _pads = new PadPanel[n];
        _names = new Label[n];
        for (var i = 0; i < n; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var x = mx + col * (padW + gapX);
            var y = contentTop + row * rowH;
            var name = new Label
            {
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            };
            name.SetBounds(x, y, padW, nameH);
            var pad = new PadPanel { Tag = i, AllowDrop = true, Accent = AccentForSlot(i, n) };
            pad.SetBounds(x, y + nameH, padW, padH);
            pad.MouseDown += Pad_MouseDown;
            pad.DragEnter += Pad_DragEnter;
            pad.DragLeave += Pad_DragLeave;
            pad.DragDrop  += Pad_DragDrop;
            _tip.SetToolTip(pad, "Glisse une manette sur une autre pour échanger leur ordre (P1 / P2…).");
            _names[i] = name;
            _pads[i] = pad;
            Controls.Add(name);
            Controls.Add(pad);
        }

        var swap = new Button
        {
            Text = "Échanger P1 / P2   (Ctrl+Alt+S)",
            Size = new Size(220, 28),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(220, 205, 240),
            BackColor = Color.FromArgb(46, 37, 70),
        };
        swap.Location = new Point((width - swap.Width) / 2, btnY + 6);
        swap.FlatAppearance.BorderColor = Color.FromArgb(70, 58, 102);
        swap.Click += (_, _) => Program.DoSwap();
        Controls.Add(swap);

        // Bande basse : manettes Xbox/XInput physiques détectées (lues directement par le jeu, pas de slot).
        _xinput.SetBounds(mx, btnY + 40, width - 2 * mx, xinfoH);
        _xinput.AutoSize = false;
        _xinput.TextAlign = ContentAlignment.MiddleCenter;
        _xinput.ForeColor = Color.FromArgb(170, 180, 242);   // pervenche clair
        _xinput.Font = new Font("Segoe UI", 8.5f);
        Controls.Add(_xinput);

        _timer = new System.Windows.Forms.Timer { Interval = 33 };  // ~30 fps
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        var v  = Program.GetSlotViews();
        var xs = Program.GetXInputDevices();   // manettes Xbox physiques (affichées dans les slots libres)
        var xi = 0;                            // prochaine Xbox à afficher dans un slot vide

        for (var i = 0; i < _pads.Length && i < v.Length; i++)
        {
            if (v[i].Connected)
            {
                _pads[i].Set(true, v[i].PlayStation, v[i].State);
                _names[i].Text = $"P{i + 1}  —  {v[i].Name}";
                _names[i].ForeColor = Color.FromArgb(241, 236, 251);
            }
            else if (xi < xs.Length)
            {
                // Slot libéré (réduction Xbox) -> on y AFFICHE une manette Xbox détectée (indicatif,
                // lue directement par le jeu ; ce n'est pas un slot P1/P2 géré). État des boutons en live.
                _pads[i].Set(true, false, xs[xi].State);
                _names[i].Text = $"🎮  {xs[xi].Name}  —  manette directe";
                _names[i].ForeColor = Color.FromArgb(170, 180, 242);
                xi++;
            }
            else
            {
                _pads[i].Set(false, false, default);
                _names[i].Text = $"P{i + 1}  —  aucune manette";
                _names[i].ForeColor = Color.FromArgb(129, 117, 160);
            }
        }

        // Bande basse : explication (Xbox affichées dans les slots) + éventuel surplus sans slot libre.
        if (xs.Length == 0)
            _xinput.Text = "";
        else if (xi >= xs.Length)
            _xinput.Text = "🎮 La manette Xbox est lue directement par le jeu (affichée à titre indicatif, hors gestion P1/P2).";
        else
            _xinput.Text = "🎮 Manette(s) Xbox en plus (lues directement) :   " + string.Join("    ", xs.Skip(xi).Select(x => x.Name));

        var (mask, reason) = Program.MaskingStatus();
        if (mask)
        {
            _status.Text = "●  Masquage actif — les manettes physiques sont cachées du jeu";
            _status.ForeColor = Color.FromArgb(180, 166, 236);   // pervenche (thème logo)
            _status.BackColor = Color.FromArgb(36, 29, 56);
        }
        else
        {
            _status.Text = "▲  MASQUAGE INACTIF (" + reason + ") — risque de double input en jeu";
            _status.ForeColor = Color.FromArgb(255, 183, 77);
            _status.BackColor = Color.FromArgb(48, 38, 22);
        }
    }

    // ---- Glisser-déposer pour réordonner les manettes (P1 / P2…) ----
    private void Pad_MouseDown(object? sender, MouseEventArgs e)
    {
        // On ne tire que depuis un slot OCCUPÉ (sinon rien à déplacer), au clic gauche.
        if (e.Button != MouseButtons.Left) return;
        if (sender is not PadPanel p || p.Tag is not int slot || !p.IsConnected) return;
        p.DoDragDrop(slot, DragDropEffects.Move);
    }

    private void Pad_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(typeof(int)) != true) return;
        e.Effect = DragDropEffects.Move;
        if (sender is PadPanel p) p.Highlight = true;   // surligne la cible
    }

    private void Pad_DragLeave(object? sender, EventArgs e)
    {
        if (sender is PadPanel p) p.Highlight = false;
    }

    private void Pad_DragDrop(object? sender, DragEventArgs e)
    {
        if (sender is not PadPanel p || p.Tag is not int dst) return;
        p.Highlight = false;
        if (e.Data?.GetData(typeof(int)) is int src)
            Program.MoveSlot(src, dst);
    }

    // Couleur « allumée » d'un slot : dégradé pervenche (P1) -> rose (P2), comme le logo.
    private static Color AccentForSlot(int i, int n)
    {
        var t = n <= 1 ? 0.0 : (double)i / (n - 1);
        int L(int a, int b) => (int)Math.Round(a + (b - a) * t);
        return Color.FromArgb(L(124, 200), L(135, 121), L(230, 210));   // #7c87e6 -> #c879d2
    }

    // ---- Barre de menus ----
    private static MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.FromArgb(36, 29, 56),   // prune
            ForeColor = Color.FromArgb(220, 205, 240),
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false },
            Padding = new Padding(6, 2, 0, 2),
        };

        var tools = new ToolStripMenuItem("Outils") { ForeColor = Color.Gainsboro };
        tools.DropDownItems.Add(Item("Installer / réparer les pilotes  (ViGEmBus, HidHide…)", InstallDrivers));
        tools.DropDownItems.Add(new ToolStripSeparator());
        var autostart = new ToolStripMenuItem("Démarrage automatique avec Windows") { ForeColor = Color.Gainsboro, Checked = IsAutostartEnabled() };
        autostart.Click += (_, _) => ToggleAutostart(autostart);
        tools.DropDownOpening += (_, _) => autostart.Checked = IsAutostartEnabled();   // reflète l'état réel à l'ouverture
        tools.DropDownItems.Add(autostart);
        tools.DropDownItems.Add(new ToolStripSeparator());
        var count = new ToolStripMenuItem("Nombre de manettes") { ForeColor = Color.Gainsboro };
        count.DropDownItems.Add(Item("2  (défaut — jeux 1v1)", () => Program.SetSlotCount(2)));
        count.DropDownItems.Add(Item("3", () => Program.SetSlotCount(3)));
        count.DropDownItems.Add(Item("4", () => Program.SetSlotCount(4)));
        tools.DropDownItems.Add(count);
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Item("Ouvrir le journal", OpenLog));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Item("Quitter", () => Application.Exit()));

        var pads = new ToolStripMenuItem("Manettes") { ForeColor = Color.Gainsboro };
        pads.DropDownItems.Add(Item("Échanger P1 / P2   (Ctrl+Alt+S)", () => Program.DoSwap()));
        pads.DropDownItems.Add(new ToolStripSeparator());
        var assign = new ToolStripMenuItem("Assigner les manettes (P1 / P2…)") { ForeColor = Color.Gainsboro };
        pads.DropDownItems.Add(assign);
        pads.DropDownOpening += (_, _) => RebuildAssignMenu(assign);   // reconstruit la liste à chaque ouverture

        var help = new ToolStripMenuItem("Aide") { ForeColor = Color.Gainsboro };
        help.DropDownItems.Add(Item("Vérifier les mises à jour", () => Program.OnUpdateClick()));
        help.DropDownItems.Add(Item("À propos de Diapason", About));

        menu.Items.Add(tools);
        menu.Items.Add(pads);
        menu.Items.Add(help);
        return menu;
    }

    private static ToolStripMenuItem Item(string text, Action onClick)
    {
        var it = new ToolStripMenuItem(text) { ForeColor = Color.Gainsboro };
        it.Click += (_, _) => onClick();
        return it;
    }

    // Reconstruit le sous-menu d'assignation : une entrée par manette branchée (avec son slot actuel),
    // chacune permettant de la mettre sur Manette 1, 2… ou de la rendre inactive.
    private static void RebuildAssignMenu(ToolStripMenuItem root)
    {
        root.DropDownItems.Clear();
        var ctrls = Program.GetControllers();
        if (ctrls.Length == 0)
        {
            root.DropDownItems.Add(new ToolStripMenuItem("(aucune manette branchée)") { Enabled = false, ForeColor = Color.Gray });
            return;
        }

        foreach (var c in ctrls.OrderBy(x => x.Slot < 0 ? int.MaxValue : x.Slot))
        {
            var etat = c.Slot >= 0 ? $"P{c.Slot + 1}" : "inactive";
            var sub  = new ToolStripMenuItem($"{c.Name}   —   {etat}") { ForeColor = Color.Gainsboro };
            var id   = c.InstanceId;

            for (var s = 0; s < Program.SlotCount; s++)
            {
                var slot = s;
                var mi = new ToolStripMenuItem($"Mettre sur Manette {s + 1}")
                {
                    ForeColor = Color.Gainsboro,
                    Checked   = c.Slot == s,
                };
                mi.Click += (_, _) => Program.AssignToSlot(id, slot);
                sub.DropDownItems.Add(mi);
            }

            sub.DropDownItems.Add(new ToolStripSeparator());
            var off = new ToolStripMenuItem("Rendre inactive")
            {
                ForeColor = Color.Gainsboro,
                Checked   = c.Slot < 0,
                Enabled   = c.Slot >= 0,
            };
            off.Click += (_, _) => Program.SetInactive(id);
            sub.DropDownItems.Add(off);

            root.DropDownItems.Add(sub);
        }
    }

    // ---- Actions du menu (Diapason tourne en admin -> exécution élevée directe) ----
    private static void InstallDrivers()
    {
        const string cmd =
            "Write-Host 'Installation / reparation des pilotes Diapason...' -ForegroundColor Cyan; " +
            "winget install --id ViGEm.ViGEmBus -e --source winget --accept-source-agreements --accept-package-agreements; " +
            "winget install --id Nefarius.HidHide -e --source winget --accept-source-agreements --accept-package-agreements; " +
            "winget install --id Microsoft.VCRedist.2015+.x64 -e --source winget --accept-source-agreements --accept-package-agreements; " +
            "Write-Host ''; Write-Host 'Termine. Si un pilote vient d''etre installe, REDEMARRE le PC.' -ForegroundColor Green; " +
            "Read-Host 'Appuie sur Entree pour fermer'";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\"",
                UseShellExecute = true,   // fenêtre visible pour suivre la progression
            });
        }
        catch (Exception ex) { Warn("Impossible de lancer l'installation :\n" + ex.Message); }
    }

    // Bascule la case à cocher : active ou désactive selon l'état actuel, puis recale la coche
    // sur l'état RÉEL de la tâche planifiée (au cas où l'opération échoue).
    private static void ToggleAutostart(ToolStripMenuItem item)
    {
        if (item.Checked) DisableAutostart();
        else EnableAutostart();
        item.Checked = IsAutostartEnabled();
    }

    // La tâche planifiée « Diapason » existe-t-elle ? (schtasks renvoie 0 si trouvée, 1 sinon.)
    private static bool IsAutostartEnabled()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                Arguments              = "/query /TN Diapason",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            });
            if (p is null) return false;
            p.WaitForExit(4000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void EnableAutostart()
    {
        var exe = (Environment.ProcessPath ?? "").Replace("'", "''");
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name.Replace("'", "''");
        var ps =
            "$a=New-ScheduledTaskAction -Execute '" + exe + "'; " +
            "$t=New-ScheduledTaskTrigger -AtLogOn; try{$t.Delay='PT20S'}catch{}; " +
            "$p=New-ScheduledTaskPrincipal -UserId '" + user + "' -LogonType Interactive -RunLevel Highest; " +
            "$s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero); " +
            "Register-ScheduledTask -TaskName 'Diapason' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null";
        RunPsHidden(ps, "Démarrage automatique ACTIVÉ.\nDiapason se lancera tout seul à l'ouverture de session.");
    }

    private static void DisableAutostart()
        => RunPsHidden("schtasks /Delete /TN 'Diapason' /F | Out-Null", "Démarrage automatique DÉSACTIVÉ.");

    private static void RunPsHidden(string ps, string okMsg)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(8000);
            MessageBox.Show(okMsg, "Diapason", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { Warn("Échec :\n" + ex.Message); }
    }

    private static void OpenLog()
    {
        var log = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diapason", "diapason.log");
        try
        {
            if (File.Exists(log))
                Process.Start(new ProcessStartInfo { FileName = log, UseShellExecute = true });
            else
                MessageBox.Show("Pas encore de journal.", "Diapason", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private static void About()
        => MessageBox.Show(
            $"Diapason — normaliseur de manettes pour les tournois FGC.\nVersion {Program.Version}\n\n" +
            "Toutes les manettes (Switch, PlayStation, Xbox…) deviennent des manettes Xbox identiques, " +
            "et les manettes physiques sont masquées au jeu (fini les boutons inversés).\n\n" +
            "Association FGC.",
            "À propos de Diapason", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static void Warn(string msg)
        => MessageBox.Show(msg, "Diapason", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    // Palette sombre pour la barre de menus (sinon menu clair par défaut, qui jure avec le thème).
    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        private static readonly Color Bar = Color.FromArgb(36, 29, 56);    // prune
        private static readonly Color Hover = Color.FromArgb(60, 51, 88);  // lilas survol
        private static readonly Color Edge = Color.FromArgb(70, 58, 102);  // bord lilas
        public override Color MenuStripGradientBegin => Bar;
        public override Color MenuStripGradientEnd => Bar;
        public override Color ToolStripDropDownBackground => Bar;
        public override Color ImageMarginGradientBegin => Bar;
        public override Color ImageMarginGradientMiddle => Bar;
        public override Color ImageMarginGradientEnd => Bar;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Bar;
        public override Color MenuItemPressedGradientEnd => Bar;
        public override Color MenuItemBorder => Edge;
        public override Color MenuBorder => Edge;
        public override Color SeparatorDark => Edge;
        public override Color SeparatorLight => Edge;
    }

    /// <summary>Rouvre la fenêtre depuis le tray.</summary>
    public void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        if (!_timer.Enabled) _timer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // La croix réduit dans le tray (Diapason doit continuer de tourner en match).
        // La vraie sortie passe par le menu « Quitter » du tray (Application.Exit).
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _timer.Stop();
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}

/// <summary>Dessine une manette Xbox et allume les contrôles actifs (GDI+).</summary>
internal sealed class PadPanel : Panel
{
    private bool _connected;
    private bool _playstation;
    private bool _highlight;
    private PadState _s;

    /// <summary>Une manette occupe-t-elle ce slot ? (pour n'autoriser le drag que d'un slot rempli).</summary>
    public bool IsConnected => _connected;

    /// <summary>Surligne le panneau comme cible de dépôt (glisser-déposer).</summary>
    public bool Highlight { set { if (_highlight != value) { _highlight = value; Invalidate(); } } }

    // Palette — thème « Diapason » : prune sombre + dégradé pervenche->rose du logo.
    private static readonly Color Bg       = Color.FromArgb(36, 29, 56);    // surface carte (prune)
    private static readonly Color Border   = Color.FromArgb(60, 51, 88);    // contour carte
    private static readonly Color Idle     = Color.FromArgb(94, 84, 120);   // contour au repos (lilas-gris)
    private static readonly Color IdleFill = Color.FromArgb(39, 31, 60);    // remplissage au repos
    private static readonly Color Label    = Color.FromArgb(200, 187, 230); // libellés
    // Couleur « allumée » du panneau : PROPRE À CHAQUE manette (P1 pervenche, P2 rose = bouts du dégradé).
    public Color Accent { get; set; } = Color.FromArgb(139, 127, 208);
    // Boutons de face recolorés dans la famille du logo (4 teintes : bleu->violet->orchidée->rose).
    private static readonly Color AGreen   = Color.FromArgb(142, 152, 238); // A (bas)    — pervenche
    private static readonly Color BRed     = Color.FromArgb(214, 143, 218); // B (droite) — rose
    private static readonly Color XBlue    = Color.FromArgb(169, 140, 224); // X (gauche) — violet
    private static readonly Color YYellow  = Color.FromArgb(207, 150, 224); // Y (haut)   — orchidée
    // Symboles PlayStation (couleurs classiques)
    private enum Sym { Cross, Circle, Square, Triangle }
    private static readonly Color PsBlue  = Color.FromArgb(72, 138, 226);   // ✕
    private static readonly Color PsRed   = Color.FromArgb(231, 76, 76);    // ◯
    private static readonly Color PsPink  = Color.FromArgb(214, 96, 165);   // □
    private static readonly Color PsGreen = Color.FromArgb(64, 196, 153);   // △

    public PadPanel()
    {
        DoubleBuffered = true;
        BackColor = Bg;
    }

    public void Set(bool connected, bool playstation, PadState s)
    {
        _connected = connected;
        _playstation = playstation;
        _s = s;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var bodyPen = new Pen(Border, 2f);
        using (var bg = new SolidBrush(Bg)) g.FillRectangle(bg, ClientRectangle);
        RoundRect(g, bodyPen, null, new Rectangle(6, 6, 348, 318), 22);

        if (_highlight)   // cible de dépôt (glisser-déposer) : contour accentué
        {
            using var hp = new Pen(Accent, 3f);
            RoundRect(g, hp, null, new Rectangle(6, 6, 348, 318), 22);
        }

        if (!_connected)
        {
            using var f = new Font("Segoe UI", 11f);
            using var br = new SolidBrush(Color.FromArgb(129, 117, 160));   // lilas-gris
            var msg = "Branche une manette";
            var sz = g.MeasureString(msg, f);
            g.DrawString(msg, f, br, (Width - sz.Width) / 2f, (Height - sz.Height) / 2f);
            return;
        }

        // Épaules (LB / RB)
        Shoulder(g, new Rectangle(16, 12, 110, 18), "LB", _s.LB);
        Shoulder(g, new Rectangle(234, 12, 110, 18), "RB", _s.RB);
        // Gâchettes (LT / RT) : barres proportionnelles
        Trigger(g, new Rectangle(16, 34, 110, 11), "LT", _s.LT);
        Trigger(g, new Rectangle(234, 34, 110, 11), "RT", _s.RT);

        // Stick gauche (haut-gauche)
        Stick(g, 74, 128, 34, _s.LX, _s.LY, _s.L3);
        // Croix directionnelle (bas-gauche)
        Dpad(g, 104, 238, _s.Up, _s.Down, _s.Left, _s.Right);
        // Boutons de face (droite) — symboles PlayStation (✕◯□△) si manette PS, sinon lettres Xbox
        if (_playstation)
        {
            SymBtn(g, 288, 96,  Sym.Triangle, _s.Y);   // haut
            SymBtn(g, 288, 160, Sym.Cross,    _s.A);   // bas
            SymBtn(g, 256, 128, Sym.Square,   _s.X);   // gauche
            SymBtn(g, 320, 128, Sym.Circle,   _s.B);   // droite
        }
        else
        {
            FaceBtn(g, 288, 96,  "Y", YYellow, _s.Y);
            FaceBtn(g, 288, 160, "A", AGreen,  _s.A);
            FaceBtn(g, 256, 128, "X", XBlue,   _s.X);
            FaceBtn(g, 320, 128, "B", BRed,    _s.B);
        }
        // Stick droit (bas-milieu)
        Stick(g, 238, 242, 30, _s.RX, _s.RY, _s.R3);
        // Back / Start / Guide (centre)
        Pill(g, new Rectangle(146, 118, 30, 16), _s.Back);
        Pill(g, new Rectangle(192, 118, 30, 16), _s.Start);
        FaceBtn(g, 184, 154, "", Accent, _s.Guide, 12);
    }

    // ---- primitives de dessin ----

    private static void FaceBtn(Graphics g, int cx, int cy, string label, Color color, bool on, int r = 15)
    {
        var rect = new Rectangle(cx - r, cy - r, r * 2, r * 2);
        using var fill = new SolidBrush(on ? color : IdleFill);
        using var pen = new Pen(on ? color : Idle, 2f);
        g.FillEllipse(fill, rect);
        g.DrawEllipse(pen, rect);
        if (label.Length > 0)
        {
            using var f = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var tb = new SolidBrush(on ? Color.Black : Label);
            var sz = g.MeasureString(label, f);
            g.DrawString(label, f, tb, cx - sz.Width / 2f, cy - sz.Height / 2f);
        }
    }

    // Bouton de face PlayStation : dessine ✕ / ◯ / □ / △ (au lieu d'une lettre).
    private static void SymBtn(Graphics g, int cx, int cy, Sym sym, bool on, int r = 15)
    {
        var color = sym switch
        {
            Sym.Cross    => PsBlue,
            Sym.Circle   => PsRed,
            Sym.Square   => PsPink,
            Sym.Triangle => PsGreen,
            _            => PsBlue,
        };
        var rect = new Rectangle(cx - r, cy - r, r * 2, r * 2);
        using (var fill = new SolidBrush(on ? color : IdleFill))
        using (var pen = new Pen(on ? color : Idle, 2f))
        {
            g.FillEllipse(fill, rect);
            g.DrawEllipse(pen, rect);
        }

        var symColor = on ? Color.White : color;
        using var sp = new Pen(symColor, 2.4f)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
        };
        const int s = 7;
        switch (sym)
        {
            case Sym.Cross:
                g.DrawLine(sp, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(sp, cx + s, cy - s, cx - s, cy + s);
                break;
            case Sym.Circle:
                g.DrawEllipse(sp, cx - s, cy - s, s * 2, s * 2);
                break;
            case Sym.Square:
                g.DrawRectangle(sp, cx - s, cy - s, s * 2, s * 2);
                break;
            case Sym.Triangle:
                g.DrawPolygon(sp, new[]
                {
                    new Point(cx, cy - s - 1),
                    new Point(cx - s - 1, cy + s),
                    new Point(cx + s + 1, cy + s),
                });
                break;
        }
    }

    private void Shoulder(Graphics g, Rectangle rect, string label, bool on)
    {
        using var fill = new SolidBrush(on ? Accent : IdleFill);
        using var pen = new Pen(on ? Accent : Idle, 2f);
        RoundRect(g, pen, fill, rect, 8);
        using var f = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var tb = new SolidBrush(on ? Color.Black : Label);
        var sz = g.MeasureString(label, f);
        g.DrawString(label, f, tb, rect.X + (rect.Width - sz.Width) / 2f, rect.Y + (rect.Height - sz.Height) / 2f);
    }

    private void Trigger(Graphics g, Rectangle rect, string label, byte value)
    {
        using var bgPen = new Pen(Idle, 1.5f);
        using var bgFill = new SolidBrush(IdleFill);
        RoundRect(g, bgPen, bgFill, rect, 5);
        if (value > 0)
        {
            var w = (int)(rect.Width * (value / 255f));
            if (w > 0)
            {
                using var fb = new SolidBrush(Accent);
                RoundRect(g, null, fb, new Rectangle(rect.X, rect.Y, w, rect.Height), 5);
            }
        }
        using var f = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var tb = new SolidBrush(Label);
        g.DrawString(label, f, tb, rect.X + 3, rect.Y - 1);
    }

    private void Stick(Graphics g, int cx, int cy, int r, short ax, short ay, bool pressed)
    {
        var ring = new Rectangle(cx - r, cy - r, r * 2, r * 2);
        using (var pen = new Pen(pressed ? Accent : Idle, 2.5f))
        using (var fill = new SolidBrush(IdleFill))
        {
            g.FillEllipse(fill, ring);
            g.DrawEllipse(pen, ring);
        }
        var maxOff = r - 7;
        var dx = (int)(ax / 32767f * maxOff);
        var dy = (int)(-ay / 32767f * maxOff);   // ay : haut = positif -> écran : vers le haut = -y
        var dotR = 9;
        var dot = new Rectangle(cx + dx - dotR, cy + dy - dotR, dotR * 2, dotR * 2);
        using var db = new SolidBrush(pressed ? Accent : Color.FromArgb(129, 117, 160));
        g.FillEllipse(db, dot);
    }

    private void Dpad(Graphics g, int cx, int cy, bool up, bool down, bool left, bool right)
    {
        const int arm = 22, t = 16;
        Arrow(g, new Rectangle(cx - t / 2, cy - arm, t, arm), up);
        Arrow(g, new Rectangle(cx - t / 2, cy, t, arm), down);
        Arrow(g, new Rectangle(cx - arm, cy - t / 2, arm, t), left);
        Arrow(g, new Rectangle(cx, cy - t / 2, arm, t), right);
    }

    private void Arrow(Graphics g, Rectangle rect, bool on)
    {
        using var fill = new SolidBrush(on ? Accent : IdleFill);
        using var pen = new Pen(on ? Accent : Idle, 1.5f);
        g.FillRectangle(fill, rect);
        g.DrawRectangle(pen, rect);
    }

    private void Pill(Graphics g, Rectangle rect, bool on)
    {
        using var fill = new SolidBrush(on ? Accent : IdleFill);
        using var pen = new Pen(on ? Accent : Idle, 2f);
        RoundRect(g, pen, fill, rect, rect.Height / 2);
    }

    private static void RoundRect(Graphics g, Pen? pen, Brush? fill, Rectangle r, int radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        if (fill != null) g.FillPath(fill, path);
        if (pen != null) g.DrawPath(pen, path);
    }
}
