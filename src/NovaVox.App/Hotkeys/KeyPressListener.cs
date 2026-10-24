using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NovaVox.App.Hotkeys;

/// <summary>
/// Écoute les appuis de touches PHYSIQUES du joueur dans tout Windows
/// (crochet clavier bas niveau), pour l'option "Lire" des commandes : Nova
/// lit la phrase d'une commande quand on appuie soi-même sur sa touche.
/// Les touches envoyées par NovaVox (commandes vocales, SendInput) sont
/// marquées "injectées" par Windows et ignorées, la répétition automatique
/// d'une touche maintenue aussi, ainsi que les touches tapées dans NovaVox.
/// </summary>
public sealed class KeyPressListener : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;
    private const uint LlkhfExtended = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private readonly LowLevelKeyboardProc _proc;
    private readonly HashSet<uint> _down = new();
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private IntPtr _hook;

    /// <summary>Touche physique enfoncée (code VK), avec l'état des modificateurs à cet instant. Appelé sur le fil qui a installé le crochet (interface).</summary>
    public event EventHandler<KeyPressedEventArgs>? KeyPressed;

    public KeyPressListener()
    {
        _proc = HookCallback;
    }

    /// <summary>À appeler depuis le fil de l'interface (qui a une boucle de messages).</summary>
    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(module?.ModuleName), 0);
        return _hook != IntPtr.Zero;
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _down.Clear();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                var message = wParam.ToInt32();
                if (message is WmKeyUp or WmSysKeyUp)
                {
                    _down.Remove(data.VkCode);
                }
                else if (message is WmKeyDown or WmSysKeyDown
                         && (data.Flags & LlkhfInjected) == 0
                         && _down.Add(data.VkCode)
                         && !NovaVoxHasFocus())
                {
                    KeyPressed?.Invoke(this, new KeyPressedEventArgs((int)data.VkCode, (int)data.ScanCode, (data.Flags & LlkhfExtended) != 0,
                        Ctrl: KeyboardState.IsKeyDown(0x11),
                        Shift: KeyboardState.IsKeyDown(0x10),
                        Alt: KeyboardState.IsKeyDown(0xA4) || data.VkCode == 0xA4,
                        AltGr: KeyboardState.IsKeyDown(0xA5) || data.VkCode == 0xA5));
                }
            }
            catch
            {
                // Ne jamais bloquer le clavier de Windows à cause d'une erreur ici.
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool NovaVoxHasFocus()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var pid);
        return pid == _ownProcessId;
    }

    public void Dispose() => Stop();
}

public sealed record KeyPressedEventArgs(int VirtualKey, int ScanCode, bool Extended, bool Ctrl, bool Shift, bool Alt, bool AltGr);

/// <summary>Correspondance entre une touche pressée et le champ "touches" d'une commande ("alt+n", "f5", "ctrl+shift+m"...).</summary>
public static class CommandKeyMatcher
{
    private static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase) { "ctrl", "shift", "alt", "altright", "winleft" };

    /// <summary>Vrai si l'appui correspond exactement à la combinaison (même touche principale, mêmes modificateurs).</summary>
    public static bool Matches(string? keys, KeyPressedEventArgs press)
    {
        if (string.IsNullOrWhiteSpace(keys)) return false;
        var parts = keys.Split('+').Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
        var main = parts.Where(p => !Modifiers.Contains(p)).ToList();
        if (main.Count != 1) return false;
        // Les noms de touches NovaVox sont des POSITIONS physiques (comme KeyScanCodes) :
        // comparer le code de balayage, pas le code virtuel qui suit la disposition
        // (sur AZERTY, la position "q" donne VK_A).
        if (NovaVox.App.Input.KeyScanCodes.Map.TryGetValue(main[0], out var sc))
        {
            if (sc.Code != press.ScanCode || sc.Extended != press.Extended) return false;
        }
        else if (!VirtualKeys.Map.TryGetValue(main[0], out var vk) || vk != press.VirtualKey) return false;
        return parts.Contains("ctrl") == press.Ctrl
            && parts.Contains("shift") == press.Shift
            && parts.Contains("alt") == press.Alt
            && parts.Contains("altright") == press.AltGr;
    }
}
