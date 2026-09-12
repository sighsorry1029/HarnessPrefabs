using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using ReloadTimer = System.Timers.Timer;

namespace HarnessPrefabs;

[BepInPlugin(ModGuid, ModName, ModVersion)]
[BepInIncompatibility(MoreVanillaBuildPrefabsGuid)]
public sealed class HarnessPrefabsPlugin : BaseUnityPlugin
{
    internal const string ModName = "HarnessPrefabs";
    internal const string ModVersion = "1.1.1";
    internal const string Author = "sighsorry";
    internal const string ModGuid = "sighsorry.valheim.harnessprefabs";
    internal const string MoreVanillaBuildPrefabsGuid = "Searica.Valheim.MoreVanillaBuildPrefabs";
    private const double ReloadDebounceMilliseconds = 500d;

    private static readonly string ConfigFileName = $"{ModGuid}.cfg";
    private static readonly string ConfigFileFullPath = Path.Combine(Paths.ConfigPath, ConfigFileName);
    private static readonly ConfigSync SyncedConfig = new(ModGuid)
    {
        DisplayName = ModName,
        CurrentVersion = ModVersion,
        MinimumRequiredVersion = ModVersion
    };

    private static readonly CustomSyncedValue<string> SyncedRules = new(SyncedConfig, "prefabs-yaml", string.Empty, 100);
    private readonly Harmony _harmony = new(ModGuid);
    private readonly object _reloadLock = new();
    private FileSystemWatcher? _configWatcher;
    private FileSystemWatcher? _rulesWatcher;
    private ReloadTimer? _configReloadTimer;
    private ReloadTimer? _rulesReloadTimer;
    private string? _lastConfigFileText;
    private bool _reloadingConfig;
    private static bool _sourceOfTruthFileModeReady;

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static bool IsAdmin => SyncedConfig.IsAdmin;
    internal static bool IsSourceOfTruth => SyncedConfig.IsSourceOfTruth;
    internal static bool IsDebugMode => Player.m_debugMode;
    internal static bool HarnessHammerTabsEnabled => IsAdmin && IsDebugMode && (ShowHarnessPrefabTabs == null || ShowHarnessPrefabTabs.Value == Toggle.On);
    internal static bool UnsafeBedPatchesEnabled => EnableUnsafeBedPatches.Value == Toggle.On;
    internal static bool ArmorStandEquipmentSwapEnabled => EnableArmorStandEquipmentSwap.Value == Toggle.On;
    internal static int FermenterPatchDurationPercent => Math.Min(100, Math.Max(0, UnsafeFermenterPatchDurationPercent.Value));

    internal enum Toggle
    {
        Off = 0,
        On = 1
    }

    private static ConfigEntry<Toggle> LockConfiguration = null!;
    private static ConfigEntry<Toggle> ShowHarnessPrefabTabs = null!;
    private static ConfigEntry<Toggle> EnableUnsafeBedPatches = null!;
    private static ConfigEntry<Toggle> EnableArmorStandEquipmentSwap = null!;
    private static ConfigEntry<int> UnsafeFermenterPatchDurationPercent = null!;

