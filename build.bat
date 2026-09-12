@echo off
REM ============================================================
REM  Compile NovaVox (edition .NET/WPF) en executable Windows
REM  autonome, puis genere l'installateur (Inno Setup).
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

echo.
echo [4/4] Compilation de l'installateur (Inno Setup)...
set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC% goto :iscc_missing

%ISCC% /DMyAppVersion=%APPVER% installer.iss
if not exist "Output\NovaVoxNET_Setup.exe" goto :iscc_failed
echo   -^> Installateur genere : Output\NovaVoxNET_Setup.exe
goto :iscc_done

:iscc_missing
echo   -^> Inno Setup introuvable, installateur non genere.
echo      Installe-le depuis https://jrsoftware.org/isdl.php
goto :iscc_done

:iscc_failed
echo   -^> ERREUR : la compilation de l'installateur a echoue.

:iscc_done

echo.
echo ============================================================
echo  Termine !
echo  Executable : dist\NovaVox\NovaVox.exe
echo  (le modele Vosk et le moteur Piper ne sont PAS inclus -- l'appli
echo   propose de les telecharger automatiquement depuis Reglages
echo   au premier lancement)
echo.
echo  Pour regenerer uniquement l'installateur (Setup.exe) a partir
echo  d'un dist\NovaVox\ deja publie, ouvre installer.iss avec
echo  Inno Setup Compiler : https://jrsoftware.org/isdl.php
echo ============================================================
pause
