using NovaVox.Core.Config;
using Xunit;

namespace NovaVox.Core.Tests.Config;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novavox-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void AudioConfig_ClampsAndValidatesOnLoad()
    {
        File.WriteAllText(Path.Combine(_dir, "audio_config.json"), """
        {"mic_gain": 999, "mic_gate": -5, "listen_mode": "bogus", "listen_hotkey": "Ctrl+F9",
         "kb_layout": "qwerty", "aec_enabled": true, "tts_volume": 5}
        """);
        var config = new AudioConfigStore(_dir).Load();
        Assert.Equal(3.0, config.MicGain);
        Assert.Equal(0, config.MicGate);
        Assert.Equal(AudioConfig.DefaultListenMode, config.ListenMode); // "bogus" rejeté
        Assert.Equal("ctrl+f9", config.ListenHotkey);
        Assert.Equal("qwerty", config.KbLayout);
        Assert.True(config.AecEnabled);
        Assert.Equal(1.5, config.TtsVolume);
    }

    [Fact]
    public void AudioConfig_JoystickHotkeyKeepsCase()
    {
        File.WriteAllText(Path.Combine(_dir, "audio_config.json"), """
        {"listen_hotkey": "joy:{\"name\":\"XInput Controller\",\"guid\":\"ABC\",\"button\":3}"}
        """);
        var config = new AudioConfigStore(_dir).Load();
        Assert.StartsWith("joy:", config.ListenHotkey);
        Assert.Contains("XInput Controller", config.ListenHotkey!);
    }

    [Fact]
    public void AudioConfig_RoundTrips()
    {
        var store = new AudioConfigStore(_dir);
        var config = new AudioConfig { InputDevice = "Micro USB", MicGain = 1.5, ListenMode = "toggle_key" };
        store.Save(config);
        var reloaded = new AudioConfigStore(_dir).Load();
        Assert.Equal("Micro USB", reloaded.InputDevice);
        Assert.Equal(1.5, reloaded.MicGain);
        Assert.Equal("toggle_key", reloaded.ListenMode);
    }

    [Fact]
    public void WindowConfig_DefaultsWhenNoFile()
    {
        var bounds = new WindowConfigStore(_dir).Load();
        Assert.Equal(WindowConfigStore.DefaultWidth, bounds.Width);
        Assert.Equal(WindowConfigStore.DefaultHeight, bounds.Height);
        Assert.Null(bounds.X);
    }

    [Fact]
    public void WindowConfig_IgnoresPositionOffAllScreens()
    {
        var store = new WindowConfigStore(_dir);
        store.Save(1000, 900, x: 5000, y: 5000);
        var bounds = store.Load(virtualScreenBounds: (0, 0, 1920, 1080));
        Assert.Null(bounds.X);
        Assert.Null(bounds.Y);
    }

    [Fact]
    public void WindowConfig_KeepsPositionWithinScreen()
    {
        var store = new WindowConfigStore(_dir);
        store.Save(1000, 900, x: 100, y: 100);
        var bounds = store.Load(virtualScreenBounds: (0, 0, 1920, 1080));
        Assert.Equal(100, bounds.X);
        Assert.Equal(100, bounds.Y);
    }

    [Fact]
    public void OverlayConfig_ValidatesHexColorAndOpacity()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"), """
        {"enabled": true, "bg_color": "not-a-color", "bg_opacity": 250, "text_opacity": -10}
        """);
        var config = new OverlayConfigStore(_dir).Load();
        Assert.True(config.Enabled);
        Assert.Equal(OverlayConfig.DefaultBgColor, config.BgColor);
        Assert.Equal(100, config.BgOpacity);
        Assert.Equal(0, config.TextOpacity);
    }

    [Fact]
    public void OverlayConfig_PreservesUnknownRowsDefaultVisible()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"), """{"visible_rows": {"mic": false}}""");
        var config = new OverlayConfigStore(_dir).Load();
        Assert.False(config.VisibleRows["mic"]);
        Assert.True(config.VisibleRows["zone"]);
    }

    [Fact]
    public void AiConfig_FallsBackWhenModelUnknown()
    {
        File.WriteAllText(Path.Combine(_dir, "ai_config.json"), """{"gemini_model": "gemini-retired-model"}""");
        var config = new AiConfigStore(_dir).Load(knownGeminiModels: new[] { "gemini-3.6-flash" });
        Assert.Equal(AiConfig.DefaultGeminiModel, config.GeminiModel);
    }

    [Fact]
    public void AiConfig_RoundTripsGameLogDictionaries()
    {
        var store = new AiConfigStore(_dir);
        var config = new AiConfig();
        config.GameLogHudOverrides["Pyro I"] = "Pyro Un";
        store.Save(config);
        var reloaded = new AiConfigStore(_dir).Load();
        Assert.Equal("Pyro Un", reloaded.GameLogHudOverrides["Pyro I"]);
    }
}