    private void Awake()
    {
        Log = Logger;
        Instance = this;
        PrefabAssetResolver.Prepare();

        bool saveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            LockConfiguration = BindSynced("1 - General", "Lock Configuration", Toggle.On, "If on, prefab policy is controlled by the server and can only be changed by admins.");
            ShowHarnessPrefabTabs = BindSynced("1 - General", "Show Harness Tabs", Toggle.On, "If on, the HarnessPrefabs Hammer section is visible to admin clients while Valheim debugmode is enabled. If off, the section stays hidden even in debugmode.", synchronizedSetting: false);
            ShowHarnessPrefabTabs.SettingChanged += OnHarnessHammerVisibilityChanged;
            EnableUnsafeBedPatches = BindSynced("2 - Prefab Tweaks", "Enable Bed Patches", Toggle.On, "If on, player-built MVBP bed prefabs get Bed components and spawn points. Unsafe: disabling the mod later can affect spawn points.");
            EnableArmorStandEquipmentSwap = BindSynced("2 - Prefab Tweaks", "Enable Armor Stand Equipment Swap", Toggle.On, "If on, Left/Right Alt+Use swaps the equipped armor and drawn or sheathed hand set with player-built ArmorStand, ArmorStand_Female, or ArmorStand_Male. The base stand uses its back slots; female and male stands always use their hand slots and leave their back slots unchanged. The player's drawn or sheathed hand state is preserved. Displayable Utility items are swapped when safe; incompatible Utility items stay unchanged. Turn this off when another mod handles ArmorStand interaction.");
            UnsafeFermenterPatchDurationPercent = BindSynced(
                "2 - Prefab Tweaks",
                "Fermenter Patch Duration Percent",
                70,
                new ConfigDescription(
                    "0 disables the player-built dvergrprops_barrel fermenter patch. 1-100 enables the patch and sets fermentation time as a percentage of the vanilla fermenter duration. Default: 70. Unsafe: disabling the mod later can affect fermenting contents.",
                    new AcceptableValueRange<int>(0, 100)));
            _ = SyncedConfig.AddLockingConfigEntry(LockConfiguration);

            PrefabLocalizationOverrideManager.Initialize(SyncedConfig);
            PrefabRuleStore.Initialize(SyncedRules);
            HarnessPrefabsConsoleCommands.Register();
            SyncedRules.ValueChanged += OnSyncedRulesChanged;
            SyncedConfig.SourceOfTruthChanged += OnSourceOfTruthChanged;

            _harmony.PatchAll(typeof(HarnessPrefabsPlugin).Assembly);
            SetupConfigWatcher();

            SaveConfig(reload: false);
            _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
        }
        finally
        {
            Config.SaveOnConfigSet = saveOnSet;
        }

