using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using ServerSync;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using ReloadTimer = System.Timers.Timer;

namespace HarnessPrefabs;

internal static class PrefabLocalizationOverrideManager
{
    private static readonly AccessTools.FieldRef<Localization, Dictionary<string, string>> LocalizationTranslations = AccessTools.FieldRefAccess<Localization, Dictionary<string, string>>("m_translations");

    private const string DomainName = "localization";
    private const string DefaultLanguageFileName = "English.yml";
    private const string SyncedPayloadKey = "localization-yaml";
    private const double ReloadDebounceMilliseconds = 500d;

    private static readonly object StateLock = new();
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .DisableAliases()
        .Build();

    private static LocalizationPayload _activePayload = new();
    private static CustomSyncedValue<string>? _syncedPayload;
    private static FileSystemWatcher? _watcher;
    private static ReloadTimer? _reloadTimer;
    private static string? _lastParsedPayload;

    private static readonly Dictionary<string, Dictionary<string, string?>> OriginalTranslationsByLanguage = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<string>> AppliedKeysByLanguage = new(StringComparer.OrdinalIgnoreCase);

    private static string LocalizationDirectory => Path.Combine(PrefabRuleStore.RulesDirectory, DomainName);

    public static void Initialize(ConfigSync configSync)
    {
        _syncedPayload = new CustomSyncedValue<string>(configSync, SyncedPayloadKey, "", priority: 90);
        _syncedPayload.ValueChanged += OnSyncedPayloadChanged;
    }

    public static void SetupFileWatcher()
    {
        if (!HarnessPrefabsPlugin.IsSourceOfTruth)
        {
            _reloadTimer?.Stop();
            _watcher?.Dispose();
            _watcher = null;
            return;
        }

        _watcher?.Dispose();
        _reloadTimer ??= CreateReloadTimer();
        EnsureDirectoryAndDefaultFile();
        _watcher = new FileSystemWatcher(LocalizationDirectory, "*.*")
        {
            IncludeSubdirectories = false,
            SynchronizingObject = ThreadingHelper.SynchronizingObject,
            EnableRaisingEvents = true
        };
        _watcher.Changed += ScheduleLocalizationReload;
        _watcher.Created += ScheduleLocalizationReload;
        _watcher.Deleted += ScheduleLocalizationReload;
        _watcher.Renamed += ScheduleLocalizationReload;
    }

    public static void Dispose()
    {
        if (_syncedPayload != null)
        {
            _syncedPayload.ValueChanged -= OnSyncedPayloadChanged;
        }

        _watcher?.Dispose();
        _watcher = null;
        _reloadTimer?.Dispose();
        _reloadTimer = null;
    }

    public static bool ReloadFromDiskAndSync()
    {
        if (!HarnessPrefabsPlugin.IsSourceOfTruth)
        {
            return ApplySyncedPayload(_syncedPayload?.Value ?? "");
        }

        LocalizationPayload payload;
        string serializedPayload;
        try
        {
            EnsureDirectoryAndDefaultFile();
            payload = LoadPayloadFromDisk();
            serializedPayload = SerializePayload(payload);
            PublishPayload(serializedPayload);
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogError($"Failed to reload localization YAML. Keeping the last known good localization. Error: {ex.Message}");
            return false;
        }

        lock (StateLock)
        {
            _activePayload = payload;
            _lastParsedPayload = serializedPayload;
        }

        ApplyCurrentLocalization();
        return true;
    }

    public static void ApplyCurrentLocalization()
    {
        Localization localization = Localization.instance;
        if (localization == null)
        {
            return;
        }

        ApplyCurrentLocalization(localization, localization.GetSelectedLanguage());
    }

