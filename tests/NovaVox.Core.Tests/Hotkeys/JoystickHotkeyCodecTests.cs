using NovaVox.Core.Hotkeys;
using Xunit;

namespace NovaVox.Core.Tests.Hotkeys;

public class JoystickHotkeyCodecTests
{
    [Fact]
    public void Decode_ReturnsNullForKeyboardCombo()
    {
        Assert.Null(JoystickHotkeyCodec.Decode("ctrl+f9"));
    }

    [Fact]
    public void Decode_ReturnsNullForEmpty()
    {
        Assert.Null(JoystickHotkeyCodec.Decode(null));
        Assert.Null(JoystickHotkeyCodec.Decode(""));
    }

    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        var encoded = JoystickHotkeyCodec.Encode("XInput Controller", "ABC-123", 5);
        var decoded = JoystickHotkeyCodec.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Equal("XInput Controller", decoded!.Name);
        Assert.Equal("ABC-123", decoded.Guid);
        Assert.Equal(5, decoded.Button);
    }

    [Fact]
    public void Decode_ReturnsNullForMalformedJson()
    {
        Assert.Null(JoystickHotkeyCodec.Decode("joy:not-json"));
    }

    [Fact]
    public void MatchJoystickIndex_PrefersGuidOverName()
    {
        var target = new JoystickHotkey("Wrong Name", "GUID-1", 0);
        var connected = new List<(string Name, string Guid)>
        {
            ("Some Pad", "guid-1"),
            ("Wrong Name", "guid-2"),
        };
        Assert.Equal(0, JoystickHotkeyCodec.MatchJoystickIndex(target, connected));
    }

    [Fact]
    public void MatchJoystickIndex_FallsBackToNameWhenGuidMissing()
    {
        var target = new JoystickHotkey("My Pad", null, 0);
        var connected = new List<(string Name, string Guid)> { ("my pad", "guid-1") };
        Assert.Equal(0, JoystickHotkeyCodec.MatchJoystickIndex(target, connected));
    }

    [Fact]
    public void MatchJoystickIndex_ReturnsNullWhenNoMatch()
    {
        var target = new JoystickHotkey("Ghost Pad", "GUID-X", 0);
        var connected = new List<(string Name, string Guid)> { ("Real Pad", "guid-1") };
        Assert.Null(JoystickHotkeyCodec.MatchJoystickIndex(target, connected));
    }
}
