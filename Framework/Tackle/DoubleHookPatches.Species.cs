using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace FishingHorizonsExpanded.Framework.Tackle
{
    /// <summary>Deciding what each extra fish is, and handing it to the player once landed.</summary>
    internal static partial class DoubleHookPatches
    {
        /*********
        ** Private methods — species, size and quality
        *********/
        /// <summary>
        /// Decide what an extra fish actually is. Half the time it's another one of whatever is already
        /// on the line; otherwise the location's fish table is rolled again for a different species,
        /// which then gets its own size and quality rather than inheriting the first fish's.
        /// </summary>
        private static void RollExtraSpecies(BobberBar bar, out string? fishId, out int size, out int quality)
        {
            fishId = null;
            size = 0;
            quality = 0;

            try
            {
                if (Game1.random.NextDouble() >= DifferentSpeciesChance)
                    return;
                if (Game1.player?.CurrentTool is not FishingRod rod)
                    return;

                GameLocation? location = Game1.currentLocation;
                if (location == null)
                    return;

                // The table hands back trash and forage as often as fish, so one roll would turn a
                // 50% chance into a much rarer event. A few attempts keep the odds close to the
                // advertised half without looping long enough to matter on a single frame.
                string ownId = ItemRegistry.QualifyItemId(bar.whichFish);
                for (int attempt = 0; attempt < SpeciesRollAttempts; attempt++)
                {
                    Item? candidate = GameLocation.GetFishFromLocationData(
                        location.Name,
                        rod.bobber.Value / 64f,
                        rod.clearWaterDistance,
                        Game1.player,
                        isTutorialCatch: false,
                        isInherited: false,
                        location);

                    if (candidate is not StardewValley.Object obj || obj.Category != StardewValley.Object.FishCategory)
                        continue;

                    // Rolling the fish already on the line just means a plain duplicate.
                    string qualifiedId = candidate.QualifiedItemId;
                    if (qualifiedId == ownId)
                        continue;

                    if (!TryRollSizeAndQuality(bar, rod, qualifiedId, out size, out quality))
                        continue;

                    fishId = qualifiedId;
                    return;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed rolling an extra fish species, falling back to a duplicate:\n{ex}", LogLevel.Warn);
                fishId = null;
            }
        }

        /// <summary>
        /// Roll a size and quality for a different species from scratch, the way the game would have
        /// done had this fish been the one on the hook.
        /// </summary>
        /// <remarks>
        /// The size percentage roll is <c>FishingRod.DoFunction</c>'s, and turning that percentage into
        /// a length and a star rating is <c>BobberBar</c>'s constructor, including the Quality Bobber
        /// and training rod rules. Both are read from this fish's own <c>Data/Fish</c> bounds.
        /// </remarks>
        private static bool TryRollSizeAndQuality(BobberBar bar, FishingRod rod, string qualifiedId, out int size, out int quality)
        {
            size = 0;
            quality = 0;

            string localId = ItemRegistry.GetMetadata(qualifiedId)?.LocalItemId ?? qualifiedId;
            if (!DataLoader.Fish(Game1.content).TryGetValue(localId, out string? rawData))
                return false;

            // Crab-pot entries have a different layout and no size range, so they're skipped.
            string[] fields = rawData.Split('/');
            if (fields.Length < 5
                || !int.TryParse(fields[3], out int minFishSize)
                || !int.TryParse(fields[4], out int maxFishSize))
                return false;

            float percent = rod.clearWaterDistance / 5f;
            int minimumSizeContribution = 1 + Game1.player.FishingLevel / 2;
            percent *= Game1.random.Next(minimumSizeContribution, Math.Max(6, minimumSizeContribution)) / 5f;
            if (rod.favBait)
                percent *= 1.2f;
            percent *= 1f + Game1.random.Next(-10, 11) / 100f;
            percent = Math.Max(0f, Math.Min(1f, percent));

            size = (int)(minFishSize + (maxFishSize - minFishSize) * percent) + 1;
            quality = (percent < 0.33f) ? 0 : ((percent < 0.66f) ? 1 : 2);

            for (int i = 0; i < Utility.getStringCountInList(bar.bobbers, "(O)877"); i++)
            {
                quality++;
                if (quality > 2)
                    quality = 4;
            }

            if (bar.beginnersRod)
            {
                quality = 0;
                size = minFishSize;
            }

            return true;
        }


        /*********
        ** Private methods — awarding
        *********/
        /// <summary>Award extra fish that were secured.</summary>
        private static void BeforePullFishFromWater(ref int numCaught)
        {
            try
            {
                if (!Armed) return;

                // numCaught only ever stacks copies of the species the minigame was built around, so
                // duplicates ride along with it and anything else is handed over separately.
                PendingAwards.Clear();
                CollectExtra(SecondSecured, SecondFishId, SecondFishSize, SecondFishQuality, ref numCaught);
                CollectExtra(ThirdSecured, ThirdFishId, ThirdFishSize, ThirdFishQuality, ref numCaught);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(BeforePullFishFromWater)}:\n{ex}", LogLevel.Error);
            }
            finally
            {
                Reset();
            }

            foreach ((string id, int size, int quality) in PendingAwards)
            {
                try
                {
                    AwardExtraFish(id, size, quality);
                }
                catch (Exception ex)
                {
                    Monitor.Log($"Failed awarding extra fish {id}:\n{ex}", LogLevel.Error);
                }
            }

            PendingAwards.Clear();
        }

        /// <summary>Route one landed extra fish either into vanilla's stack or into the pending awards.</summary>
        private static void CollectExtra(bool secured, string? fishId, int size, int quality, ref int numCaught)
        {
            if (!secured)
                return;

            if (fishId == null)
                numCaught += 1;
            else
                PendingAwards.Add((fishId, size, quality));
        }

        /// <summary>Hand the player a landed fish that isn't the species the minigame was built around.</summary>
        private static void AwardExtraFish(string qualifiedId, int size, int quality)
        {
            Game1.player.caughtFish(qualifiedId, size, from_fish_pond: false, numberCaught: 1);

            Item fish = ItemRegistry.Create(qualifiedId, 1, quality);
            if (!Game1.player.addItemToInventoryBool(fish))
                Game1.createItemDebris(fish, Game1.player.getStandingPosition(), -1);
        }
    }
}
