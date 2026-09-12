using System.Text.Json.Nodes;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Hotkeys;

public sealed record JoystickHotkey(string? Name, string? Guid, int? Button);

/// <summary>
/// Encodage/décodage et résolution d'un listen_hotkey qui désigne un
/// bouton de manette (préfixe "joy:" + JSON {name, guid, button}) plutôt
/// qu'une combinaison clavier — port de Api._joystick_hotkey_decode/
/// _match_joystick (app.py).
/// </summary>
public static class JoystickHotkeyCodec
{
    public static JoystickHotkey? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("joy:", StringComparison.Ordinal)) return null;
        try
        {
            if (JsonNode.Parse(value["joy:".Length..]) is not JsonObject obj) return null;
            return new JoystickHotkey(GetStringOrNull(obj["name"]), GetStringOrNull(obj["guid"]), GetInt(obj["button"]));
        }
        catch
        {
            return null;
        }
    }

    public static string Encode(string? name, string? guid, int button)
    {
        var obj = new JsonObject { ["name"] = name, ["guid"] = guid, ["button"] = button };
        return "joy:" + obj.ToJsonString();
    }

    /// <summary>
    /// Index, dans <paramref name="connected"/>, de la manette visée par
    /// <paramref name="target"/> — GUID matériel en priorité (identité
    /// stable même si l'index de branchement change), nom en repli.
    /// Comparaison insensible à la casse/aux espaces.
    /// </summary>
    public static int? MatchJoystickIndex(JoystickHotkey target, IReadOnlyList<(string Name, string Guid)> connected)
    {
        var guid = (target.Guid ?? "").Trim().ToLowerInvariant();
        var name = (target.Name ?? "").Trim().ToLowerInvariant();

        if (guid.Length > 0)
        {
            for (int i = 0; i < connected.Count; i++)
                if (connected[i].Guid.Trim().ToLowerInvariant() == guid) return i;
        }
        if (name.Length > 0)
        {
            for (int i = 0; i < connected.Count; i++)
                if (connected[i].Name.Trim().ToLowerInvariant() == name) return i;
        }
        return null;
    }
}
