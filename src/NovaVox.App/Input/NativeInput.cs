using System.Runtime.InteropServices;

namespace NovaVox.App.Input;

/// <summary>
/// P/Invoke direct de user32!SendInput en mode "code de balayage brut"
/// (KEYEVENTF_SCANCODE) — port de _send_raw_scan_code (app.py), généralisé
/// à toutes les touches (voir KeyScanCodes) et aux boutons souris.
/// </summary>
internal static class NativeInput
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint KeyEventFExtendedKey = 0x0001;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFScanCode = 0x0008;

    public const uint MouseEventFLeftDown = 0x0002;
    public const uint MouseEventFLeftUp = 0x0004;
    public const uint MouseEventFRightDown = 0x0008;
    public const uint MouseEventFRightUp = 0x0010;
    public const uint MouseEventFMiddleDown = 0x0020;
    public const uint MouseEventFMiddleUp = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint UMsg;
        public ushort WParamL;
        public ushort WParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mi;
        [FieldOffset(0)] public KeyboardInput Ki;
        [FieldOffset(0)] public HardwareInput Hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    public static void SendKeyScanCode(ushort scanCode, bool extended, bool keyUp)
    {
        var flags = KeyEventFScanCode
            | (extended ? KeyEventFExtendedKey : 0)
            | (keyUp ? KeyEventFKeyUp : 0);

        var input = new Input
        {
            Type = InputKeyboard,
            U = new InputUnion { Ki = new KeyboardInput { WVk = 0, WScan = scanCode, DwFlags = flags, Time = 0, DwExtraInfo = IntPtr.Zero } },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    public static void SendMouseButton(uint flags)
    {
        var input = new Input
        {
            Type = InputMouse,
            U = new InputUnion { Mi = new MouseInput { Dx = 0, Dy = 0, MouseData = 0, DwFlags = flags, Time = 0, DwExtraInfo = IntPtr.Zero } },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }
}
