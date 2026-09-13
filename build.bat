@echo off
REM ============================================================
REM  Compile NovaVox (edition .NET/WPF) en executable Windows
REM  autonome, genere l'installateur (Inno Setup, 2 variantes),
REM  puis publie/notifie (site officiel, GitHub Releases, Discord).
REM
REM  A executer SUR WINDOWS, avec le SDK .NET 8 installe et dans
REM  le PATH, depuis la racine du depot (a cote de NovaVox.sln).
REM
REM  Resultat : dist\NovaVox\NovaVox.exe
REM  (plus toutes les DLL necessaires a cote -- publication
REM  "self-contained" : la machine cible n'a PAS besoin d'installer
REM  .NET separement, contrairement a une publication classique).
REM ============================================================
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo [1/4] Verification du SDK .NET...
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERREUR : "dotnet" introuvable dans le PATH.
    echo Installe le SDK .NET 8 depuis https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

if not exist "src\NovaVox.App\NovaVox.App.csproj" (
    echo ERREUR : src\NovaVox.App\NovaVox.App.csproj introuvable.
    echo Ce script doit etre lance depuis la racine du depot.
    pause
    exit /b 1
)

echo [2/4] Nettoyage des anciens builds...
if exist dist rmdir /s /q dist

echo [3/4] Publication (Release, autonome, win-x64)...
REM --self-contained (pas PublishSingleFile) : NAudio/Vortice.DirectInput
REM (COM) et Vosk (bindings natifs) sont plus fiables publies "en dossier
REM eclate" qu'assembles dans un seul .exe -- meme choix que --onedir
REM (plutot que --onefile) cote build_exe.bat de la version Python, pour
REM les memes raisons.
dotnet publish src\NovaVox.App\NovaVox.App.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=false ^
    -o dist\NovaVox

if errorlevel 1 (
    echo ERREUR pendant la publication. Voir le detail ci-dessus.
    pause
    exit /b 1
)

echo   -^> Publie dans dist\NovaVox\
echo   -^> icon.ico / patch_maj.txt / logo.png / background.png (s'ils
echo      existent a la racine du depot) sont deja copies automatiquement
echo      par la publication (voir les entrees Content du .csproj) --
echo      pas de copie manuelle necessaire ici, contrairement a PyInstaller.

echo.
echo Extraction de la version depuis patch_maj.txt...
set APPVER=0.0.0
for /f "tokens=1 delims= " %%v in ('findstr /r "^v[0-9]" patch_maj.txt') do (
    set APPVER=%%v
    goto :gotver
)
:gotver
set APPVER=%APPVER:v=%
echo   -^> Version detectee : %APPVER%

echo Mise a jour automatique de novavoxnet_version.json...
REM Nom DISTINCT de version.json (celui de la version Python, sur le
REM meme site) : cette edition .NET a sa propre numerotation (repartie
REM a 0.0.1) et son propre installeur -- reutiliser le meme fichier
REM signalerait une "mise a jour" en permanence (voir UpdateChecker.cs).
echo { "version": "%APPVER%", "url": "https://novanox.1ercorpscolonial.fr/NovaVoxNET_Setup.exe" }> novavoxnet_version.json
echo   -^> novavoxnet_version.json mis a jour avec la version %APPVER%.

REM Notes de version : extrait uniquement le bloc de la version courante
REM depuis patch_maj.txt, plutot que tout l'historique complet. Partage
REM entre la publication GitHub et la notification Discord ci-dessous.
REM Delegue a PowerShell (extract_release_notes.ps1) plutot qu'une boucle
REM batch native : le changelog utilise ">" comme separateur visuel de
REM chemin de menu (ex. "Reglages > Sons"), et une boucle "for /f" avec
REM expansion retardee (!LINE!) directement collee a une redirection
REM re-interprete CE ">" comme une VRAIE redirection a l'execution.
set "NOTES_FILE=%TEMP%\novavoxnet_release_notes.txt"
if exist "%NOTES_FILE%" del /q "%NOTES_FILE%"
powershell -NoProfile -ExecutionPolicy Bypass -File "extract_release_notes.ps1" -PatchFile "patch_maj.txt" -NotesFile "%NOTES_FILE%"
if not exist "%NOTES_FILE%" echo Voir patch_maj.txt pour le detail.> "%NOTES_FILE%"

echo.
echo [4/4] Compilation de l'installateur (Inno Setup) -- variante site officiel...
set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC% goto :iscc1_missing

echo https://novanox.1ercorpscolonial.fr/novavoxnet_version.json> "dist\NovaVox\update_source.txt"
%ISCC% /DMyAppVersion=%APPVER% installer.iss
if not exist "Output\NovaVoxNET_Setup.exe" goto :iscc1_failed
echo   -^> Installateur genere (site officiel) : Output\NovaVoxNET_Setup.exe
goto :iscc1_done

:iscc1_missing
echo   -^> Inno Setup introuvable, installateur non genere.
echo      Installe-le depuis https://jrsoftware.org/isdl.php
goto :iscc1_done

:iscc1_failed
echo   -^> ERREUR : la compilation de l'installateur a echoue.

:iscc1_done

REM Details de connexion SSH (IP, utilisateur, nom de la cle) lus depuis
REM un fichier local non versionne (voir .gitignore) plutot qu'ecrits en
REM clair ici, puisque ce depot est public sur GitHub.
set SSH_HOST=
set SSH_USER=
set SSH_KEYNAME=
if not exist "deploy_config.txt" goto :deploy_skip

