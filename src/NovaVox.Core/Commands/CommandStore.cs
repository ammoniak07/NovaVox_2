using System.Text.Json.Nodes;
using NovaVox.Core.Json;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Commands;

public sealed class ProfileInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Count { get; set; }

    // Le ComboBox custom-templaté de la sélection de profil (MainWindow.xaml)
    // affiche l'élément sélectionné via SelectionBoxItem, qui retombe sur
    // ToString() plutôt que DisplayMemberPath dans ce cas précis — sans ceci,
    // la case affichait le nom complet du type ("NovaVox.Core.Commands.ProfileInfo")
    // au lieu du nom du profil.
    public override string ToString() => Name;
}

/// <summary>
/// Port de la persistance JSON de app.py : commands.json, profils multiples
/// (profiles/*.json + profiles_config.json). Mêmes noms de fichiers et même
/// schéma JSON (snake_case) qu'en Python pour rester compatible avec une
/// configuration NovaVox existante.
/// </summary>
public sealed class CommandStore
{
    public const string DefaultProfileName = "Défaut";
    public const int MaxCommandExtraSteps = 4;
    public const double DefaultExtraStepDelay = 0.3;

    private readonly string _baseDir;

    public string ConfigFile => Path.Combine(_baseDir, "commands.json");
    public string ProfilesDir => Path.Combine(_baseDir, "profiles");
    public string ProfilesMetaFile => Path.Combine(_baseDir, "profiles_config.json");

    /// <summary>Id du profil actuellement actif — piloté par l'appelant (Api côté Python).</summary>
    public string? ActiveProfileId { get; set; }

    public CommandStore(string baseDir)
    {
        _baseDir = baseDir;
    }

    public static List<VoiceCommand> DefaultCommands() => new()
    {
        new VoiceCommand
        {
            Phrase = "train d'atterrissage", Keys = "n",
            Synonyms = new() { "train d atterrissage", "train d'atterissage", "train d aterissage" },
        },
        new VoiceCommand { Phrase = "scanner", Keys = "v" },
        new VoiceCommand
        {
            Phrase = "poste combustion", Keys = "shift",
            Synonyms = new() { "postcombustion", "post combustion", "boss combustion", "poste-combustion", "passe combustion" },
        },
        new VoiceCommand { Phrase = "mode quantique", Keys = "b" },
        new VoiceCommand { Phrase = "bouclier", Keys = "insert", Synonyms = new() { "boucliers", "goupillier" } },
    };

    /// <summary>
    /// Assure la compatibilité ascendante et la cohérence de la liste
    /// (commandes ET titres de groupe, mélangés dans une seule liste
    /// ordonnée). Les entrées invalides sont ignorées plutôt que de faire
    /// planter le chargement, comme _normalize_commands côté Python.
    /// </summary>
    public static List<VoiceCommand> NormalizeCommands(JsonNode? data)
    {
        var normalized = new List<VoiceCommand>();
        if (data is not JsonArray array) return normalized;

        foreach (var itemNode in array)
        {
            if (itemNode is not JsonObject item) continue;

            if (GetString(item["type"]) == "title")
            {
                normalized.Add(new VoiceCommand
                {
                    Type = "title",
                    Phrase = GetString(item["text"]).Trim(),
                    Collapsed = GetBool(item["collapsed"]),
                });
                continue;
            }

            if (item["phrase"] is null || item["keys"] is null) continue;

            var cmd = new VoiceCommand
            {
                Type = "command",
                Phrase = GetString(item["phrase"]),
                Keys = GetString(item["keys"]),
                Hold = GetBool(item["hold"]),
                Synonyms = (item["synonyms"] as JsonArray)?.Select(n => GetString(n)).ToList() ?? new List<string>(),
                RepeatCount = ClampInt(item["repeat_count"], fallback: 1, min: 1, max: 50),
                RepeatDelay = ClampDouble(item["repeat_delay"], fallback: 0.1, min: 0.0, max: 10.0),
                ExtraSteps = NormalizeExtraSteps(item["extra_steps"] as JsonArray),
            };
            normalized.Add(cmd);
        }
        return normalized;
    }

    public static List<ExtraStep> NormalizeExtraSteps(JsonArray? rawSteps)
    {
        var steps = new List<ExtraStep>();
        if (rawSteps is null) return steps;
        foreach (var rawNode in rawSteps.Take(MaxCommandExtraSteps))
        {
            if (rawNode is not JsonObject raw) continue;
            var keys = GetString(raw["keys"]).Trim();
            if (keys.Length == 0) continue;
            steps.Add(new ExtraStep
            {
                Keys = keys,
                DelayBefore = ClampDouble(raw["delay"], fallback: DefaultExtraStepDelay, min: 0.0, max: 10.0),
            });
        }
        return steps;
    }

