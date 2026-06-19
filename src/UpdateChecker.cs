using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Diapason;

/// <summary>
/// Mise à jour intégrée : interroge les Releases GitHub, compare à la version locale, et si une
/// version plus récente existe, télécharge l'installeur (asset .exe de la Release) puis le lance.
/// Aucun passage par le site GitHub côté utilisateur. API publique (pas de jeton requis).
/// </summary>
internal static class UpdateChecker
{
    private const string Owner  = "kiokouwhite";
    private const string Repo   = "diapason";
    private const string ApiUrl = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

    public static string? LatestVersion { get; private set; }   // ex. "1.1"
    public static string? DownloadUrl   { get; private set; }   // URL de l'installeur (asset .exe)
    public static string? ReleaseUrl    { get; private set; }   // page de la Release (repli manuel)

    /// <summary>Vrai si une Release plus récente que <see cref="Program.Version"/> est disponible.</summary>
    public static async Task<bool> CheckAsync(Action<string> log)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Diapason-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            ReleaseUrl = root.TryGetProperty("html_url", out var hu) ? hu.GetString() : null;

            // Cherche l'asset installeur (le premier .exe attaché à la Release).
            string? url = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out var du))
                    { url = du.GetString(); break; }
                }

            var latest  = ParseVersion(tag);
            var current = ParseVersion(Program.Version);
            log($"MAJ : version locale {Program.Version}, dernière Release {tag}");

            if (latest != null && current != null && latest > current && url != null)
            {
                LatestVersion = tag.TrimStart('v', 'V');
                DownloadUrl   = url;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            // Pas de Release publiée (404), pas d'internet, etc. → on ignore silencieusement.
            log("MAJ : vérification impossible — " + ex.Message);
            return false;
        }
    }

    // Extrait un numéro x.y(.z) d'un tag ("v1.2" → 1.2), pour comparer proprement.
    private static Version? ParseVersion(string s)
    {
        var m = Regex.Match((s ?? "").Trim().TrimStart('v', 'V'), @"\d+(\.\d+)+");
        return m.Success && Version.TryParse(m.Value, out var v) ? v : null;
    }

    /// <summary>Télécharge l'installeur dans %TEMP%, le lance, puis quitte Diapason (pour libérer les fichiers).</summary>
    public static async Task DownloadAndRunAsync(Action<string> log)
    {
        if (DownloadUrl == null) throw new InvalidOperationException("Aucune URL de téléchargement.");
        var dest = Path.Combine(Path.GetTempPath(), "Setup-Tournoi-Diapason.exe");

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Diapason-Updater");
            var bytes = await http.GetByteArrayAsync(DownloadUrl);
            await File.WriteAllBytesAsync(dest, bytes);
        }
        log("MAJ : installeur téléchargé → " + dest);

        // Lance l'installeur (il s'élève + remplace les fichiers), puis on quitte.
        Process.Start(new ProcessStartInfo { FileName = dest, UseShellExecute = true });
        Application.Exit();
    }
}
