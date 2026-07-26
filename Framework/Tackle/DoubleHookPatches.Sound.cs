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
    /// <summary>Making the reel sound react to every fish on the line, not just the first.</summary>
    internal static partial class DoubleHookPatches
    {
        /*********
        ** Transpiler — make the reel sound follow every fish
        *********/
        /// <summary>
        /// Inject a call to <see cref="AdjustBobberInBar"/> straight after <c>BobberBar.update</c>
        /// finishes working out <c>bobberInBar</c>, so the rest of the tick sees whether <em>any</em>
        /// fish is inside the green bar rather than only the vanilla one.
        /// </summary>
        /// <remarks>
        /// <c>bobberInBar</c> drives the reel sounds, the reel rotation and the shake, so overriding it
        /// here is what makes the audio track all three fish. It also drives the first fish's catch
        /// progress, which <see cref="AfterUpdate"/> corrects afterwards.
        /// <para>The hook goes after the <em>last</em> write to the field, and inherits that
        /// instruction's labels so branches that skip the final write still run it.</para>
        /// </remarks>
        private static IEnumerable<CodeInstruction> TranspileUpdate(IEnumerable<CodeInstruction> instructions)
        {
            var field = AccessTools.Field(typeof(BobberBar), nameof(BobberBar.bobberInBar));
            var hook = AccessTools.Method(typeof(DoubleHookPatches), nameof(AdjustBobberInBar));
            var code = new List<CodeInstruction>(instructions);

            int lastStore = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].StoresField(field))
                    lastStore = i;
            }

            if (lastStore < 0 || lastStore + 1 >= code.Count)
            {
                Monitor.Log("Could not find where BobberBar.update sets bobberInBar — the reel sound will only follow the first fish.", LogLevel.Warn);
                return code;
            }

            // Take over the labels of the instruction we're pushing down, so any branch that jumps
            // over the final assignment lands on our hook instead of skipping it.
            CodeInstruction next = code[lastStore + 1];
            var loadInstance = new CodeInstruction(OpCodes.Ldarg_0) { labels = new List<Label>(next.labels) };
            next.labels.Clear();

            code.InsertRange(lastStore + 1, new[]
            {
                loadInstance,
                new CodeInstruction(OpCodes.Call, hook)
            });

            return code;
        }


        /*********
        ** Private methods — sound
        *********/
        /// <summary>
        /// Widen <c>bobberInBar</c> to mean "any fish still in play is inside the green bar".
        /// </summary>
        /// <remarks>
        /// Two things were wrong before. The reel sound only ever reacted to the vanilla fish, so extra
        /// fish were fought in silence; and once the first fish was secured the vanilla fish was parked
        /// inside the bar to stop its progress draining, which pinned <c>bobberInBar</c> to true and
        /// looped the reel sound forever. Recomputing the flag from the real state of every unresolved
        /// fish fixes both at once, and vanilla's own sound handling then does the right thing.
        /// </remarks>
        internal static void AdjustBobberInBar(BobberBar bar)
        {
            try
            {
                ForcedInBar = false;
                if (!Armed)
                    return;

                // Once the first fish is secured its sprite is only a leftover — it must not keep the sound alive.
                VanillaFishInBar = bar.bobberInBar && !FirstFishSecured;

                bool anyInBar =
                    VanillaFishInBar
                    || IsExtraInBar(bar, SecondSpawned, SecondLost, SecondSecured, SecondPosition)
                    || IsExtraInBar(bar, ThirdSpawned, ThirdLost, ThirdSecured, ThirdPosition);

                ForcedInBar = anyInBar && !bar.bobberInBar;
                bar.bobberInBar = anyInBar;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AdjustBobberInBar)}:\n{ex}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Apply to the first fish everything vanilla would have done to it this tick had its
        /// <c>bobberInBar</c> not been borrowed to keep the reel sound going for another fish.
        /// </summary>
        /// <remarks>
        /// A faithful transcription of the "fish outside the bar" branch of <c>BobberBar.update</c>:
        /// the escape whip and lost <c>perfect</c>, the shrinking fish, and the catch-progress drain
        /// with its Cork Bobber and Trap Bobber modifiers. Keeping it in step with that branch is the
        /// price of driving the sound from the flag, but it means the first fish is punished for
        /// drifting out of the bar exactly as it is in vanilla.
        /// </remarks>
        private static void ApplyOutOfBarPenalty(BobberBar bar, GameTime time)
        {
            // Vanilla ran its in-bar branch, which credited progress the first fish never earned.
            bar.distanceFromCatching = DistanceBeforeUpdate;

            // A Cork Bobber shields you while the treasure chest is inside the bar.
            bool treasureInBar =
                bar.treasure
                && bar.treasurePosition + 12f <= bar.bobberBarPos - 32f + bar.bobberBarHeight
                && bar.treasurePosition - 16f >= bar.bobberBarPos - 32f;

            if (treasureInBar && !bar.treasureCaught && bar.bobbers.Contains("(O)693"))
                return;

            // Vanilla's own sprite shake can't be used to spot the fish leaving the bar here: the
            // borrowed flag sends it down the in-bar branch, which refreshes the shake every tick, so
            // the escape would fire on every frame. Our own reading of where the fish really is does
            // not have that problem.
            bar.fishShake = Vector2.Zero;

            if (FirstFishWasInBar)
            {
                Game1.playSound("tinyWhip");
                bar.perfect = false;
                Rumble.stopRumbling();

                if (bar.challengeBaitFishes > 0)
                {
                    bar.challengeBaitFishes--;
                    if (bar.challengeBaitFishes <= 0)
                        bar.distanceFromCatching = 0f;
                }
            }

            bar.fishSizeReductionTimer -= time.ElapsedGameTime.Milliseconds;
            if (bar.fishSizeReductionTimer <= 0)
            {
                bar.fishSize = Math.Max(bar.minFishSize, bar.fishSize - 1);
                bar.fishSizeReductionTimer = 800;
            }

            if ((Game1.player.fishCaught != null && Game1.player.fishCaught.Length != 0) || Game1.currentMinigame != null)
            {
                if (bar.bobbers.Contains("(O)694"))
                {
                    float reduction = 0.003f;
                    float amount = 0.001f;
                    for (int i = 0; i < Utility.getStringCountInList(bar.bobbers, "(O)694"); i++)
                    {
                        reduction -= amount;
                        amount /= 2f;
                    }

                    bar.distanceFromCatching -= Math.Max(0.001f, reduction) * bar.distanceFromCatchPenaltyModifier;
                }
                else
                {
                    bar.distanceFromCatching -= (bar.beginnersRod ? 0.002f : 0.003f) * bar.distanceFromCatchPenaltyModifier;
                }
            }

            bar.distanceFromCatching = Math.Max(0f, Math.Min(1f, bar.distanceFromCatching));
        }

        /// <summary>Whether an extra fish that is still in play sits inside the green bar.</summary>
        /// <remarks>Same test vanilla uses for its own fish, so the extras behave identically.</remarks>
        private static bool IsExtraInBar(BobberBar bar, bool spawned, bool lost, bool secured, float position)
        {
            if (!spawned || lost || secured)
                return false;

            return position + 12f <= bar.bobberBarPos - 32f + bar.bobberBarHeight
                && position - 16f >= bar.bobberBarPos - 32f;
        }
    }
}
