using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.FishPonds;
using StardewValley.ItemTypeDefinitions;
using SObject = StardewValley.Object;

namespace FishingHorizonsExpanded.Framework.Journal
{
    /// <summary>One item a fish pond can produce.</summary>
    internal sealed class PondDrop
    {
        /// <summary>The parsed item data (name + sprite), or an error item if the ID can't be resolved.</summary>
        public ParsedItemData Item { get; }

        /// <summary>The minimum pond population required to produce this item.</summary>
        public int RequiredPopulation { get; }

        /// <summary>The chance this entry is picked when the pond produces output (0–1).</summary>
        public float Chance { get; }

        /// <summary>The minimum stack size produced.</summary>
        public int MinStack { get; }

        /// <summary>The maximum stack size produced.</summary>
        public int MaxStack { get; }

        public PondDrop(ParsedItemData item, int requiredPopulation, float chance, int minStack, int maxStack)
        {
            this.Item = item;
            this.RequiredPopulation = requiredPopulation;
            this.Chance = chance;
            this.MinStack = minStack;
            this.MaxStack = maxStack;
        }
    }

    /// <summary>One item option for a population gate quest.</summary>
    internal sealed class PondGateItem
    {
        /// <summary>The parsed item data (name + sprite), or an error item if the ID can't be resolved.</summary>
        public ParsedItemData Item { get; }

        /// <summary>The minimum count that may be requested.</summary>
        public int MinCount { get; }

        /// <summary>The maximum count that may be requested.</summary>
        public int MaxCount { get; }

        public PondGateItem(ParsedItemData item, int minCount, int maxCount)
        {
            this.Item = item;
            this.MinCount = minCount;
            this.MaxCount = maxCount;
        }
    }

    /// <summary>One population gate: the pond stops growing at this population until the player brings one of the requested items.</summary>
    internal sealed class PondGate
    {
        /// <summary>The population at which this gate applies.</summary>
        public int Population { get; }

        /// <summary>The possible quest items (one is requested at random).</summary>
        public List<PondGateItem> Items { get; }

        public PondGate(int population, List<PondGateItem> items)
        {
            this.Population = population;
            this.Items = items;
        }
    }

    /// <summary>Everything the journal shows about keeping one fish in a fish pond.</summary>
    internal sealed class PondInfo
    {
        /// <summary>The maximum pond population.</summary>
        public int MaxPopulation { get; }

        /// <summary>How many days it takes for a new fish to spawn.</summary>
        public int SpawnDays { get; }

        /// <summary>The population gates, sorted by population.</summary>
        public List<PondGate> Gates { get; }

        /// <summary>The producible items, sorted by required population.</summary>
        public List<PondDrop> Drops { get; }

        public PondInfo(int maxPopulation, int spawnDays, List<PondGate> gates, List<PondDrop> drops)
        {
            this.MaxPopulation = maxPopulation;
            this.SpawnDays = spawnDays;
            this.Gates = gates;
            this.Drops = drops;
        }
    }

    /// <summary>Resolves <c>Data/FishPondData</c> for journal fish.</summary>
    /// <remarks>
    /// The matching entry comes from the game's own lookup (<see cref="FishPond.GetRawData"/>).
    /// A <c>SpawnTime</c> of -1 falls back to the game's price-based spawn time (see
    /// <c>FishPond.GetFishPondData</c>). Data errors never throw: a fish with broken pond
    /// data simply shows no pond page.
    /// </remarks>
    internal static class PondRegistry
    {
        /*********
        ** Accessors
        *********/
        /// <summary>The SMAPI monitor for diagnostics, set by the journal module on activation.</summary>
        public static IMonitor? Monitor { get; set; }


