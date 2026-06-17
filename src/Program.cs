// =============================================================================
//  Diapason — normalisation des manettes pour les tournois FGC
//
//  SDL lit + NORMALISE chaque manette physique -> recopiée dans une manette Xbox 360
//  VIRTUELLE (ViGEmBus) -> HidHide masque la physique. Le jeu ne voit que des manettes
//  Xbox identiques. Slots P1/P2 déterministes (pads permanents pré-réservés au démarrage).
//
//  MODE TOURNOI : appli sans console (icône barre des tâches). La boucle de lecture
//  tourne sur un thread de fond ; l'icône tray gère l'UI (état P1/P2, swap, quitter).
//  Swap P1/P2 : menu de l'icône, double-clic, ou raccourci GLOBAL Ctrl+Alt+S (même en
//  jeu). Pas de console -> logs dans %LOCALAPPDATA%\Diapason\diapason.log.
//  Nécessite de tourner EN ADMIN (HidHide).
// =============================================================================

using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using static SDL2.SDL;

namespace Diapason;

internal static class Program
{
    private const int PollDelayMs = 1;    // ~1000 Hz
    internal static readonly int SlotCount = ReadSlotCount();   // 2 par défaut ; slots.txt / menu pour 3-4

    private static readonly Dictionary<int, ControllerBridge> Bridges = new();   // SDL instanceId -> bridge (manettes GÉRÉES)
    private static readonly Dictionary<int, ControllerBridge> XInputDevices = new();  // Xbox PHYSIQUES : détectées/affichées, NON gérées (ni pad, ni slot, ni masquage)
    private static readonly HashSet<int> OwnXboxSdlIds = new();                   // instanceId SDL de NOS pads ViGEm (recensés au démarrage) -> distinguer une VRAIE Xbox
    private static readonly HidHideManager HidHide = new();
    private static readonly IXbox360Controller[] Pads = new IXbox360Controller[SlotCount];  // pads permanents (slots)
    private static readonly ControllerBridge?[] Slot = new ControllerBridge?[SlotCount];    // occupant par slot
    private static readonly object Gate = new();      // sync boucle de fond <-> UI
    private static readonly object LogLock = new();

    private static volatile bool _running = true;
    private static ViGEmClient _client = null!;
    private static string _logPath = "";

    // UI
    private static TrayForm _trayForm = null!;
    private static MainForm _mainForm = null!;
    private static NotifyIcon _tray = null!;
    private static ToolStripMenuItem _statusItem = null!;

    [STAThread]
    private static int Main()
    {
        // Forcer le chargement de NOTRE SDL2.dll (2.32, à côté de l'exe) au lieu du 2.0.14
        // embarqué par le binding (qui plante au lancement d'un jeu Steam). AVANT tout appel SDL.
        NativeLibrary.SetDllImportResolver(typeof(SDL2.SDL).Assembly, ResolveSdl2);

        _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Diapason", "diapason.log");
        try { Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!); } catch { /* ignore */ }
        Log("=== Diapason — démarrage ===");

