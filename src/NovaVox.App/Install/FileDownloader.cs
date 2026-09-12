using System.Net.Http;

namespace NovaVox.App.Install;

/// <summary>
/// Téléchargement HTTP avec ré-essai automatique — port de
/// _download_file_with_retry (app.py) : reprend depuis zéro (pas de
/// range request, comme en Python) jusqu'à 3 fois en cas de coupure
/// réseau, et vérifie que la taille reçue correspond au Content-Length
/// annoncé avant de considérer le téléchargement complet.
/// </summary>
public static class FileDownloader
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static async Task DownloadAsync(string url, string destPath, Action<long, long?>? onChunk = null, int maxAttempts = 3, CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength;
                long downloaded = 0;

                await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await httpStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        downloaded += read;
                        onChunk?.Invoke(downloaded, total);
                    }
                }

                if (total is not null && downloaded != total)
                    throw new IOException($"Téléchargement incomplet : {downloaded} octet(s) reçu(s) sur {total} attendu(s).");

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                if (attempt < maxAttempts) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
        }
        throw lastError ?? new IOException("Échec du téléchargement.");
    }
}
