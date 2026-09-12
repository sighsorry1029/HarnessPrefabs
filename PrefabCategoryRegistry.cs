#nullable disable

using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HarnessPrefabs;

// Admin categories live in their own top-level BuildUi list. They stay outside
// PieceTable.m_pieces so native categories, materials, recent and favorites do
// not expose them to ordinary players.
internal static class PrefabCategoryRegistry
{
    private static readonly string[] AdminCategories =
    {
        BuildCategories.HarnessNature,
        BuildCategories.HarnessStructures,
        BuildCategories.HarnessProps
    };
    private static readonly Dictionary<Piece, string> Categories = new();
    private static readonly List<Piece> OrderedPieces = new();
    private static readonly Dictionary<string, int> TagIds = new(StringComparer.Ordinal)
    {
        [BuildCategories.HarnessNature] = 1000000,
        [BuildCategories.HarnessStructures] = 1000001,
        [BuildCategories.HarnessProps] = 1000002
    };
    private static readonly AccessTools.FieldRef<BuildUi, List<IPieceList>> Lists =
        AccessTools.FieldRefAccess<BuildUi, List<IPieceList>>("m_pieceLists");
    private static readonly AccessTools.FieldRef<BuildUi, List<Button>> Buttons =
        AccessTools.FieldRefAccess<BuildUi, List<Button>>("m_tabButtons");
    private static readonly AccessTools.FieldRef<BuildUi, TabHandler> Tabs =
        AccessTools.FieldRefAccess<BuildUi, TabHandler>("m_tabHandler");
    private static readonly AccessTools.FieldRef<BuildUi, int> CurrentList =
        AccessTools.FieldRefAccess<BuildUi, int>("m_currentPieceList");
    private static readonly Dictionary<BuildUi, Registration> Registrations = new();
    private static BuildUi _ui;

    public static void Clear()
    {
        Categories.Clear();
        OrderedPieces.Clear();
    }

    public static void Dispose()
    {
        foreach (BuildUi ui in new List<BuildUi>(Registrations.Keys))
        {
            if (ui) Detach(ui, destroying: false);
            else Registrations.Remove(ui);
        }
        _ui = null;
        Clear();
    }

    public static void Assign(Piece piece, string categoryName)
    {
        string name = BuildCategories.NormalizeCategory(categoryName);
        if (!Categories.ContainsKey(piece)) OrderedPieces.Add(piece);
        Categories[piece] = name;
        piece.m_category = name switch
        {
            BuildCategories.Crafting => Piece.PieceCategory.Crafting,
            BuildCategories.Building => Piece.PieceCategory.BuildingWorkbench,
            BuildCategories.Stonecutter => Piece.PieceCategory.BuildingStonecutter,
            BuildCategories.Furniture => Piece.PieceCategory.Furniture,
            "DeepNorth" => Piece.PieceCategory.DeepNorth,
            "Feasts" => Piece.PieceCategory.Feasts,
            "Food" => Piece.PieceCategory.Food,
            "Meads" => Piece.PieceCategory.Meads,
            _ => Piece.PieceCategory.Misc
        };
        if (piece.m_usage == 0) piece.m_usage = Piece.UsageTagFlags.Misc;
    }

    public static void RefreshUi()
    {
        BuildUi ui = _ui;
        if (!ui) return;
        SetAttached(ui, HarnessPrefabsPlugin.HarnessHammerTabsEnabled);
        if (ui.gameObject.activeInHierarchy && Player.m_localPlayer)
            ui.SelectPieceList(CurrentList(ui), refreshOnly: true);
    }

    private static void SetAttached(BuildUi ui, bool attached)
    {
        if (attached) Attach(ui);
        else Detach(ui, destroying: false);
    }

    private static void Attach(BuildUi ui)
    {
        if (Registrations.ContainsKey(ui)) return;
        List<IPieceList> lists = Lists(ui);
        List<Button> buttons = Buttons(ui);
        TabHandler handler = Tabs(ui);
        if (buttons.Count == 0 || lists.Count != buttons.Count || handler.m_tabs.Count != lists.Count)
        {
            HarnessPrefabsPlugin.Log.LogWarning("Build menu tab layout is inconsistent; HarnessPrefabs section was not inserted.");
            return;
        }

        HarnessPieceList pieceList = new();
        Button button = UnityEngine.Object.Instantiate(buttons[0], buttons[0].transform.parent);
        button.name = "HarnessPrefabsTab";
        button.transform.SetAsLastSibling();
        button.onClick = new Button.ButtonClickedEvent();
        foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
            label.text = pieceList.DisplayName;

        UnityEvent selectList = new();
        selectList.AddListener(() => ui.SelectPieceList(lists.IndexOf(pieceList), refreshOnly: false));
        TabHandler.Tab tab = new()
        {
            m_button = button,
            m_onClick = selectList
        };
        button.onClick.AddListener(() => handler.SetActiveTab(lists.IndexOf(pieceList), forceSelect: false, invokeOnClick: true));
        lists.Add(pieceList);
        buttons.Add(button);
        handler.m_tabs.Add(tab);
        Registrations.Add(ui, new Registration(pieceList, button, tab));
    }

