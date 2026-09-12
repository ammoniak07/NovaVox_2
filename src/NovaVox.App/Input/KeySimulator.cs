namespace NovaVox.App.Input;

/// <summary>
/// Port de Api._press_keys / Api._press_keys_repeated (app.py) : envoie
/// une combinaison "touche+touche+..." (ex. "ctrl+n", "mouseleft") via
/// SendInput. Le bouton souris éventuel est traité exactement comme une
/// touche clavier (combinable avec Ctrl/Alt/Maj) mais exécuté comme un
/// clic — voir MOUSE_BUTTONS côté Python.
/// </summary>
public sealed class KeySimulator
{
    private static readonly IReadOnlySet<string> MouseButtons = new HashSet<string> { "mouseleft", "mouseright", "mousemiddle" };

    public const double HoldDurationSeconds = 2.0;

    /// <summary>Appelé pour toute erreur inattendue pendant l'envoi (journal système côté app.py).</summary>
    public Action<string, Exception>? OnError { get; set; }

    /// <summary>Appelé pour un avertissement non bloquant (touche inconnue, échec de relâchement isolé).</summary>
    public Action<string>? OnWarning { get; set; }

    /// <summary>
    /// Enfonce (et relâche) la combinaison de touches une fois.
    /// GARDE-FOU CRITIQUE : toute touche/bouton enfoncé DOIT toujours être
    /// relâché, même si une exception survient en cours de route (ex. un
    /// clic qui échoue après un appui clavier réussi) — sans ça, une
    /// touche peut rester bloquée enfoncée indéfiniment dans le jeu.
    /// </summary>
    public void PressKeys(string keysStr, bool hold = false)
    {
        var keys = keysStr.Split('+').Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
        if (keys.Count == 0) return;

        string? mouseButton = null;
        var keyboardKeys = new List<string>();
        foreach (var k in keys)
        {
            if (MouseButtons.Contains(k)) mouseButton = k;
            else keyboardKeys.Add(k);
        }

        var pressedKeys = new List<string>();
        var mousePressed = false;
        try
        {
            foreach (var k in keyboardKeys)
            {
                if (!KeyScanCodes.Map.TryGetValue(k, out var scanCode))
                {
                    OnWarning?.Invoke($"[Touche inconnue] '{k}' ignorée.");
                    continue;
                }
                NativeInput.SendKeyScanCode(scanCode.Code, scanCode.Extended, keyUp: false);
                pressedKeys.Add(k);
            }
            if (mouseButton is not null)
            {
                NativeInput.SendMouseButton(MouseDownFlag(mouseButton));
                mousePressed = true;
            }
            Thread.Sleep(TimeSpan.FromSeconds(hold ? HoldDurationSeconds : 0.05));
        }
        catch (Exception e)
        {
            OnError?.Invoke($"Envoi de la touche '{keysStr}'", e);
        }
        finally
        {
            // Relâchement inconditionnel de tout ce qui a été enfoncé,
            // même partiellement — chaque relâchement isolé dans son
            // propre try pour qu'un échec sur l'un n'empêche pas les autres.
            if (mousePressed)
            {
                try
                {
                    NativeInput.SendMouseButton(MouseUpFlag(mouseButton!));
                }
                catch (Exception e)
                {
                    OnWarning?.Invoke($"[Erreur touche] Relâchement souris '{keysStr}' : {e.Message}");
                }
            }
            for (int i = pressedKeys.Count - 1; i >= 0; i--)
            {
                var k = pressedKeys[i];
                try
                {
                    var scanCode = KeyScanCodes.Map[k];
                    NativeInput.SendKeyScanCode(scanCode.Code, scanCode.Extended, keyUp: true);
                }
                catch (Exception e)
                {
                    OnWarning?.Invoke($"[Erreur touche] Relâchement de '{k}' : {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Enfonce (et relâche) la combinaison `repeatCount` fois de suite,
    /// avec `repeatDelay` secondes entre chaque répétition. À appeler
    /// depuis un thread dédié (voir Api._execute_command côté Python) pour
    /// ne jamais geler la reconnaissance vocale pendant les délais.
    /// </summary>
    public void PressKeysRepeated(string keysStr, bool hold = false, int repeatCount = 1, double repeatDelay = 0.1)
    {
        var count = Math.Max(1, repeatCount);
        for (int i = 0; i < count; i++)
        {
            PressKeys(keysStr, hold);
            if (i < count - 1 && repeatDelay > 0)
                Thread.Sleep(TimeSpan.FromSeconds(repeatDelay));
        }
    }

    private static uint MouseDownFlag(string button) => button switch
    {
        "mouseleft" => NativeInput.MouseEventFLeftDown,
        "mouseright" => NativeInput.MouseEventFRightDown,
        "mousemiddle" => NativeInput.MouseEventFMiddleDown,
        _ => 0,
    };

    private static uint MouseUpFlag(string button) => button switch
    {
        "mouseleft" => NativeInput.MouseEventFLeftUp,
        "mouseright" => NativeInput.MouseEventFRightUp,
        "mousemiddle" => NativeInput.MouseEventFMiddleUp,
        _ => 0,
    };
}
