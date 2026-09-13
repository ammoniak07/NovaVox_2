# extract_release_notes.ps1
# Extrait le bloc de notes de la version COURANTE (la plus recente, en
# tete de fichier) depuis patch_maj.txt, et l'ecrit dans -NotesFile.
#
# Port direct du script du meme nom cote NovaVox Python (meme format de
# patch_maj.txt) : remplace un equivalent en batch natif (boucle
# "for /f" dans build.bat) qui poserait les deux memes problemes que
# cote Python :
# 1) cmd.exe re-analyse le texte substitue par !LINE! (expansion
#    retardee) a la recherche d'operateurs de redirection (>, <, &, |)
#    AVANT execution -- or patch_maj.txt utilise justement ">" comme
#    separateur visuel de chemin de menu ("Reglages > Sons",
#    "Game.log > Alias de destinations"...). Chaque ">" rencontre dans
#    une ligne de changelog serait donc traite comme une VRAIE
#    redirection, creant des fichiers parasites a la racine du projet.
# 2) cmd.exe lit le fichier avec le code page actif de la console (pas
#    forcement UTF-8), ce qui deformerait les caracteres accentues/emoji
#    du changelog dans ces noms de fichiers parasites.
# PowerShell n'a ni l'un ni l'autre defaut : Get-Content -Encoding UTF8
# lit le texte correctement, et le contenu d'une variable n'est jamais
# re-interprete comme une redirection.
param(
    [Parameter(Mandatory = $true)]
    [string]$PatchFile,
    [Parameter(Mandatory = $true)]
    [string]$NotesFile
)

if (-not (Test-Path $PatchFile)) {
    "Voir patch_maj.txt pour le detail." | Set-Content -Path $NotesFile -Encoding UTF8
    exit 0
}

$lines = Get-Content -Path $PatchFile -Encoding UTF8
$notes = New-Object System.Collections.Generic.List[string]
$capturing = 0

foreach ($line in $lines) {
    if ($line -match '^v[0-9]') {
        if ($capturing -eq 0) { $capturing = 1 } else { break }
        continue
    }
    if ($capturing -eq 1 -and $line -notmatch '^-+$') {
        $notes.Add($line)
    }
}

if ($notes.Count -eq 0) {
    "Voir patch_maj.txt pour le detail." | Set-Content -Path $NotesFile -Encoding UTF8
} else {
    ($notes -join "`r`n") | Set-Content -Path $NotesFile -Encoding UTF8
}
