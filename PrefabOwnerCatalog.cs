using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using UnityEngine;

namespace HarnessPrefabs;

internal static class PrefabOwnerCatalog
{
    public const string VanillaOwnerName = "Valheim";
    public const string UnknownOwnerName = "Unknown / Untracked";

    private enum CatalogState
    {
        Uninitialized,
        Loaded,
        Unavailable
    }

    private sealed class PluginResourceSnapshot
    {
        public string OwnerName { get; set; } = "";
        public string PluginName { get; set; } = "";
        public string PluginGuid { get; set; } = "";
        public string AssemblyName { get; set; } = "";
        public string[] ResourceNames { get; set; } = Array.Empty<string>();
    }

    private static readonly object VanillaSync = new();
    private static readonly object ModSync = new();
    private static readonly HashSet<string> VanillaPrefabNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> ModPrefabOwners = new(StringComparer.OrdinalIgnoreCase);
    private static CatalogState _vanillaState;
    private static string _loadedModSignature = "";

    public static IReadOnlyDictionary<string, string> GetOwnerNames(IEnumerable<string> prefabNames)
    {
        EnsureVanillaCatalogLoaded();
        EnsureModMappingsLoaded();

        Dictionary<string, string> owners = new(StringComparer.OrdinalIgnoreCase);
        foreach (string prefabName in prefabNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_vanillaState == CatalogState.Loaded && VanillaPrefabNames.Contains(prefabName))
            {
                owners[prefabName] = VanillaOwnerName;
                continue;
            }

            owners[prefabName] = ResolveLoadedModOwner(prefabName);
        }