        // Filet : sur exception MANAGÉE non gérée, on réaffiche les manettes (un AV natif SDL,
        // lui, ne passera pas par ici -> voir DEBLOQUER-MANETTES.bat pour le cas extrême).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { Log("UNHANDLED : " + (e.ExceptionObject as Exception)?.Message); HidHide.Cleanup(); } catch { /* ignore */ }
        };

        // --- Hints SDL : OBLIGATOIREMENT avant SDL_Init ---
        // Hot-plug fiable (le thread interne SDL pompe WM_DEVICECHANGE et ne fait que poser un flag).
        SDL_SetHint("SDL_JOYSTICK_THREAD", "1");
        // ANTI-CRASH : couper les backends manettes concurrents de Windows (WGI + RawInput) qui se
        // battent et libèrent une manette pendant qu'on la lit quand un jeu Steam bouscule les
        // périphériques (cause du crash au lancement de GG Strive). On GARDE HIDAPI : c'est lui qui
        // gère proprement Switch Pro / DualSense, donc l'anti-inversion A/B.
        SDL_SetHint("SDL_JOYSTICK_WGI", "0");
        SDL_SetHint("SDL_JOYSTICK_RAWINPUT", "0");
        SDL_SetHint("SDL_JOYSTICK_HIDAPI", "1");
        // Backend GameCube de SDL (adaptateurs Nintendo Wii U/Switch). NB : sans effet avec la SDL2.dll
        // STANDARD (l'adaptateur OFFICIEL exige libusb, non compilé dedans -> passer par Delfinovin) ;
        // le Mayflash en mode PC, lui, passe par le chemin HID/gamecontrollerdb ci-dessous.
        SDL_SetHint("SDL_JOYSTICK_HIDAPI_GAMECUBE", "1");
        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        if (SDL_Init(SDL_INIT_GAMECONTROLLER | SDL_INIT_JOYSTICK) != 0)
        {
            Fatal($"Échec SDL_Init : {SDL_GetError()}");
            return 1;
        }

        // Preuve de la version réellement chargée (doit afficher 2.32.x, pas 2.0.14).
        SDL_GetVersion(out var sdlVer);
        Log($"SDL chargé : {sdlVer.major}.{sdlVer.minor}.{sdlVer.patch}");

        // Mappings manettes communautaires (gamecontrollerdb.txt à côté de l'exe) : élargit la
        // reconnaissance des pads tiers (adaptateurs GameCube en mode PC, manettes génériques…).
        try
        {
            var db = Path.Combine(AppContext.BaseDirectory, "gamecontrollerdb.txt");
            if (File.Exists(db))
            {
                var added = SDL_GameControllerAddMappingsFromFile(db);
                Log(added >= 0 ? $"gamecontrollerdb.txt : {added} mappings chargés"
                              : $"gamecontrollerdb.txt : échec ({SDL_GetError()})");
            }
        }
        catch (Exception ex) { Log("gamecontrollerdb.txt : " + ex.Message); }

        var client = ConnectViGem();   // réessais : le pilote peut être en cours de chargement
        if (client is null)
        {
            Fatal("Impossible de se connecter à ViGEmBus après plusieurs tentatives.\n\n" +
                  "• Si tu viens d'installer les pilotes : REDÉMARRE le PC, puis relance Diapason.\n" +
                  "• Sinon : le pilote ViGEmBus n'est pas installé — relance « 1-Installer-pilotes.ps1 ».");
            SDL_Quit();
            return 2;
        }
        _client = client;

        HidHide.Log = Log;   // les messages HidHide vont dans diapason.log (app sans console)
        HidHide.Initialize(Environment.ProcessPath ?? "");
        var elevated = IsElevated();
        if (!elevated)
            Log("AVERTISSEMENT : Diapason n'est PAS lancé en administrateur — le masquage sera inactif.");

        // Pré-réserver les slots XInput dans l'ordre (1er Connect = P1, 2e = P2). Permanents.
        for (var i = 0; i < SlotCount; i++)
        {
            var pad = _client.CreateXbox360Controller();
            pad.AutoSubmitReport = false;
            pad.Connect();
            Neutralize(pad);
            Pads[i] = pad;
            System.Threading.Thread.Sleep(250);
        }
        Log($"{SlotCount} slots réservés (P1..P{SlotCount}).");

        // Boucle de lecture/écriture des manettes sur un thread de fond.
        var loop = new System.Threading.Thread(PollLoop) { IsBackground = true, Name = "Diapason-Poll" };
        loop.Start();

        // --- Interface : fenêtre cachée (hotkey + invoke) + icône barre des tâches ---
        Application.EnableVisualStyles();

        _trayForm = new TrayForm(DoSwap);
        _ = _trayForm.Handle;   // force la création du handle -> enregistre le raccourci Ctrl+Alt+S

        _mainForm = new MainForm();

        _statusItem = new ToolStripMenuItem("(initialisation…)") { Enabled = false };
        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Ouvrir Diapason", null, (_, _) => _mainForm.ShowFromTray());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        menu.Items.Add(openItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Échanger P1 / P2  (Ctrl+Alt+S)", null, (_, _) => DoSwap()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quitter", null, (_, _) => Application.Exit()));

        _tray = new NotifyIcon
        {
            Icon             = LoadAppIcon(16),
            Visible          = true,
            Text             = "Diapason — normaliseur de manettes",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => _mainForm.ShowFromTray();

        if (HidHide.IsMasking)
        {
            _tray.ShowBalloonTip(2500, "Diapason actif", "Clic droit sur l'icône pour le menu. Ctrl+Alt+S = swap P1/P2.", ToolTipIcon.Info);
        }
        else
        {
            // Masquage inactif = double input invisible -> on ALERTE l'opérateur.
            _tray.Icon = SystemIcons.Warning;
            _tray.Text = "Diapason — MASQUAGE INACTIF (double input possible)";
            var why = elevated
                ? "Le pilote HidHide est absent ou pas encore prêt (redémarre le PC)."
                : "Diapason n'est PAS lancé en administrateur.";
            _tray.ShowBalloonTip(10000, "⚠ Masquage INACTIF — double input possible",
                why + "\nLes manettes physiques ne sont PAS cachées. Relance Diapason en admin " +
                "(2-Lancer-Diapason.bat) ou redémarre le PC.", ToolTipIcon.Warning);
            Log("AVERTISSEMENT : masquage HidHide INACTIF — " + HidHide.Reason);
        }

        PushStatus();
        _mainForm.Show();    // fenêtre testeur à l'ouverture (croix = réduire dans le tray)
        Application.Run();   // boucle de messages jusqu'à « Quitter » (Application.Exit)

        // --- Arrêt propre ---
        _running = false;
        loop.Join(1500);
        HidHide.Cleanup();
        lock (Gate)
        {
            foreach (var b in Bridges.Values) b.Dispose();
            Bridges.Clear();
            foreach (var b in XInputDevices.Values) b.Dispose();
            XInputDevices.Clear();
        }
        foreach (var pad in Pads) { try { pad.Disconnect(); } catch { /* ignore */ } }
        _client.Dispose();
        SDL_Quit();
        _tray.Visible = false;
        _tray.Dispose();
        try { _trayForm.Dispose(); } catch { /* ignore */ }
        try { _mainForm.Dispose(); } catch { /* ignore */ }
        Log("=== Arrêt propre ===");
        return 0;
    }

    private static void PollLoop()
    {
        lock (Gate)
        {
            SDL_GameControllerUpdate();
            SnapshotOwnXboxPads();   // recense NOS pads ViGEm avant de pouvoir reconnaître une vraie Xbox
            RescanControllers();
        }
        var rescanTick = 0;

        while (_running)
        {
            lock (Gate)
            {
                while (SDL_PollEvent(out var ev) == 1)
                {
                    switch (ev.type)
                    {
                        case SDL_EventType.SDL_CONTROLLERDEVICEADDED:   AddController(ev.cdevice.which); break;
                        case SDL_EventType.SDL_CONTROLLERDEVICEREMOVED:
                            RemoveController(ev.cdevice.which);
                            RemoveXInput(ev.cdevice.which);
                            break;
                        case SDL_EventType.SDL_QUIT:                    _running = false; break;
                    }
                }

                SDL_GameControllerUpdate();
                foreach (var bridge in Bridges.Values)
                    bridge.Sync();
                foreach (var x in XInputDevices.Values)
                    x.Sync();   // lecture seule (Pad = null) : juste pour l'affichage dans le testeur

                if (++rescanTick >= 1000)
                {
                    rescanTick = 0;
                    RescanControllers();
                }
            }
            SDL_Delay(PollDelayMs);
        }
    }

    // Déclenché depuis le menu / double-clic / raccourci global (thread UI).
    internal static void DoSwap()
    {
        lock (Gate) SwapP1P2();
    }

    private static int FirstFreeSlot()
    {
        for (var i = 0; i < SlotCount; i++)
            if (Slot[i] is null) return i;
        return -1;
    }

    private static void AddController(int deviceIndex)
    {
        if (SDL_IsGameController(deviceIndex) == SDL_bool.SDL_FALSE)
            return;

        var instanceId = SDL_JoystickGetDeviceInstanceID(deviceIndex);
        if (Bridges.ContainsKey(instanceId) || XInputDevices.ContainsKey(instanceId))
            return;

        var handle = SDL_GameControllerOpen(deviceIndex);
        if (handle == IntPtr.Zero)
        {
            Log($"Ouverture manette {deviceIndex} échouée : {SDL_GetError()}");
            return;
        }

        // Manette Xbox/XInput : soit un de NOS pads ViGEm (à ignorer, sinon boucle infinie de création),
        // soit une VRAIE manette physique. Une vraie Xbox est déjà au bon format (rien à normaliser) ET
        // ne peut PAS être masquée aux jeux XInput -> on la DÉTECTE/AFFICHE seulement, sans pad virtuel,
        // sans slot, sans masquage (elle reste lue directement par le jeu).
        var type = SDL_GameControllerGetType(handle);
        if (type == SDL_GameControllerType.SDL_CONTROLLER_TYPE_XBOX360 ||
            type == SDL_GameControllerType.SDL_CONTROLLER_TYPE_XBOXONE)
        {
            if (OwnXboxSdlIds.Contains(instanceId))   // un de NOS pads virtuels -> ignorer (zéro boucle)
            {
                SDL_GameControllerClose(handle);
                return;
            }
            var xname = SDL_GameControllerName(handle);
            XInputDevices[instanceId] = new ControllerBridge(handle, null, xname);   // Pad = null : lue, pas pilotée
            Log($"[=] « {xname} » détectée (Xbox/XInput physique — lue directement par le jeu, hors P1/P2).");
            PushStatus();
            return;
        }

        var name = SDL_GameControllerName(handle);
        var slot = FirstFreeSlot();   // -1 si tous les slots sont pris -> manette INACTIVE (assignable via le menu)

        // On NE ferme PLUS les manettes en trop : on les garde « inactives » (Pad = null) pour pouvoir
        // les mettre sur P1/P2 à la demande (menu Manettes -> Assigner). Elles sont quand même masquées.
        var bridge = new ControllerBridge(handle, slot >= 0 ? Pads[slot] : null, name);
        Bridges[instanceId] = bridge;
        if (slot >= 0)
        {
            Slot[slot] = bridge;
            Log($"[+] « {name} » -> P{slot + 1}");
        }
        else
        {
            Log($"[+] « {name} » branchée (INACTIVE — {SlotCount} manettes déjà actives ; assignable via le menu Manettes).");
        }

        HidHide.HideController(instanceId, SDL_GameControllerGetVendor(handle), SDL_GameControllerGetProduct(handle));
        PushStatus();
    }

    private static void RescanControllers()
    {
        var present = new HashSet<int>();
        var n = SDL_NumJoysticks();
        for (var i = 0; i < n; i++)
        {
            if (SDL_IsGameController(i) == SDL_bool.SDL_FALSE) continue;
            present.Add(SDL_JoystickGetDeviceInstanceID(i));
            AddController(i);
        }

        foreach (var id in Bridges.Keys.Where(id => !present.Contains(id)).ToList())
            RemoveController(id);
        foreach (var id in XInputDevices.Keys.Where(id => !present.Contains(id)).ToList())
            RemoveXInput(id);
    }

    // Recense les pads Xbox VIRTUELS présents au démarrage (= NOS pads ViGEm créés juste avant, AVANT que
    // les joueurs branchent leurs manettes). Toute Xbox NON recensée ici est ensuite traitée comme une
    // VRAIE manette physique. NB : si une vraie Xbox est déjà branchée au lancement de Diapason, la
    // rebrancher après démarrage pour qu'elle soit détectée comme physique.
    private static void SnapshotOwnXboxPads()
    {
        var n = SDL_NumJoysticks();
        for (var i = 0; i < n; i++)
        {
            if (SDL_IsGameController(i) == SDL_bool.SDL_FALSE) continue;
            var t = SDL_GameControllerTypeForIndex(i);
            if (t == SDL_GameControllerType.SDL_CONTROLLER_TYPE_XBOX360 ||
                t == SDL_GameControllerType.SDL_CONTROLLER_TYPE_XBOXONE)
                OwnXboxSdlIds.Add(SDL_JoystickGetDeviceInstanceID(i));
        }
        Log($"{OwnXboxSdlIds.Count} pad(s) Xbox virtuel(s) recensé(s) (référence anti-boucle ; attendu : {SlotCount}).");
    }

    private static void RemoveController(int instanceId)
    {
        if (!Bridges.TryGetValue(instanceId, out var bridge))
            return;

        Log($"[-] « {bridge.Name} » débranchée");

        var slot = Array.IndexOf(Slot, bridge);
        if (slot >= 0)
        {
            Slot[slot] = null;
            Neutralize(Pads[slot]);   // le pad reste connecté mais en état neutre
        }

        HidHide.UnhideController(instanceId);
        bridge.Dispose();
        Bridges.Remove(instanceId);
        PushStatus();
    }

    // Débranchement d'une manette Xbox/XInput physique (détectée mais non gérée).
    private static void RemoveXInput(int instanceId)
    {
        if (!XInputDevices.TryGetValue(instanceId, out var b)) return;
        Log($"[-] « {b.Name} » (Xbox/XInput) débranchée");
        b.Dispose();
        XInputDevices.Remove(instanceId);
        OwnXboxSdlIds.Remove(instanceId);
        PushStatus();
    }

    // Échange quelle manette physique alimente P1 vs P2 (on échange les SOURCES, pas les
    // slots -> les slots Windows ne bougent pas -> correction 100% fiable).
    private static void SwapP1P2()
    {
        var a = Slot[0];
        var b = Slot[1];
        if (a != null) a.Pad = Pads[1];
        if (b != null) b.Pad = Pads[0];
        Slot[0] = b;
        Slot[1] = a;

        if (Slot[0] is null) Neutralize(Pads[0]);
        if (Slot[1] is null) Neutralize(Pads[1]);

        Log(">>> SWAP P1 <-> P2");
        PushStatus();
    }

    // Glisser-déposer (testeur) : échange les occupants des slots src et dst (et leurs pads).
    // Généralise le swap P1/P2 à n'importe quelle paire de slots.
    internal static void MoveSlot(int src, int dst)
    {
        lock (Gate)
        {
            if (src == dst || src < 0 || dst < 0 || src >= SlotCount || dst >= SlotCount) return;

            var a = Slot[src];
            var b = Slot[dst];
            Slot[dst] = a;
            Slot[src] = b;
            if (a != null) a.Pad = Pads[dst];
            if (b != null) b.Pad = Pads[src];
            if (Slot[src] is null) Neutralize(Pads[src]);
            if (Slot[dst] is null) Neutralize(Pads[dst]);

            Log($">>> glisser-déposer  P{src + 1} <-> P{dst + 1}");
            PushStatus();
        }
    }

    // Force une manette précise sur un slot (P1, P2, …). Utile quand >2 manettes sont branchées.
    // Si le slot cible est déjà occupé : on ÉCHANGE (l'occupant prend l'ancien slot) ; si la manette
    // choisie était inactive, l'occupant devient inactif.
    internal static void AssignToSlot(int instanceId, int targetSlot)
    {
        lock (Gate)
        {
            if (targetSlot < 0 || targetSlot >= SlotCount) return;
            if (!Bridges.TryGetValue(instanceId, out var b)) return;

            var src = Array.IndexOf(Slot, b);   // slot actuel de b, -1 si inactive
            if (src == targetSlot) return;

            var occupant = Slot[targetSlot];

            Slot[targetSlot] = b;
            b.Pad = Pads[targetSlot];

            if (occupant != null && occupant != b)
            {
                if (src >= 0) { Slot[src] = occupant; occupant.Pad = Pads[src]; }  // échange
                else occupant.Pad = null;                                          // évincé -> inactif
            }
            else if (src >= 0)
            {
                Slot[src] = null;          // b libère son ancien slot (aucun occupant à y remettre)
                Neutralize(Pads[src]);
            }

            Log($">>> « {b.Name} » -> P{targetSlot + 1}");
            PushStatus();
        }
    }

    // Sort une manette des slots actifs (elle reste branchée + masquée, mais ne pilote plus de pad).
    internal static void SetInactive(int instanceId)
    {
        lock (Gate)
        {
            if (!Bridges.TryGetValue(instanceId, out var b)) return;
            var src = Array.IndexOf(Slot, b);
            if (src < 0) return;
            Slot[src] = null;
            Neutralize(Pads[src]);
            b.Pad = null;
            Log($">>> « {b.Name} » rendue inactive");
            PushStatus();
        }
    }

    private static void Neutralize(IXbox360Controller pad)
    {
        pad.ResetReport();
        pad.SubmitReport();
    }

    // ---- Statut / UI ----
    private static string BuildStatus()
    {
        var parts = new List<string>();
        for (var i = 0; i < SlotCount; i++)
            parts.Add($"P{i + 1} = {Slot[i]?.Name ?? "(libre)"}");
        parts.Add(HidHide.IsMasking ? "Masquage ON" : "⚠ Masquage OFF");
        return string.Join("   |   ", parts);
    }

    private static void PushStatus()
    {
        string s;
        lock (Gate) { s = BuildStatus(); }   // lock ré-entrant : OK même si déjà tenu
        Log("   " + s);
        try { _trayForm?.BeginInvoke((Action)(() => ApplyStatus(s))); } catch { /* UI pas prête */ }
    }

    private static void ApplyStatus(string s)
    {
        if (_statusItem != null) _statusItem.Text = s;
    }

    // ---- Accès pour la fenêtre testeur (MainForm) ----
    internal struct SlotView { public bool Connected; public string Name; public bool PlayStation; public PadState State; }

    internal static SlotView[] GetSlotViews()
    {
        var v = new SlotView[SlotCount];
        lock (Gate)
            for (var i = 0; i < SlotCount; i++)
            {
                var b = Slot[i];
                v[i] = new SlotView
                {
                    Connected   = b != null,
                    Name        = b?.Name ?? "",
                    PlayStation = b?.IsPlayStation ?? false,
                    State       = b?.State ?? default,
                };
            }
        return v;
    }

    // Liste de TOUTES les manettes branchées (actives + inactives) pour le menu d'assignation.
    internal struct ControllerInfo { public int InstanceId; public string Name; public int Slot; public bool PlayStation; }

    internal static ControllerInfo[] GetControllers()
    {
        lock (Gate)
            return Bridges.Select(kv => new ControllerInfo
            {
                InstanceId  = kv.Key,
                Name        = kv.Value.Name,
                Slot        = Array.IndexOf(Slot, kv.Value),   // -1 = inactive
                PlayStation = kv.Value.IsPlayStation,
            }).ToArray();
    }

    // Manettes Xbox/XInput physiques détectées (affichées dans le testeur, NON gérées).
    internal static (string Name, bool Active)[] GetXInputDevices()
    {
        lock (Gate)
            return XInputDevices.Values.Select(b => (b.Name, IsActive(b.State))).ToArray();
    }

    private static bool IsActive(PadState s) =>
        s.A || s.B || s.X || s.Y || s.LB || s.RB || s.Start || s.Back || s.Guide ||
        s.Up || s.Down || s.Left || s.Right || s.L3 || s.R3 || s.LT > 40 || s.RT > 40 ||
        Math.Abs((int)s.LX) > 12000 || Math.Abs((int)s.LY) > 12000 ||
        Math.Abs((int)s.RX) > 12000 || Math.Abs((int)s.RY) > 12000;

    internal static (bool masking, string reason) MaskingStatus() => (HidHide.IsMasking, HidHide.Reason);

    // Nombre de manettes : 2 par défaut (jeux 1v1, évite les pads fantômes qui gênent GGST).
    // Power-user : un fichier "slots.txt" à côté de l'exe (contenant 3 ou 4) ou le menu Outils.
    private static int ReadSlotCount()
    {
        try
        {
            var f = Path.Combine(AppContext.BaseDirectory, "slots.txt");
            if (File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out var n))
                return Math.Clamp(n, 1, 4);
        }
        catch { /* ignore */ }
        return 2;
    }

    internal static void SetSlotCount(int n)
    {
        n = Math.Clamp(n, 1, 4);
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "slots.txt"), n.ToString()); } catch { /* ignore */ }
        MessageBox.Show($"Nombre de manettes réglé sur {n}.\nFerme et relance Diapason pour l'appliquer.",
            "Diapason", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Connexion à ViGEmBus avec réessais : juste après l'install des pilotes (sans reboot),
    // le bus peut ne pas être encore prêt. 4 essais espacés évitent un faux « driver absent ».
    // Icône de l'app (le logo) depuis la ressource embarquée logo.ico, à la taille demandée.
    internal static Icon LoadAppIcon(int size)
    {
        try
        {
            using var s = typeof(Program).Assembly.GetManifestResourceStream("Diapason.logo.ico");
            if (s != null) return new Icon(s, new Size(size, size));
        }
        catch { /* ignore */ }
        return SystemIcons.Application;
    }

    // Charge NOTRE SDL2.dll voisin de l'exe (2.32) au lieu du 2.0.14 embarqué par le binding.
    private static IntPtr ResolveSdl2(string name, Assembly asm, DllImportSearchPath? path)
    {
        if (name == "SDL2")
        {
            var local = Path.Combine(AppContext.BaseDirectory, "SDL2.dll");
            if (File.Exists(local) && NativeLibrary.TryLoad(local, out var h)) return h;
        }
        return IntPtr.Zero;
    }

    private static ViGEmClient? ConnectViGem()
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try { return new ViGEmClient(); }
            catch (Exception ex)
            {
                Log($"ViGEmBus non joignable (essai {attempt}/4) : {ex.Message}");
                if (attempt < 4) System.Threading.Thread.Sleep(1500);
            }
        }
        return null;
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {msg}";
        lock (LogLock)
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine); } catch { /* ignore */ }
            // Secours : log À CÔTÉ de l'exe (au cas où %LOCALAPPDATA% n'est pas accessible).
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "diapason.log"), line + Environment.NewLine); } catch { /* ignore */ }
        }
    }

    private static void Fatal(string msg)
    {
        Log("FATAL : " + msg);
        try { MessageBox.Show(msg, "Diapason — erreur", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { /* ignore */ }
    }
}
