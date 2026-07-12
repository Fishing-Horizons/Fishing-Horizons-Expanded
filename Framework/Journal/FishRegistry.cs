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
        public int MinPossibleSize { get; set; }

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

        /// <summary>The internal location names by display name (for world map markers).</summary>
        public Dictionary<string, string> LocationInternalNames { get; } = new();


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

        /// <summary>Whether a specific player has ever caught this fish.</summary>
        public bool IsCaughtBy(Farmer farmer)
        {
            return farmer.fishCaught.ContainsKey(this.QualifiedId);
        }

        /// <summary>The biggest size (in inches) a specific player caught, or 0 if they never caught it.</summary>
        public int MaxCaughtSizeFor(Farmer farmer)
        {
            return farmer.fishCaught.TryGetValue(this.QualifiedId, out int[]? stats) && stats.Length > 1
                ? stats[1]
                : 0;
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

    /// <summary>How the journal's fish list is grouped into sections.</summary>
    internal enum JournalSortMode
    {
        /// <summary>Group by source mod (vanilla first, then one section per mod).</summary>
        Mods,

        /// <summary>Group by catch location (one section per location). A fish caught in several locations appears in each.</summary>
        Locations,
    }

    /// <summary>A journal section: a group of fish from one source (vanilla or a specific mod).</summary>
    internal sealed class JournalSection
    {
        /*********
        ** Accessors
        *********/
        /// <summary>The translated section title.</summary>
        public string Title { get; }

        /// <summary>The short label shown on the section's bookmark tab.</summary>
        public string TabLabel { get; }

        /// <summary>The fish in this section.</summary>
        public List<FishEntry> Fish { get; }


        /*********
        ** Public methods
        *********/
        public JournalSection(string title, List<FishEntry> fish, string? tabLabel = null)
        {
            this.Title = title;
            this.TabLabel = tabLabel ?? title;
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

        /// <summary>Vanilla <c>Data/Fish</c> entries that aren't caught with the fishing minigame (algae/seaweed and trash), excluded from the journal.</summary>
        private static readonly HashSet<string> NonMinigameIds = new() { "152", "153", "157", "167", "168", "169", "170", "171", "172", "173" };

        /// <summary>The mod translations, used for fallback location names (set by <see cref="BuildSections"/>).</summary>
        private static ITranslationHelper? I18n;

        /// <summary>Internal/technical location keys in <c>Data/Locations</c> that must never show up as catch locations.</summary>
        private static readonly HashSet<string> HiddenLocations = new(StringComparer.OrdinalIgnoreCase) { "Default", "fishingGame", "Temp" };


        /// <summary>The vanilla fishing locations in journal display order. Locations not listed here (like mod locations) are appended alphabetically.</summary>
        private static readonly string[] LocationOrder = { "Town", "Forest", "Mountain", "Beach", "BeachNightMarket", "Submarine", "Woods", "Backwoods", "Railroad", "Desert", "UndergroundMine", "Sewer", "BugLand", "WitchSwamp", "IslandSouth", "IslandSouthEast", "IslandSouthEastCave", "IslandSecret", "IslandWest", "IslandNorth", "Caldera", "Farm" };

        /// <summary>The resolved display names of locations with fish (internal name → display name), filled by <see cref="PopulateLocations"/>.</summary>
        private static readonly Dictionary<string, string> LocationTitles = new(StringComparer.OrdinalIgnoreCase);


        /*********
        ** Public methods
        *********/
        /// <summary>Build the journal sections grouped by the given sort mode.</summary>
        /// <param name="modRegistry">The SMAPI mod registry, used to resolve mod display names.</param>
        /// <param name="vanillaSectionTitle">The translated title of the vanilla section.</param>
        public static List<JournalSection> BuildSections(IModRegistry modRegistry, string vanillaSectionTitle, ITranslationHelper? i18n = null, JournalSortMode sortMode = JournalSortMode.Mods)
        {
            I18n = i18n;
            var fishData = Game1.content.Load<Dictionary<string, string>>("Data\\Fish");
            var bySource = new Dictionary<string, List<FishEntry>>(); // key: "" for vanilla, else mod ID or raw prefix
            var byQualifiedId = new Dictionary<string, FishEntry>();

            foreach ((string id, string rawData) in fishData)
            {
                ParsedItemData data = ItemRegistry.GetData(ItemRegistry.type_object + id);
                if (data is null || data.IsErrorItem)
                    continue;

                // skip items that aren't caught with the fishing minigame (algae, seaweed, trash)
                if (NonMinigameIds.Contains(id) || data.Category == StardewValley.Object.junkCategory)
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

            LocationTitles.Clear();
            PopulateLocations(byQualifiedId);

            return sortMode == JournalSortMode.Locations
                ? BuildLocationSections(byQualifiedId.Values)
                : BuildModSections(modRegistry, vanillaSectionTitle, bySource);
        }


        /*********
        ** Private methods — grouping
        *********/
        /// <summary>Build the sections for the "mods" sort: vanilla fish first, then one section per mod.</summary>
        private static List<JournalSection> BuildModSections(IModRegistry modRegistry, string vanillaSectionTitle, Dictionary<string, List<FishEntry>> bySource)
        {
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

        /// <summary>Build the sections for the "locations" sort: one section per catch location, then crab pot fish, then fish without location data. A fish caught in several locations appears in each of them.</summary>
        private static List<JournalSection> BuildLocationSections(IEnumerable<FishEntry> allFish)
        {
            var byLocation = new Dictionary<string, List<FishEntry>>(StringComparer.OrdinalIgnoreCase);
            var traps = new List<FishEntry>();
            var other = new List<FishEntry>();

            foreach (FishEntry fish in allFish)
            {
                var locationNames = new HashSet<string>(fish.LocationInternalNames.Values, StringComparer.OrdinalIgnoreCase);
                if (locationNames.Count == 0)
                {
                    (fish.IsTrapFish ? traps : other).Add(fish);
                    continue;
                }

                foreach (string name in locationNames)
                {
                    if (!byLocation.TryGetValue(name, out List<FishEntry>? list))
                        byLocation[name] = list = new List<FishEntry>();
                    list.Add(fish);
                }
            }

            // vanilla locations in their fixed order, then anything else (like mod locations) alphabetically by title
            var orderedNames = new List<string>(LocationOrder.Where(byLocation.ContainsKey));
            orderedNames.AddRange(
                byLocation.Keys
                    .Where(name => !LocationOrder.Contains(name, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(GetLocationTitle, StringComparer.OrdinalIgnoreCase)
            );

            var sections = new List<JournalSection>();
            foreach (string name in orderedNames)
            {
                string title = GetLocationTitle(name);
                sections.Add(new JournalSection(title, Sorted(byLocation[name]), GetTabLabel(name, title)));
            }
            if (traps.Count > 0)
                sections.Add(new JournalSection(GetTranslation("menu.journal.section.traps", "Crab Pot Catch"), Sorted(traps), GetTranslation("menu.journal.tab.traps", "Traps")));
            if (other.Count > 0)
                sections.Add(new JournalSection(GetTranslation("menu.journal.section.other", "Other Waters"), Sorted(other), GetTranslation("menu.journal.tab.other", "Other")));
            return sections;
        }

        /// <summary>Get the full display title for a location section.</summary>
        private static string GetLocationTitle(string internalName)
        {
            return LocationTitles.TryGetValue(internalName, out string? title) ? title : internalName;
        }

        /// <summary>Get the short bookmark tab label for a location (via <c>menu.journal.tab.*</c>), falling back to its full title.</summary>
        private static string GetTabLabel(string internalName, string fullTitle)
        {
            if (I18n is not null)
            {
                Translation label = I18n.Get($"menu.journal.tab.{internalName}");
                if (label.HasValue())
                    return label;
            }
            return fullTitle;
        }

        /// <summary>Get a translation with a hardcoded fallback if the key is missing.</summary>
        private static string GetTranslation(string key, string fallback)
        {
            if (I18n is not null)
            {
                Translation translation = I18n.Get(key);
                if (translation.HasValue())
                    return translation;
            }
            return fallback;
        }


        /*********
        ** Private methods
        *********/
        /// 
        private static void ParseFishFields(FishEntry entry, string[] fields)
        {
            try
            {
                if (entry.IsTrapFish)
                {
                    // Name/trap/Chance/ExtraItems/Location/MinSize/MaxSize
                    if (fields.Length > 4)
                        entry.TrapWaterType = fields[4];
                    if (fields.Length > 5 && int.TryParse(fields[5], out int trapMin))
                        entry.MinPossibleSize = trapMin;
                    if (fields.Length > 6 && int.TryParse(fields[6], out int trapMax))
                        entry.MaxPossibleSize = trapMax;
                    return;
                }

                // Name/Difficulty/Behavior/MinSize/MaxSize/Time/Seasons/Weather/...
                if (fields.Length > 3 && int.TryParse(fields[3], out int rodMin))
                    entry.MinPossibleSize = rodMin;
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
                if (HiddenLocations.Contains(locationName) || locationData.Fish is null)
                    continue;

                string? baseDisplayName = null;

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

                    baseDisplayName ??= GetLocationDisplayName(locationName, locationData);
                    LocationTitles[locationName] = baseDisplayName;

                    // append the fish area name if the spawn is limited to a named sub-area (like the forest pond vs the river)
                    string displayName = baseDisplayName;
                    string? areaName = GetFishAreaDisplayName(locationData, spawn.FishAreaId);
                    if (areaName is not null)
                        displayName = $"{baseDisplayName} ({areaName})";

                    entry.LocationInternalNames[displayName] = locationName;
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

        /// <summary>Get the translated display name of a fish area within a location, or null if the area is unknown or unnamed.</summary>
        private static string? GetFishAreaDisplayName(LocationData data, string? fishAreaId)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(fishAreaId)
                    && data.FishAreas is not null
                    && data.FishAreas.TryGetValue(fishAreaId, out FishAreaData? area)
                    && !string.IsNullOrWhiteSpace(area?.DisplayName))
                {
                    string parsed = TokenParser.ParseText(area.DisplayName);
                    if (!string.IsNullOrWhiteSpace(parsed))
                        return parsed;
                }
            }
            catch
            {
                // treat malformed area data as unnamed
            }
            return null;
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
            }
            catch
            {
                // fall through to the fallback name
            }

            // some vanilla locations (like the Ginger Island areas or the Witch's Swamp) have no display name
            // in Data/Locations, so check the mod translations before asking the location instance: the game's
            // GameLocation.DisplayName falls back to the raw internal name (like "IslandWest"), which would
            // otherwise hide these translations
            if (I18n is not null)
            {
                Translation fallback = I18n.Get($"menu.journal.location.{locationName}");
                if (fallback.HasValue())
                    return fallback;
            }

            try
            {
                string? fromInstance = Game1.getLocationFromName(locationName)?.DisplayName;
                if (!string.IsNullOrWhiteSpace(fromInstance) && !fromInstance.Equals(locationName, StringComparison.Ordinal))
                    return fromInstance;
            }
            catch
            {
                // fall through to the raw name
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
