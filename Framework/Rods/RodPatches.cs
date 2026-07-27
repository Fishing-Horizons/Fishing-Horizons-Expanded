using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley.Tools;

namespace FishingHorizonsExpanded.Framework.Rods
{
    /// <summary>Harmony patches shared by every custom rod.</summary>
    /// <remarks>
    /// Rod-specific behaviour does not belong here. The feeder rod's extra-fish mechanic lives in
    /// <see cref="Tackle.DoubleHookPatches"/>, which recognises that one rod by its ID.
    /// </remarks>
    internal static class RodPatches
    {
        /*********
        ** Fields
        *********/
        /// <summary>The monitor with which to log messages.</summary>
        private static IMonitor Monitor = null!;

        /// <summary>Get the rod definition matching a qualified tool ID, if it is one of ours.</summary>
        private static Func<string, RodDefinition?> GetRod = null!;


        /*********
        ** Public methods
        *********/
        /// <summary>Apply the Harmony patches.</summary>
        public static void Apply(string modId, IMonitor monitor, Func<string, RodDefinition?> getRod)
        {
            Monitor = monitor;
            GetRod = getRod;

            var harmony = new Harmony($"{modId}.rods");

            harmony.Patch(
                original: AccessTools.Method(typeof(FishingRod), nameof(FishingRod.getColor)),
                postfix: new HarmonyMethod(typeof(RodPatches), nameof(AfterGetColor))
            );
        }


        /*********
        ** Private methods
        *********/
        /// <summary>
        /// POSTFIX: stop vanilla from tinting a custom rod that already carries its own colour.
        /// </summary>
        /// <remarks>
        /// <c>Game1.drawTool</c> draws the in-hand casting animation tinted with <c>getColor()</c>, which
        /// vanilla hardcodes per upgrade level — violet at level 3, goldenrod at 0, and so on. Because
        /// that multiplies the artwork, a rod whose animation we have already tinted comes out as the
        /// product of the two colours rather than the colour that was asked for: the feeder rod's olive
        /// green times violet used to land on a murky grey-brown, and no baked value could have fixed
        /// it, since violet passes only half of the green channel.
        ///
        /// So for rods that specify their own <see cref="RodDefinition.CastingTint"/> we return white
        /// and let the baked pixels through untouched. Rods that leave the tint null keep a grey
        /// animation and vanilla's tier colour, exactly like an ordinary rod.
        /// </remarks>
        private static void AfterGetColor(FishingRod __instance, ref Color __result)
        {
            try
            {
                if (GetRod(__instance.QualifiedItemId)?.CastingTint != null)
                    __result = Color.White;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterGetColor)}:\n{ex}", LogLevel.Error);
            }
        }
    }
}
