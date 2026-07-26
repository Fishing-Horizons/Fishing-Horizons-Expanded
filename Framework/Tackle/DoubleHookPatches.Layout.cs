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
    /// <summary>Widening the bubble, frame and sonar, and drawing the extra bars and fish.</summary>
    internal static partial class DoubleHookPatches
    {
        /*********
        ** Transpiler — widen the translucent bubble, frame and sonar
        *********/
        /// <summary>
        /// Route every <c>SpriteBatch.Draw</c> call in <c>BobberBar.draw</c> that uses the
        /// nine-argument float-scale overload through <see cref="DrawSprite"/>, which swaps in wider
        /// artwork for the translucent bubble and the wooden frame when extra fish are in play.
        /// </summary>
        /// <remarks>
        /// Every call site is redirected rather than a specific one by index, so the patch does not
        /// break if the game reorders its draw calls. <see cref="DrawSprite"/> decides what to do
        /// based on the source rect and forwards anything it does not recognise untouched.
        /// </remarks>
        private static IEnumerable<CodeInstruction> TranspileDraw(IEnumerable<CodeInstruction> instructions)
        {
            var target = AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[]
            {
                typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color),
                typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float)
            });
            var replacement = AccessTools.Method(typeof(DoubleHookPatches), nameof(DrawSprite));

            var drawInMenu = AccessTools.Method(typeof(Item), nameof(Item.drawInMenu), new[]
            {
                typeof(SpriteBatch), typeof(Vector2), typeof(float)
            });
            var drawSonarFish = AccessTools.Method(typeof(DoubleHookPatches), nameof(DrawSonarFish));

            int patched = 0;
            foreach (var instruction in instructions)
            {
                // The sonar's fish icon — the only drawInMenu call in this method.
                if (drawInMenu != null && instruction.Calls(drawInMenu))
                {
                    yield return new CodeInstruction(OpCodes.Call, drawSonarFish)
                    {
                        labels = instruction.labels,
                        blocks = instruction.blocks
                    };
                    continue;
                }

                if (instruction.Calls(target))
                {
                    patched++;
                    // Stack layout is identical (the SpriteBatch is already on the stack as the
                    // instance), so we only swap the callvirt for a call to our static method.
                    yield return new CodeInstruction(OpCodes.Call, replacement)
                    {
                        labels = instruction.labels,
                        blocks = instruction.blocks
                    };
                    continue;
                }

                yield return instruction;
            }

            if (patched == 0)
                Monitor.Log("Could not find any draw calls to patch in BobberBar.draw — the bubble and frame will not widen for extra fish.", LogLevel.Warn);
        }


        /*********
        ** Private methods — sprite substitution
        *********/
        /// <summary>
        /// Stand-in for vanilla's sprite draw calls in <c>BobberBar.draw</c>. When extra fish are in
        /// play, swaps the translucent bubble and the wooden frame for their wider variants;
        /// everything else is forwarded to vanilla unchanged.
        /// </summary>
        /// <remarks>
        /// Only the texture and source rect change — position, origin, scale, colour, rotation, flip
        /// and depth all stay exactly as vanilla passed them. Because every template has the same
        /// content height as the sprite it replaces and is anchored on its left edge, the artwork
        /// simply extends further to the right.
        /// </remarks>
        internal static void DrawSprite(
            SpriteBatch b, Texture2D texture, Vector2 position, Rectangle? sourceRectangle,
            Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
        {
            try
            {
                int extras = Armed ? SpawnedExtraCount() : 0;
                if (extras > 0)
                {
                    Texture2D? swap = null;
                    bool isSonar = false;

                    if (texture == Game1.mouseCursors && sourceRectangle == VanillaBubbleSource)
                        swap = extras == 1 ? CachedTwoFishBubble : CachedThreeFishBubble;
                    else if (texture == Game1.mouseCursors && sourceRectangle == VanillaPanelSource)
                        swap = extras == 1 ? CachedTwoFishFrame : CachedThreeFishFrame;
                    else if (texture == Game1.mouseCursors_1_6 && sourceRectangle == VanillaSonarSource)
                    {
                        swap = extras == 1 ? CachedTwoFishSonar : CachedThreeFishSonar;
                        isSonar = true;
                    }

                    if (swap != null)
                    {
                        if (isSonar)
                            position.X += GetSonarShift(extras, effects);

                        b.Draw(swap, position, GetContentBounds(swap), color, rotation, origin, scale, effects, layerDepth);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(DrawSprite)}:\n{ex}", LogLevel.Error);
            }

            b.Draw(texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
        }

        /// <summary>
        /// How far right the sonar has to move so the widened bubble doesn't swallow it.
        /// </summary>
        /// <remarks>
        /// Vanilla tucks the sonar's left edge 24px inside the bubble's right edge. The bubble grows by
        /// one bar pitch per extra fish, so matching that shift keeps the overlap exactly as vanilla
        /// draws it. When the bar sits near the right of the screen the game flips the sonar over to the
        /// left instead, where the widening never reaches it — so that case stays put.
        /// </remarks>
        private static float GetSonarShift(int extras, SpriteEffects effects)
        {
            return effects == SpriteEffects.FlipHorizontally
                ? 0f
                : extras * BarSpacing;
        }

        /// <summary>
        /// Stand-in for the sonar's <c>fishObject.drawInMenu</c> call: shifts the icon along with the
        /// sonar frame and fills in the extra slots.
        /// </summary>
        internal static void DrawSonarFish(Item fish, SpriteBatch b, Vector2 location, float scaleSize)
        {
            try
            {
                int extras = Armed ? SpawnedExtraCount() : 0;
                if (extras > 0)
                {
                    // The sonar frame is drawn before the icons, so it has already picked its side;
                    // vanilla decides that from the bar's position, and we mirror the same test.
                    bool flipped = CurrentBar != null
                        && CurrentBar.xPositionOnScreen > Game1.viewport.Width * 0.75f;

                    location.X += GetSonarShift(extras, flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None);

                    for (int slot = extras; slot > 0; slot--)
                    {
                        // A slot shows its own species when the extra turned out to be a different
                        // fish, and otherwise repeats the one already on the line.
                        Item icon = (slot == 1 ? SecondFishIcon : ThirdFishIcon) ?? fish;
                        icon.drawInMenu(b, location + new Vector2(0f, slot * SonarSlotPitch), scaleSize);
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(DrawSonarFish)}:\n{ex}", LogLevel.Error);
            }

            fish.drawInMenu(b, location, scaleSize);
        }

        /// <summary>
        /// Get the opaque bounding box of a texture, ignoring transparent padding around the artwork.
        /// </summary>
        /// <remarks>
        /// Using the measured content box rather than the whole canvas means the templates can be
        /// exported with any amount of padding and still line up perfectly — no code change needed
        /// when the art is re-exported. Results are cached; the scan runs once per texture.
        /// </remarks>
        private static Rectangle GetContentBounds(Texture2D texture)
        {
            if (ContentBoundsCache.TryGetValue(texture, out Rectangle cached))
                return cached;

            Rectangle bounds;
            try
            {
                Color[] data = new Color[texture.Width * texture.Height];
                texture.GetData(data);

                int minX = texture.Width, minY = texture.Height, maxX = -1, maxY = -1;
                for (int y = 0; y < texture.Height; y++)
                {
                    for (int x = 0; x < texture.Width; x++)
                    {
                        if (data[y * texture.Width + x].A == 0)
                            continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                bounds = maxX < 0
                    ? new Rectangle(0, 0, texture.Width, texture.Height)
                    : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Could not measure texture bounds, falling back to the full canvas:\n{ex}", LogLevel.Warn);
                bounds = new Rectangle(0, 0, texture.Width, texture.Height);
            }

            ContentBoundsCache[texture] = bounds;
            return bounds;
        }


        /*********
        ** Patches — drawing
        *********/
        /// <summary>PREFIX: Record the live minigame, and hide the vanilla fish once it's secured.</summary>
        private static void BeforeDraw(BobberBar __instance)
        {
            try
            {
                CurrentBar = __instance;

                if (!Armed || !FirstFishSecured) return;
                SavedBobberPosition = __instance.bobberPosition;
                __instance.bobberPosition = -10000f;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(BeforeDraw)}:\n{ex}", LogLevel.Error);
            }
        }

        /// <summary>POSTFIX: Restore the vanilla fish position, then draw extra fish and their catch bars.</summary>
        private static void AfterDraw(BobberBar __instance, SpriteBatch b)
        {
            try
            {
                if (FirstFishSecured)
                    __instance.bobberPosition = SavedBobberPosition;

                if (!Armed || __instance.scale != 1f)
                    return;

                Game1.StartWorldDrawInUI(b);

                // --- Second fish: bar slot 1 ---
                if (SecondSpawned)
                {
                    if (!SecondLost && !SecondSecured)
                        DrawExtraFish(b, __instance, SecondPosition, SecondShake, CachedSecondFishTexture);

                    DrawCatchBar(b, __instance, SecondSecured ? 1f : SecondDistanceFromCatching, slot: 1);
                }

                // --- Third fish: bar slot 2 ---
                if (ThirdSpawned)
                {
                    if (!ThirdLost && !ThirdSecured)
                        DrawExtraFish(b, __instance, ThirdPosition, ThirdShake, CachedThirdFishTexture);

                    DrawCatchBar(b, __instance, ThirdSecured ? 1f : ThirdDistanceFromCatching, slot: 2);
                }

                Game1.EndWorldDrawInUI(b);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(AfterDraw)}:\n{ex}", LogLevel.Error);
            }
        }


        /*********
        ** Private methods — drawing
        *********/
        /// <summary>
        /// Draw an extra catch progress bar. Pixel-for-pixel the same as vanilla's bar
        /// (<see cref="Game1.staminaRect"/> + <see cref="Utility.getRedToGreenLerpColor"/>),
        /// just shifted right by <paramref name="slot"/> × <see cref="BarSpacing"/>.
        /// </summary>
        /// <param name="slot">1 for the second fish, 2 for the third.</param>
        private static void DrawCatchBar(SpriteBatch b, BobberBar bar, float progress, int slot)
        {
            b.Draw(
                Game1.staminaRect,
                new Rectangle(
                    bar.xPositionOnScreen + VanillaBarX + slot * BarSpacing,
                    bar.yPositionOnScreen + VanillaBarY + (int)(BarHeight * (1f - progress)),
                    BarWidth,
                    (int)(BarHeight * progress)),
                Utility.getRedToGreenLerpColor(progress)
            );
        }

        /// <summary>
        /// Draw an extra fish on the track, matching vanilla's fish draw call
        /// (origin 10,10 at scale 2, depth 0.88) but without the horizontal flip.
        /// </summary>
        private static void DrawExtraFish(
            SpriteBatch b, BobberBar bar, float position, Vector2 shake, Texture2D? texture)
        {
            Vector2 drawPos = new Vector2(
                bar.xPositionOnScreen + 64 + 18,
                bar.yPositionOnScreen + 12 + 24 + position)
                + shake + bar.everythingShake;

            if (texture != null)
            {
                b.Draw(texture, drawPos, new Rectangle(0, 0, texture.Width, texture.Height),
                    Color.White, 0f, new Vector2(texture.Width / 2f, texture.Height / 2f),
                    2f, SpriteEffects.None, 0.88f);
            }
            else
            {
                // Fallback: the vanilla fish sprite
                b.Draw(Game1.mouseCursors, drawPos, new Rectangle(614, 1840, 20, 20),
                    Color.White, 0f, new Vector2(10f, 10f),
                    2f, SpriteEffects.None, 0.88f);
            }
        }
    }
}
