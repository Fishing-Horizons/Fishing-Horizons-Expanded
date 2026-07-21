using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FishingHorizonsExpanded.Framework.Journal;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.Menus;
using StardewValley.Tools;

namespace FishingHorizonsExpanded.Framework.Assistant
{
    /// <summary>Whether (and when) a fish can bite at the current location.</summary>
    internal enum CatchStatus
    {
        /// <summary>All conditions are met right now.</summary>
        BitingNow,

        /// <summary>Only the time of day doesn't match — it will (or did) bite today.</summary>
        LaterToday,

        /// <summary>The season, weather or another requirement doesn't match today.</summary>
        NotToday,
    }

    /// <summary>One fish shown on the Angler's Sense panel.</summary>
    internal sealed class AssistantFish
    {
        /// <summary>The journal fish entry (sprite, names, parsed Data/Fish info).</summary>
        public FishEntry Fish { get; }

        /// <summary>The best catch status across all spawn entries for this fish.</summary>
        public CatchStatus Status { get; set; } = CatchStatus.NotToday;

        /// <summary>The catch time windows as display strings (like "18:00–2:00"), empty if all day.</summary>
        public List<string> TimeWindows { get; } = new();

        /// <summary>The translated reasons why the fish can't bite today (for <see cref="CatchStatus.NotToday"/>).</summary>
        public List<string> Reasons { get; } = new();

        public AssistantFish(FishEntry fish)
        {
            this.Fish = fish;
        }
    }

    /// <summary>Builds and draws the Angler's Sense HUD panel: which fish can bite at the current location right now, later today, or not today.</summary>
    internal sealed class AssistantOverlay
    {
        /*********
        ** Constants
        *********/
        /// <summary>The size of one fish cell in the grid.</summary>
        private const int CellSize = 56;

        /// <summary>The number of fish cells per grid row.</summary>
        private const int CellsPerRow = 5;

        /// <summary>The extra cell height in the "later today" section (for the time window text).</summary>
        private const int TimeTextHeight = 20;

        /// <summary>The horizontal panel padding.</summary>
        private const int Padding = 20;

        /// <summary>The qualified item ID of magic bait.</summary>
        private const string MagicBaitId = "(O)908";

