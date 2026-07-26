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
    /// <summary>Harmony patches implementing the multi-fish minigame mechanic.</summary>
    /// <remarks>
    /// Supports up to three fish in a single catch:
    /// <list type="bullet">
    /// <item><b>Second fish</b> — triggered by the <em>Feeder Rod</em> (inherent chance, no tackle needed)
    /// or by the <em>Double Hook</em> tackle on any rod. Spawns once the first catch bar reaches 50%.</item>
    /// <item><b>Third fish</b> — only with the <em>Feeder Rod + Double Hook</em> combo. Spawns when the
    /// second fish's bar reaches 50% AND the first fish is fully caught.</item>
    /// </list>
    ///
    /// <para><b>Rendering</b> — matches vanilla exactly. Vanilla's <c>BobberBar.draw</c> renders the
    /// catch progress bar as a plain <see cref="Game1.staminaRect"/> rectangle:</para>
    /// <code>
    /// b.Draw(Game1.staminaRect,
    ///     new Rectangle(xPositionOnScreen + 124,
    ///                   yPositionOnScreen + 4 + (int)(580f * (1f - distanceFromCatching)),
    ///                   16, (int)(580f * distanceFromCatching)),
    ///     Utility.getRedToGreenLerpColor(distanceFromCatching));
    /// </code>
    /// <para>Each extra fish gets an identical bar shifted right by <see cref="BarSpacing"/> px.</para>
    ///
    /// <para>The translucent bubble behind the minigame is vanilla's first draw call —
    /// <c>mouseCursors</c> source <c>(652, 1685, 52, 157)</c> at <c>Color.White * 0.6f * scale</c>,
    /// and the wooden frame is the second — source <c>(644, 1999, 38, 150)</c>. A transpiler routes
    /// both through <see cref="DrawSprite"/>, which substitutes wider variants of the same artwork.
    /// Only the texture and source rect change, so position/origin/scale/alpha/flip stay vanilla and
    /// both sprites simply extend further to the right.</para>
    ///
    /// State is static because the bobber bar minigame only ever runs for the local player.
    /// All patches swallow their own exceptions, so a failure can never crash the game.
    ///
    /// <para>The implementation is split across <c>DoubleHookPatches.*.cs</c>: <c>State</c> holds the
    /// tuning, geometry and per-minigame fields, <c>Runtime</c> the lifecycle and fish simulation,
    /// <c>Sound</c> the reel audio, <c>Species</c> what each extra fish is and how it's awarded, and
    /// <c>Layout</c> everything drawn.</para>
    /// </remarks>
    internal static partial class DoubleHookPatches
    {
        /*********
        ** Public methods
        *********/
        /// <summary>Apply the Harmony patches.</summary>
        public static void Apply(
            string modId,
            IMonitor monitor,
            Func<bool> isEnabled,
            Func<float> getDoubleHookChance,
            Func<float> getFeederRodChance,
            IGameContentHelper contentHelper)
        {
            Monitor = monitor;
            IsEnabled = isEnabled;
            GetDoubleHookChance = getDoubleHookChance;
            GetFeederRodChance = getFeederRodChance;
            ContentHelper = contentHelper;

            var harmony = new Harmony($"{modId}.tackle");

            harmony.Patch(
                original: AccessTools.FirstConstructor(typeof(BobberBar), c => !c.IsStatic && c.GetParameters().Length > 0),
                postfix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(AfterConstructor))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(BobberBar), nameof(BobberBar.update)),
                prefix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(BeforeUpdate)),
                postfix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(AfterUpdate)),
                transpiler: new HarmonyMethod(typeof(DoubleHookPatches), nameof(TranspileUpdate))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(BobberBar), nameof(BobberBar.draw), new[] { typeof(SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(BeforeDraw)),
                postfix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(AfterDraw)),
                transpiler: new HarmonyMethod(typeof(DoubleHookPatches), nameof(TranspileDraw))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(FishingRod), nameof(FishingRod.pullFishFromWater)),
                prefix: new HarmonyMethod(typeof(DoubleHookPatches), nameof(BeforePullFishFromWater))
            );
        }
    }
}
