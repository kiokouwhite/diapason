# Publier une nouvelle version de Diapason

La mise à jour intégrée (menu **« Vérifier les mises à jour »**) compare la version
**locale** à la dernière **Release GitHub**. Pour qu'une mise à jour se déclenche chez
les utilisateurs, il faut publier une Release avec un **numéro de version plus grand**
et y **attacher l'installeur** (`.exe`).

> ⚠️ Sans Release, ou avec un numéro identique/inférieur, Diapason se croit à jour.
> L'updater télécharge **le premier asset `.exe`** de la dernière Release → l'installeur
> doit donc bien y être attaché.

---

## 1. Bumper la version — aux **3 endroits** (mêmes chiffres)

| Fichier | Champ | Exemple (1.0 → 1.1) |
|---|---|---|
| `src/Diapason.csproj` | `<Version>` | `1.0.0` → `1.1.0` |
| `installer/Diapason.iss` | `AppVersion=` | `1.0` → `1.1` |
| Tag Git / Release GitHub | `tag` | `v1.0` → `v1.1` |

> Diapason lit son numéro depuis `<Version>` du csproj (major.minor). Le tag et
> l'`AppVersion` doivent correspondre.

## 2. Rebâtir l'exe + l'installeur

Fermer Diapason d'abord (icône barre des tâches → **Quitter**), sinon `dist\Diapason.exe`
est verrouillé.

```powershell
# Depuis le dossier Diapason/
dotnet publish src\Diapason.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# Copier les fichiers à côté de l'exe dans dist
Copy-Item src\bin\Release\net8.0-windows\win-x64\publish\Diapason.exe        dist\Diapason.exe        -Force
Copy-Item src\bin\Release\net8.0-windows\win-x64\publish\SDL2.dll            dist\SDL2.dll            -Force
Copy-Item src\bin\Release\net8.0-windows\win-x64\publish\gamecontrollerdb.txt dist\gamecontrollerdb.txt -Force

# Recompiler l'installeur (sortie : Setup-Tournoi-Diapason.exe à la racine)
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\Diapason.iss
```

## 3. Commit + tag + push

```powershell
git add -A
git commit -m "Version 1.1"
git tag -a v1.1 -m "Diapason v1.1"
git push origin main
git push origin v1.1
```

## 4. Créer la Release GitHub

1. Ouvrir `https://github.com/kiokouwhite/diapason/releases/new?tag=v1.1`
2. Titre : `Diapason v1.1` · description : le changelog.
3. Glisser **`Setup-Tournoi-Diapason.exe`** dans *Attach binaries*.
4. **Publish release**.

→ Les Diapason déjà installés afficheront la notification de mise à jour au prochain
démarrage (ou via **Vérifier les mises à jour**).

---

## Rappels build

- `gh` n'est pas requis : push via git + Git Credential Manager (compte **kiokouwhite**).
- Les gros exécutables (`dist/Diapason.exe` ~156 Mo, `Setup-Tournoi-Diapason.exe` ~79 Mo)
  sont **hors dépôt** (`.gitignore`) → ils vivent uniquement dans les **Releases**.
- Pré-requis build : SDK .NET 8 + Inno Setup 6.