        /// <summary>Matches season names inside a game state query condition.</summary>
        private static readonly Regex SeasonRegex = new("\\b(spring|summer|fall|winter)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);


        /*********
        ** Fields
        *********/
        /// <summary>The mod translations.</summary>
        private readonly ITranslationHelper I18n;

        /// <summary>The mod monitor for error logging.</summary>
        private readonly IMonitor Monitor;

        /// <summary>The cached fish entries parsed from <c>Data/Fish</c> (null = not a rod fish, skip).</summary>
        private readonly Dictionary<string, FishEntry?> FishCache = new();

        /// <summary>The location data key the panel was last built for.</summary>
        private string? CachedLocationKey;

        /// <summary>The in-game day the panel was last built on.</summary>
        private long CachedDay = -1;

        /// <summary>The time of day the statuses were last computed for.</summary>
        private int CachedTime = -1;

        /// <summary>Whether magic bait was equipped when the statuses were last computed.</summary>
        private bool CachedMagicBait;

        /// <summary>The spawn entries for the cached location (fish entry + its spawn data rows).</summary>
        private readonly Dictionary<string, (FishEntry Fish, List<SpawnFishData> Spawns)> LocationFish = new();

        /// <summary>The fish to show, grouped by status (rebuilt when the time or location changes).</summary>
        private readonly List<AssistantFish> Rows = new();


        /*********
        ** Public methods
        *********/
        public AssistantOverlay(ITranslationHelper i18n, IMonitor monitor)
        {
            this.I18n = i18n;
            this.Monitor = monitor;
        }

        /// <summary>Clear all cached data (call when the save is unloaded or assets change).</summary>
        public void InvalidateCache()
        {
            this.FishCache.Clear();
            this.LocationFish.Clear();
            this.Rows.Clear();
            this.CachedLocationKey = null;
            this.CachedDay = -1;
            this.CachedTime = -1;
        }

        /// <summary>Draw the panel for the current location. Returns without drawing if the location has no rod fish.</summary>
        public void Draw(SpriteBatch b)
        {
            try
            {
                GameLocation location = Game1.currentLocation;
                if (location is null)
                    return;

                this.RebuildIfNeeded(location);
                if (this.Rows.Count == 0)
                    return;

                this.DrawPanel(b, location);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce($"Failed drawing the Angler's Sense panel.\n{ex}", LogLevel.Warn);
            }
        }


        /*********
        ** Private methods — data
        *********/
        /// <summary>Rebuild the cached spawn list and statuses if the location, day or time changed.</summary>
        private void RebuildIfNeeded(GameLocation location)
        {
            string key = GetLocationDataKey(location);
            long day = Game1.stats.DaysPlayed;
            bool magicBait = HasMagicBait();

            if (key != this.CachedLocationKey || day != this.CachedDay)
            {
                this.CachedLocationKey = key;
                this.CachedDay = day;
                this.CachedTime = -1;
                this.BuildLocationFish(key);
            }

            if (Game1.timeOfDay != this.CachedTime || magicBait != this.CachedMagicBait)
            {
                this.CachedTime = Game1.timeOfDay;
                this.CachedMagicBait = magicBait;
                this.ComputeStatuses(location, magicBait);
            }
        }

        /// <summary>Collect the rod fish spawn entries for a location data key.</summary>
        private void BuildLocationFish(string key)
        {
            this.LocationFish.Clear();

            Dictionary<string, LocationData> locations;
            try
            {
                locations = Game1.content.Load<Dictionary<string, LocationData>>("Data\\Locations");
            }
            catch
            {
                return;
            }

            if (!locations.TryGetValue(key, out LocationData? data) || data?.Fish is null)
                return;

            foreach (SpawnFishData spawn in data.Fish)
            {
                string? itemId = spawn.ItemId;
                if (string.IsNullOrWhiteSpace(itemId))
                    continue;

                string unqualified = itemId.StartsWith("(O)", StringComparison.OrdinalIgnoreCase) ? itemId[3..] : itemId;
                if (!this.FishCache.TryGetValue(unqualified, out FishEntry? entry))
                    this.FishCache[unqualified] = entry = FishRegistry.TryCreateEntry(unqualified);

                if (entry is null || entry.IsTrapFish)
                    continue; // not a fishing-rod fish (trash, algae, trap creature)

                if (!this.LocationFish.TryGetValue(entry.QualifiedId, out var row))
                    this.LocationFish[entry.QualifiedId] = row = (entry, new List<SpawnFishData>());
                row.Spawns.Add(spawn);
            }
        }

        /// <summary>Compute the catch status of every fish for the current time and weather.</summary>
        private void ComputeStatuses(GameLocation location, bool magicBait)
        {
            this.Rows.Clear();

            foreach ((FishEntry fish, List<SpawnFishData> spawns) in this.LocationFish.Values)
            {
                var row = new AssistantFish(fish);
                var reasons = new HashSet<string>();

                foreach (SpawnFishData spawn in spawns)
                {
                    CatchStatus status = this.GetSpawnStatus(location, fish, spawn, magicBait, reasons);
                    if (status < row.Status)
                        row.Status = status; // BitingNow < LaterToday < NotToday
                }

                if (fish.TimeRanges.Count > 0)
                {
                    foreach (Point range in fish.TimeRanges)
                        row.TimeWindows.Add($"{FormatTime(range.X)}–{FormatTime(range.Y)}");
                }

                if (row.Status == CatchStatus.NotToday)
                    row.Reasons.AddRange(reasons);

                this.Rows.Add(row);
            }

            // uncaught fish first within each group, then by name
            this.Rows.Sort((a, b) =>
            {
                int byStatus = a.Status.CompareTo(b.Status);
                if (byStatus != 0)
                    return byStatus;
                int byCaught = a.Fish.IsCaught().CompareTo(b.Fish.IsCaught());
                if (byCaught != 0)
                    return byCaught;
                return string.Compare(a.Fish.Data.DisplayName, b.Fish.Data.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        /// <summary>Classify one spawn entry for the current moment, collecting translated "not today" reasons.</summary>
        private CatchStatus GetSpawnStatus(GameLocation location, FishEntry fish, SpawnFishData spawn, bool magicBait, HashSet<string> reasons)
        {
            bool notToday = false;

            // fishing level requirement
            if (spawn.MinFishingLevel > 0 && Game1.player.FishingLevel < spawn.MinFishingLevel)
            {
                reasons.Add(this.I18n.Get("hud.assistant.reason.level", new { level = spawn.MinFishingLevel }));
                notToday = true;
            }

            // magic bait requirement
            if (spawn.RequireMagicBait && !magicBait)
            {
                reasons.Add(this.I18n.Get("hud.assistant.reason.magic-bait"));
                notToday = true;
            }

            // explicit season on the spawn entry
            string currentSeason = location.GetSeasonKey();
            if (spawn.Season.HasValue && !spawn.Season.Value.ToString().Equals(currentSeason, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(this.GetSeasonReason(new[] { spawn.Season.Value.ToString().ToLowerInvariant() }));
                notToday = true;
            }

            // game state query condition (flags, seasons, weather, ...)
            if (!string.IsNullOrWhiteSpace(spawn.Condition) && !GameStateQuery.CheckConditions(spawn.Condition, location, Game1.player))
            {
                // explain season conditions in a readable way; anything else gets a generic hint
                var conditionSeasons = SeasonRegex.Matches(spawn.Condition).Select(m => m.Value.ToLowerInvariant()).Distinct().ToArray();
                if (conditionSeasons.Length > 0 && !conditionSeasons.Contains(currentSeason, StringComparer.OrdinalIgnoreCase))
                    reasons.Add(this.GetSeasonReason(conditionSeasons));
                else
                    reasons.Add(this.I18n.Get("hud.assistant.reason.special"));
                notToday = true;
            }

            // Data/Fish requirements (magic bait ignores all of them)
            bool checkFishData = !spawn.IgnoreFishDataRequirements && !magicBait;
            bool timeOk = true;
            if (checkFishData)
            {
                // season
                if (fish.Seasons.Count > 0 && !fish.Seasons.Contains(currentSeason))
                {
                    reasons.Add(this.GetSeasonReason(fish.Seasons));
                    notToday = true;
                }

                // weather
                bool raining = Game1.IsRainingHere(location);
                if (fish.Weather == "rainy" && !raining)
                {
                    reasons.Add(this.I18n.Get("hud.assistant.reason.rain"));
                    notToday = true;
                }
                else if (fish.Weather == "sunny" && raining)
                {
                    reasons.Add(this.I18n.Get("hud.assistant.reason.sun"));
                    notToday = true;
                }

                // time of day
                timeOk = fish.TimeRanges.Count == 0 || fish.TimeRanges.Any(range => Game1.timeOfDay >= range.X && Game1.timeOfDay < range.Y);
            }

            if (notToday)
                return CatchStatus.NotToday;
            return timeOk ? CatchStatus.BitingNow : CatchStatus.LaterToday;
        }

        /// <summary>Get the translated "only in season X" reason line.</summary>
        private string GetSeasonReason(IEnumerable<string> seasons)
        {
            string names = string.Join(", ", seasons
                .OrderBy(s => Array.IndexOf(new[] { "spring", "summer", "fall", "winter" }, s))
                .Select(s => (string)this.I18n.Get($"menu.journal.detail.season.{s}")));
            return this.I18n.Get("hud.assistant.reason.season", new { seasons = names });
        }

        /// <summary>Whether the player's fishing rod currently has magic bait attached.</summary>
        private static bool HasMagicBait()
        {
            try
            {
                return Game1.player?.CurrentTool is FishingRod rod && rod.GetBait()?.QualifiedItemId == MagicBaitId;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Get the <c>Data/Locations</c> key for a location instance (mine floors and farm types map to their shared entry).</summary>
        private static string GetLocationDataKey(GameLocation location)
        {
            string name = location.Name ?? "";

            // every mine floor is its own location "UndergroundMine{level}", but the data lives under "UndergroundMine"
            if (name.StartsWith("UndergroundMine", StringComparison.OrdinalIgnoreCase))
                return "UndergroundMine";

            // the farm's fish data lives under "Farm_{type}"
            if (location is Farm)
            {
                try
                {
                    string farmKey = $"Farm_{Game1.GetFarmTypeKey()}";
                    var locations = Game1.content.Load<Dictionary<string, LocationData>>("Data\\Locations");
                    if (locations.ContainsKey(farmKey))
                        return farmKey;
                }
                catch
                {
                    // fall through to the plain name
                }
                return "Farm_Standard";
            }

            return name;
        }

        /// <summary>Format a game time value (like 1830) as a clock string (like "18:30").</summary>
        private static string FormatTime(int time)
        {
            int hours = time / 100;
            int minutes = time % 100;
            if (hours >= 24)
                hours -= 24;
            return $"{hours}:{minutes:00}";
        }


        /*********
        ** Private methods — drawing
        *********/
        /// <summary>Draw the panel background, sections and tooltip.</summary>
        private void DrawPanel(SpriteBatch b, GameLocation location)
        {
            var sections = new List<(string Title, List<AssistantFish> Fish)>();
            void AddSection(string key, CatchStatus status)
            {
                var fish = this.Rows.Where(row => row.Status == status).ToList();
                if (fish.Count > 0)
                    sections.Add(((string)this.I18n.Get(key), fish));
            }
            AddSection("hud.assistant.now", CatchStatus.BitingNow);
            AddSection("hud.assistant.later", CatchStatus.LaterToday);
            AddSection("hud.assistant.not-today", CatchStatus.NotToday);
            if (sections.Count == 0)
                return;

            int headerHeight = (int)Game1.smallFont.MeasureString("Ag").Y + 6;
            int width = Padding * 2 + CellsPerRow * CellSize;

            // panel title: the location's display name
            string panelTitle = location.DisplayName ?? location.Name ?? "";
            int titleHeight = string.IsNullOrWhiteSpace(panelTitle) ? 0 : headerHeight + 8;

            // measure
            int height = Padding + titleHeight;
            foreach ((string title, List<AssistantFish> fish) in sections)
            {
                int rows = (fish.Count + CellsPerRow - 1) / CellsPerRow;
                int cellHeight = CellSize + (fish[0].Status == CatchStatus.LaterToday ? TimeTextHeight : 0);
                height += headerHeight + rows * cellHeight + 8;
            }
            height += Padding - 8;

            int x = 16;
            int y = Math.Max(16, (Game1.uiViewport.Height - height) / 3);

            IClickableMenu.drawTextureBox(b, x, y, width, height, Color.White);

            // sections
            AssistantFish? hovered = null;
            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();
            int drawY = y + Padding;

            if (titleHeight > 0)
            {
                string fitted = panelTitle;
                while (fitted.Length > 3 && Game1.smallFont.MeasureString(fitted).X > width - 2 * Padding)
                    fitted = fitted[..^4] + "…";
                float titleWidth = Game1.smallFont.MeasureString(fitted).X;
                Utility.drawTextWithShadow(b, fitted, Game1.smallFont, new Vector2((int)(x + (width - titleWidth) / 2), drawY), new Color(86, 22, 12));
                drawY += titleHeight;
            }
            foreach ((string title, List<AssistantFish> fish) in sections)
            {
                Utility.drawTextWithShadow(b, title, Game1.smallFont, new Vector2(x + Padding, drawY), Game1.textColor);
                drawY += headerHeight;

                int cellHeight = CellSize + (fish[0].Status == CatchStatus.LaterToday ? TimeTextHeight : 0);
                for (int i = 0; i < fish.Count; i++)
                {
                    int cellX = x + Padding + (i % CellsPerRow) * CellSize;
                    int cellY = drawY + (i / CellsPerRow) * cellHeight;
                    var cell = new Rectangle(cellX, cellY, CellSize, cellHeight);
                    if (cell.Contains(mouseX, mouseY))
                        hovered = fish[i];

                    this.DrawFishCell(b, fish[i], cellX, cellY, hovered == fish[i]);
                }
                drawY += ((fish.Count + CellsPerRow - 1) / CellsPerRow) * cellHeight + 8;
            }

            if (hovered is not null)
                IClickableMenu.drawHoverText(b, this.GetTooltip(hovered), Game1.smallFont);
        }

        /// <summary>Draw one fish cell: the sprite (silhouette until caught), a "new" marker, and the time window in the "later" section.</summary>
        private void DrawFishCell(SpriteBatch b, AssistantFish row, int x, int y, bool hovered)
        {
            bool caught = row.Fish.IsCaught();
            float scale = hovered ? 3.3f : 3f;
            var center = new Vector2(x + CellSize / 2f, y + CellSize / 2f);

            Color color = caught ? Color.White : Color.Black * 0.85f;
            if (row.Status == CatchStatus.NotToday)
                color *= 0.4f;

            b.Draw(row.Fish.Data.GetTexture(), center, row.Fish.Data.GetSourceRect(), color, 0f, new Vector2(8f, 8f), scale, SpriteEffects.None, 1f);

            // gold "!" for fish the player never caught but which bites now
            if (!caught && row.Status == CatchStatus.BitingNow)
                b.Draw(Game1.mouseCursors, new Vector2(x + CellSize - 16, y + 2), new Rectangle(403, 496, 5, 14), Color.White, 0f, Vector2.Zero, 2f, SpriteEffects.None, 1f);

            // exact time window under the sprite in the "later today" section
            if (row.Status == CatchStatus.LaterToday && row.TimeWindows.Count > 0)
            {
                string text = row.TimeWindows[0];
                float textScale = 0.7f;
                Vector2 size = Game1.smallFont.MeasureString(text) * textScale;
                b.DrawString(Game1.smallFont, text, new Vector2((int)(x + (CellSize - size.X) / 2), y + CellSize - 2), Game1.textColor * 0.8f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);
            }
        }

        /// <summary>Build the hover tooltip for a fish: name and stats if caught, otherwise "???" plus readable hints.</summary>
        private string GetTooltip(AssistantFish row)
        {
            bool caught = row.Fish.IsCaught();
            var text = new StringBuilder();

            text.AppendLine(caught ? row.Fish.Data.DisplayName : (string)this.I18n.Get("menu.journal.unknown-fish"));

            if (caught)
                text.AppendLine((string)this.I18n.Get("menu.journal.caught-count", new { count = row.Fish.TimesCaught() }));
            else
                text.AppendLine((string)this.I18n.Get("hud.assistant.not-caught"));

            switch (row.Status)
            {
                case CatchStatus.BitingNow:
                    text.AppendLine((string)this.I18n.Get("hud.assistant.tip.now"));
                    break;

                case CatchStatus.LaterToday:
                    text.AppendLine((string)this.I18n.Get("hud.assistant.tip.time", new { windows = string.Join(", ", row.TimeWindows) }));
                    break;

                case CatchStatus.NotToday:
                    foreach (string reason in row.Reasons)
                        text.AppendLine(reason);
                    break;
            }

            return text.ToString().TrimEnd();
        }
    }
}
