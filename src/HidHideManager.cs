using Nefarius.Drivers.HidHide;
using Nefarius.Utilities.DeviceManagement.PnP;

namespace Diapason;

/// <summary>
/// Pilote le driver HidHide directement depuis Diapason : se met en liste blanche,
/// active le masquage global, et masque/démasque les manettes physiques que Diapason
/// gère. Objectif : plus AUCUNE manip manuelle dans l'appli HidHide.
///
/// Timing clé (« lire d'abord, masquer ensuite ») : on masque une manette seulement
/// APRÈS que SDL l'ait captée → évite le conflit hot-plug (manette masquée avant
/// d'être vue = non détectée / crash).
///
/// Nécessite que Diapason tourne EN ADMIN (changer la config HidHide exige l'élévation).
/// Sinon : dégradation propre — Diapason normalise quand même, mais sans masquer.
/// L'état (<see cref="IsMasking"/> / <see cref="Reason"/>) est exposé pour que l'UI
/// puisse ALERTER l'opérateur quand le masquage est inactif (sinon double input invisible).
/// </summary>
internal sealed class HidHideManager
{
    // GUID d'interface des périphériques HID (standard Windows).
    private static readonly Guid HidInterface = Guid.Parse("4d1e55b2-f16f-11cf-88cb-001111000030");
    // GUID d'interface des périphériques USB : certaines manettes (Switch Pro) sont AUSSI lues par le
    // jeu/Steam via leur nœud USB parent -> il faut le masquer aussi (masquer le parent cache l'enfant).
    private static readonly Guid UsbInterface = Guid.Parse("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    private readonly HidHideControlService _svc = new();
    // SDL instance id de la manette -> liste des nœuds Windows qu'on a masqués pour elle.
    private readonly Dictionary<int, List<string>> _hiddenByController = new();
    // (vid<<16|pid) -> dernier TickCount d'une ré-énumération, pour éviter une boucle hot-plug.
    private readonly Dictionary<uint, long> _lastRestart = new();

    /// <summary>Logger injecté (vers le fichier de log). Si null → Console (dev).</summary>
    public Action<string>? Log { get; set; }
    private void L(string m) { if (Log != null) Log(m); else Console.WriteLine(m); }

    /// <summary>true si le masquage est réellement actif.</summary>
    public bool IsMasking { get; private set; }
    /// <summary>Raison lisible de l'état courant (pour l'UI / le log).</summary>
    public string Reason { get; private set; } = "non initialisé";

    public void Initialize(string ownExePath)
    {
        if (!_svc.IsInstalled)
        {
            IsMasking = false;
            Reason = "pilote HidHide non installé";
            L("HidHide non installé → pas de masquage (double input possible en jeu).");
            return;
        }

        // Réessais : un autre process (HidHideCLI, watchdog, ancienne instance...) peut tenir le
        // pilote une fraction de seconde au démarrage -> on retente avant d'abandonner le masquage.
        const int attempts = 5;
        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                _svc.AddApplicationPath(ownExePath);  // Diapason autorisé à voir les manettes masquées
                _svc.ClearBlockedInstancesList();      // on repart propre : Diapason gère seul la liste
                _svc.IsActive = true;                  // masquage global ON

                IsMasking = true;
                Reason = "actif";
                L($"HidHide piloté par Diapason : actif + Diapason en liste blanche (essai {i}).");
                return;
            }
            catch (Exception ex)
            {
                IsMasking = false;
                var inner = ex.InnerException != null ? "  ||  inner: " + ex.InnerException.Message : "";
                Reason = ex.GetType().Name + " : " + ex.Message + inner;
                L($"HidHide non pilotable (essai {i}/{attempts}). " + Reason);
                if (i < attempts) System.Threading.Thread.Sleep(700);
            }
        }
        L($"/!\\ HidHide reste injoignable après {attempts} essais — masquage INACTIF (double input possible).");
    }

    /// <summary>Masque TOUS les nœuds (HID + USB) de cette manette (repérés par VID/PID).</summary>
    public void HideController(int sdlInstanceId, ushort vid, ushort pid)
    {
        if (!IsMasking || _hiddenByController.ContainsKey(sdlInstanceId)) return;

        var ids = FindDeviceInstanceIds(vid, pid);
        foreach (var id in ids)
        {
            try { _svc.AddBlockedInstanceId(id); }
            catch (Exception ex) { L($"   HidHide masquage échec : {ex.Message}"); }
        }
        _hiddenByController[sdlInstanceId] = ids;
        if (ids.Count > 0)
            L($"   HidHide -> masqué {ids.Count} noeud(s) (VID_{vid:X4}&PID_{pid:X4})");

        // Forcer la ré-énumération du nœud USB parent : ça fait TOMBER les handles déjà ouverts
        // (ex. Steam qui tient la manette physique) ; à sa réapparition elle est déjà masquée, donc
        // invisible du jeu — même après un rebranchement. UNE fois par VID/PID toutes les ~6 s
        // (sinon le remove/add que ça génère relancerait HideController en boucle).
        var key = ((uint)vid << 16) | pid;
        var now = Environment.TickCount64;
        if (!_lastRestart.TryGetValue(key, out var last) || now - last > 6000)
        {
            _lastRestart[key] = now;
            foreach (var id in ids.Where(x => x.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    PnPDevice.GetDeviceByInstanceId(id, DeviceLocationFlags.Normal).Restart();
                    L($"   ré-énumération {id} (handles externes relâchés)");
                }
                catch (Exception ex) { L($"   ré-énum échec {id} : {ex.Message}"); }
            }
        }
    }

    public void UnhideController(int sdlInstanceId)
    {
        if (!IsMasking || !_hiddenByController.TryGetValue(sdlInstanceId, out var ids)) return;
        foreach (var id in ids)
        {
            try { _svc.RemoveBlockedInstanceId(id); } catch { /* ré-affiché au nettoyage sinon */ }
        }
        _hiddenByController.Remove(sdlInstanceId);
    }

    public void Cleanup()
    {
        if (!IsMasking) return;
        foreach (var ids in _hiddenByController.Values)
            foreach (var id in ids)
            {
                try { _svc.RemoveBlockedInstanceId(id); } catch { /* ignore */ }
            }
        _hiddenByController.Clear();
        L("HidHide : manettes physiques ré-affichées (nettoyage).");
    }

    // Énumère les périphériques (HID + USB) dont le VID/PID matche -> on masque TOUS leurs nœuds.
    // Masquer le nœud USB parent en plus du HID est nécessaire pour des manettes comme la Switch Pro,
    // que le jeu/Steam lit aussi via le nœud USB resté visible si on ne masque que le HID.
    private static List<string> FindDeviceInstanceIds(ushort vid, ushort pid)
    {
        var tag = $"VID_{vid:X4}&PID_{pid:X4}";
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var guid in new[] { HidInterface, UsbInterface })
        {
            var i = 0;
            while (Devcon.FindByInterfaceGuid(guid, out _, out var instanceId, i++))
            {
                if (!string.IsNullOrEmpty(instanceId) &&
                    instanceId.Contains(tag, StringComparison.OrdinalIgnoreCase))
                    found.Add(instanceId);
            }
        }
        return found.ToList();
    }
}
