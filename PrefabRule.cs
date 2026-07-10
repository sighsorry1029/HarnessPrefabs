using System.Collections.Generic;

namespace HarnessPrefabs;

public sealed class PrefabRuleEntry
{
    public string Prefab { get; set; } = "";
    public bool? Enabled { get; set; }
    public string? Category { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? CraftingStation { get; set; }
    public List<PrefabRequirement>? Requirements { get; set; }
    public string? Flags { get; set; }
    public ComponentList? Components { get; set; }
}

public sealed class PrefabRule
{
    public PrefabAccess Access { get; set; } = PrefabAccess.Hidden;
    public string Category { get; set; } = BuildCategories.HarnessProps;
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string CraftingStation { get; set; } = "None";
    public List<PrefabRequirement> Requirements { get; set; } = new();
    public bool ClipEverything { get; set; }
    public bool ClipGround { get; set; }
    public bool AllowedInDungeons { get; set; }
    public bool CanBeRemoved { get; set; } = true;
    public ComponentList Components { get; set; } = new();
}

public sealed class PrefabRequirement
{
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
}

public sealed class ComponentList : List<string>
{
    public ComponentList()
    {
    }

    public ComponentList(IEnumerable<string> values) : base(values)
    {
    }
}

internal sealed class PrefabDiscovery
{
    public string Name { get; set; } = "";
    public PrefabAccess Access { get; set; }
    public string Category { get; set; } = BuildCategories.HarnessProps;
    public bool ClipEverything { get; set; }
    public bool ClipGround { get; set; }
    public bool AllowedInDungeons { get; set; }
    public ComponentList Components { get; set; } = new();
    public UnityEngine.GameObject? Prefab { get; set; }
}
