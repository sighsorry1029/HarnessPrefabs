using System;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Managers;
using UnityEngine;
using PieceCategory = Piece.PieceCategory;

namespace HarnessPrefabs;

internal static class PrefabCategoryRegistry
{
    private const int FirstCustomCategory = (int)PieceCategory.Max + 1;

    private static readonly Dictionary<string, PieceCategory> CategoriesByName = new(StringComparer.Ordinal);

    public static PieceCategory GetOrAdd(PieceTable table, string categoryName)
    {
        categoryName = NormalizeCategoryName(categoryName);
        if (TryGetBuiltIn(categoryName, out PieceCategory builtIn))
        {
            EnsureTableHasCategory(table, builtIn, categoryName);
            return builtIn;
        }

        if (TryGetExistingCustomCategory(table, categoryName, out PieceCategory existing))
        {
            if (!IsCategoryUsedByDifferentLabel(table, existing, categoryName))
            {
                CategoriesByName[categoryName] = existing;
                EnsureTableHasCategory(table, existing, categoryName);
                return existing;
            }
        }

        if (CategoriesByName.TryGetValue(categoryName, out PieceCategory category))
        {
            if (!IsCategoryUsedByDifferentLabel(table, category, categoryName))
            {
                EnsureTableHasCategory(table, category, categoryName);
                return category;
            }

            CategoriesByName.Remove(categoryName);
        }

        if (TryGetJotunnCategory(categoryName, out category) &&
            !IsCategoryUsedByDifferentLabel(table, category, categoryName))
        {
            CategoriesByName[categoryName] = category;
            EnsureTableHasCategory(table, category, categoryName);
            return category;
        }

        if (TryAddJotunnCategory(categoryName, out category) &&
            !IsCategoryUsedByDifferentLabel(table, category, categoryName))
        {
            CategoriesByName[categoryName] = category;
            EnsureTableHasCategory(table, category, categoryName);
            return category;
        }

        category = AllocateCategory(table);
        CategoriesByName[categoryName] = category;
        EnsureTableHasCategory(table, category, categoryName);
        return category;
    }

    public static void EnsureGrown(PieceTable table)
    {
        if (!table || table.m_availablePieces == null)
        {
            return;
        }

        int needed = (int)PieceCategory.Max;
        if (table.m_categories != null)
        {
            foreach (PieceCategory category in table.m_categories)
            {
                int value = (int)category;
                if (value >= 0 && value < (int)PieceCategory.All)
                {
                    needed = Math.Max(needed, value + 1);
                }
            }
        }

        if (table.m_pieces != null)
        {
            foreach (GameObject pieceObject in table.m_pieces)
            {
                if (!pieceObject)
                {
                    continue;
                }

                Piece piece = pieceObject.GetComponent<Piece>();
                if (piece)
                {
                    int value = (int)piece.m_category;
                    if (value >= 0 && value < (int)PieceCategory.All)
                    {
                        needed = Math.Max(needed, value + 1);
                    }
                }
            }
        }

        while (table.m_availablePieces.Count < needed)
        {
            table.m_availablePieces.Add(new List<Piece>());
        }

        GrowVectorArray(ref table.m_selectedPiece, needed);
        GrowVectorArray(ref table.m_lastSelectedPiece, needed);
    }

    private static string NormalizeCategoryName(string categoryName)
    {
        return BuildCategories.NormalizeCategory(categoryName);
    }

    private static bool TryGetBuiltIn(string name, out PieceCategory category)
    {
        switch (name)
        {
            case BuildCategories.Misc:
                category = PieceCategory.Misc;
                return true;
            case BuildCategories.Crafting:
                category = PieceCategory.Crafting;
                return true;
            case BuildCategories.Building:
                category = PieceCategory.BuildingWorkbench;
                return true;
            case BuildCategories.Stonecutter:
                category = PieceCategory.BuildingStonecutter;
                return true;
            case BuildCategories.Furniture:
                category = PieceCategory.Furniture;
                return true;
            default:
                category = PieceCategory.Misc;
                return false;
        }
    }