    public static JsonArray CommandsToJson(IEnumerable<VoiceCommand> commands)
    {
        var arr = new JsonArray();
        foreach (var cmd in commands)
        {
            if (cmd.IsTitle)
            {
                arr.Add(new JsonObject { ["type"] = "title", ["text"] = cmd.Phrase, ["collapsed"] = cmd.Collapsed });
                continue;
            }
            arr.Add(new JsonObject
            {
                ["type"] = "command",
                ["phrase"] = cmd.Phrase,
                ["keys"] = cmd.Keys,
                ["hold"] = cmd.Hold,
                ["repeat_count"] = cmd.RepeatCount,
                ["repeat_delay"] = cmd.RepeatDelay,
                ["synonyms"] = new JsonArray(cmd.Synonyms.Select(s => (JsonNode)JsonValue.Create(s)).ToArray()),
                ["extra_steps"] = new JsonArray(cmd.ExtraSteps.Select(s => (JsonNode)new JsonObject
                {
                    ["keys"] = s.Keys,
                    ["delay"] = s.DelayBefore,
                }).ToArray()),
            });
        }
        return arr;
    }

    public List<VoiceCommand> LoadCommands()
    {
        if (File.Exists(ConfigFile))
        {
            try
            {
                var data = JsonNode.Parse(StripBom(File.ReadAllText(ConfigFile)));
                return NormalizeCommands(data);
            }
            catch
            {
                // Retour aux commandes par défaut, comme côté Python.
            }
        }
        return NormalizeCommands(CommandsToJson(DefaultCommands()));
    }

    /// <param name="mirrorToProfile">
    /// false quand on vient justement de charger commands.json À PARTIR du
    /// profil actif (au démarrage, ou juste après un switch) : le fichier
    /// de profil est alors déjà identique, pas besoin de le réécrire.
    /// </param>
    public void SaveCommands(List<VoiceCommand> commands, bool mirrorToProfile = true)
    {
        Directory.CreateDirectory(_baseDir);
        File.WriteAllText(ConfigFile, CommandsToJson(commands).ToJsonString(WriteOptions));

        if (!mirrorToProfile || ActiveProfileId is null) return;
        var profilePath = ProfilePath(ActiveProfileId);
        if (!File.Exists(profilePath))
        {
            // Le profil actif vient d'être supprimé : ne pas recréer un
            // profil "Défaut" fantôme à cet emplacement.
            return;
        }
        try
        {
            var (name, _, game) = ReadProfile(ActiveProfileId);
            WriteProfile(ActiveProfileId, name, commands, game);
        }
        catch
        {
            // Best effort, comme côté Python.
        }
    }

    public string ProfilePath(string profileId)
    {
        Directory.CreateDirectory(ProfilesDir);
        return Path.Combine(ProfilesDir, $"{profileId}.json");
    }

    /// <summary>Génère un identifiant de profil basé sur l'horodatage (millisecondes), vérifié inoccupé.</summary>
    public string NewProfileId()
    {
        Directory.CreateDirectory(ProfilesDir);
        while (true)
        {
            var candidate = $"p{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            if (!File.Exists(ProfilePath(candidate))) return candidate;
            Thread.Sleep(1);
        }
    }

    public void WriteProfile(string profileId, string displayName, List<VoiceCommand> commands, ProfileGameSettings? game = null)
    {
        var path = ProfilePath(profileId);
        var tmp = path + ".tmp";
        var obj = new JsonObject
        {
            ["name"] = displayName,
            ["commands"] = CommandsToJson(commands),
            ["game"] = GameSettingsToJson(game ?? new ProfileGameSettings()),
        };
        File.WriteAllText(tmp, obj.ToJsonString(WriteOptions));
        File.Move(tmp, path, overwrite: true); // écriture atomique
    }

    /// <summary>Port JSON de ProfileGameSettings — clé "game" du fichier de profil, à côté de "commands".</summary>
    public static JsonObject GameSettingsToJson(ProfileGameSettings game)
    {
        var rows = new JsonObject();
        foreach (var (key, value) in game.OverlayVisibleRows) rows[key] = value;
        return new JsonObject
        {
            ["game_log_enabled"] = game.GameLogEnabled,
            ["gemini_wiki_enabled"] = game.GeminiWikiEnabled,
            ["overlay_visible_rows"] = rows,
            ["background_image_path"] = game.BackgroundImagePath,
        };
    }