        Log.LogInfo($"{ModName} {ModVersion} loaded.");
    }

    private void OnDestroy()
    {
        try
        {
            SaveConfig(reload: false);
        }
        catch (Exception ex)
        {
            Log.LogError($"Error saving configuration during shutdown: {ex.Message}");
        }

        _configWatcher?.Dispose();
        _rulesWatcher?.Dispose();
        _configReloadTimer?.Dispose();
        _rulesReloadTimer?.Dispose();
        if (ShowHarnessPrefabTabs != null)
        {
            ShowHarnessPrefabTabs.SettingChanged -= OnHarnessHammerVisibilityChanged;
        }
        PrefabLocalizationOverrideManager.Dispose();
        PrefabCategoryRegistry.Dispose();
        PrefabBuildManager.EndPrefabEpoch(ZNetScene.instance);
        SyncedRules.ValueChanged -= OnSyncedRulesChanged;
        SyncedConfig.SourceOfTruthChanged -= OnSourceOfTruthChanged;
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
        _harmony.UnpatchSelf();
    }

    private void OnHarnessHammerVisibilityChanged(object sender, EventArgs e)
    {
        if (!_reloadingConfig)
        {
            PrefabBuildManager.RefreshIfHarnessHammerVisibilityChanged();
        }
    }

    private static void OnSyncedRulesChanged()
    {
        if (IsSourceOfTruth)
        {
            return;
        }

        if (PrefabRuleStore.LoadSyncedRulesForCurrentAuthority())
        {
            PrefabBuildManager.RefreshFromCachedRules("synced prefab rules changed");
        }
    }

    private static void OnSourceOfTruthChanged(bool isSourceOfTruth)
    {
        if (isSourceOfTruth)
        {
            EnsureSourceOfTruthFileMode();
        }
        else
        {
            _sourceOfTruthFileModeReady = false;
            Instance?.SetupRuleWatcher();
            PrefabLocalizationOverrideManager.SetupFileWatcher();
        }

        PrefabBuildManager.Refresh("config authority changed");
    }

    internal static void EnsureSourceOfTruthFileMode()
    {
        if (!IsSourceOfTruth || _sourceOfTruthFileModeReady)
        {
            return;
        }

        Instance?.SetupRuleWatcher();
        PrefabLocalizationOverrideManager.SetupFileWatcher();
        if (PrefabLocalizationOverrideManager.ReloadFromDiskAndSync())
        {
            _sourceOfTruthFileModeReady = true;
        }
    }

    private static HarnessPrefabsPlugin? Instance { get; set; }

    private void SetupConfigWatcher()
    {
        _configReloadTimer ??= CreateReloadTimer(ReadConfigValues);
        _configWatcher = new FileSystemWatcher(Paths.ConfigPath, ConfigFileName)
        {
            IncludeSubdirectories = false,
            SynchronizingObject = ThreadingHelper.SynchronizingObject,
            EnableRaisingEvents = true
        };
        _configWatcher.Changed += ScheduleConfigReload;
        _configWatcher.Created += ScheduleConfigReload;
        _configWatcher.Renamed += ScheduleConfigReload;
    }

    private void SetupRuleWatcher()
    {
        if (!IsSourceOfTruth)
        {
            _rulesReloadTimer?.Stop();
            _rulesWatcher?.Dispose();
            _rulesWatcher = null;
            return;
        }

        _rulesWatcher?.Dispose();
        _rulesReloadTimer ??= CreateReloadTimer(ReadRuleValues);
        string rulesDirectory = PrefabRuleStore.RulesDirectory;
        Directory.CreateDirectory(rulesDirectory);
        _rulesWatcher = new FileSystemWatcher(rulesDirectory, "*.*")
        {
            IncludeSubdirectories = false,
            SynchronizingObject = ThreadingHelper.SynchronizingObject,
            EnableRaisingEvents = true
        };
        _rulesWatcher.Changed += ScheduleRuleReload;
        _rulesWatcher.Created += ScheduleRuleReload;
        _rulesWatcher.Deleted += ScheduleRuleReload;
        _rulesWatcher.Renamed += ScheduleRuleReload;
    }

    private void ScheduleConfigReload(object sender, FileSystemEventArgs e)
    {
        RestartTimer(_configReloadTimer);
    }

    private void ScheduleRuleReload(object sender, FileSystemEventArgs e)
    {
        if (!IsSourceOfTruth || !PrefabRuleStore.IsOverrideFileEvent(e))
        {
            return;
        }

        RestartTimer(_rulesReloadTimer);
    }

    private void ReadConfigValues(object sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_reloadLock)
        {
            if (!File.Exists(ConfigFileFullPath))
            {
                Log.LogWarning("Config file does not exist. Skipping reload.");
                return;
            }

            try
            {
                string configFileText = File.ReadAllText(ConfigFileFullPath);
                if (string.Equals(_lastConfigFileText, configFileText, StringComparison.Ordinal))
                {
                    return;
                }

                _reloadingConfig = true;
                try
                {
                    SaveConfig(reload: true);
                    _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
                }
                finally
                {
                    // Reload can change entries before it fails; apply the resulting values once.
                    _reloadingConfig = false;
                    PrefabBuildManager.Refresh("config file reload");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error reloading configuration: {ex.Message}");
            }
        }
    }

    private void ReadRuleValues(object sender, System.Timers.ElapsedEventArgs e)
    {
        if (!IsSourceOfTruth)
        {
            return;
        }

        lock (_reloadLock)
        {
            try
            {
                if (!PrefabRuleStore.HasCachedDiscoveries)
                {
                    PrefabBuildManager.Refresh("prefab rules file reloaded");
                    return;
                }

                if (PrefabRuleStore.PublishRulesFromDisk())
                {
                    PrefabBuildManager.RefreshFromCachedRules("prefab rules file reloaded");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error reloading prefab rules: {ex.Message}");
            }
        }
    }

    private static ReloadTimer CreateReloadTimer(System.Timers.ElapsedEventHandler handler)
    {
        ReloadTimer timer = new(ReloadDebounceMilliseconds)
        {
            AutoReset = false,
            SynchronizingObject = ThreadingHelper.SynchronizingObject
        };
        timer.Elapsed += handler;
        return timer;
    }

    private static void RestartTimer(ReloadTimer? timer)
    {
        if (timer == null)
        {
            return;
        }

        timer.Stop();
        timer.Start();
    }

    private static string? ReadFileTextIfExists(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private void SaveConfig(bool reload)
    {
        bool originalSaveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            if (reload)
            {
                Config.Reload();
            }

            Config.Save();
        }
        finally
        {
            Config.SaveOnConfigSet = originalSaveOnSet;
        }
    }

    private ConfigEntry<T> BindSynced<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
    {
        return BindSynced(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    private ConfigEntry<T> BindSynced<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        object[] tags = description.Tags ?? Array.Empty<object>();
        ConfigDescription extendedDescription = new(
            description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
            description.AcceptableValues,
            tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
        SyncedConfigEntry<T> syncedConfigEntry = SyncedConfig.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
        return configEntry;
    }

}
