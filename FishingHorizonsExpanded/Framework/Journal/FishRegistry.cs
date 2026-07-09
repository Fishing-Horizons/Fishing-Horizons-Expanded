using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace FishingHorizonsExpanded.Framework.Journal
{
    /// <summary>A single fish entry in the journal.</summary>
    internal sealed class FishEntry
    {
        /*********
        ** Accessors
        *********/
        /// <summary>The unqualified item ID (key in <c>Data/Fish</c>).</summary>
        public string Id { get; }

        /// <summary>The qualified item ID (like <c>(O)128</c>).</summary>
        public string QualifiedId { get; }

        /// <summary>The parsed item data (display name, description, sprite).</summary>
        public ParsedItemData Data { get; }

        /// <summary>Whether this fish is caught with a crab pot instead of a fishing rod.</summary>
        public bool IsTrapFish { get; }


        /*********
        ** Public methods
        *********/
        public FishEntry(string id, ParsedItemData data, bool isTrapFish)
        {
            this.Id = id;
            this.QualifiedId = data.QualifiedItemId;
            this.Data = data;
            this.IsTrapFish = isTrapFish;
        }

        /// <summary>Whether the current player has ever caught this fish.</summary>
        public bool IsCaught()
        {
            return Game1.player.fishCaught.ContainsKey(this.QualifiedId);
        }

        /// <summary>How many of this fish the current player has caught.</summary>
        public int TimesCaught()
        {
            return Game1.player.fishCaught.TryGetValue(this.QualifiedId, out int[]? stats) && stats.Length > 0
                ? stats[0]
                : 0;
        }

        /// <summary>The biggest caught size in inches (vanilla unit), or 0 if never caught.</summary>
        public int MaxSize()
        {
            return Game1.player.fishCaught.TryGetValue(this.QualifiedId, out int[]? stats) && stats.Length > 1
                ? stats[1]
                : 0;
        }
    }

    /// <summary>A journal section: a group of fish from one source (vanilla or a specific mod).</summary>
    internal sealed class JournalSection
    {
        /*********
        ** Accessors
        *********/
        /// <summary>The translated section title.</summary>
        public string Title { get; }

        /// <summary>The fish in this section.</summary>
        public List<FishEntry> Fish { get; }


        /*********
        ** Public methods
        *********/
        public JournalSection(string title, List<FishEntry> fish)
        {
            this.Title = title;
            this.Fish = fish;
        }
    }

    /// <summary>Builds the journal's fish list from the game data, grouped by source mod.</summary>
    /// <remarks>
    /// Fish are read from <c>Data/Fish</c>, so any fish added by any mod is picked up automatically.
    /// The source mod is detected from the item ID prefix (convention: <c>{modID}_{name}</c>) or,
    /// as a fallback, from the sprite texture path (convention: <c>Mods/{modID}/...</c>), since some
    /// content packs (including Fishing Horizons) use unprefixed item IDs.
    /// </remarks>
    internal static class FishRegistry
    {
        /*********
        ** Public methods
        *********/
        /// <summary>Build the journal sections: vanilla fish first, then one section per mod.</summary>
        /// <param name="modRegistry">The SMAPI mod registry, used to resolve mod display names.</param>
        /// <param name="vanillaSectionTitle">The translated title of the vanilla section.</param>
        public static List<JournalSection> BuildSections(IModRegistry modRegistry, string vanillaSectionTitle)
        {
            var fishData = Game1.content.Load<Dictionary<string, string>>("Data\\Fish");
            var bySource = new Dictionary<string, List<FishEntry>>(); // key: "" for vanilla, else mod ID or raw prefix

            foreach ((string id, string rawData) in fishData)
            {
                ParsedItemData data = ItemRegistry.GetData(ItemRegistry.type_object + id);
                if (data is null || data.IsErrorItem)
                    continue;

                string[] fields = rawData.Split('/');
                bool isTrapFish = fields.Length > 1 && fields[1] == "trap";

                string sourceKey = GetSourceKey(id, data);
                if (!bySource.TryGetValue(sourceKey, out List<FishEntry>? list))
                    bySource[sourceKey] = list = new List<FishEntry>();
                list.Add(new FishEntry(id, data, isTrapFish));
            }

            var sections = new List<JournalSection>();

            // vanilla first
            if (bySource.TryGetValue("", out List<FishEntry>? vanilla))
                sections.Add(new JournalSection(vanillaSectionTitle, Sorted(vanilla)));

            // then mod sections, sorted by title
            var modSections = new List<JournalSection>();
            foreach ((string sourceKey, List<FishEntry> fish) in bySource)
            {
                if (sourceKey == "")
                    continue;
                string title = modRegistry.Get(sourceKey)?.Manifest.Name ?? sourceKey;
                modSections.Add(new JournalSection(title, Sorted(fish)));
            }
            sections.AddRange(modSections.OrderBy(section => section.Title, StringComparer.OrdinalIgnoreCase));

            return sections;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Get the source key for a fish: empty string for vanilla, else the source mod ID (or raw prefix).</summary>
        private static string GetSourceKey(string id, ParsedItemData data)
        {
            // 1.6 convention: modded item IDs look like "{modID}_{name}" where modID contains a dot
            int underscore = id.IndexOf('_');
            if (underscore > 0)
            {
                string prefix = id[..underscore];
                if (prefix.Contains('.'))
                    return prefix;
            }

            // fallback: detect mod from a custom sprite texture path like "Mods/{modID}/SpriteFish"
            string? texture = data.TextureName?.Replace('\\', '/');
            if (texture is not null && texture.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = texture.Split('/');
                if (parts.Length >= 2 && parts[1].Length > 0)
                    return parts[1];
            }

            // numeric or otherwise unrecognized ID with a vanilla texture → vanilla
            return "";
        }

        /// <summary>Sort fish by display name within a section.</summary>
        private static List<FishEntry> Sorted(List<FishEntry> fish)
        {
            return fish
                .OrderBy(entry => entry.Data.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}
