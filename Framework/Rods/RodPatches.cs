using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Tools;

namespace FishingHorizonsExpanded.Framework.Rods
{
    /// <summary>Harmony patches for the custom rods' special effects.</summary>
    /// <remarks>
    /// Patched method (Stardew Valley 1.6): <see cref="FishingRod.pullFishFromWater"/>.
    ///
    /// The bobber bar computes the catch quality (size, perfect catch, tackle) and passes it to
    /// <c>pullFishFromWater</c>, which stores it on the rod before the caught item is created.
    /// This prefix rewrites that quality for the golden rod: every actual fish (not trash, seaweed
    /// or other junk) is always exactly gold quality — a worse catch is upgraded, an iridium-grade
    /// catch is capped down. That keeps the rod strictly below the iridium tier while making its
    /// gimmick 100% reliable.
    ///
    /// The patch swallows its own exceptions, so a failure can never crash the game.
    /// </remarks>
    internal static class RodPatches
    {
        /*********
        ** Fields
        *********/
        /// <summary>The mod monitor for error logging.</summary>
        private static IMonitor Monitor = null!;

        /// <summary>Get whether the module is currently enabled (checked per call so GMCM toggling works mid-game).</summary>
        private static Func<bool> IsEnabled = () => false;


        /*********
        ** Public methods
        *********/
        /// <summary>Apply the Harmony patches.</summary>
        /// <param name="modId">The unique mod ID (used as the Harmony ID).</param>
        /// <param name="monitor">The mod monitor for error logging.</param>
        /// <param name="isEnabled">Get whether the module is currently enabled.</param>
        public static void Apply(string modId, IMonitor monitor, Func<bool> isEnabled)
        {
            Monitor = monitor;
            IsEnabled = isEnabled;

            var harmony = new Harmony($"{modId}.rods");

            harmony.Patch(
                original: AccessTools.Method(typeof(FishingRod), nameof(FishingRod.pullFishFromWater)),
                prefix: new HarmonyMethod(typeof(RodPatches), nameof(Before_PullFishFromWater))
            );
        }


        /*********
        ** Private methods — patches
        *********/
        /// <summary>Before the rod registers the catch: golden rod fish are always gold quality.</summary>
        private static void Before_PullFishFromWater(FishingRod __instance, string fishId, ref int fishQuality)
        {
            try
            {
                if (!IsEnabled())
                    return;

                if (__instance.QualifiedItemId != RodsModule.GoldenRodQualifiedId)
                    return;

                // only real fish get the golden touch — trash, seaweed and the like keep no quality
                ParsedItemData data = ItemRegistry.GetDataOrErrorItem(fishId);
                if (data.Category != StardewValley.Object.FishCategory)
                    return;

                fishQuality = StardewValley.Object.highQuality; // gold — never less, never more
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Failed applying the golden rod quality effect; the catch keeps its normal quality.\n{ex}", LogLevel.Warn);
            }
        }
    }
}
