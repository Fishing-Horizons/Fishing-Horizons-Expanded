using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace FishingHorizonsExpanded.Framework.CrabPots
{
    /// <summary>Persists crab pots placed on mine floors and processes their daily catches.</summary>
    /// <remarks>
    /// Mine floors are transient (<see cref="MineShaft"/> instances are discarded on exit and regular floor
    /// layouts are re-randomized per visit), so pots placed there would silently vanish in vanilla. This manager:
    ///
    /// <list type="number">
    /// <item>records pots placed in the mines (via <c>ObjectListChanged</c>) into per-save mod data;</item>
    /// <item>re-materializes them when the player enters their floor (<c>Warped</c>) — at their original tile if
    /// it's still water, otherwise at the nearest water tile of the regenerated layout;</item>
    /// <item>rolls catches at <c>DayStarted</c> even while the floor doesn't exist, using <c>Data/Fish</c> trap
    /// entries whose water type is <c>Cave</c> (floors outside 80–119) or <c>Lava</c> (floors 80–119), with a
    /// cave/lava jelly fallback so pots work before any content pack adds such fish.</item>
    /// </list>
    ///
    /// Known limitations of this first version (flagged for review): only the host tracks pots (farmhand pots in
    /// the mines are not persisted), the Volcano Dungeon is not covered (separate location class), and quality is
    /// always normal like vanilla trap catches.
    /// </remarks>
    internal sealed class CaveCrabPotManager
    {
        /*********
        ** Fields
        *********/
        /// <summary>The per-save data key for the pot records.</summary>
        private const string SaveDataKey = "cave-crab-pots";

        /// <summary>The mod instance.</summary>
        private readonly ModEntry Mod;

        /// <summary>Get whether the module is currently enabled.</summary>
        private readonly Func<bool> IsEnabled;

        /// <summary>Get whether lava floors (80–119) are enabled for crab pots.</summary>
        private readonly Func<bool> LavaEnabled;

        /// <summary>The tracked pots, keyed by mine level.</summary>
        private List<SavedCavePot> Pots = new();

        /// <summary>Whether the manager is currently re-adding saved pots (so <c>ObjectListChanged</c> must ignore them).</summary>
        private bool IsRestoring;


        /*********
        ** Public methods
        *********/
        public CaveCrabPotManager(ModEntry mod, Func<bool> isEnabled, Func<bool> lavaEnabled)
        {
            this.Mod = mod;
            this.IsEnabled = isEnabled;
            this.LavaEnabled = lavaEnabled;
        }

        /// <summary>Hook up the event handlers.</summary>
        public void Activate(IModHelper helper)
        {
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.Saving += this.OnSaving;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.Pots = new();
            helper.Events.Player.Warped += this.OnWarped;
            helper.Events.World.ObjectListChanged += this.OnObjectListChanged;
        }


        /*********
        ** Private methods — event handlers
        *********/
        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;
            this.Pots = this.Mod.Helper.Data.ReadSaveData<List<SavedCavePot>>(SaveDataKey) ?? new();
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;
            if (Game1.currentLocation is MineShaft mine)
                this.SyncFromLocation(mine);
            this.Mod.Helper.Data.WriteSaveData(SaveDataKey, this.Pots);
        }

        /// <summary>At day start, roll a catch for every tracked pot (the floors don't exist overnight, so vanilla <c>CrabPot.DayUpdate</c> can't do it).</summary>
        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer || !this.IsEnabled())
                return;

            try
            {
                foreach (SavedCavePot pot in this.Pots)
                {
                    if (pot.HeldId is not null)
                        continue; // still waiting to be collected
                    if (pot.BaitId is null && !this.OwnerHasLuremaster(pot.OwnerId))
                        continue; // no bait

                    (string id, int quality)? catchResult = this.RollCatch(pot);
                    if (catchResult is not null)
                    {
                        pot.HeldId = catchResult.Value.id;
                        pot.HeldQuality = catchResult.Value.quality;
                        pot.BaitId = null; // bait is consumed by a catch, like vanilla
                    }
                }
            }
            catch (Exception ex)
            {
                this.Mod.Monitor.Log($"Failed processing cave crab pot catches.\n{ex}", LogLevel.Warn);
            }
        }

        /// <summary>When the player changes location: sync records from the floor they left, spawn saved pots on the floor they entered.</summary>
        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (!Context.IsMainPlayer || !this.IsEnabled() || !e.IsLocalPlayer)
                return;

            try
            {
                if (e.OldLocation is MineShaft oldMine)
                    this.SyncFromLocation(oldMine);

                if (e.NewLocation is MineShaft newMine)
                    this.SpawnPotsIn(newMine);
            }
            catch (Exception ex)
            {
                this.Mod.Monitor.Log($"Failed restoring cave crab pots.\n{ex}", LogLevel.Warn);
            }
        }

        /// <summary>Track pots the player places in or removes from the mines.</summary>
        private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
        {
            if (!Context.IsMainPlayer || !this.IsEnabled() || this.IsRestoring || e.Location is not MineShaft mine)
                return;

            foreach (KeyValuePair<Vector2, SObject> pair in e.Added)
            {
                if (pair.Value is CrabPot pot)
                {
                    this.Pots.Add(new SavedCavePot
                    {
                        MineLevel = mine.mineLevel,
                        TileX = (int)pair.Key.X,
                        TileY = (int)pair.Key.Y,
                        OwnerId = pot.owner.Value != 0 ? pot.owner.Value : Game1.player.UniqueMultiplayerID,
                        BaitId = pot.bait.Value?.QualifiedItemId
                    });
                }
            }

            foreach (KeyValuePair<Vector2, SObject> pair in e.Removed)
            {
                if (pair.Value is CrabPot)
                    this.Pots.RemoveAll(p => p.MineLevel == mine.mineLevel && p.TileX == (int)pair.Key.X && p.TileY == (int)pair.Key.Y);
            }
        }


        /*********
        ** Private methods — pot lifecycle
        *********/
        /// <summary>Replace the records for a floor with a snapshot of its live pots (captures bait changes and collected catches).</summary>
        private void SyncFromLocation(MineShaft mine)
        {
            this.Pots.RemoveAll(p => p.MineLevel == mine.mineLevel);
            foreach (KeyValuePair<Vector2, SObject> pair in mine.objects.Pairs)
            {
                if (pair.Value is not CrabPot pot)
                    continue;

                this.Pots.Add(new SavedCavePot
                {
                    MineLevel = mine.mineLevel,
                    TileX = (int)pair.Key.X,
                    TileY = (int)pair.Key.Y,
                    OwnerId = pot.owner.Value,
                    BaitId = pot.bait.Value?.QualifiedItemId,
                    HeldId = pot.heldObject.Value?.QualifiedItemId,
                    HeldQuality = pot.heldObject.Value?.Quality ?? 0
                });
            }
        }

        /// <summary>Re-materialize the saved pots on a freshly generated mine floor.</summary>
        private void SpawnPotsIn(MineShaft mine)
        {
            this.IsRestoring = true;
            try
            {
                foreach (SavedCavePot saved in this.Pots.Where(p => p.MineLevel == mine.mineLevel).ToList())
                {
                    Vector2? tile = this.FindWaterTile(mine, new Vector2(saved.TileX, saved.TileY));
                    if (tile is null)
                        continue; // no water on this layout roll — the pot stays virtual and keeps catching

                    saved.TileX = (int)tile.Value.X;
                    saved.TileY = (int)tile.Value.Y;

                    var pot = new CrabPot();
                    pot.TileLocation = tile.Value;
                    pot.owner.Value = saved.OwnerId;
                    if (saved.BaitId is not null)
                        pot.bait.Value = ItemRegistry.Create<SObject>(saved.BaitId);
                    if (saved.HeldId is not null)
                    {
                        SObject held = ItemRegistry.Create<SObject>(saved.HeldId);
                        held.Quality = saved.HeldQuality;
                        pot.heldObject.Value = held;
                        pot.readyForHarvest.Value = true;
                        pot.tileIndexToShow = 714;
                    }
                    mine.objects[tile.Value] = pot;
                }
            }
            finally
            {
                this.IsRestoring = false;
            }
        }

        /// <summary>Find the preferred tile if it's still open water, else the nearest open water tile, else null.</summary>
        private Vector2? FindWaterTile(MineShaft mine, Vector2 preferred)
        {
            bool IsOpenWater(int x, int y) => mine.isWaterTile(x, y) && !mine.objects.ContainsKey(new Vector2(x, y));

            if (IsOpenWater((int)preferred.X, (int)preferred.Y))
                return preferred;

            var layer = mine.Map?.Layers?.FirstOrDefault();
            if (layer is null)
                return null;

            Vector2? best = null;
            float bestDistance = float.MaxValue;
            for (int x = 0; x < layer.LayerWidth; x++)
            {
                for (int y = 0; y < layer.LayerHeight; y++)
                {
                    if (!IsOpenWater(x, y))
                        continue;
                    float distance = Vector2.DistanceSquared(preferred, new Vector2(x, y));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = new Vector2(x, y);
                    }
                }
            }
            return best;
        }


        /*********
        ** Private methods — catch rolls
        *********/
        /// <summary>Roll what a pot catches overnight, mirroring the vanilla trap roll on <c>Data/Fish</c> plus a jelly fallback.</summary>
        private (string id, int quality)? RollCatch(SavedCavePot pot)
        {
            bool isLava = this.IsLavaLevel(pot.MineLevel);
            if (isLava && !this.LavaEnabled())
                return null;
            string waterType = isLava ? "Lava" : "Cave";

            var random = Utility.CreateDaySaveRandom(pot.MineLevel, pot.TileX * 1000 + pot.TileY);

            // Data/Fish trap entries matching this water type (works as soon as a content pack adds Cave/Lava trap fish)
            Dictionary<string, string> fishData = Game1.content.Load<Dictionary<string, string>>("Data\\Fish");
            foreach (KeyValuePair<string, string> entry in fishData)
            {
                string[] fields = entry.Value.Split('/');
                if (fields.Length < 7 || fields[1] != "trap" || fields[4] != waterType)
                    continue;
                if (double.TryParse(fields[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double chance) && random.NextDouble() < chance)
                    return ($"(O){entry.Key}", 0);
            }

            // fallback so cave pots are useful before any such trap fish exist
            if (isLava)
            {
                if (random.NextDouble() < 0.25)
                    return ("(O)LavaJelly", 0);
                if (random.NextDouble() < 0.20)
                    return ("(O)848", 0); // cinder shard
            }
            else
            {
                if (random.NextDouble() < 0.30)
                    return ("(O)CaveJelly", 0);
                if (random.NextDouble() < 0.20)
                    return ("(O)881", 0); // bone fragment
            }

            // junk, like an empty vanilla pot
            return ($"(O){random.Next(168, 173)}", 0);
        }

        /// <summary>Get whether a mine level is in the lava area (floors 80–119).</summary>
        private bool IsLavaLevel(int mineLevel)
        {
            return mineLevel is >= 80 and < 120;
        }

        /// <summary>Get whether a pot's owner has the Luremaster profession (crab pots don't need bait).</summary>
        private bool OwnerHasLuremaster(long ownerId)
        {
            foreach (Farmer farmer in Game1.getAllFarmers())
            {
                if (farmer.UniqueMultiplayerID == ownerId)
                    return farmer.professions.Contains(11); // 11 = Luremaster, same raw ID vanilla CrabPot.DayUpdate checks (the Farmer constant names for 10/11 are swapped in the game code, so the raw ID is safer)
            }
            return false;
        }
    }
}