    private static PieceCategory AllocateCategory(PieceTable table)
    {
        int next = FirstCustomCategory;
        if (table.m_categories != null)
        {
            foreach (PieceCategory category in table.m_categories)
            {
                int value = (int)category;
                if (value >= FirstCustomCategory && value < (int)PieceCategory.All)
                {
                    next = Math.Max(next, value + 1);
                }
            }
        }

        foreach (PieceCategory category in CategoriesByName.Values)
        {
            int value = (int)category;
            if (value >= FirstCustomCategory && value < (int)PieceCategory.All)
            {
                next = Math.Max(next, value + 1);
            }
        }

        if (next >= (int)PieceCategory.All)
        {
            next = (int)PieceCategory.All - 1;
        }

        return (PieceCategory)next;
    }

    private static bool TryGetJotunnCategory(string categoryName, out PieceCategory category)
    {
        category = (PieceCategory)0;
        try
        {
            PieceCategory? existing = PieceManager.Instance.GetPieceCategory(categoryName);
            if (!existing.HasValue || existing.Value == PieceCategory.Max || existing.Value == PieceCategory.All)
            {
                return false;
            }

            category = existing.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAddJotunnCategory(string categoryName, out PieceCategory category)
    {
        category = (PieceCategory)0;
        try
        {
            category = PieceManager.Instance.AddPieceCategory(categoryName);
            return category != PieceCategory.Max && category != PieceCategory.All;
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogWarning($"Jotunn failed to add Hammer category '{categoryName}': {ex.Message}");
            return false;
        }
    }

    private static bool IsCategoryUsedByDifferentLabel(PieceTable table, PieceCategory category, string label)
    {
        if (!table || table.m_categories == null || table.m_categoryLabels == null)
        {
            return false;
        }

        for (int i = 0; i < table.m_categories.Count && i < table.m_categoryLabels.Count; i++)
        {
            if (table.m_categories[i] == category &&
                !string.Equals(table.m_categoryLabels[i], label, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetExistingCustomCategory(PieceTable table, string label, out PieceCategory category)
    {
        category = (PieceCategory)0;
        if (!table || table.m_categories == null || table.m_categoryLabels == null)
        {
            return false;
        }

        for (int i = table.m_categoryLabels.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(table.m_categoryLabels[i], label, StringComparison.Ordinal) || i >= table.m_categories.Count)
            {
                continue;
            }

            PieceCategory found = table.m_categories[i];
            if (found == PieceCategory.Max || found == PieceCategory.All)
            {
                table.m_categoryLabels.RemoveAt(i);
                table.m_categories.RemoveAt(i);
                continue;
            }

            category = found;
            return true;
        }

        return false;
    }

    private static void EnsureTableHasCategory(PieceTable table, PieceCategory category, string label)
    {
        if (!table)
        {
            return;
        }

        table.m_categories ??= new List<PieceCategory>();
        table.m_categoryLabels ??= new List<string>();
        for (int i = table.m_categoryLabels.Count - 1; i >= 0; i--)
        {
            if (i >= table.m_categories.Count)
            {
                table.m_categoryLabels.RemoveAt(i);
                continue;
            }

            if (string.Equals(table.m_categoryLabels[i], label, StringComparison.Ordinal) &&
                table.m_categories[i] != category)
            {
                table.m_categoryLabels.RemoveAt(i);
                table.m_categories.RemoveAt(i);
            }
        }

        if (!table.m_categories.Contains(category))
        {
            table.m_categories.Add(category);
            table.m_categoryLabels.Add(label);
        }
    }

    private static void GrowVectorArray(ref Vector2Int[] array, int needed)
    {
        if (array == null)
        {
            array = new Vector2Int[needed];
            return;
        }

        if (array.Length >= needed)
        {
            return;
        }

        Vector2Int[] grown = new Vector2Int[needed];
        Array.Copy(array, grown, array.Length);
        array = grown;
    }
}