        /*********
        ** Public methods
        *********/
        /// <summary>Get the pond info for a fish, or null if it can't live in a fish pond.</summary>
        public static PondInfo? Get(FishEntry fish)
        {
            try
            {
                return Build(fish);
            }
            catch (Exception ex)
            {
                // broken pond data — treat as "can't live in a pond"
                Monitor?.Log($"Pond info failed for {fish.QualifiedId}: {ex}", LogLevel.Debug);
                return null;
            }
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Log why a fish has no pond page (for troubleshooting).</summary>
        private static void LogSkip(FishEntry fish, string reason)
        {
            Monitor?.Log($"No pond page for {fish.QualifiedId}: {reason}.", LogLevel.Debug);
        }


        /// <summary>Build the pond info for a fish, or return null if it can't live in a pond.</summary>
        private static PondInfo? Build(FishEntry fish)
        {
            if (ItemRegistry.Create(fish.QualifiedId, allowNull: true) is not SObject obj)
            {
                LogSkip(fish, "couldn't create item instance");
                return null;
            }

            // the game only allows fish items in ponds, and excludes legendaries and opt-outs (see FishPond::doAction)
            if (obj.Category != SObject.FishCategory)
            {
                LogSkip(fish, $"item category {obj.Category} isn't the fish category ({SObject.FishCategory})");
                return null;
            }
            if (obj.HasContextTag("fish_legendary") || obj.HasContextTag("fish_pond_ignore"))
            {
                LogSkip(fish, "fish is legendary or tagged fish_pond_ignore");
                return null;
            }

            FishPondData? data = FishPond.GetRawData(fish.Id);
            if (data is null)
            {
                LogSkip(fish, "no matching entry in Data/FishPondData");
                return null;
            }

            // spawn time: -1 means the game derives it from the fish's price
            int spawnDays = data.SpawnTime;
            if (spawnDays <= 0)
            {
                int price = obj.Price;
                spawnDays = price > 30 ? (price > 80 ? (price > 120 ? (price > 250 ? 5 : 4) : 3) : 2) : 1;
            }

            int maxPopulation = data.MaxPopulation > 0 ? Math.Min(data.MaxPopulation, FishPond.MAXIMUM_OCCUPANCY) : FishPond.MAXIMUM_OCCUPANCY;

            // population gates: "{itemId} [minCount] [maxCount]" strings keyed by population
            var gates = new List<PondGate>();
            if (data.PopulationGates is not null)
            {
                foreach ((int population, List<string> rawItems) in data.PopulationGates.OrderBy(gate => gate.Key))
                {
                    if (rawItems is null)
                        continue;

                    var items = new List<PondGateItem>();
                    foreach (string rawItem in rawItems)
                    {
                        string[] parts = rawItem?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                        if (parts.Length is < 1 or > 3)
                            continue;

                        int minCount = parts.Length > 1 && int.TryParse(parts[1], out int min) ? Math.Max(1, min) : 1;
                        int maxCount = parts.Length > 2 && int.TryParse(parts[2], out int max) ? Math.Max(minCount, max) : minCount;
                        items.Add(new PondGateItem(GetItemData(parts[0]), minCount, maxCount));
                    }

                    if (items.Count > 0)
                        gates.Add(new PondGate(population, items));
                }
            }

            // produced items
            var drops = new List<PondDrop>();
            if (data.ProducedItems is not null)
            {
                foreach (FishPondReward? reward in data.ProducedItems)
                {
                    if (reward is null)
                        continue;

                    // plain item IDs cover vanilla + Fishing Horizons data; complex item queries are skipped
                    string? itemId = !string.IsNullOrWhiteSpace(reward.ItemId) ? reward.ItemId : reward.RandomItemId?.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
                    if (itemId is null || itemId.Contains(' '))
                        continue;

                    int minStack = Math.Max(1, reward.MinStack);
                    int maxStack = Math.Max(minStack, reward.MaxStack);
                    drops.Add(new PondDrop(GetItemData(itemId), reward.RequiredPopulation, Math.Clamp(reward.Chance, 0f, 1f), minStack, maxStack));
                }
            }
            drops = drops
                .OrderBy(drop => drop.RequiredPopulation)
                .ThenByDescending(drop => drop.Chance)
                .ToList();

            Monitor?.Log($"Pond page ready for {fish.QualifiedId}: population {maxPopulation}, {gates.Count} gates, {drops.Count} drops.", LogLevel.Debug);
            return new PondInfo(maxPopulation, spawnDays, gates, drops);
        }

        /// <summary>Resolve an item ID (qualified or not) to parsed item data, falling back to an error item.</summary>
        private static ParsedItemData GetItemData(string itemId)
        {
            return ItemRegistry.GetDataOrErrorItem(ItemRegistry.QualifyItemId(itemId) ?? itemId);
        }
    }
}
