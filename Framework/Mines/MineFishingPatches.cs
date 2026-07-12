using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Tools;

namespace FishingHorizonsExpanded.Framework.Mines
{
    /// <summary>Harmony patch that checks <c>Data/Locations</c> fish on the lava mine floors (80–119).</summary>
    /// <remarks>
    /// Patched method (Stardew Valley 1.6): <see cref="MineShaft.getFish"/>.
    ///
    /// The vanilla method handles the lava area (<c>getMineArea() == 80</c>) entirely in hardcoded rolls
    /// (lava eel → cave jelly → trash) and returns before reaching <c>GameLocation.GetFishFromLocationData</c>.
    /// This prefix runs the location-data roll first on those floors only. If no data fish matches
    /// (the vanilla case — no vanilla entries target floors 80–119), the original method runs unchanged,
    /// so lava eel and cave jelly odds are untouched. All other floors and the Skull Cavern already reach
    /// location data in vanilla and are not intercepted.
    ///
    /// Each floor is a location named <c>UndergroundMine{level}</c>, so packs can target specific lava floors
    /// with a condition like <c>LOCATION_NAME Here UndergroundMine100</c> on the <c>UndergroundMine</c> entry.
    /// The patch swallows its own exceptions, so a failure can never crash the game.
    /// </remarks>
    internal static class MineFishingPatches
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

            var harmony = new Harmony($"{modId}.mines");

            harmony.Patch(
                original: AccessTools.Method(typeof(MineShaft), nameof(MineShaft.getFish)),
                prefix: new HarmonyMethod(typeof(MineFishingPatches), nameof(Before_GetFish))
            );
        }


        /*********
        ** Private methods — patches
        *********/
        /// <summary>Before the mine picks a fish: on lava floors, try the <c>Data/Locations</c> fish first.</summary>
        private static bool Before_GetFish(MineShaft __instance, int waterDepth, Farmer who, Vector2 bobberTile, ref Item __result)
        {
            try
            {
                if (!IsEnabled())
                    return true;

                // only the lava area is cut off from location data in vanilla
                if (__instance.getMineArea() != 80)
                    return true;

                // the training rod only catches trash in the mines — keep vanilla behavior
                if (who?.CurrentTool is FishingRod rod && rod.QualifiedItemId.Contains("TrainingRod"))
                    return true;

                Item? fish = GameLocation.GetFishFromLocationData(__instance.Name, bobberTile, waterDepth, who, isTutorialCatch: false, isInherited: false, __instance);
                if (fish is not null)
                {
                    __result = fish;
                    return false; // skip the vanilla hardcoded lava rolls
                }
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Failed checking location data fish for a lava mine floor; falling back to vanilla behavior.\n{ex}", LogLevel.Warn);
            }
            return true;
        }
    }
}
