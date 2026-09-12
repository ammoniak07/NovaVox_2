using System.Runtime.InteropServices;

namespace NovaVox.App.Hotkeys;

internal static class KeyboardState
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}
