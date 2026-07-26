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
    /// <summary>The minigame lifecycle: arming, per-tick updates and the extra fish simulation.</summary>
    internal static partial class DoubleHookPatches
    {
        /*********
        ** Patches — lifecycle
        *********/
        /// <summary>Arm the mechanic when a new minigame starts.</summary>
        private static void AfterConstructor(BobberBar __instance)
        {
            Reset();

            try
            {
                if (!IsEnabled())
                    return;
                if (__instance.bossFish || __instance.fromFishPond)
                    return;

                // The sonar is restyled on every cast, with or without the extra-fish tackle, so it
                // loads ahead of the checks below that bail out on an ordinary rod.
                try
                {
                    CachedOneFishSonar = ContentHelper.Load<Texture2D>(OneFishSonarAsset);
                }
                catch (Exception ex)
                {
                    Monitor.Log($"Could not load the restyled sonar texture, falling back to vanilla art:\n{ex}", LogLevel.Warn);
                    CachedOneFishSonar = null;
                }

                var rod = Game1.player.CurrentTool as FishingRod;
                bool hasFeederRod = rod?.QualifiedItemId == Rods.RodsModule.FeederRodQualifiedId;
                bool hasDoubleHook = __instance.bobbers?.Contains(TackleModule.DoubleHookQualifiedId) == true;

                if (!hasFeederRod && !hasDoubleHook)
                    return;

                float chance = hasFeederRod
                    ? Math.Clamp(GetFeederRodChance(), 0f, 1f)
                    : Math.Clamp(GetDoubleHookChance(), 0f, 1f);

                Armed = Game1.random.NextDouble() < (double)chance;
                CanSpawnThird = hasFeederRod && hasDoubleHook;

                try
                {
                    CachedSecondFishTexture = ContentHelper.Load<Texture2D>(SecondFishTextureAsset);
                    CachedThirdFishTexture = ContentHelper.Load<Texture2D>(ThirdFishTextureAsset);
                    CachedTwoFishBubble = ContentHelper.Load<Texture2D>(TwoFishBubbleAsset);
                    CachedThreeFishBubble = ContentHelper.Load<Texture2D>(ThreeFishBubbleAsset);
                    CachedTwoFishFrame = ContentHelper.Load<Texture2D>(TwoFishFrameAsset);
                    CachedThreeFishFrame = ContentHelper.Load<Texture2D>(ThreeFishFrameAsset);
                    CachedTwoFishSonar = ContentHelper.Load<Texture2D>(TwoFishSonarAsset);
                    CachedThreeFishSonar = ContentHelper.Load<Texture2D>(ThreeFishSonarAsset);
                }
                catch (Exception ex)
                {
                    Monitor.Log($"Could not load the multi-fish UI textures, falling back to vanilla art:\n{ex}", LogLevel.Warn);
                    CachedSecondFishTexture = null;
                    CachedThirdFishTexture = null;
                    CachedTwoFishBubble = null;
                    CachedThreeFishBubble = null;
                    CachedTwoFishFrame = null;
                    CachedThreeFishFrame = null;
                    CachedTwoFishSonar = null;
                    CachedThreeFishSonar = null;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterConstructor)}:\n{ex}", LogLevel.Error);
                Armed = false;
            }
        }

        /// <summary>
        /// PREFIX: Remember the first fish's progress, and while extras are still being fought keep
        /// that progress just short of full so the vanilla update can't finish the minigame early.
        /// </summary>
        /// <remarks>
        /// This used to park the vanilla fish in the middle of the green bar to stop its progress
        /// draining. That pinned <c>bobberInBar</c> to true, which looped the reel sound for as long as
        /// the extras were in play. Holding the progress value directly does the same job without
        /// lying about where the fish is, and <see cref="AdjustBobberInBar"/> now drives the sound.
        /// </remarks>
        private static void BeforeUpdate(BobberBar __instance)
        {
            try
            {
                if (!Armed)
                    return;

                // Carry last tick's reading forward before the update overwrites it.
                FirstFishWasInBar = VanillaFishInBar;
                ForcedInBar = false;

                DistanceBeforeUpdate = __instance.distanceFromCatching;
                PerfectBeforeUpdate = __instance.perfect;
                FishSizeBeforeUpdate = __instance.fishSize;
                ChallengeBaitBeforeUpdate = __instance.challengeBaitFishes;

                if (!FirstFishSecured)
                    return;

                if (HasUnresolvedExtras())
                {
                    // A single tick can only move this by ±0.003, so clamping here keeps it clear of
                    // both ends and vanilla never fires the caught or escaped branch while extras remain.
                    __instance.distanceFromCatching = Math.Min(__instance.distanceFromCatching, 0.99f);
                    __instance.fadeOut = false;
                }
                else if (!__instance.fadeOut)
                {
                    // Every fish has been settled, so the minigame has to finish. Simply setting the
                    // progress to 1 isn't enough: with no fish left in the bar, vanilla drains it a
                    // little before it checks, so it never quite arrives and the window stays open
                    // forever. Overshooting lets that drain happen and still leaves vanilla's own clamp
                    // to land exactly on 1, so vanilla plays its own win — jingle, shake, perfect
                    // banner and all — instead of us imitating it.
                    __instance.distanceFromCatching = 2f;
                    __instance.fishShake = Vector2.Zero;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(BeforeUpdate)}:\n{ex}", LogLevel.Error);
            }
        }

        /// <summary>POSTFIX: Spawn and simulate extra fish; keep the minigame alive until all resolve.</summary>
        private static void AfterUpdate(BobberBar __instance, GameTime time)
        {
            try
            {
                if (!Armed || __instance.fadeIn)
                    return;

                // 0. Undo the side effects of borrowing bobberInBar for the reel sound, so the first
                //    fish behaves exactly as it does in vanilla.
                if (FirstFishSecured && HasUnresolvedExtras())
                {
                    // Already won — nothing the remaining fish do may still change its outcome.
                    __instance.distanceFromCatching = DistanceBeforeUpdate;
                    __instance.perfect = PerfectBeforeUpdate;
                    __instance.fishSize = FishSizeBeforeUpdate;
                    __instance.challengeBaitFishes = ChallengeBaitBeforeUpdate;
                }
                else if (ForcedInBar)
                {
                    // The first fish is out of the bar but another fish is in it, so vanilla ran its
                    // "reeling in" branch for all three fish. Put the first fish back on the branch it
                    // should have taken.
                    ApplyOutOfBarPenalty(__instance, time);
                }

                // 1. Spawn second fish at 50% of the first bar
                if (!SecondSpawned)
                {
                    if (__instance.distanceFromCatching >= SecondFishSpawnProgress)
                    {
                        SpawnFish(__instance, out SecondSpawned, out SecondPosition, out SecondSpeed, out SecondTarget, out SecondDistanceFromCatching);
                        RollExtraSpecies(__instance, out SecondFishId, out SecondFishSize, out SecondFishQuality);
                        SecondFishIcon = SecondFishId == null ? null : ItemRegistry.Create(SecondFishId);
                    }
                    return;
                }

                // 2. Detect the first fish being caught (vanilla just set fadeOut)
                if (!FirstFishSecured && __instance.fadeOut && __instance.distanceFromCatching > 0.9f)
                {
                    if (HasUnresolvedExtras())
                    {
                        FirstFishSecured = true;
                        __instance.fadeOut = false;
                        __instance.distanceFromCatching = 0.99f;
                        __instance.scale = 1f;
                    }
                    return;
                }

                // 3. Keep the minigame alive while extras are still in play. Ending it once they are
                //    all settled is the prefix's job, since only it can act before vanilla's own check.
                if (FirstFishSecured && HasUnresolvedExtras())
                {
                    __instance.distanceFromCatching = Math.Min(__instance.distanceFromCatching, 0.99f);
                    __instance.fadeOut = false;
                }

                // 4. Update second fish
                if (SecondSpawned && !SecondLost && !SecondSecured)
                    UpdateFish(__instance, ref SecondPosition, ref SecondSpeed, ref SecondTarget, ref SecondDistanceFromCatching, ref SecondShake, out SecondLost, ref SecondSecured);

                // 5. Spawn third fish (feeder rod + double hook only)
                if (CanSpawnThird && !ThirdSpawned && SecondSpawned && !SecondLost)
                {
                    bool firstFull = FirstFishSecured || __instance.distanceFromCatching >= 0.99f;
                    bool secondHalf = SecondDistanceFromCatching >= ThirdFishSpawnProgress;
                    if (firstFull && secondHalf)
                    {
                        SpawnFish(__instance, out ThirdSpawned, out ThirdPosition, out ThirdSpeed, out ThirdTarget, out ThirdDistanceFromCatching);
                        RollExtraSpecies(__instance, out ThirdFishId, out ThirdFishSize, out ThirdFishQuality);
                        ThirdFishIcon = ThirdFishId == null ? null : ItemRegistry.Create(ThirdFishId);
                    }
                }

                // 6. Update third fish
                if (ThirdSpawned && !ThirdLost && !ThirdSecured)
                    UpdateFish(__instance, ref ThirdPosition, ref ThirdSpeed, ref ThirdTarget, ref ThirdDistanceFromCatching, ref ThirdShake, out ThirdLost, ref ThirdSecured);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterUpdate)}:\n{ex}", LogLevel.Error);
                Armed = false;
            }
        }


        /*********
        ** Private methods — fish simulation
        *********/
        private static void SpawnFish(
            BobberBar bar, out bool spawned,
            out float position, out float speed, out float target,
            out float distanceFromCatching)
        {
            spawned = true;
            speed = 0f;
            target = -1f;
            distanceFromCatching = 0.5f;

            float barCenter = bar.bobberBarPos + bar.bobberBarHeight / 2f;
            position = barCenter < 274f
                ? Game1.random.Next(350, 500)
                : Game1.random.Next(30, 180);

            Game1.playSound("FishHit");
            bar.everythingShakeTimer = 300f;
        }

        private static void UpdateFish(
            BobberBar bar,
            ref float position, ref float speed, ref float target,
            ref float distanceFromCatching, ref Vector2 shake,
            out bool lost, ref bool secured)
        {
            lost = false;
            float difficulty = bar.difficulty;

            if (target < 0f || Math.Abs(position - target) <= 3f)
            {
                if (Game1.random.NextDouble() < (double)(difficulty / 3000f) || target < 0f)
                {
                    float spaceBelow = 532f - position;
                    float spaceAbove = position;
                    float percent = Math.Min(99f, difficulty + Game1.random.Next(10, 45)) / 100f;
                    target = position + Game1.random.Next(
                        (int)Math.Min(0f - spaceAbove, spaceBelow),
                        (int)spaceBelow) * percent;
                    target = Math.Max(0f, Math.Min(532f, target));
                }
            }
            else
            {
                float acceleration = (target - position)
                    / (Game1.random.Next(10, 30) + (100f - Math.Min(100f, difficulty)));
                speed += (acceleration - speed) / 5f;
            }
            position = Math.Max(0f, Math.Min(532f, position + speed));

            bool inBar = position + 12f <= bar.bobberBarPos - 32f + bar.bobberBarHeight
                      && position - 16f >= bar.bobberBarPos - 32f;

            if (inBar)
            {
                distanceFromCatching = Math.Min(1f, distanceFromCatching + CatchGainPerTick);
                shake = new Vector2(Game1.random.Next(-10, 11) / 10f, Game1.random.Next(-10, 11) / 10f);
            }
            else
            {
                distanceFromCatching -= CatchLossPerTick;
                shake = Vector2.Zero;
                if (distanceFromCatching <= 0f)
                {
                    distanceFromCatching = 0f;
                    lost = true;
                    Game1.playSound("fishEscape");
                    bar.everythingShakeTimer = 300f;
                }
            }

            if (distanceFromCatching >= 1f)
            {
                distanceFromCatching = 1f;
                secured = true;
                Game1.playSound("newArtifact");
            }
        }
    }
}
