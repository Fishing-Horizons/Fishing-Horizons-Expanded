using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.ItemTypeDefinitions;
using StardewValley.TokenizableStrings;

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

        /// <summary>The maximum possible size in inches (vanilla unit), or 0 if unknown.</summary>
        public int MaxPossibleSize { get; set; }

        /// <summary>The catch time ranges as (start, end) in game time (like 600–2600), for rod fish.</summary>
        public List<Point> TimeRanges { get; } = new();

        /// <summary>The seasons from <c>Data/Fish</c> (like "spring", "fall"), for rod fish.</summary>
        public HashSet<string> Seasons { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The required weather for rod fish: "sunny", "rainy", or "both".</summary>
        public string Weather { get; set; } = "both";

        /// <summary>The water type for trap fish: "ocean" or "freshwater".</summary>
        public string? TrapWaterType { get; set; }

        /// <summary>The locations where this fish can be caught (display name → seasons; empty set = any season).</summary>
        public Dictionary<string, HashSet<string>> Locations { get; } = new();


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
        public int MaxCaughtSize()
        {
            return Game1.player.fishCaught.TryGetValue(this.QualifiedId, out int[]? stats) && stats.Length > 1
                ? stats[1]
                : 0;
        }

        /// <summary>Whether the player caught a big specimen (≥90% of the maximum possible size).</summary>
        public bool HasBigSpecimen()
        {
            return this.MaxPossibleSize > 0 && this.MaxCaughtSize() >= 0.9 * this.MaxPossibleSize;
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
        ** Fields
        *********/
        /// <summary>Matches season names inside a game state query condition.</summary>
        private static readonly Regex SeasonRegex = new("\\b(spring|summer|fall|winter)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);


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
            var byQualifiedId = new Dictionary<string, FishEntry>();

            foreach ((string id, string rawData) in fishData)
            {
                ParsedItemData data = ItemRegistry.GetData(ItemRegistry.type_object + id);
                if (data is null || data.IsErrorItem)
                    continue;

                string[] fields = rawData.Split('/');
                bool isTrapFish = fields.Length > 1 && fields[1] == "trap";

                var entry = new FishEntry(id, data, isTrapFish);
                ParseFishFields(entry, fields);

                string sourceKey = GetSourceKey(id, data);
                if (!bySource.TryGetValue(sourceKey, out List<FishEntry>? list))
                    bySource[sourceKey] = list = new List<FishEntry>();
                list.Add(entry);
                byQualifiedId[entry.QualifiedId] = entry;
            }

            PopulateLocations(byQualifiedId);

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
        /// <summary>Parse size/time/season/weather info from the raw <c>Data/Fish</c> fields.</summary>
        private static void ParseFishFields(FishEntry entry, string[] fields)
        {
            try
            {
                if (entry.IsTrapFish)
                {
                    // Name/trap/Chance/WaterType/MinSize/MaxSize/...
                    if (fields.Length > 3)
                        entry.TrapWaterType = fields[3];
                    if (fields.Length > 5 && int.TryParse(fields[5], out int trapMax))
                        entry.MaxPossibleSize = trapMax;
                    return;
                }

                // Name/Difficulty/Behavior/MinSize/MaxSize/Time/Seasons/Weather/...
                if (fields.Length > 4 && int.TryParse(fields[4], out int rodMax))
                    entry.MaxPossibleSize = rodMax;

                if (fields.Length > 5)
                {
                    string[] times = fields[5].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i + 1 < times.Length; i += 2)
                    {
                        if (int.TryParse(times[i], out int start) && int.TryParse(times[i + 1], out int end))
                            entry.TimeRanges.Add(new Point(start, end));
                    }
                }

                if (fields.Length > 6)
                {
                    foreach (Match match in SeasonRegex.Matches(fields[6]))
                        entry.Seasons.Add(match.Value.ToLowerInvariant());
                }

                if (fields.Length > 7 && fields[7] is "sunny" or "rainy")
                    entry.Weather = fields[7];
            }
            catch
            {
                // malformed custom fish data — leave defaults, the journal still shows the fish
            }
        }

        /// <summary>Scan <c>Data/Locations</c> to find where each fish can be caught.</summary>
        private static void PopulateLocations(Dictionary<string, FishEntry> byQualifiedId)
        {
            Dictionary<string, LocationData> locations;
            try
            {
                locations = Game1.content.Load<Dictionary<string, LocationData>>("Data\\Locations");
            }
            catch
            {
                return; // journal still works without location info
            }

            foreach ((string locationName, LocationData locationData) in locations)
            {
                if (locationName is "Default" || locationData.Fish is null)
                    continue;

                string? displayName = null;

                foreach (SpawnFishData spawn in locationData.Fish)
                {
                    string? itemId = spawn.ItemId;
                    if (itemId is null)
                        continue;
                    if (!byQualifiedId.TryGetValue(itemId, out FishEntry? entry))
                    {
                        // the ID may be unqualified in custom data
                        string? qualified = ItemRegistry.QualifyItemId(itemId);
                        if (qualified is null || !byQualifiedId.TryGetValue(qualified, out entry))
                            continue;
                    }

                    displayName ??= GetLocationDisplayName(locationName, locationData);

                    if (!entry.Locations.TryGetValue(displayName, out HashSet<string>? seasons))
                        entry.Locations[displayName] = seasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // season restriction: explicit field, or heuristically from the condition query
                    if (spawn.Season.HasValue)
                        seasons.Add(spawn.Season.Value.ToString().ToLowerInvariant());
                    else if (!string.IsNullOrWhiteSpace(spawn.Condition))
                    {
                        foreach (Match match in SeasonRegex.Matches(spawn.Condition))
                            seasons.Add(match.Value.ToLowerInvariant());
                    }
                    else
                        seasons.Add("*"); // any season — overrides specific ones
                }
            }
        }

        /// <summary>Get a location's translated display name.</summary>
        private static string GetLocationDisplayName(string locationName, LocationData data)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(data.DisplayName))
                {
                    string parsed = TokenParser.ParseText(data.DisplayName);
                    if (!string.IsNullOrWhiteSpace(parsed))
                        return parsed;
                }

                string? fromInstance = Game1.getLocationFromName(locationName)?.DisplayName;
                if (!string.IsNullOrWhiteSpace(fromInstance))
                    return fromInstance;
            }
            catch
            {
                // fall through to raw name
            }
            return locationName;
        }

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