    public static void ApplyCurrentLocalization(Localization localization, string? language)
    {
        if (localization == null)
        {
            return;
        }

        string languageKey = NormalizeLanguage(language);
        Dictionary<string, string> translations;
        lock (StateLock)
        {
            translations = BuildTranslationsForLanguage(_activePayload, languageKey);
        }

        RestoreRemovedTranslations(localization, languageKey, translations.Keys);
        foreach (KeyValuePair<string, string> translation in translations)
        {
            ApplyTranslation(localization, languageKey, translation.Key, translation.Value);
        }

        AppliedKeysByLanguage[languageKey] = new HashSet<string>(translations.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private static void ScheduleLocalizationReload(object sender, FileSystemEventArgs e)
    {
        if (!HarnessPrefabsPlugin.IsSourceOfTruth || !IsLocalizationFile(e.FullPath))
        {
            return;
        }

        _reloadTimer?.Stop();
        _reloadTimer?.Start();
    }

    private static void ReadLocalizationValues(object sender, System.Timers.ElapsedEventArgs e)
    {
        if (!HarnessPrefabsPlugin.IsSourceOfTruth)
        {
            return;
        }

        if (ReloadFromDiskAndSync())
        {
            HarnessPrefabsPlugin.Log.LogInfo("Localization YAML reload complete.");
        }
    }

    private static ReloadTimer CreateReloadTimer()
    {
        ReloadTimer timer = new(ReloadDebounceMilliseconds)
        {
            AutoReset = false,
            SynchronizingObject = ThreadingHelper.SynchronizingObject
        };
        timer.Elapsed += ReadLocalizationValues;
        return timer;
    }

    private static void OnSyncedPayloadChanged()
    {
        if (HarnessPrefabsPlugin.IsSourceOfTruth)
        {
            return;
        }

        _ = ApplySyncedPayload(_syncedPayload?.Value ?? "");
    }

    private static bool ApplySyncedPayload(string payload)
    {
        if (string.Equals(_lastParsedPayload, payload, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryDeserializePayload(payload, "synced localization payload", out LocalizationPayload localizationPayload))
        {
            return false;
        }

        lock (StateLock)
        {
            _activePayload = localizationPayload;
            _lastParsedPayload = payload;
        }

        ApplyCurrentLocalization();
        return true;
    }

    private static void PublishPayload(string payload)
    {
        if (_syncedPayload == null || string.Equals(_syncedPayload.Value ?? "", payload, StringComparison.Ordinal))
        {
            return;
        }

        _syncedPayload.Value = payload;
    }

    private static LocalizationPayload LoadPayloadFromDisk()
    {
        LocalizationPayload payload = new();
        if (!Directory.Exists(LocalizationDirectory))
        {
            return payload;
        }

        foreach (string file in Directory.GetFiles(LocalizationDirectory, "*.yml")
                     .Concat(Directory.GetFiles(LocalizationDirectory, "*.yaml"))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string language = Path.GetFileNameWithoutExtension(file);
            Dictionary<string, string> translations = LoadTranslationMap(file, $"{language} localization");
            if (translations.Count == 0)
            {
                continue;
            }

            if (!payload.Languages.TryGetValue(language, out Dictionary<string, string>? languageTranslations))
            {
                languageTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                payload.Languages[language] = languageTranslations;
            }

            MergeTranslations(languageTranslations, translations);
        }

        return payload;
    }

    private static Dictionary<string, string> LoadTranslationMap(string path, string source)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        string yaml = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            Dictionary<string, string?>? map = Deserializer.Deserialize<Dictionary<string, string?>>(yaml);
            return NormalizeTranslationMap(map, source);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to parse {source} from '{path}': {ex.Message}", ex);
        }
    }

    private static Dictionary<string, string> NormalizeTranslationMap(Dictionary<string, string?>? map, string source)
    {
        Dictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);
        if (map == null)
        {
            return normalized;
        }

        foreach (KeyValuePair<string, string?> pair in map)
        {
            string token = NormalizeToken(pair.Key);
            if (token.Length == 0)
            {
                HarnessPrefabsPlugin.Log.LogWarning($"Skipping localization entry with an empty token in {source}.");
                continue;
            }

            normalized[token] = pair.Value ?? "";
        }

        return normalized;
    }

    private static string SerializePayload(LocalizationPayload payload)
    {
        return Serializer.Serialize(payload);
    }

    private static bool TryDeserializePayload(string payload, string source, out LocalizationPayload localizationPayload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            localizationPayload = new LocalizationPayload();
            return true;
        }

