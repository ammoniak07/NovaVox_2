; ============================================================
; Script Inno Setup - Installeur "NovaVox" (edition .NET/WPF)
;
; A COMPILER SUR WINDOWS avec Inno Setup (gratuit) :
;   https://jrsoftware.org/isdl.php
;
; Prerequis : avoir deja lance build.bat (ou "dotnet publish"
; manuellement, voir ce script), pour obtenir le dossier
; dist\NovaVox\ a cote de ce fichier .iss.
;
; Contrairement a la version Python (pywebview), aucun runtime
; WebView2 n'est necessaire -- WPF est natif a Windows.
;
; Utilisation : ouvre ce fichier avec l'appli Inno Setup Compiler
; (ou clic droit > "Compile"), le Setup.exe final est genere dans
; le dossier Output\ a cote de ce script.
; ============================================================

; "NovaVox V2" (pas juste "NovaVox") : Inno Setup dérive de MyAppName à
; la fois le nom du groupe Start Menu ET le nom de fichier de CHAQUE
; raccourci (voir [Icons] plus bas) -- avec le même nom que la version
; Python ("NovaVox"), les deux installeurs auraient généré des
; raccourcis Bureau/Menu Démarrer au même chemin (ex. Bureau\NovaVox.lnk),
; installer l'un après l'autre aurait donc silencieusement ECRASÉ le
; raccourci de l'autre (pointant vers son .exe) sans aucun message.
; Les dossiers d'installation eux-mêmes étaient déjà distincts (voir
; DefaultDirName ci-dessous) : seuls les raccourcis collisionnaient.
#define MyAppName "NovaVox V2"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppPublisher "Ammoniak007"
#define MyAppExeName "NovaVox.exe"

[Setup]
; AppId et DefaultDirName volontairement DISTINCTS de la version Python
; de NovaVox (AppId {{B4E1C9A2-6F3D-4A21-9E7C-2C8F1D5A9B10}},
; {localappdata}\NovaVox) : cette edition .NET est un portage encore en
; cours de tests (versionnage repart a 0.0.1), pas un remplacement --
; s'installe dans son propre dossier, cote a cote avec une eventuelle
; installation Python existante, plutot que de risquer de l'ecraser
; silencieusement. A aligner sur l'AppId Python le jour ou cette edition
; .NET devient la version officielle qui la remplace.
AppId={{7B2E9F41-3C8A-4D6E-9F12-1A5C6D8E3B7F}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\NovaVoxNET
DefaultGroupName={#MyAppName}
; Affiche explicitement la page "Selectionner le dossier de destination"
; du wizard, plutot que de compter sur le comportement par defaut.
DisableDirPage=no
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=NovaVoxNET_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
; NovaVox reste actif en arriere-plan (icone dans la barre des taches)
; quand on ferme sa fenetre au lieu de vraiment quitter (voir
; MainWindow.OnClosing) : ses fichiers restent verrouilles pendant une
; mise a jour, d'ou "force" (TerminateProcess, jamais de prompt bloquant)
; plutot que le comportement par defaut (juste une DEMANDE de fermeture
; via le Restart Manager de Windows, pas toujours respectee par une appli
; de fond a fenetre masquee) -- meme raison que cote Python.
CloseApplications=force
RestartApplications=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[Files]
; Exclut les fichiers de configuration utilisateur (voir NovaVox.Core.
; Config.*) d'une eventuelle installation existante a ce meme
; emplacement -- une mise a jour ne doit jamais ecraser les commandes/
; reglages/profils deja personnalises. Ils n'existent de toute facon pas
; dans dist\NovaVox\ (publication fraiche, sans donnees utilisateur) :
; l'appli les cree elle-meme avec des valeurs par defaut au premier
; lancement.
Source: "dist\NovaVox\*"; DestDir: "{app}"; Excludes: "commands.json,ai_config.json,audio_config.json,overlay_config.json,window_config.json,profiles_config.json,profiles\*,Log\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Messages]
FinishedLabel=L'installation est terminée.%n%nIMPORTANT : le modèle de reconnaissance vocale Vosk et le moteur de synthèse vocale Piper ne sont PAS inclus dans cet installeur — l'application te proposera de les télécharger automatiquement dès le premier lancement (Réglages > 🔊 Sons).
