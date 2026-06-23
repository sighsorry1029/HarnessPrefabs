using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;

namespace HarnessPrefabs;

internal static class HarnessPrefabsReferenceState
{
    private static string CacheDirectory => Path.Combine(PrefabRuleStore.RulesDirectory, "cache");

    public static bool ShouldSkip(string stateKey, string referencePath, string sourceSignature, string logicVersion)
    {
        string statePath = GetStatePath(stateKey);
        if (!File.Exists(referencePath) || !File.Exists(statePath))
        {
            return false;
        }

        string[] lines = File.ReadAllLines(statePath);
        if (lines.Length < 4)
        {
            return false;
        }

        return string.Equals(lines[0], logicVersion, StringComparison.Ordinal) &&
               string.Equals(lines[1], sourceSignature, StringComparison.Ordinal) &&
               string.Equals(lines[2], BuildFileStamp(referencePath), StringComparison.Ordinal);
    }

    public static void Record(string stateKey, string referencePath, string sourceSignature, string logicVersion)
    {
        Directory.CreateDirectory(CacheDirectory);
        File.WriteAllLines(GetStatePath(stateKey), new[]
        {
            logicVersion,
            sourceSignature,
            BuildFileStamp(referencePath),
            DateTime.UtcNow.ToString("O")
        });
    }

    public static string BuildFileStamp(string path)
    {
        if (!File.Exists(path))
        {
            return "missing";
        }

        FileInfo file = new(path);
        return $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
    }

    public static string ComputeStableHash(string value)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static string GetStatePath(string stateKey)
    {
        string safeKey = string.Join("_", (stateKey ?? "").Split(Path.GetInvalidFileNameChars()));
        if (safeKey.Length == 0)
        {
            safeKey = "default";
        }

        return Path.Combine(CacheDirectory, $".reference-state.{safeKey}.txt");
    }
}