    /// <summary>Absente (profil créé avant cette fonctionnalité) : réglages par défaut, comportement inchangé pour un profil existant.</summary>
    public static ProfileGameSettings ParseGameSettings(JsonObject? data)
    {
        var game = new ProfileGameSettings();
        if (data is null) return game;
        game.GameLogEnabled = GetBool(data["game_log_enabled"], true);
        game.GeminiWikiEnabled = GetBool(data["gemini_wiki_enabled"], true);
        if (data["overlay_visible_rows"] is JsonObject rows)
        {
            foreach (var kv in rows)
                if (kv.Value is not null) game.OverlayVisibleRows[kv.Key] = GetBool(kv.Value, true);
        }
        var bg = GetStringOrNull(data["background_image_path"]);
        game.BackgroundImagePath = string.IsNullOrWhiteSpace(bg) ? null : bg;
        return game;
    }

    public (string Name, List<VoiceCommand> Commands, ProfileGameSettings Game) ReadProfile(string profileId)
    {
        var path = ProfilePath(profileId);
        var data = JsonNode.Parse(StripBom(File.ReadAllText(path)));
        if (data is JsonObject obj)
        {
            var name = GetString(obj["name"]);
            if (string.IsNullOrWhiteSpace(name)) name = profileId;
            return (name.Trim(), NormalizeCommands(obj["commands"]), ParseGameSettings(obj["game"] as JsonObject));
        }
        // Tolère un fichier qui ne serait qu'une liste brute.
        return (profileId, NormalizeCommands(data), new ProfileGameSettings());
    }

    public List<ProfileInfo> ListProfiles()
    {
        Directory.CreateDirectory(ProfilesDir);
        var result = new List<ProfileInfo>();
        foreach (var path in Directory.EnumerateFiles(ProfilesDir, "*.json"))
        {
            if (path.EndsWith(".tmp", StringComparison.Ordinal)) continue;
            var pid = Path.GetFileNameWithoutExtension(path);
            try
            {
                var (name, commands, _) = ReadProfile(pid);
                var count = commands.Count(c => !c.IsTitle);
                result.Add(new ProfileInfo { Id = pid, Name = name, Count = count });
            }
            catch
            {
                // Fichier illisible/corrompu : ignoré, comme côté Python.
            }
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public string? LoadActiveProfileId()
    {
        try
        {
            var data = JsonNode.Parse(StripBom(File.ReadAllText(ProfilesMetaFile))) as JsonObject;
            var pid = GetString(data?["active"]);
            return string.IsNullOrEmpty(pid) ? null : pid;
        }
        catch
        {
            return null;
        }
    }

    public void SaveActiveProfileId(string profileId)
    {
        try
        {
            var obj = new JsonObject { ["active"] = profileId };
            File.WriteAllText(ProfilesMetaFile, obj.ToJsonString(WriteOptions));
        }
        catch
        {
            // Confort seulement : au pire on retombe sur le 1er profil au prochain lancement.
        }
    }

    /// <summary>
    /// Première utilisation de cette fonctionnalité (dossier profiles/
    /// absent ou vide) : crée un profil "Défaut" reprenant telles quelles
    /// les commandes actuelles de commands.json. Retourne l'id du profil
    /// créé, ou null si des profils existaient déjà.
    /// </summary>
    /// <param name="currentGameSettings">
    /// Réglages jeu (Game.log/wiki Gemini/lignes overlay) actuellement en
    /// vigueur (ai_config.json/overlay_config.json), à figer dans ce
    /// premier profil migré — sinon il repartirait sur les valeurs par
    /// défaut de ProfileGameSettings au lieu de ce que l'utilisateur a
    /// déjà configuré. Passer null pour les valeurs par défaut (tests).
    /// </param>
    public string? EnsureProfilesMigrated(ProfileGameSettings? currentGameSettings = null)
    {
        Directory.CreateDirectory(ProfilesDir);
        var existingFiles = Directory.EnumerateFiles(ProfilesDir, "*.json")
            .Where(p => !p.EndsWith(".tmp", StringComparison.Ordinal))
            .ToList();
        if (existingFiles.Count > 0) return null;

        var commands = LoadCommands();
        var pid = NewProfileId();
        WriteProfile(pid, DefaultProfileName, commands, currentGameSettings);
        SaveActiveProfileId(pid);
        return pid;
    }

}
