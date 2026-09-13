using NovaVox.Core;
using Vortice.DirectInput;

namespace NovaVox.App.Hotkeys;

/// <summary>
/// Énumération et lecture de l'état des manettes/joysticks via
/// DirectInput (Vortice.DirectInput) — remplace pygame.joystick côté
/// Python. Couvre un plus large éventail de périphériques (HOTAS,
/// volants...) que les API modernes limitées au XInput (manettes Xbox).
/// </summary>
public sealed class JoystickManager : IDisposable
{
    private readonly IDirectInput8 _directInput = DInput.DirectInput8Create();
    private readonly Dictionary<Guid, IDirectInputDevice8> _acquired = new();
    private readonly IntPtr _windowHandle;

    public JoystickManager(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    /// <summary>Manettes actuellement branchées : (nom, GUID produit stable — identité du modèle, pas de l'instance de branchement).</summary>
    public IReadOnlyList<(string Name, string Guid, Guid InstanceGuid)> GetJoysticks()
    {
        var result = new List<(string, string, Guid)>();
        foreach (var device in _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            result.Add((device.InstanceName, device.ProductGuid.ToString(), device.InstanceGuid));
        return result;
    }

    private IDirectInputDevice8? AcquireDevice(Guid instanceGuid)
    {
        if (_acquired.TryGetValue(instanceGuid, out var existing)) return existing;
        try
        {
            var device = _directInput.CreateDevice(instanceGuid);
            device.SetDataFormat<RawJoystickState>();
            device.SetCooperativeLevel(_windowHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
            device.Acquire();
            _acquired[instanceGuid] = device;
            return device;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True/False si l'état du bouton est déterminable, null sinon (manette débranchée, bouton hors limites, erreur).</summary>
    public bool? IsButtonHeld(Guid instanceGuid, int buttonIndex)
    {
        var device = AcquireDevice(instanceGuid);
        if (device is null) return null;
        try
        {
            device.Poll();
            var state = device.GetCurrentJoystickState();
            if (buttonIndex < 0 || buttonIndex >= state.Buttons.Length) return null;
            return state.Buttons[buttonIndex];
        }
        catch (Exception ex)
        {
            // Manette débranchée en cours de route : abandonne cette
            // instance, une prochaine lecture retentera une acquisition.
            // Ne se reproduit qu'une fois par débranchement réel (pas à
            // chaque poll), donc sans risque de saturer le journal.
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Joystick] Manette {instanceGuid} perdue en cours de lecture ({ex.Message}).", "diagnostic");
            _acquired.Remove(instanceGuid);
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var device in _acquired.Values)
        {
            try
            {
                device.Unacquire();
            }
            catch (Exception ex)
            {
                AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Joystick] Libération d'une manette échouée à la fermeture ({ex.Message}).", "diagnostic");
            }
            device.Dispose();
        }
        _acquired.Clear();
        _directInput.Dispose();
    }
}
