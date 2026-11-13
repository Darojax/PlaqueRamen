using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace PlaqueRamen;

internal sealed class Artwork
{
    public string Id = "", OrderGuid = "", AssetName = "", Dish = "", File = "", Sha256 = "";
    public int Yen = 0;
    public int InkTop = 0, InkBottom = 0;
    public bool Drink = false;
    public int DrinkPriority = 0;
    public string DarkFile = "", DarkSha256 = "", DarkMaskFile = "", DarkMaskSha256 = "";
    public string DarkGrainA = "", DarkGrainB = "";
}

internal readonly struct MenuEntry
{
    internal readonly int ButtonId, Yen;
    internal readonly string Guid;
    internal readonly bool Ramen;
    internal MenuEntry(int buttonId, string guid, int yen, bool ramen)
    { ButtonId = buttonId; Guid = guid; Yen = yen; Ramen = ramen; }
}

internal sealed class PlannedPlaque
{
    internal readonly int ButtonId;
    internal readonly Artwork Art;
    internal PlannedPlaque(int buttonId, Artwork art) { ButtonId = buttonId; Art = art; }
}

internal static class MenuPlan
{
    // The reviewed machine's IDs are row-major: 0 1 2 / 3 4 5 / 6 7 8.
    // The resulting dish list is assigned to the wall from right to left.
    internal static PlannedPlaque[] Build(IEnumerable<MenuEntry> entries, IReadOnlyList<Artwork> artwork)
    {
        var slots = entries.OrderBy(e => e.ButtonId).ToArray();
        if (slots.Any(e => e.ButtonId < 0 || e.ButtonId >= 9) || slots.Select(e => e.ButtonId).Distinct().Count() != slots.Length)
            throw new InvalidDataException("Ticket-machine slot layout is not the reviewed 3 x 3 layout.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PlannedPlaque>();
        foreach (var entry in slots)
        {
            if (!seen.Add(entry.Guid)) continue;
            var matches = artwork.Where(a => a.OrderGuid.Equals(entry.Guid, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!entry.Ramen && matches.Length == 0) continue;
            if (matches.Length != 1) throw new InvalidDataException("No unique approved plaque artwork for recipe " + entry.Guid + ".");
            if (matches[0].Drink == entry.Ramen) throw new InvalidDataException("The recipe category changed for " + matches[0].Dish + ".");
            if (matches[0].Yen != entry.Yen) throw new InvalidDataException("The selling price changed for " + matches[0].Dish + "; plaque sync stopped to avoid displaying an incorrect price.");
            result.Add(new PlannedPlaque(entry.ButtonId, matches[0]));
        }
        return result.OrderBy(p => p.Art.Drink).ThenBy(p => p.ButtonId).ToArray();
    }

    internal static PlannedPlaque[] ForCapacity(PlannedPlaque[] plan, int capacity)
    {
        if (capacity is not (7 or 9)) throw new InvalidDataException("Unsupported plaque capacity.");
        // Price decides inclusion only. The retained dishes keep the agreed
        // ticket-machine reading order; ties favour the earlier machine button.
        var food = plan.Where(p => !p.Art.Drink).OrderByDescending(p => p.Art.Yen).ThenBy(p => p.ButtonId).Take(capacity).OrderBy(p => p.ButtonId).ToArray();
        var drinks = plan.Where(p => p.Art.Drink).OrderBy(p => p.Art.DrinkPriority).ThenBy(p => p.ButtonId)
            .Take(capacity - food.Length).OrderBy(p => p.ButtonId);
        return food.Concat(drinks).ToArray();
    }

    // Ranks run right-to-left. The span, including an optional separator, stays
    // centred with the accepted right bias, while the native hooks never move.
    internal static int[] SlotRanks(PlannedPlaque[] selected, int capacity)
    {
        if (capacity is not (7 or 9) || selected.Length > capacity) throw new InvalidDataException("Invalid plaque slot plan.");
        int foods = selected.Count(p => !p.Art.Drink), drinks = selected.Length - foods;
        if (selected.Take(foods).Any(p => p.Art.Drink)) throw new InvalidDataException("Food must precede drinks in right-to-left order.");
        int gap = foods > 0 && drinks > 0 && selected.Length < capacity ? 1 : 0;
        int start = (capacity - selected.Length - gap) / 2;
        return Enumerable.Range(start, foods).Concat(Enumerable.Range(start + foods + gap, drinks)).ToArray();
    }

    internal static int[][] Submeshes(int[] source, HolderDefinition holder, HolderParts parts, int[] rightToLeft, int visible)
    {
        return SubmeshesAt(source, holder, parts, rightToLeft, CenteredPlaques(rightToLeft, visible));
    }

    internal static int[][] SubmeshesAt(int[] source, HolderDefinition holder, HolderParts parts, int[] rightToLeft, int[] selected)
    {
        int capacity = holder.Capacity, visible = selected.Length;
        if (rightToLeft.Length != capacity || rightToLeft.Distinct().Count() != capacity || visible < 0 || visible > capacity
            || rightToLeft.Any(id => !holder.Plaques.Any(p => p.Id == id)))
            throw new InvalidDataException("Invalid right-to-left plaque assignment.");
        if (selected.Distinct().Count() != visible || selected.Any(id => !rightToLeft.Contains(id)))
            throw new InvalidDataException("Invalid occupied hook selection.");
        parts.Validate(holder, source);
        var wood = new HashSet<int>(parts.Plaques.SelectMany(p => p.BoardTriangleOffsets));
        var result = new int[capacity + 1][];
        // The rail and all native hooks/pins are permanent in material slot 0.
        result[0] = Enumerable.Range(0, source.Length / 3).Where(t => !wood.Contains(t))
            .SelectMany(t => source.Skip(t * 3).Take(3)).ToArray();
        for (int i = 0; i < capacity; i++)
            result[i + 1] = i >= visible ? Array.Empty<int>() : parts.Plaques.Single(p => p.Id == selected[i]).BoardTriangleOffsets
                .SelectMany(t => source.Skip(t * 3).Take(3)).ToArray();
        return result;
    }

    internal static int[] CenteredPlaques(int[] rightToLeft, int visible)
    {
        int capacity = rightToLeft.Length;
        if (capacity is not (7 or 9) || rightToLeft.Distinct().Count() != capacity || visible < 0 || visible > capacity)
            throw new InvalidDataException("Invalid centered plaque selection.");
        // Floor the start in right-to-left order: even counts retain the middle
        // hook and have their extra board on the viewer's right, with no gaps.
        return rightToLeft.Skip((capacity - visible) / 2).Take(visible).ToArray();
    }
}
