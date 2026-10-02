# notify_discord.ps1
# Envoie une notification dans un salon Discord via un Webhook, avec le
# numero de version et les notes de version (extraites de patch_maj.txt
# par build.bat) de cette edition .NET/WPF de NovaVox.
#
# Appele automatiquement par build.bat apres un deploiement reussi.
# Ne fait jamais planter le build : toute erreur ici est juste affichee,
# jamais fatale. Port direct du script du meme nom cote NovaVox Python,
# avec le titre/libelle adaptes pour distinguer clairement cette edition
# .NET de l'ancienne version Python (meme salon Discord -- aucun canal
# dedie separe n'existe pour le moment).
param(
    [string]$Version,
    [string]$NotesFile,
    [string]$WebhookFile = "discord_webhook.txt",
    [string]$FirstPostFile = "discord_first_post.txt",
    [string]$ChannelLink = "https://discord.com/channels/1545838159261204510/1545868137545601044"
)

# Decoupe $Text en morceaux d'au plus $MaxSize caracteres, toujours sur une
# frontiere de ligne (jamais au milieu d'un mot ni, surtout, d'un emoji qui
# occupe deux unites de texte en .NET -- une coupure en plein milieu produit
# un caractere invalide que l'API Discord rejette avec une erreur 400, ce
# qui arrivait avant avec Substring(0, 3900) sur un changelog deja plus long
# que ca). Une ligne seule deja plus longue que $MaxSize (rare) est coupee
# brutalement en dernier recours, plutot que de produire un morceau
# surdimensionne qui ferait encore echouer l'envoi.
function Split-IntoChunks {
    param([string]$Text, [int]$MaxSize)

    $chunks = New-Object System.Collections.Generic.List[string]
    $lines = $Text -split "`r`n|`n"
    $current = ""

    foreach ($line in $lines) {
        if ($line.Length -gt $MaxSize) {
            if ($current.Length -gt 0) { $chunks.Add($current); $current = "" }
            for ($i = 0; $i -lt $line.Length; $i += $MaxSize) {
                $len = [Math]::Min($MaxSize, $line.Length - $i)
                $chunks.Add($line.Substring($i, $len))
            }
            continue
        }

        $candidate = if ($current.Length -eq 0) { $line } else { "$current`r`n$line" }
        if ($candidate.Length -gt $MaxSize) {
            $chunks.Add($current)
            $current = $line
        } else {
            $current = $candidate
        }
    }
    if ($current.Length -gt 0) { $chunks.Add($current) }
    if ($chunks.Count -eq 0) { $chunks.Add($Text) }

    # Virgule unaire OBLIGATOIRE : "return $chunks" tout seul laisse
    # PowerShell DÉROULER la List[string] en écrivant chacun de ses éléments
    # séparément dans le flux de sortie -- avec un SEUL morceau (le cas
    # normal, un changelog qui tient sous 3500 caractères), l'appelant ne
    # reçoit donc pas une liste à un élément mais la CHAÎNE elle-même.
    # $chunks.Count valait alors 1 par pur hasard (PowerShell donne .Count=1
    # à n'importe quel objet non-collection), et $chunks[0] n'indexait plus
    # "le premier élément de la liste" mais "le premier CARACTÈRE de la
    # chaîne" -- d'où une description Discord réduite à "-", le tout premier
    # caractère du changelog. La virgule force PowerShell à transmettre la
    # liste comme un seul objet, jamais déroulé, quel que soit son nombre
    # d'éléments (0, 1 ou plus).
    return ,$chunks
}

if (-not (Test-Path $WebhookFile)) {
    Write-Host "  -> discord_webhook.txt introuvable, notification Discord ignoree."
    exit 0
}

$webhookUrl = (Get-Content $WebhookFile -Raw -Encoding UTF8).Trim()
if ([string]::IsNullOrWhiteSpace($webhookUrl)) {
    Write-Host "  -> discord_webhook.txt est vide, notification Discord ignoree."
    exit 0
}

$baseTitle = "NovaVox v$Version disponible"
$notes = "Voir le changelog complet dans l'application."

# Recap complet a usage UNIQUE : si discord_first_post.txt existe (premier
# envoi dans le salon), on l'utilise a la place des notes de version
# normales pour ce lancement, puis on le supprime aussitot -- tous les
# envois suivants repassent automatiquement en mode normal (juste les
# notes de la version courante).
if ($FirstPostFile -and (Test-Path $FirstPostFile)) {
    $recap = (Get-Content $FirstPostFile -Raw -Encoding UTF8).Trim()
    if ($recap) {
        $notes = $recap
        $baseTitle = "NovaVox - recap complet + v$Version"
    }
    Remove-Item -Path $FirstPostFile -Force -ErrorAction SilentlyContinue
} elseif ($NotesFile -and (Test-Path $NotesFile)) {
    $fileContent = (Get-Content $NotesFile -Raw -Encoding UTF8).Trim()
    if ($fileContent) {
        $notes = $fileContent
    }
}

# 3500 plutot que la limite reelle de Discord (4096 par description, 6000
# pour l'embed entier titre+description+champs) : laisse de la marge pour
# le titre et le champ "Salon" du premier message sans jamais en approcher.
$chunks = Split-IntoChunks -Text $notes -MaxSize 3500
$total = $chunks.Count

# HttpClient plutot qu'Invoke-RestMethod : ce dernier s'appuie, sous Windows
# PowerShell 5.1 (.NET Framework), sur l'ancien System.Net.HttpWebRequest, qui
# a un defaut connu -- si le serveur repond par une redirection, le POST est
# rejoue en GET en recollant tout le corps JSON dans l'URL, ce qui a produit
# exactement l'erreur constatee ("Request Line is too large (8192 > 4094)",
# cote Cloudflare, identique sur les 3 morceaux quelle que soit leur taille,
# donc rien a voir avec la longueur du changelog). HttpClient n'a pas ce
# comportement historique.
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient

try {
    for ($i = 0; $i -lt $total; $i++) {
        $title = if ($total -gt 1) { "$baseTitle ($($i + 1)/$total)" } else { $baseTitle }

        $embed = @{
            title       = $title
            description = $chunks[$i]
            url         = $ChannelLink
            color       = 1752262
        }
        # Le champ "Salon" n'a besoin d'apparaitre qu'une fois, pas repete dans
        # chaque morceau d'un changelog decoupe en plusieurs messages.
        if ($i -eq 0) {
            $embed.fields = @(@{ name = "Salon"; value = $ChannelLink })
        }

        $payload = @{
            username = "NovaVox"
            embeds   = @($embed)
        } | ConvertTo-Json -Depth 6

        try {
            $content = New-Object System.Net.Http.StringContent($payload, [System.Text.Encoding]::UTF8, "application/json")
            $response = $client.PostAsync($webhookUrl, $content).GetAwaiter().GetResult()
            if ($response.IsSuccessStatusCode) {
                Write-Host "  -> Notification Discord envoyee ($($i + 1)/$total)."
            } else {
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                Write-Host "  -> ERREUR envoi notification Discord (partie $($i + 1)/$total) : HTTP $([int]$response.StatusCode) $($response.ReasonPhrase)`r`nDetail Discord : $body"
            }
        } catch {
            Write-Host "  -> ERREUR envoi notification Discord (partie $($i + 1)/$total) : $_"
        }

        # Evite de se heurter a la limite de frequence des webhooks Discord
        # (5 requetes / 2 secondes) quand le changelog tient en plusieurs morceaux.
        if ($i -lt $total - 1) { Start-Sleep -Seconds 1 }
    }
} finally {
    $client.Dispose()
}
