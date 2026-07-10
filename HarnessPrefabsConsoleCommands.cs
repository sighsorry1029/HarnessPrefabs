namespace HarnessPrefabs;

internal static class HarnessPrefabsConsoleCommands
{
    private const string WriteFullCommandName = "harnessprefabs:full";
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        new Terminal.ConsoleCommand(
            WriteFullCommandName,
            "Write HarnessPrefabs full scaffold YAML with explicit active defaults. Usage: harnessprefabs:full",
            WriteFullScaffoldFile);
    }

    private static void WriteFullScaffoldFile(Terminal.ConsoleEventArgs args)
    {
        if (PrefabRuleStore.TryWriteFullScaffoldConfigurationFile(out string path, out string error))
        {
            args.Context?.AddString($"Wrote prefab full scaffold to {path}");
            return;
        }

        args.Context?.AddString(error);
    }
}