        return owners;
    }

    private static string ResolveLoadedModOwner(string prefabName)
    {
        foreach (string candidate in EnumerateLookupCandidates(prefabName))
        {
            if (ModPrefabOwners.TryGetValue(candidate, out string ownerName) &&
                !string.IsNullOrWhiteSpace(ownerName))
            {
                return ownerName;
            }
        }

        return UnknownOwnerName;
    }

    private static void EnsureVanillaCatalogLoaded()
    {
        if (_vanillaState != CatalogState.Uninitialized)
        {
            return;
        }

        lock (VanillaSync)
        {
            if (_vanillaState != CatalogState.Uninitialized)
            {
                return;
            }

            string manifestPath = Path.Combine(Application.dataPath, "StreamingAssets", "SoftRef", "manifest_extended");
            if (!File.Exists(manifestPath))
            {
                _vanillaState = CatalogState.Unavailable;
                HarnessPrefabsPlugin.Log.LogWarning($"Vanilla asset manifest was not found at '{manifestPath}'. Reference sections may place vanilla entries under '{UnknownOwnerName}'.");
                return;
            }

            const string marker = "path in bundle:";
            foreach (string rawLine in File.ReadLines(manifestPath))
            {
                int markerIndex = rawLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                {
                    continue;
                }

                string assetPath = rawLine.Substring(markerIndex + marker.Length).Trim();
                if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string assetName = Path.GetFileNameWithoutExtension(assetPath);
                if (!string.IsNullOrWhiteSpace(assetName))
                {
                    VanillaPrefabNames.Add(assetName);
                }
            }

            _vanillaState = CatalogState.Loaded;
        }
    }

    private static void EnsureModMappingsLoaded()
    {
        List<PluginResourceSnapshot> plugins = GetPluginResources();
        string signature = BuildSignature(plugins);
        if (string.Equals(signature, _loadedModSignature, StringComparison.Ordinal))
        {
            return;
        }

        lock (ModSync)
        {
            if (string.Equals(signature, _loadedModSignature, StringComparison.Ordinal))
            {
                return;
            }

            ModPrefabOwners.Clear();
            foreach (AssetBundle assetBundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                string bundleName = assetBundle.name ?? "";
                if (bundleName.Length == 0)
                {
                    continue;
                }

                string ownerName = ResolveOwnerName(bundleName, plugins);
                if (string.IsNullOrWhiteSpace(ownerName))
                {
                    continue;
                }

                foreach (string assetPath in assetBundle.GetAllAssetNames())
                {
                    if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string assetName = Path.GetFileNameWithoutExtension(assetPath);
                    if (!string.IsNullOrWhiteSpace(assetName))
                    {
                        ModPrefabOwners[assetName] = ownerName;
                    }
                }
            }

            _loadedModSignature = signature;
        }
    }

    private static IEnumerable<string> EnumerateLookupCandidates(string prefabName)
    {
        string normalizedName = (prefabName ?? "").Replace("(Clone)", "").Trim();
        if (normalizedName.Length == 0)
        {
            yield break;
        }

        yield return normalizedName;
        int aliasSeparatorIndex = normalizedName.IndexOf(':');
        if (aliasSeparatorIndex > 0)
        {
            yield return normalizedName.Substring(0, aliasSeparatorIndex);
        }
    }

    private static List<PluginResourceSnapshot> GetPluginResources()
    {
        return Chainloader.PluginInfos.Values
            .Select(pluginInfo =>
            {
                string pluginName = (pluginInfo.Metadata.Name ?? "").Trim();
                string pluginGuid = (pluginInfo.Metadata.GUID ?? "").Trim();
                string assemblyName = "";
                string[] resourceNames = Array.Empty<string>();
                try
                {
                    assemblyName = pluginInfo.Instance?.GetType().Assembly.GetName().Name ?? "";
                    resourceNames = pluginInfo.Instance?.GetType().Assembly.GetManifestResourceNames() ?? Array.Empty<string>();
                }
                catch
                {
                    // Some plugins are in a transient load state while Valheim and Jotunn register assets.
                }

                return new PluginResourceSnapshot
                {
                    OwnerName = pluginName.Length > 0 ? pluginName : pluginGuid,
                    PluginName = pluginName,
                    PluginGuid = pluginGuid,
                    AssemblyName = assemblyName,
                    ResourceNames = resourceNames
                };
            })
            .Where(plugin => plugin.OwnerName.Length > 0)
            .ToList();
    }

    private static string ResolveOwnerName(string bundleName, List<PluginResourceSnapshot> plugins)
    {
        PluginResourceSnapshot? embeddedOwner = plugins.FirstOrDefault(plugin =>
            plugin.ResourceNames.Any(resourceName =>
                resourceName.EndsWith(bundleName, StringComparison.OrdinalIgnoreCase)));
        if (embeddedOwner != null)
        {
            return embeddedOwner.OwnerName;
        }

        string normalizedBundleName = NormalizeToken(Path.GetFileNameWithoutExtension(bundleName));
        if (normalizedBundleName.Length == 0)
        {
            return "";
        }

        PluginResourceSnapshot? tokenOwner = plugins.FirstOrDefault(plugin =>
            IsTokenMatch(normalizedBundleName, NormalizeToken(plugin.PluginName)) ||
            IsTokenMatch(normalizedBundleName, NormalizeToken(plugin.PluginGuid)) ||
            IsTokenMatch(normalizedBundleName, NormalizeToken(plugin.AssemblyName)));

        return tokenOwner?.OwnerName ?? "";
    }

    private static bool IsTokenMatch(string bundleName, string pluginToken)
    {
        return pluginToken.Length > 0 &&
               (bundleName.IndexOf(pluginToken, StringComparison.OrdinalIgnoreCase) >= 0 ||
                pluginToken.IndexOf(bundleName, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        StringBuilder builder = new();
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string BuildSignature(IEnumerable<PluginResourceSnapshot> plugins)
    {
        IEnumerable<string> bundleTokens = AssetBundle.GetAllLoadedAssetBundles()
            .Select(bundle => bundle.name ?? "")
            .Where(name => name.Length > 0)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> pluginTokens = plugins
            .Select(plugin =>
                $"{plugin.PluginGuid}:{plugin.PluginName}:{plugin.AssemblyName}:" +
                string.Join(",", plugin.ResourceNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase);

        return string.Join("|", bundleTokens) + "||" + string.Join("|", pluginTokens);
    }
}