    private static void Detach(BuildUi ui, bool destroying)
    {
        if (!Registrations.TryGetValue(ui, out Registration registration)) return;
        Registrations.Remove(ui);
        registration.Tab.m_onClick?.RemoveAllListeners();
        if (registration.Button) registration.Button.onClick.RemoveAllListeners();
        if (destroying) return;

        List<IPieceList> lists = Lists(ui);
        int index = lists.IndexOf(registration.PieceList);
        TabHandler handler = Tabs(ui);
        if (index >= 0 && (handler.GetActiveTab() == index || CurrentList(ui) == index))
        {
            CurrentList(ui) = 0;
            handler.SetActiveTabWithoutInvokingOnClick(0);
            if (ui.isActiveAndEnabled && Player.m_localPlayer)
                ui.SelectPieceList(0, refreshOnly: false);
        }
        if (index >= 0)
        {
            lists.RemoveAt(index);
            Buttons(ui).Remove(registration.Button);
            handler.m_tabs.Remove(registration.Tab);
        }
        if (registration.Button) UnityEngine.Object.Destroy(registration.Button.gameObject);
    }

    [HarmonyPatch(typeof(BuildUi), "Awake")]
    private static class BuildUiAwakePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last - 100)]
        private static void Postfix(BuildUi __instance)
        {
            _ui = __instance;
            SetAttached(__instance, HarnessPrefabsPlugin.HarnessHammerTabsEnabled);
        }
    }

    [HarmonyPatch(typeof(BuildUi), "OnDestroy")]
    private static class BuildUiDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix(BuildUi __instance)
        {
            Detach(__instance, destroying: true);
            if (ReferenceEquals(_ui, __instance)) _ui = null;
        }
    }

    [HarmonyPatch(typeof(BuildUiPieceButton), nameof(BuildUiPieceButton.UpdateRequirements))]
    private static class BuildIconRefreshPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildUiPieceButton __instance, Image ___m_icon)
        {
            Piece piece = __instance.Piece;
            if (piece && Categories.ContainsKey(piece) && ___m_icon && ___m_icon.sprite != piece.m_icon)
                ___m_icon.sprite = piece.m_icon;
        }
    }

    private sealed class HarnessPieceList : IPieceList
    {
        private readonly List<string> _availableCategories = new();
        public string DisplayName => "HarnessPrefabs";
        public bool ShowTags => true;
        public bool CanCustomizeTags => false;
        public int TagCount => _availableCategories.Count;
        public int TagSeparatorIndex => -1;

        public void UpdateAvailableTags(PieceTable pieceTable)
        {
            _availableCategories.Clear();
            foreach (string category in AdminCategories)
            {
                foreach (Piece piece in OrderedPieces)
                {
                    if (piece && Categories.TryGetValue(piece, out string label) && label == category)
                    {
                        _availableCategories.Add(category);
                        break;
                    }
                }
            }
        }

        public string GetTagDisplayName(int index) => _availableCategories[index];
        public int GetTagIdByIndex(int index) => TagIds[_availableCategories[index]];

        public void GetAvailablePiecesWithTag(int tagId, PieceTable pieceTable, IList<Piece> resultOut)
        {
            if (!HarnessPrefabsPlugin.HarnessHammerTabsEnabled) return;
            foreach (Piece piece in OrderedPieces)
                if (piece && Categories.TryGetValue(piece, out string category) &&
                    BuildCategories.IsAdminCategory(category) &&
                    (tagId == -1 || TagIds[category] == tagId))
                    resultOut.Add(piece);
        }
    }

    private sealed class Registration
    {
        internal readonly HarnessPieceList PieceList;
        internal readonly Button Button;
        internal readonly TabHandler.Tab Tab;
        internal Registration(HarnessPieceList pieceList, Button button, TabHandler.Tab tab)
        {
            PieceList = pieceList;
            Button = button;
            Tab = tab;
        }
    }
}
