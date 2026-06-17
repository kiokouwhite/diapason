# Diapason 🎵

Normalisation des manettes pour les tournois FGC de l'association.

Quand on mélange des manettes de types différents (DualShock/DualSense, Xbox, Switch)
sur un PC, les boutons s'inversent (A↔B, X↔Y). **Diapason** fait en sorte que le jeu ne
voie plus que des manettes **Xbox rigoureusement identiques**, quelle que soit la manette
branchée — et masque les manettes physiques pour éviter le double input.

> Le nom : un *diapason* est la note de référence sur laquelle tous les instruments
> s'accordent — ici, la manette de référence sur laquelle toutes les autres s'alignent.

---

## 📦 Déployer sur un PC de tournoi

**Un seul fichier à copier : `Setup-Tournoi-Diapason.exe`.**

1. Copie-le sur le PC (clé USB en **exFAT** = évite l'avertissement SmartScreen).
2. Double-clic → accepte l'UAC → laisse cochée l'option « démarrage auto ».
3. Il installe **tout, pilotes compris** (ViGEmBus + HidHide + VC++), crée les raccourcis,
   puis propose un **redémarrage** (nécessaire pour les pilotes noyau).
4. Après reboot, Diapason démarre seul à l'ouverture de session.

> ⚠️ Diapason n'est pas signé. Sur un Windows 11 installé « propre », il faut peut-être
> désactiver **Smart App Control** (Sécurité Windows). Au 1er lancement, SmartScreen peut
> afficher « Windows a protégé votre PC » → *Informations complémentaires → Exécuter quand même*.

## 🎮 Utiliser

- **Lancer** : double-clic sur `Diapason.exe` (→ UAC). Il tourne en **icône** dans la barre
  des tâches **et** ouvre la **fenêtre testeur**.
- **Fenêtre testeur** : affiche P1 et P2, les boutons qui s'allument quand tu presses
  (disposition Xbox = ce que le jeu reçoit), les sticks, les gâchettes, et l'état du masquage.
  Parfait pour vérifier une manette **avant** un match.
- **Échanger P1/P2** : bouton dans la fenêtre, menu de l'icône, ou **Ctrl+Alt+S** (même en jeu).
- **Fermer la fenêtre** (croix) = réduit dans le tray (Diapason continue). Pour quitter
  vraiment : clic droit sur l'icône → **Quitter**.

---

## 🗂️ Structure du dossier

| Élément | Rôle |
|---|---|
| `Setup-Tournoi-Diapason.exe` | **L'installeur tout-en-un** à déployer (pilotes inclus). |
| `src/` | Code source C# (.NET 8 / WinForms). |
| `installer/` | Sources de l'installeur Inno Setup (`Diapason.iss`, scripts, `redist/` = pilotes embarqués). |
| `dist/` | Kit de déploiement **manuel** (alternative à l'installeur) : l'exe + scripts + LISEZMOI. |
| `_archive/` | Anciens scripts de mise au point, **non utilisés** (gardés pour mémoire). |
| `README.md` | Ce fichier. |

## 🔧 Comment ça marche

```
 Manette physique          Diapason                       Jeu
 (DualShock / Xbox /  ┌──────────────────────────┐
  Switch / stick…)    │  SDL lit + NORMALISE      │   ne voit QUE
    └───────────────► │  → ViGEmBus : manette     │── des manettes ──►
                      │     Xbox 360 virtuelle    │   Xbox identiques
                      │  → HidHide masque la      │
                      │     manette physique      │
                      └──────────────────────────┘
```

- **SDL** lit *toutes* les manettes et corrige l'inversion (A = toujours le bouton du bas).
- **ViGEmBus** expose une manette Xbox 360 virtuelle par joueur (slots P1/P2 fixes).
- **HidHide** cache les manettes physiques au jeu (sinon double input).

## 🛠️ Développer / recompiler

Prérequis : **.NET 8 SDK** + **Inno Setup 6** (pour l'installeur).

```powershell
# 1. Publier l'exe autonome (FERMER Diapason d'abord, sinon le fichier est verrouillé)
dotnet publish src\Diapason.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 2. Copier l'exe publié dans dist\
Copy-Item src\bin\Release\net8.0-windows\win-x64\publish\Diapason.exe dist\Diapason.exe -Force

# 3. Recompiler l'installeur
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\Diapason.iss
```

> L'exe exige l'admin (manifeste `requireAdministrator`) : il s'auto-élève à chaque lancement.