        try
        {
            LocalizationPayload? parsed = Deserializer.Deserialize<LocalizationPayload>(payload);
            localizationPayload = NormalizePayload(parsed, source);
            return true;
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogError($"Failed to parse {source}: {ex.Message}");
            localizationPayload = null!;
            return false;
        }
    }

    private static LocalizationPayload NormalizePayload(LocalizationPayload? payload, string source)
    {
        LocalizationPayload normalized = new();
        if (payload == null)
        {
            return normalized;
        }

        MergeTranslations(normalized.All, NormalizeStringTranslationMap(payload.All, $"{source} common"));
        foreach (KeyValuePair<string, Dictionary<string, string>> language in payload.Languages)
        {
            string languageName = NormalizeLanguage(language.Key);
            if (languageName.Length == 0)
            {
                continue;
            }

            normalized.Languages[languageName] = NormalizeStringTranslationMap(language.Value, $"{source} {languageName}");
        }

        return normalized;
    }

    private static Dictionary<string, string> NormalizeStringTranslationMap(Dictionary<string, string>? map, string source)
    {
        return NormalizeTranslationMap(map?.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase), source);
    }

    private static Dictionary<string, string> BuildTranslationsForLanguage(LocalizationPayload payload, string language)
    {
        Dictionary<string, string> translations = new(StringComparer.OrdinalIgnoreCase);
        MergeTranslations(translations, payload.All);
        if (!language.Equals("English", StringComparison.OrdinalIgnoreCase) &&
            payload.Languages.TryGetValue("English", out Dictionary<string, string>? englishTranslations))
        {
            MergeTranslations(translations, englishTranslations);
        }

        if (payload.Languages.TryGetValue(language, out Dictionary<string, string>? languageTranslations))
        {
            MergeTranslations(translations, languageTranslations);
        }

        return translations;
    }

    private static void MergeTranslations(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (KeyValuePair<string, string> pair in source)
        {
            target[NormalizeToken(pair.Key)] = pair.Value;
        }
    }

    private static void ApplyTranslation(Localization localization, string language, string token, string text)
    {
        token = NormalizeToken(token);
        if (token.Length == 0)
        {
            return;
        }

        Dictionary<string, string?> originalTranslations = GetOriginalTranslations(language);
        if (!originalTranslations.ContainsKey(token))
        {
            originalTranslations[token] = LocalizationTranslations(localization).TryGetValue(token, out string? originalText)
                ? originalText
                : null;
        }

        LocalizationTranslations(localization)[token] = text;
    }

    private static void RestoreRemovedTranslations(Localization localization, string language, IEnumerable<string> currentTokens)
    {
        HashSet<string> current = new(currentTokens.Select(NormalizeToken), StringComparer.OrdinalIgnoreCase);
        if (!AppliedKeysByLanguage.TryGetValue(language, out HashSet<string>? previous))
        {
            return;
        }

        Dictionary<string, string?> originalTranslations = GetOriginalTranslations(language);
        foreach (string token in previous.Where(token => !current.Contains(token)).ToArray())
        {
            if (!originalTranslations.TryGetValue(token, out string? originalText))
            {
                continue;
            }

            if (originalText == null)
            {
                LocalizationTranslations(localization).Remove(token);
            }
            else
            {
                LocalizationTranslations(localization)[token] = originalText;
            }

            originalTranslations.Remove(token);
        }
    }

    private static Dictionary<string, string?> GetOriginalTranslations(string language)
    {
        if (!OriginalTranslationsByLanguage.TryGetValue(language, out Dictionary<string, string?>? translations))
        {
            translations = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            OriginalTranslationsByLanguage[language] = translations;
        }

        return translations;
    }

    private static void EnsureDirectoryAndDefaultFile()
    {
        Directory.CreateDirectory(LocalizationDirectory);
        string path = Path.Combine(LocalizationDirectory, DefaultLanguageFileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, DefaultEnglishLocalizationTemplate());
        }
    }

    private static string DefaultEnglishLocalizationTemplate()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "# HarnessPrefabs server-synced localization.",
            "#",
            "# Put language files in this folder using Valheim language names:",
            "# English.yml, Korean.yml, Turkish.yml, German.yml, etc.",
            "#",
            "# English.yml is the fallback file. Other languages apply English first,",
            "# then the selected language file.",
            "#",
            "# Use tokens in prefabs.yml displayName or description fields:",
            "# - prefab: barrell",
            "#   enabled: true",
            "#   displayName: $ph_piece_barrell",
            "#   description: $ph_piece_barrell_desc",
            "#",
            "# Example localization entries:",
            "# $ph_piece_barrell: \"Barrel\"",
            "# $ph_piece_barrell_desc: \"A decorative barrel.\"",
            ""
        });
    }

    private static bool IsLocalizationFile(string path)
    {
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        string localizationRoot = Path.GetFullPath(LocalizationDirectory);
        return fullPath.StartsWith(localizationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string token)
    {
        return (token ?? "").Trim().TrimStart('$').Trim();
    }

    private static string NormalizeLanguage(string? language)
    {
        string normalized = language?.Trim() ?? "";
        return normalized.Length == 0 ? "English" : normalized;
    }

    private sealed class LocalizationPayload
    {
        public Dictionary<string, string> All { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> Languages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

[HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
internal static class HarnessPrefabsLocalizationSetupLanguagePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Localization __instance, string language)
    {
        PrefabLocalizationOverrideManager.ApplyCurrentLocalization(__instance, language);
    }
}
