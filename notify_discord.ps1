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
    return $chunks
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
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
        Invoke-RestMethod -Uri $webhookUrl -Method Post -ContentType "application/json; charset=utf-8" -Body $bytes | Out-Null
        Write-Host "  -> Notification Discord envoyee ($($i + 1)/$total)."
    } catch {
        # Le message d'exception .NET seul ("(400) Demande incorrecte") ne dit
        # jamais CE QUI est refuse -- Discord renvoie le detail exact (quel
        # champ, pourquoi) dans le CORPS de sa reponse d'erreur, qu'il faut
        # lire explicitement (Invoke-RestMethod ne l'expose pas tout seul).
        $errorDetail = $_.Exception.Message
        if ($_.Exception.Response) {
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($stream)
                $body = $reader.ReadToEnd()
                if ($body) { $errorDetail = "$errorDetail`r`nDetail Discord : $body" }
            } catch {
                # Corps de la reponse illisible : on garde juste le message generique ci-dessus.
            }
        }
        Write-Host "  -> ERREUR envoi notification Discord (partie $($i + 1)/$total) : $errorDetail"
    }

    # Evite de se heurter a la limite de frequence des webhooks Discord
    # (5 requetes / 2 secondes) quand le changelog tient en plusieurs morceaux.
    if ($i -lt $total - 1) { Start-Sleep -Seconds 1 }
}