for /f "usebackq delims=" %%A in ("deploy_config.txt") do (
    if not defined SSH_HOST (
        set "SSH_HOST=%%A"
    ) else if not defined SSH_USER (
        set "SSH_USER=%%A"
    ) else if not defined SSH_KEYNAME (
        set "SSH_KEYNAME=%%A"
    )
)
if not defined SSH_HOST goto :deploy_skip

echo.
echo Envoi vers le site officiel (novanox.1ercorpscolonial.fr)...
scp -i "%USERPROFILE%\.ssh\%SSH_KEYNAME%" "Output\NovaVoxNET_Setup.exe" %SSH_USER%@%SSH_HOST%:
scp -i "%USERPROFILE%\.ssh\%SSH_KEYNAME%" "novavoxnet_version.json" %SSH_USER%@%SSH_HOST%:
goto :deploy_done

:deploy_skip
echo   -^> deploy_config.txt introuvable ou incomplet, envoi vers le site officiel ignore.
echo      Cree ce fichier (3 lignes : IP, utilisateur SSH, nom de la cle) pour l'activer
echo      -- le meme fichier que celui utilise pour la version Python fonctionne tel quel.

:deploy_done

echo.
echo Compilation de l'installateur (Inno Setup) -- variante GitHub...
if exist "Output\NovaVoxNET_Setup_GitHub.exe" del /q "Output\NovaVoxNET_Setup_GitHub.exe"
if not exist %ISCC% goto :iscc2_missing

echo https://api.github.com/repos/ammoniak07/NovaVox_2/releases/latest> "dist\NovaVox\update_source.txt"
%ISCC% /DMyAppVersion=%APPVER% installer.iss
if not exist "Output\NovaVoxNET_Setup.exe" goto :iscc2_failed
ren "Output\NovaVoxNET_Setup.exe" "NovaVoxNET_Setup_GitHub.exe"
echo   -^> Installateur genere (GitHub) : Output\NovaVoxNET_Setup_GitHub.exe
goto :iscc2_done

:iscc2_missing
echo   -^> Inno Setup introuvable, installateur (variante GitHub) non genere.
goto :iscc2_done

:iscc2_failed
echo   -^> ERREUR : la compilation de l'installateur (variante GitHub) a echoue.

:iscc2_done

echo.
echo Notification Discord...
powershell -NoProfile -ExecutionPolicy Bypass -File "notify_discord.ps1" -Version "%APPVER%" -NotesFile "%NOTES_FILE%"

echo.
REM ============================================================
REM  Publication automatique sur GitHub Releases, en plus du site
REM  officiel ci-dessus. La variante "site officiel" de l'installeur
REM  ne verifie les mises a jour que sur ce site ; la variante
REM  "GitHub" (voir plus haut) verifie via l'API GitHub Releases --
REM  chacune reste independante de l'autre.
REM
REM  Necessite GitHub CLI (gh), installe une seule fois
REM  manuellement (https://cli.github.com/) puis connecte via
REM  "gh auth login" une seule fois (session ensuite memorisee sur
REM  cette machine).
REM ============================================================
echo Publication sur GitHub Releases (ammoniak07/NovaVox_2)...
where gh >nul 2>&1
if errorlevel 1 goto :gh_missing

gh auth status >nul 2>&1
if errorlevel 1 goto :gh_not_logged_in

if not exist "Output\NovaVoxNET_Setup_GitHub.exe" goto :gh_no_exe

set GH_REPO=ammoniak07/NovaVox_2
set GH_TAG=v%APPVER%

gh release view %GH_TAG% --repo %GH_REPO% >nul 2>&1
if errorlevel 1 goto :gh_create
goto :gh_upload

:gh_create
gh release create %GH_TAG% "Output\NovaVoxNET_Setup_GitHub.exe" --repo %GH_REPO% --title "NovaVoxNET %APPVER%" --notes-file "%NOTES_FILE%"
if errorlevel 1 goto :gh_create_failed
echo   -^> Release %GH_TAG% creee sur GitHub, NovaVoxNET_Setup_GitHub.exe joint.
goto :gh_done

:gh_create_failed
echo   -^> ERREUR lors de la creation de la release GitHub %GH_TAG%.
goto :gh_done

:gh_upload
gh release upload %GH_TAG% "Output\NovaVoxNET_Setup_GitHub.exe" --repo %GH_REPO% --clobber
if errorlevel 1 goto :gh_upload_failed
echo   -^> Release %GH_TAG% existante mise a jour sur GitHub.
goto :gh_done

:gh_upload_failed
echo   -^> ERREUR lors de la mise a jour de la release GitHub %GH_TAG%.
goto :gh_done

:gh_missing
echo   -^> gh introuvable, publication GitHub ignoree.
echo      Installe-le depuis https://cli.github.com/ puis lance "gh auth login" une fois.
goto :gh_done

:gh_not_logged_in
echo   -^> gh n'est pas connecte a un compte GitHub, publication ignoree.
echo      Lance "gh auth login" une fois manuellement, puis relance ce script.
goto :gh_done

:gh_no_exe
echo   -^> Output\NovaVoxNET_Setup_GitHub.exe introuvable, publication GitHub ignoree.

:gh_done

echo.
echo ============================================================
echo  Termine !
echo  Executable : dist\NovaVox\NovaVox.exe
echo  (le modele Vosk et le moteur Piper ne sont PAS inclus -- l'appli
echo   propose de les telecharger automatiquement depuis Reglages
echo   au premier lancement)
echo.
echo  Pour activer le deploiement vers le site officiel : cree
echo  deploy_config.txt (3 lignes : IP, utilisateur SSH, nom de la cle).
echo  Pour activer la notification Discord : cree discord_webhook.txt
echo  (URL du webhook, une seule ligne). Aucun des deux n'est requis
echo  pour generer les installateurs eux-memes.
echo ============================================================
pause
