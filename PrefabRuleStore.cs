using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using ServerSync;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HarnessPrefabs;

internal static class PrefabRuleStore
{
    public const string OverrideFileName = "prefabs.yml";
    public const string ReferenceFileName = "prefabs.reference.yml";
    public const string FullScaffoldFileName = "prefabs.full.yml";

    private const string DomainName = "prefabs";
    private static readonly ISerializer SparseSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new PrefabRequirementYamlConverter())
        .WithTypeConverter(new ComponentListYamlConverter())
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .DisableAliases()
        .Build();

    private static readonly ISerializer FullSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new PrefabRequirementYamlConverter())
        .WithTypeConverter(new ComponentListYamlConverter())
        .DisableAliases()
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new PrefabRequirementYamlConverter())
        .WithTypeConverter(new ComponentListYamlConverter())
        .IgnoreUnmatchedProperties()
        .Build();

    private static CustomSyncedValue<string> _syncedRules = null!;
    private static Dictionary<string, PrefabRule> _activeRules = new(StringComparer.Ordinal);
    private static List<PrefabDiscovery> _lastDiscoveries = new();
    private static string? _lastSyncedRulesPayload;

    public static string RulesDirectory => Path.Combine(Paths.ConfigPath, HarnessPrefabsPlugin.ModName);
    public static string RulesPath => Path.Combine(RulesDirectory, OverrideFileName);
    public static string ReferencePath => Path.Combine(RulesDirectory, ReferenceFileName);
    public static string FullScaffoldPath => Path.Combine(RulesDirectory, FullScaffoldFileName);
    public static IReadOnlyList<PrefabDiscovery> LastDiscoveries => _lastDiscoveries;
    public static bool HasCachedDiscoveries => _lastDiscoveries.Count > 0;
    public static int ActiveRuleCount => _activeRules.Count;

    public static void Initialize(CustomSyncedValue<string> syncedRules)
    {
        _syncedRules = syncedRules;
    }

    public static bool IsOverrideFileEvent(FileSystemEventArgs e)
    {
        string fileName = Path.GetFileName(e.FullPath);
        return IsOverrideFileName(fileName);
    }

    public static bool PublishRulesFromDisk()
    {
        if (!HarnessPrefabsPlugin.IsSourceOfTruth)
        {
            return false;
        }

        if (!TryBuildActiveRulesFromDisk(_lastDiscoveries, out Dictionary<string, PrefabRule> rules))
        {
            return false;
        }

        CommitSourceRules(rules, _lastDiscoveries);
        return true;
    }

    public static bool LoadSyncedRulesForCurrentAuthority()
    {
        if (HarnessPrefabsPlugin.IsSourceOfTruth)
        {
            return false;
        }

        string syncedYaml = _syncedRules.Value ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(syncedYaml) &&
            string.Equals(_lastSyncedRulesPayload, syncedYaml, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryBuildSyncedRules(_lastDiscoveries, syncedYaml, out Dictionary<string, PrefabRule> rules))
        {
            return false;
        }

        _activeRules = rules;
        _lastSyncedRulesPayload = syncedYaml;
        return true;
    }

    public static void LoadRulesForCurrentAuthority(IEnumerable<PrefabDiscovery> discoveries)
    {
        _lastDiscoveries = discoveries
            .OrderBy(discovery => discovery.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (HarnessPrefabsPlugin.IsSourceOfTruth)
        {
            if (TryBuildActiveRulesFromDisk(_lastDiscoveries, out Dictionary<string, PrefabRule> rules))
            {
                CommitSourceRules(rules, _lastDiscoveries);
            }
            else if (_activeRules.Count == 0)
            {
                Dictionary<string, PrefabRule> defaults = BuildActiveRules(_lastDiscoveries, includeDiskOverrides: false);
                CommitSourceRules(defaults, _lastDiscoveries);
            }

            return;
        }

        string syncedYaml = _syncedRules.Value;
        if (!string.IsNullOrWhiteSpace(syncedYaml))
        {
            if (TryBuildSyncedRules(_lastDiscoveries, syncedYaml, out Dictionary<string, PrefabRule> syncedRules))
            {
                _activeRules = syncedRules;
                _lastSyncedRulesPayload = syncedYaml;
            }
            else if (_activeRules.Count == 0)
            {
                _activeRules = BuildActiveRules(_lastDiscoveries, includeDiskOverrides: false);
            }

            return;
        }

        _activeRules = BuildActiveRules(_lastDiscoveries, includeDiskOverrides: false);
    }

    public static bool TryGetRule(string prefabName, out PrefabRule rule)
    {
        return _activeRules.TryGetValue(prefabName, out rule);
    }

    public static bool TryWriteFullScaffoldConfigurationFile(out string path, out string error)
    {
        path = FullScaffoldPath;
        error = "";
        if (_activeRules.Count == 0)
        {
            error = $"{HarnessPrefabsPlugin.ModName}: no active prefab rules are available yet.";
            return false;
        }

        try
        {
            WriteIfChanged(path, GeneratedHeader("full scaffold") + SerializeFull(_activeRules));
            HarnessPrefabsPlugin.Log.LogInfo($"Wrote prefab full scaffold to {path}.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static Dictionary<string, PrefabRule> BuildActiveRules(IEnumerable<PrefabDiscovery> discoveries, bool includeDiskOverrides = true)
    {
        if (includeDiskOverrides)
        {
            EnsureOverrideFile();
        }

        Dictionary<string, PrefabRuleEntry> overrides = includeDiskOverrides
            ? LoadOverridesFromDisk()
            : new Dictionary<string, PrefabRuleEntry>(StringComparer.Ordinal);
        Dictionary<string, PrefabRule> rules = new(StringComparer.Ordinal);

        foreach (PrefabDiscovery discovery in discoveries)
        {
            PrefabRule rule = CreateDefaultRule(discovery);
            if (overrides.TryGetValue(discovery.Name, out PrefabRuleEntry entry))
            {
                ApplyOverride(rule, entry);
            }

            rules[discovery.Name] = rule;
        }

        return NormalizeRules(rules);
    }

    private static bool TryBuildActiveRulesFromDisk(IEnumerable<PrefabDiscovery> discoveries, out Dictionary<string, PrefabRule> rules)
    {
        try
        {
            rules = BuildActiveRules(discoveries);
            return true;
        }
        catch (Exception ex)
        {
            rules = null!;
            HarnessPrefabsPlugin.Log.LogError($"Failed to load prefab overrides. Keeping the last known good policy. Error: {ex.Message}");
            return false;
        }
    }

    private static bool TryBuildSyncedRules(IEnumerable<PrefabDiscovery> discoveries, string yaml, out Dictionary<string, PrefabRule> rules)
    {
        try
        {
            rules = !string.IsNullOrWhiteSpace(yaml)
                ? BuildRulesFromSyncedPayload(discoveries, yaml)
                : BuildActiveRules(discoveries, includeDiskOverrides: false);
            return true;
        }
        catch (Exception ex)
        {
            rules = null!;
            HarnessPrefabsPlugin.Log.LogError($"Failed to load synced prefab policy. Keeping the last known good policy. Error: {ex.Message}");
            return false;
        }
    }

    private static Dictionary<string, PrefabRule> BuildRulesFromSyncedPayload(IEnumerable<PrefabDiscovery> discoveries, string yaml)
    {
        Dictionary<string, PrefabRule> rules = BuildActiveRules(discoveries, includeDiskOverrides: false);
        foreach (PrefabRuleEntry entry in DeserializeEntries(yaml, "synced prefab payload"))
        {
            string prefabName = NormalizePrefabName(entry.Prefab);
            if (prefabName.Length == 0 || !rules.TryGetValue(prefabName, out PrefabRule rule))
            {
                continue;
            }

            entry.Prefab = prefabName;
            NormalizeEntry(entry);
            ApplyOverride(rule, entry);
        }

        return NormalizeRules(rules);
    }

    private static PrefabRule CreateDefaultRule(PrefabDiscovery discovery)
    {
        PrefabRule rule = MvbpPrefabDefaults.TryCreateRule(discovery, out PrefabRule seededRule)
            ? seededRule
            : new PrefabRule
            {
                Access = discovery.Access,
                Category = discovery.Category,
                ClipEverything = discovery.ClipEverything,
                ClipGround = discovery.ClipGround,
                AllowedInDungeons = discovery.AllowedInDungeons,
                CanBeRemoved = true,
                Components = new ComponentList(discovery.Components)
            };

        return NormalizeRule(rule);
    }

    private static void ApplyOverride(PrefabRule rule, PrefabRuleEntry entry)
    {
        if (entry.Category != null)
        {
            rule.Category = NormalizeCategory(entry.Category);
            if (rule.Access != PrefabAccess.Hidden && !entry.Enabled.HasValue)
            {
                rule.Access = ResolveAccess(enabled: true, rule.Category);
            }
        }

        if (entry.DisplayName != null)
        {
            rule.DisplayName = entry.DisplayName;
        }

        if (entry.Description != null)
        {
            rule.Description = entry.Description;
        }

        if (entry.CraftingStation != null)
        {
            rule.CraftingStation = entry.CraftingStation;
        }

        if (entry.Requirements != null)
        {
            rule.Requirements = PrefabRequirementParser.Clone(entry.Requirements);
        }

        if (entry.Flags != null)
        {
            ApplyFlags(rule, entry.Flags);
        }

        if (entry.Enabled.HasValue)
        {
            rule.Access = ResolveAccess(entry.Enabled.Value, rule.Category);
        }
    }

    private static Dictionary<string, PrefabRuleEntry> LoadOverridesFromDisk()
    {
        Dictionary<string, PrefabRuleEntry> merged = new(StringComparer.Ordinal);
        foreach (string file in EnumerateOverrideFiles())
        {
            foreach (PrefabRuleEntry entry in DeserializeEntries(File.ReadAllText(file), file))
            {
                string prefabName = NormalizePrefabName(entry.Prefab);
                if (prefabName.Length == 0)
                {
                    continue;
                }

                entry.Prefab = prefabName;
                NormalizeEntry(entry);
                if (!merged.TryGetValue(prefabName, out PrefabRuleEntry existing))
                {
                    merged[prefabName] = entry;
                    continue;
                }

                OverlayEntry(existing, entry);
            }
        }

        return merged;
    }

    private static void OverlayEntry(PrefabRuleEntry target, PrefabRuleEntry source)
    {
        target.Enabled = source.Enabled ?? target.Enabled;
        target.Category = source.Category ?? target.Category;
        target.DisplayName = source.DisplayName ?? target.DisplayName;
        target.Description = source.Description ?? target.Description;
        target.CraftingStation = source.CraftingStation ?? target.CraftingStation;
        target.Requirements = source.Requirements ?? target.Requirements;
        target.Flags = source.Flags ?? target.Flags;
    }

    private static IEnumerable<string> EnumerateOverrideFiles()
    {
        if (!Directory.Exists(RulesDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(RulesDirectory, "*.yml")
            .Concat(Directory.GetFiles(RulesDirectory, "*.yaml"))
            .Where(path => IsOverrideFileName(Path.GetFileName(path)))
            .OrderBy(path => IsBaseOverrideFile(path) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsOverrideFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string extension = Path.GetExtension(fileName);
        if (!extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.Equals(ReferenceFileName, StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(FullScaffoldFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string baseName = Path.GetFileNameWithoutExtension(fileName);
        return baseName.Equals(DomainName, StringComparison.OrdinalIgnoreCase) ||
               baseName.StartsWith(DomainName + "_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBaseOverrideFile(string path)
    {
        return Path.GetFileName(path).Equals(OverrideFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureOverrideFile()
    {
        Directory.CreateDirectory(RulesDirectory);
        if (File.Exists(RulesPath))
        {
            return;
        }

        WriteIfChanged(RulesPath, DefaultOverrideTemplate());
    }

    private static string DefaultOverrideTemplate()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "# HarnessPrefabs prefab overrides.",
            "# Top-level list schema. Copy entries from prefabs.reference.yml.",
            "# Later entries and later prefabs_*.yml files override earlier values for the same prefab.",
            "#",
            "# Full override example:",
            "# - prefab: barrell # Internal prefab name.",
            "#   enabled: true # true exposes it; false hides/overrides it off.",
            "#   category: Furniture # Public category, or one of the three admin-only Harness groups.",
            "#   displayName: Barrel # Optional in-game piece name. Empty uses prefab name.",
            "#   description: \"\" # Optional in-game tooltip/body text.",
            "#   craftingStation: Workbench # None, Workbench, Forge, Stonecutter, BlackForge, etc.",
            "#   requirements: # Optional build costs. Omit or [] for free placement.",
            "#     - FineWood: 2 # ItemPrefabName: amount.",
            "#     - Iron: 1",
            "#   flags: true, true, false, true # clipEverything, clipGround, allowedInDungeons, canBeRemoved.",
            "#   components: [Piece, WearNTear] # Reference/sort hint from full scaffold; usually leave copied.",
            "#",
            "# Run `harnessprefabs:full` in-game to generate prefabs.full.yml with displayName,",
            "# description, flags, and components for deeper review.",
            ""
        });
    }

    private static string GeneratedHeader(string label)
    {
        StringBuilder builder = new();
        builder.AppendLine($"# Generated by {HarnessPrefabsPlugin.ModName}: {DomainName} {label}.");
        builder.AppendLine($"# Do not edit this generated file directly. Copy entries into {OverrideFileName} or {DomainName}_*.yml.");
        builder.AppendLine(label.Equals("full scaffold", StringComparison.OrdinalIgnoreCase)
            ? "# Schema: top-level list. flags = clipEverything, clipGround, allowedInDungeons, canBeRemoved."
            : "# Schema: top-level list. Reference omits full-only fields: displayName, description, flags, components.");
        return builder.ToString();
    }

    private static void CommitSourceRules(Dictionary<string, PrefabRule> rules, IReadOnlyCollection<PrefabDiscovery> discoveries)
    {
        string yaml = SerializePolicy(rules, discoveries);
        if (!string.Equals(_syncedRules.Value, yaml, StringComparison.Ordinal))
        {
            _syncedRules.Value = yaml;
        }

        _activeRules = rules;
        try
        {
            WriteReferenceArtifact(discoveries, rules);
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogWarning($"Failed to update prefab reference artifact: {ex.Message}");
        }
    }

    private static void WriteReferenceArtifact(IReadOnlyCollection<PrefabDiscovery> discoveries, IReadOnlyDictionary<string, PrefabRule> rules)
    {
        if (!HarnessPrefabsPlugin.IsSourceOfTruth || discoveries.Count == 0 || rules.Count == 0)
        {
            return;
        }

        string content = BuildReferenceContent(discoveries, rules);
        WriteIfChanged(ReferencePath, content);
    }

    private static string BuildReferenceContent(IEnumerable<PrefabDiscovery> discoveries, IReadOnlyDictionary<string, PrefabRule> rules)
    {
        StringBuilder builder = new();
        builder.Append(GeneratedHeader("reference"));
        List<PrefabDiscovery> includedDiscoveries = discoveries
            .Where(discovery => rules.ContainsKey(discovery.Name))
            .ToList();
        IReadOnlyDictionary<string, string> owners = PrefabOwnerCatalog.GetOwnerNames(
            includedDiscoveries.Select(discovery => discovery.Name));

        bool wroteAny = false;
        foreach (IGrouping<string, PrefabDiscovery> section in includedDiscoveries
                     .GroupBy(
                         discovery => owners.TryGetValue(discovery.Name, out string ownerName)
                             ? ownerName
                             : PrefabOwnerCatalog.UnknownOwnerName,
                         StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => GetOwnerSortBucket(group.Key))
                     .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("# ===== ");
            builder.Append(string.IsNullOrWhiteSpace(section.Key) ? PrefabOwnerCatalog.UnknownOwnerName : section.Key.Trim());
            builder.AppendLine(" =====");

            foreach (PrefabDiscovery discovery in section.OrderBy(discovery => discovery.Name, StringComparer.OrdinalIgnoreCase))
            {
                string yaml = SparseSerializer.Serialize(new[] { ToReferenceEntry(discovery.Name, rules[discovery.Name]) })
                    .TrimEnd('\r', '\n');
                builder.AppendLine(yaml);
                wroteAny = true;
            }
        }

        return wroteAny ? builder.ToString() : GeneratedHeader("reference");
    }

    private static int GetOwnerSortBucket(string ownerName)
    {
        if (string.Equals(ownerName, PrefabOwnerCatalog.VanillaOwnerName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return string.Equals(ownerName, PrefabOwnerCatalog.UnknownOwnerName, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    private static PrefabRuleEntry ToReferenceEntry(string prefabName, PrefabRule rule)
    {
        PrefabRuleEntry entry = new()
        {
            Prefab = prefabName,
            Enabled = rule.Access != PrefabAccess.Hidden,
            Category = rule.Category,
            CraftingStation = rule.CraftingStation,
            Requirements = PrefabRequirementParser.Clone(rule.Requirements)
        };

        if (IsNone(entry.CraftingStation))
        {
            entry.CraftingStation = null;
        }

        if (entry.Requirements is { Count: 0 })
        {
            entry.Requirements = null;
        }

        return entry;
    }

    private static PrefabRuleEntry ToSyncedOverrideEntry(string prefabName, PrefabRule rule, PrefabRule defaultRule)
    {
        PrefabRuleEntry entry = new()
        {
            Prefab = prefabName
        };

        if (rule.Access != defaultRule.Access)
        {
            entry.Enabled = rule.Access != PrefabAccess.Hidden;
        }

        if (!string.Equals(rule.Category, defaultRule.Category, StringComparison.Ordinal))
        {
            entry.Category = rule.Category;
        }

        if (!string.Equals(rule.DisplayName, defaultRule.DisplayName, StringComparison.Ordinal))
        {
            entry.DisplayName = rule.DisplayName;
        }

        if (!string.Equals(rule.Description, defaultRule.Description, StringComparison.Ordinal))
        {
            entry.Description = rule.Description;
        }

        if (!string.Equals(rule.CraftingStation, defaultRule.CraftingStation, StringComparison.Ordinal))
        {
            entry.CraftingStation = rule.CraftingStation;
        }

        if (!RequirementsEqual(rule.Requirements, defaultRule.Requirements))
        {
            entry.Requirements = PrefabRequirementParser.Clone(rule.Requirements);
        }

        if (!FlagsEqual(rule, defaultRule))
        {
            entry.Flags = FormatFlags(rule);
        }

        return entry;
    }

    private static PrefabRuleEntry ToFullEntry(string prefabName, PrefabRule rule)
    {
        return new PrefabRuleEntry
        {
            Prefab = prefabName,
            Enabled = rule.Access != PrefabAccess.Hidden,
            Category = rule.Category,
            DisplayName = rule.DisplayName,
            Description = rule.Description,
            CraftingStation = rule.CraftingStation,
            Requirements = PrefabRequirementParser.Clone(rule.Requirements),
            Flags = FormatFlags(rule),
            Components = new ComponentList(rule.Components)
        };
    }

    private static List<PrefabRuleEntry> ToFullEntries(IReadOnlyDictionary<string, PrefabRule> rules)
    {
        return rules
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => ToFullEntry(pair.Key, pair.Value))
            .ToList();
    }

    private static List<PrefabRuleEntry> ToSyncedPolicyEntries(IReadOnlyDictionary<string, PrefabRule> rules, IReadOnlyCollection<PrefabDiscovery> discoveries)
    {
        Dictionary<string, PrefabRule> defaults = discoveries
            .Where(discovery => !string.IsNullOrWhiteSpace(discovery.Name))
            .GroupBy(discovery => NormalizePrefabName(discovery.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => CreateDefaultRule(group.First()), StringComparer.Ordinal);

        if (rules.Count != defaults.Count || rules.Keys.Any(prefabName => !defaults.ContainsKey(prefabName)))
        {
            throw new InvalidDataException("Active prefab rules must have exactly one matching discovery default before synchronization.");
        }

        return rules
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => ToSyncedOverrideEntry(pair.Key, pair.Value, defaults[pair.Key]))
            .Where(HasSyncedOverrideFields)
            .ToList();
    }

    private static bool HasSyncedOverrideFields(PrefabRuleEntry entry)
    {
        return entry.Enabled.HasValue ||
               entry.Category != null ||
               entry.DisplayName != null ||
               entry.Description != null ||
               entry.CraftingStation != null ||
               entry.Requirements != null ||
               entry.Flags != null;
    }

    private static bool FlagsEqual(PrefabRule left, PrefabRule right)
    {
        return left.ClipEverything == right.ClipEverything &&
               left.ClipGround == right.ClipGround &&
               left.AllowedInDungeons == right.AllowedInDungeons &&
               left.CanBeRemoved == right.CanBeRemoved;
    }

    private static bool RequirementsEqual(IReadOnlyList<PrefabRequirement>? left, IReadOnlyList<PrefabRequirement>? right)
    {
        left ??= Array.Empty<PrefabRequirement>();
        right ??= Array.Empty<PrefabRequirement>();
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Item, right[i].Item, StringComparison.Ordinal) ||
                left[i].Amount != right[i].Amount)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, PrefabRule> NormalizeRules(IEnumerable<KeyValuePair<string, PrefabRule>>? rules)
    {
        return (rules ?? Enumerable.Empty<KeyValuePair<string, PrefabRule>>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => NormalizePrefabName(pair.Key), pair => NormalizeRule(pair.Value), StringComparer.Ordinal);
    }

    private static PrefabRule NormalizeRule(PrefabRule rule)
    {
        rule.Category = NormalizeCategory(rule.Category);

        if (rule.Access != PrefabAccess.Hidden)
        {
            rule.Access = ResolveAccess(enabled: true, rule.Category);
        }

        rule.DisplayName ??= "";
        rule.Description ??= "";
        rule.CraftingStation = string.IsNullOrWhiteSpace(rule.CraftingStation) ? "None" : rule.CraftingStation.Trim();
        rule.Requirements = PrefabRequirementParser.Clone(rule.Requirements);
        rule.Components = NormalizeComponents(rule.Components);
        return rule;
    }

    private static ComponentList NormalizeComponents(IEnumerable<string>? components)
    {
        return new ComponentList((components ?? Enumerable.Empty<string>())
            .Where(component => !string.IsNullOrWhiteSpace(component))
            .Select(component => component.Trim())
            .Where(IsComponentName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(component => component, StringComparer.OrdinalIgnoreCase));
    }

    private static void NormalizeEntry(PrefabRuleEntry entry)
    {
        entry.Prefab = NormalizePrefabName(entry.Prefab ?? "");
        entry.Category = entry.Category == null ? null : NormalizeCategory(entry.Category);
        entry.DisplayName = entry.DisplayName?.Trim();
        entry.Description = entry.Description?.Trim();
        entry.CraftingStation = entry.CraftingStation == null
            ? null
            : string.IsNullOrWhiteSpace(entry.CraftingStation)
                ? "None"
                : entry.CraftingStation!.Trim();
        entry.Requirements = entry.Requirements == null ? null : PrefabRequirementParser.Clone(entry.Requirements);
        entry.Flags = string.IsNullOrWhiteSpace(entry.Flags) ? null : entry.Flags!.Trim();
        entry.Components = entry.Components == null ? null : NormalizeComponents(entry.Components);
    }

    private static bool IsComponentName(string value)
    {
        return value.Length > 0 &&
               !value.StartsWith("MVBP", StringComparison.Ordinal) &&
               value.IndexOf(':') < 0;
    }

    private static string NormalizeCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return BuildCategories.HarnessProps;
        }

        return BuildCategories.NormalizeCategory(category);
    }

    private static PrefabAccess ResolveAccess(bool enabled, string category)
    {
        if (!enabled)
        {
            return PrefabAccess.Hidden;
        }

        return BuildCategories.IsAdminCategory(category) ? PrefabAccess.Admin : PrefabAccess.Public;
    }

    private static void ApplyFlags(PrefabRule rule, string? flags)
    {
        if (string.IsNullOrWhiteSpace(flags))
        {
            return;
        }

        string[] parts = flags!.Split(',')
            .Select(part => part.Trim())
            .ToArray();
        if (parts.Length != 4 || parts.Any(part => !bool.TryParse(part, out _)))
        {
            throw new InvalidDataException(
                "Prefab flags must contain exactly four true/false values: clipEverything, clipGround, allowedInDungeons, canBeRemoved.");
        }

        rule.ClipEverything = bool.Parse(parts[0]);
        rule.ClipGround = bool.Parse(parts[1]);
        rule.AllowedInDungeons = bool.Parse(parts[2]);
        rule.CanBeRemoved = bool.Parse(parts[3]);
    }

    private static string FormatFlags(PrefabRule rule)
    {
        return string.Join(", ", new[]
        {
            FormatBool(rule.ClipEverything),
            FormatBool(rule.ClipGround),
            FormatBool(rule.AllowedInDungeons),
            FormatBool(rule.CanBeRemoved)
        });
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static List<PrefabRuleEntry> DeserializeEntries(string yaml, string source)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new List<PrefabRuleEntry>();
        }

        try
        {
            return Deserializer.Deserialize<List<PrefabRuleEntry>>(yaml) ?? new List<PrefabRuleEntry>();
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException($"Invalid HarnessPrefabs prefab YAML in {source}: {ex.Message}", ex);
        }
    }

    private static string SerializeFull(IReadOnlyDictionary<string, PrefabRule> rules)
    {
        return FullSerializer.Serialize(ToFullEntries(rules));
    }

    private static string SerializePolicy(IReadOnlyDictionary<string, PrefabRule> rules, IReadOnlyCollection<PrefabDiscovery> discoveries)
    {
        return SparseSerializer.Serialize(ToSyncedPolicyEntries(rules, discoveries));
    }

    private static string NormalizePrefabName(string name)
    {
        return HarnessPrefabsRuntime.NormalizePrefabName(name);
    }

    private static bool IsNone(string? value)
    {
        string trimmed = value?.Trim() ?? "";
        return trimmed.Length == 0 ||
               trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("null", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteIfChanged(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(path, content);
    }

    private sealed class PrefabRequirementYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
        {
            return type == typeof(PrefabRequirement);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            if (!parser.TryConsume<MappingStart>(out _))
            {
                Scalar scalar = parser.Consume<Scalar>();
                throw new YamlException(scalar.Start, scalar.End, "Prefab requirements must use shorthand, for example '- FineWood: 2'.");
            }

            List<KeyValuePair<string, string>> pairs = new();
            while (!parser.Accept<MappingEnd>(out _))
            {
                Scalar key = parser.Consume<Scalar>();
                if (parser.Accept<MappingStart>(out _) || parser.Accept<SequenceStart>(out _))
                {
                    throw new YamlException(key.Start, key.End, $"Unsupported nested requirement shorthand for '{key.Value}'.");
                }

                Scalar value = parser.Consume<Scalar>();
                pairs.Add(new KeyValuePair<string, string>(key.Value, value.Value));
            }

            parser.Consume<MappingEnd>();
            if (pairs.Count != 1)
            {
                throw new YamlException("Prefab requirements must use shorthand, for example '- FineWood: 2'.");
            }

            return ParseRequirement(pairs[0].Key, pairs[0].Value);
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            PrefabRequirement requirement = (PrefabRequirement)value!;
            string item = requirement?.Item?.Trim() ?? "";
            if (item.Length == 0 || requirement!.Amount < 1)
            {
                throw new YamlException("Prefab requirements must have a non-empty item and a positive integer amount.");
            }

            emitter.Emit(new MappingStart());
            emitter.Emit(new Scalar(item));
            emitter.Emit(new Scalar(requirement.Amount.ToString(CultureInfo.InvariantCulture)));
            emitter.Emit(new MappingEnd());
        }

        private static PrefabRequirement ParseRequirement(string item, string value)
        {
            string itemName = item.Trim();
            if (itemName.Length == 0)
            {
                throw new YamlException("Prefab requirement item names cannot be empty.");
            }

            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount < 1)
            {
                throw new YamlException($"Prefab requirement '{itemName}' must have a positive integer amount.");
            }

            return new PrefabRequirement
            {
                Item = itemName,
                Amount = amount
            };
        }
    }

    private sealed class ComponentListYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
        {
            return type == typeof(ComponentList);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            if (!parser.TryConsume<SequenceStart>(out _))
            {
                Scalar scalar = parser.Consume<Scalar>();
                throw new YamlException(
                    scalar.Start,
                    scalar.End,
                    "Prefab components must use a YAML sequence, for example 'components: [Piece, WearNTear]'.");
            }

            ComponentList components = new();
            while (!parser.Accept<SequenceEnd>(out _))
            {
                Scalar scalar = parser.Consume<Scalar>();
                if (!string.IsNullOrWhiteSpace(scalar.Value))
                {
                    components.Add(scalar.Value.Trim());
                }
            }

            parser.Consume<SequenceEnd>();
            return components;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            ComponentList components = (ComponentList)value!;
            emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, isImplicit: true, SequenceStyle.Flow));
            foreach (string component in components)
            {
                emitter.Emit(new Scalar(component));
            }

            emitter.Emit(new SequenceEnd());
        }
    }
}
