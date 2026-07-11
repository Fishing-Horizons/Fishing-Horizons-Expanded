using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using StardewValley.WorldMaps;

namespace FishingHorizonsExpanded.Framework.Journal
{
    /// <summary>The Fisherman's Journal book menu.</summary>
    /// <remarks>
    /// Layout: a two-page spread.
    /// Spread 0 is the title page (left: title + collection progress, right: statistics).
    /// The following spreads list all fish grouped into sections by source mod; every list page is
    /// a 2×2 grid (4 fish) at the same position on every page, with section titles at the top edge
    /// of a section's first page. Uncaught fish are shown as black silhouettes with hidden names.
    /// Clicking a fish opens its detail spread: stats, sizes and Willy's note on the left page;
    /// the world map with catch markers, locations and season/time/weather on the right page.
    /// The last spread is the catch diary (recent catches across both pages).
    /// </remarks>
    internal sealed class JournalMenu : IClickableMenu
    {
        /*********
        ** Constants
        *********/
        /// <summary>The menu width in UI pixels.</summary>
        private const int MenuWidth = 1100;

        /// <summary>The menu height in UI pixels.</summary>
        private const int MenuHeight = 660;

        /// <summary>The number of fish per single page (2×2 grid).</summary>
        private const int FishPerPage = 4;

        /// <summary>The sprite draw scale (16px sprites → 64px).</summary>
        private const float SpriteScale = 4f;

        /// <summary>The horizontal padding inside a page.</summary>
        private const int PagePadding = 48;

        /// <summary>Inches → centimeters.</summary>
        private const double InchesToCm = 2.54;

        /// <summary>The height reserved at the top of every page for a section title, so the grid is identical on all pages.</summary>
        private const int HeaderHeight = 64;

        /// <summary>The smallest text scale used when shrinking text to fit its area.</summary>
        private const float MinTextScale = 0.8f;


        /*********
        ** Fields
        *********/
        /// <summary>The mod translations.</summary>
        private readonly ITranslationHelper I18n;

        /// <summary>The tracked journal progress (best quality/catch diary).</summary>
        private readonly ProgressTracker Progress;

        /// <summary>The prebuilt list pages. Each section starts on a fresh page with its title.</summary>
        private readonly List<Page> Pages = new();

        /// <summary>All fish known to the journal (for collection statistics).</summary>
        private readonly List<FishEntry> AllFish = new();

        /// <summary>All fish by qualified item ID (for diary display).</summary>
        private readonly Dictionary<string, FishEntry> FishById = new();

        /// <summary>The current spread index (0 = title spread, last = diary spread).</summary>
        private int Spread;

        /// <summary>The total number of spreads.</summary>
        private readonly int SpreadCount;

        /// <summary>The fish whose detail spread is open, if any.</summary>
        private FishEntry? DetailFish;

        /// <summary>The fish currently under the cursor on a list page, if any (for the hover zoom effect).</summary>
        private FishEntry? HoveredFish;

        /// <summary>The world map region shown on detail pages, if available.</summary>
        private readonly MapRegion? MapRegion;

        /// <summary>The world map base texture, if available.</summary>
        private readonly MapAreaTexture? WorldMap;

        /// <summary>Willy's portrait texture, if available.</summary>
        private readonly Texture2D? WillyPortrait;

        /// <summary>Cached catch marker positions per fish (as 0–1 fractions of the map texture).</summary>
        private readonly Dictionary<string, List<Vector2>> MapMarkerCache = new();

        /// <summary>The previous-page arrow.</summary>
        private readonly ClickableTextureComponent PrevArrow;

        /// <summary>The next-page arrow.</summary>
        private readonly ClickableTextureComponent NextArrow;

        /// <summary>The back button shown on the detail spread.</summary>
        private readonly ClickableTextureComponent BackButton;

        /// <summary>A single list page: an optional section title (only on the section's first page) and up to <see cref="FishPerPage"/> fish.</summary>
        private sealed record Page(string? Header, List<FishEntry> Fish);

        /// <summary>The index of the diary spread (always the last spread).</summary>
        private int DiarySpread => this.SpreadCount - 1;


        /*********
        ** Public methods
        *********/
        public JournalMenu(ITranslationHelper i18n, IModRegistry modRegistry, ProgressTracker progress)
            : base(
                x: (Game1.uiViewport.Width - MenuWidth) / 2,
                y: (Game1.uiViewport.Height - MenuHeight) / 2,
                width: MenuWidth,
                height: MenuHeight,
                showUpperRightCloseButton: true)
        {
            this.I18n = i18n;
            this.Progress = progress;

            // build the list pages: each section starts on a fresh page with its title,
            // then continues with untitled pages of 4 fish until the section ends
            foreach (JournalSection section in FishRegistry.BuildSections(modRegistry, i18n.Get("menu.journal.section.vanilla"), i18n))
            {
                this.AllFish.AddRange(section.Fish);
                for (int i = 0; i < section.Fish.Count; i += FishPerPage)
                {
                    this.Pages.Add(new Page(
                        Header: i == 0 ? section.Title : null,
                        Fish: section.Fish.Skip(i).Take(FishPerPage).ToList()
                    ));
                }
            }
            foreach (FishEntry fish in this.AllFish)
                this.FishById[fish.QualifiedId] = fish;

            // spreads: title + fish lists + diary
            this.SpreadCount = 1 + (int)Math.Ceiling(this.Pages.Count / 2.0) + 1;

            // optional textures (the journal still works without them)
            try
            {
                this.MapRegion = WorldMapManager.GetMapRegions().FirstOrDefault();
                this.WorldMap = this.MapRegion?.GetBaseTexture();
            }
            catch
            {
                this.MapRegion = null;
                this.WorldMap = null;
            }
            try
            {
                this.WillyPortrait = Game1.content.Load<Texture2D>("Portraits\\Willy");
            }
            catch
            {
                this.WillyPortrait = null;
            }

            // navigation buttons
            this.PrevArrow = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen - 64, this.yPositionOnScreen + this.height - 60, 48, 44),
                Game1.mouseCursors, new Rectangle(352, 495, 12, 11), 4f);
            this.NextArrow = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen + this.width + 16, this.yPositionOnScreen + this.height - 60, 48, 44),
                Game1.mouseCursors, new Rectangle(365, 495, 12, 11), 4f);
            this.BackButton = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen - 64, this.yPositionOnScreen + this.height - 60, 48, 44),
                Game1.mouseCursors, new Rectangle(352, 495, 12, 11), 4f);
        }

        /// <inheritdoc/>
        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            // close button always closes the menu
            if (this.upperRightCloseButton?.containsPoint(x, y) == true)
            {
                base.receiveLeftClick(x, y, playSound);
                return;
            }
            base.receiveLeftClick(x, y, playSound);

            // detail spread: only the back button returns to the list (other clicks do nothing)
            if (this.DetailFish is not null)
            {
                if (this.BackButton.containsPoint(x, y))
                {
                    this.DetailFish = null;
                    Game1.playSound("shwip");
                }
                return;
            }

            // pagination
            if (this.PrevArrow.containsPoint(x, y) && this.Spread > 0)
            {
                this.Spread--;
                Game1.playSound("shwip");
                return;
            }
            if (this.NextArrow.containsPoint(x, y) && this.Spread < this.SpreadCount - 1)
            {
                this.Spread++;
                Game1.playSound("shwip");
                return;
            }

            // clicking a fish opens its detail spread
            if (this.Spread > 0 && this.Spread < this.DiarySpread)
            {
                FishEntry? clicked = this.GetFishAt(x, y);
                if (clicked is not null)
                {
                    this.DetailFish = clicked;
                    Game1.playSound("shwip");
                }
            }
        }

        /// <inheritdoc/>
        public override void receiveRightClick(int x, int y, bool playSound = true)
        {
            // right click on the detail spread goes back to the list
            if (this.DetailFish is not null)
            {
                this.DetailFish = null;
                Game1.playSound("shwip");
            }
        }

        /// <inheritdoc/>
        public override void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys key)
        {
            // Escape/menu key from the detail spread goes back to the list instead of closing
            if (this.DetailFish is not null && (key == Microsoft.Xna.Framework.Input.Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key)))
            {
                this.DetailFish = null;
                Game1.playSound("shwip");
                return;
            }
            base.receiveKeyPress(key);
        }

        /// <inheritdoc/>
        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            this.PrevArrow.tryHover(x, y, 0.2f);
            this.NextArrow.tryHover(x, y, 0.2f);
            this.BackButton.tryHover(x, y, 0.2f);

            this.HoveredFish = this.DetailFish is null && this.Spread > 0 && this.Spread < this.DiarySpread
                ? this.GetFishAt(x, y)
                : null;
        }

        /// <inheritdoc/>
        public override void draw(SpriteBatch b)
        {
            // dim the game behind the menu
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

            // book background: a single box (one clean shadow) with a subtle spine down the middle
            int pageWidth = this.width / 2;
            drawTextureBox(b, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);
            b.Draw(Game1.staminaRect, new Rectangle(this.xPositionOnScreen + pageWidth - 2, this.yPositionOnScreen + 20, 2, this.height - 40), Game1.textColor * 0.25f);
            b.Draw(Game1.staminaRect, new Rectangle(this.xPositionOnScreen + pageWidth + 1, this.yPositionOnScreen + 20, 1, this.height - 40), Game1.textColor * 0.12f);

            if (this.DetailFish is not null)
            {
                this.DrawDetailSpread(b, this.DetailFish, pageWidth);
                this.BackButton.draw(b);
            }
            else if (this.Spread == 0)
            {
                this.DrawTitleSpread(b, pageWidth);
                this.NextArrow.draw(b);
            }
            else if (this.Spread == this.DiarySpread)
            {
                this.DrawDiarySpread(b, pageWidth);
                this.PrevArrow.draw(b);
            }
            else
            {
                this.DrawListPage(b, this.xPositionOnScreen, pageWidth, pageIndex: (this.Spread - 1) * 2);
                this.DrawListPage(b, this.xPositionOnScreen + pageWidth, pageWidth, pageIndex: (this.Spread - 1) * 2 + 1);
                this.PrevArrow.draw(b);
                if (this.Spread < this.SpreadCount - 1)
                    this.NextArrow.draw(b);
            }

            // page numbers (bookish touch)
            if (this.DetailFish is null)
            {
                this.DrawPageNumber(b, this.xPositionOnScreen, pageWidth, this.Spread * 2 + 1);
                this.DrawPageNumber(b, this.xPositionOnScreen + pageWidth, pageWidth, this.Spread * 2 + 2);
            }

            base.draw(b);
            this.drawMouse(b);
        }


        /*********
        ** Private methods — title spread
        *********/
        /// <summary>Draw the title spread (spread 0): title + collection progress on the left, statistics on the right.</summary>
        private void DrawTitleSpread(SpriteBatch b, int pageWidth)
        {
            int contentWidth = pageWidth - 2 * PagePadding;

            // left page: title + collection progress
            string title = this.I18n.Get("menu.journal.title");
            SpriteText.drawStringHorizontallyCenteredAt(
                b,
                title,
                this.xPositionOnScreen + pageWidth / 2,
                this.yPositionOnScreen + 96
            );

            int caught = this.AllFish.Count(f => f.IsCaught());
            int total = this.AllFish.Count;
            int percent = total > 0 ? (int)Math.Round(100.0 * caught / total) : 0;
            string collection = this.I18n.Get("menu.journal.collection.progress", new { caught, total, percent });
            int cy = this.yPositionOnScreen + 230;
            this.DrawTextCentered(b, collection, this.xPositionOnScreen + pageWidth / 2, cy, contentWidth);
            this.DrawProgressBar(b, this.xPositionOnScreen + PagePadding + 32, cy + 44, contentWidth - 64, 16, total > 0 ? (float)caught / total : 0f);

            // Willy's handwritten dedication (flavor)
            string quote = this.WrapAndClampLines(this.I18n.Get("menu.journal.flavor-quote"), contentWidth - 32, maxLines: 3);
            Vector2 quoteSize = Game1.smallFont.MeasureString(quote);
            b.DrawString(Game1.smallFont, quote, new Vector2(this.xPositionOnScreen + (pageWidth - quoteSize.X) / 2, cy + 110), new Color(120, 78, 43) * 0.9f);

            string hint = this.I18n.Get("menu.journal.turn-page-hint");
            Vector2 hintSize = Game1.smallFont.MeasureString(hint);
            b.DrawString(
                Game1.smallFont,
                this.FitToWidth(hint, contentWidth),
                new Vector2(this.xPositionOnScreen + (pageWidth - Math.Min(hintSize.X, contentWidth)) / 2, this.yPositionOnScreen + this.height - 120),
                Game1.textColor * 0.75f
            );

            // right page: statistics
            this.DrawStatsPage(b, this.xPositionOnScreen + pageWidth, pageWidth);
        }

        /// <summary>Draw the statistics page (right page of the title spread).</summary>
        private void DrawStatsPage(SpriteBatch b, int pageX, int pageWidth)
        {
            int contentWidth = pageWidth - 2 * PagePadding;
            int textX = pageX + PagePadding;

            this.DrawPageHeader(b, this.I18n.Get("menu.journal.stats.title"), pageX, pageWidth);
            int y = this.yPositionOnScreen + 24 + HeaderHeight + 20;

            int totalCatches = this.AllFish.Sum(f => f.TimesCaught());
            int species = this.AllFish.Count(f => f.IsCaught());
            int bigSpecimens = this.AllFish.Count(f => f.IsCaught() && f.HasBigSpecimen());
            int maxSizeCm = (int)Math.Round(this.AllFish.Select(f => f.MaxCaughtSize()).DefaultIfEmpty(0).Max() * InchesToCm);
            IReadOnlyList<CatchLogEntry> log = this.Progress.Log;
            string avgSize = log.Count > 0
                ? ((int)Math.Round(log.Average(e => e.SizeInches) * InchesToCm)).ToString()
                : "—";

            var lines = new List<string>
            {
                this.I18n.Get("menu.journal.stats.total-caught", new { count = totalCatches }),
                this.I18n.Get("menu.journal.stats.species", new { count = species }),
                this.I18n.Get("menu.journal.stats.big", new { count = bigSpecimens }),
                this.I18n.Get("menu.journal.stats.max-size", new { cm = maxSizeCm }),
                this.I18n.Get("menu.journal.stats.avg-size", new { cm = avgSize }),
            };
            foreach (string line in lines)
                y += this.DrawTextWrapped(b, line, textX, y, contentWidth, Game1.textColor, maxLines: 2) + 12;

            // best fisherman note in multiplayer (small flavor line)
            if (Game1.getOnlineFarmers().Count > 1)
            {
                Farmer? best = Game1.getOnlineFarmers()
                    .OrderByDescending(f => this.AllFish.Count(fish => fish.IsCaughtBy(f)))
                    .FirstOrDefault();
                if (best is not null)
                {
                    y += 8;
                    string line = this.FitToWidth(this.I18n.Get("menu.journal.stats.best-fisher", new { name = best.Name, count = this.AllFish.Count(fish => fish.IsCaughtBy(best)) }), contentWidth);
                    b.DrawString(Game1.smallFont, line, new Vector2(textX, y), Game1.textColor * 0.75f);
                }
            }
        }


        /*********
        ** Private methods — list view
        *********/
        /// <summary>Get the bounds of a fish slot on a page (2×2 grid). The grid position is the same on every page, whether or not a section title is shown.</summary>
        /// <param name="pageX">The page's left screen coordinate.</param>
        /// <param name="pageWidth">The page width.</param>
        /// <param name="slot">The slot index (0–3: left-to-right, top-to-bottom).</param>
        private Rectangle GetSlotBounds(int pageX, int pageWidth, int slot)
        {
            int topMargin = 24 + HeaderHeight;
            int gridWidth = pageWidth - 64;
            int gridHeight = this.height - topMargin - 56;
            int cellWidth = gridWidth / 2;
            int cellHeight = gridHeight / 2;
            int col = slot % 2;
            int row = slot / 2;
            return new Rectangle(pageX + 32 + col * cellWidth, this.yPositionOnScreen + topMargin + row * cellHeight, cellWidth, cellHeight);
        }

        /// <summary>Get the fish at the given screen position, if any.</summary>
        private FishEntry? GetFishAt(int x, int y)
        {
            int pageWidth = this.width / 2;
            foreach ((int pageX, int pageIndex) in new[] { (this.xPositionOnScreen, (this.Spread - 1) * 2), (this.xPositionOnScreen + pageWidth, (this.Spread - 1) * 2 + 1) })
            {
                if (pageIndex < 0 || pageIndex >= this.Pages.Count)
                    continue;

                Page page = this.Pages[pageIndex];
                for (int slot = 0; slot < page.Fish.Count; slot++)
                {
                    if (this.GetSlotBounds(pageX, pageWidth, slot).Contains(x, y))
                        return page.Fish[slot];
                }
            }
            return null;
        }

        /// <summary>Draw one list page: optional section title at the top edge, then a 2×2 fish grid.</summary>
        private void DrawListPage(SpriteBatch b, int pageX, int pageWidth, int pageIndex)
        {
            if (pageIndex >= this.Pages.Count)
                return;

            Page page = this.Pages[pageIndex];

            if (page.Header is not null)
                this.DrawPageHeader(b, page.Header, pageX, pageWidth, scale: 1.2f);

            for (int slot = 0; slot < page.Fish.Count; slot++)
                this.DrawFishCell(b, page.Fish[slot], this.GetSlotBounds(pageX, pageWidth, slot));
        }

        /// <summary>Draw a page header: centered title with a divider line and a small diamond ornament. Returns the header's bottom screen coordinate.</summary>
        private int DrawPageHeader(SpriteBatch b, string title, int pageX, int pageWidth, float scale = 1f)
        {
            title = this.FitToWidth(title, (int)((pageWidth - 96) / scale), Game1.dialogueFont);
            Vector2 size = Game1.dialogueFont.MeasureString(title) * scale;
            Vector2 position = new((int)(pageX + (pageWidth - size.X) / 2), this.yPositionOnScreen + 24);
            Utility.drawTextWithShadow(b, title, Game1.dialogueFont, position, Game1.textColor, scale);

            // divider line with a diamond ornament in the middle (the diamond rotates around its center)
            int lineY = (int)(position.Y + size.Y + 4);
            int centerX = pageX + pageWidth / 2;
            b.Draw(Game1.staminaRect, new Rectangle(pageX + 48, lineY, pageWidth / 2 - 48 - 14, 2), Game1.textColor * 0.35f);
            b.Draw(Game1.staminaRect, new Rectangle(centerX + 14, lineY, pageWidth / 2 - 48 - 14, 2), Game1.textColor * 0.35f);
            b.Draw(Game1.staminaRect, new Rectangle(centerX, lineY + 1, 8, 8), null, Game1.textColor * 0.35f, MathF.PI / 4f, new Vector2(0.5f, 0.5f), SpriteEffects.None, 1f);
            return lineY + 2;
        }

        /// <summary>Draw a page number at the bottom center of a page.</summary>
        private void DrawPageNumber(SpriteBatch b, int pageX, int pageWidth, int number)
        {
            string text = $"— {number} —";
            Vector2 size = Game1.smallFont.MeasureString(text);
            b.DrawString(Game1.smallFont, text, new Vector2((int)(pageX + (pageWidth - size.X) / 2), this.yPositionOnScreen + this.height - 52), Game1.textColor * 0.4f);
        }

        /// <summary>Draw a fish cell: sprite on top, name and brief info below, centered in its grid slot. The hovered fish is drawn slightly larger.</summary>
        private void DrawFishCell(SpriteBatch b, FishEntry fish, Rectangle bounds)
        {
            bool caught = fish.IsCaught();
            bool hovered = this.HoveredFish == fish;
            float spriteScale = hovered ? SpriteScale * 1.18f : SpriteScale;
            float textScale = hovered ? 1.08f : 1f;
            int spriteSize = (int)(16 * SpriteScale);

            string name = caught ? fish.Data.DisplayName : this.I18n.Get("menu.journal.unknown-fish");
            string info = caught
                ? this.I18n.Get("menu.journal.caught-count", new { count = fish.TimesCaught() })
                : this.I18n.Get("menu.journal.not-caught-hint");
            int cellTextWidth = (int)(bounds.Width / textScale) - 8;
            string[] nameLines = this.WrapAndClampLines(name, cellTextWidth, maxLines: 2).Split('\n');
            info = this.FitToWidth(info, cellTextWidth);
            int nameLineHeight = (int)(Game1.smallFont.MeasureString("Ag").Y * textScale);
            Vector2 infoSize = Game1.smallFont.MeasureString(info) * textScale;

            // center the content block vertically in the slot (based on the unhovered size, so nothing jumps)
            int contentHeight = spriteSize + 2 + nameLines.Length * nameLineHeight + (int)infoSize.Y;
            int top = bounds.Y + Math.Max(0, (bounds.Height - contentHeight) / 2);

            // sprite (black silhouette if not caught yet), scaled around its center on hover
            Vector2 spriteCenter = new(bounds.X + bounds.Width / 2f, top + spriteSize / 2f);
            b.Draw(
                fish.Data.GetTexture(),
                spriteCenter,
                fish.Data.GetSourceRect(),
                caught ? Color.White : Color.Black,
                0f, new Vector2(8f, 8f), spriteScale, SpriteEffects.None, 1f);

            // best quality star in the cell's top-right corner
            if (caught)
            {
                Rectangle? star = this.GetQualityStarRect(this.Progress.Get(fish.QualifiedId).BestQuality);
                if (star.HasValue)
                    b.Draw(Game1.mouseCursors, new Vector2(bounds.Right - 40, bounds.Y + 8), star.Value, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
            }

            // name (hidden until caught), wrapped to two centered lines when long
            int textY = top + spriteSize + 2;
            foreach (string line in nameLines)
            {
                float lineWidth = Game1.smallFont.MeasureString(line).X * textScale;
                Utility.drawTextWithShadow(b, line, Game1.smallFont, new Vector2((int)(bounds.X + (bounds.Width - lineWidth) / 2), textY), Game1.textColor, textScale);
                textY += nameLineHeight;
            }

            // brief info
            b.DrawString(Game1.smallFont, info, new Vector2((int)(bounds.X + (bounds.Width - infoSize.X) / 2), textY), Game1.textColor * 0.6f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);
        }


        /*********
        ** Private methods — detail view
        *********/
        /// <summary>Draw the detail spread for one fish.</summary>
        private void DrawDetailSpread(SpriteBatch b, FishEntry fish, int pageWidth)
        {
            this.DrawDetailLeftPage(b, fish, pageWidth);
            this.DrawDetailRightPage(b, fish, pageWidth);
        }

        /// <summary>Draw the detail spread's left page: name, sprite, description, stats, sizes and Willy's note.</summary>
        private void DrawDetailLeftPage(SpriteBatch b, FishEntry fish, int pageWidth)
        {
            bool caught = fish.IsCaught();
            int leftX = this.xPositionOnScreen;
            int contentWidth = pageWidth - 2 * PagePadding;
            int textX = leftX + PagePadding;
            FishProgress progress = this.Progress.Get(fish.QualifiedId);

            int y = this.yPositionOnScreen + 32;

            // name
            string name = caught ? fish.Data.DisplayName : this.I18n.Get("menu.journal.unknown-fish");
            SpriteText.drawStringHorizontallyCenteredAt(b, name, leftX + pageWidth / 2, y);
            y += 52;

            // sprite (black silhouette if not caught yet)
            int spriteSize = 48;
            b.Draw(
                fish.Data.GetTexture(),
                new Vector2(leftX + (pageWidth - spriteSize) / 2f, y),
                fish.Data.GetSourceRect(),
                caught ? Color.White : Color.Black,
                0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
            y += spriteSize + 10;

            // description (hidden until caught; clamped to 2 lines so the layout never overflows)
            string description = caught ? (fish.Data.Description ?? "") : this.I18n.Get("menu.journal.detail.hidden-description");
            string wrapped = this.WrapAndClampLines(description, contentWidth, maxLines: 2);
            b.DrawString(Game1.smallFont, wrapped, new Vector2(textX, y), Game1.textColor * 0.9f);
            y += (int)Game1.smallFont.MeasureString(wrapped).Y + 12;

            const int lineHeight = 30;

            // caught status
            this.DrawCheckboxLine(b, textX, y, caught, this.I18n.Get(caught ? "menu.journal.detail.caught" : "menu.journal.not-caught-hint"), contentWidth);
            y += lineHeight;

            if (caught)
            {
                // times caught
                this.DrawTextFit(b, this.I18n.Get("menu.journal.caught-count", new { count = fish.TimesCaught() }), textX, y, contentWidth, Game1.textColor);
                y += lineHeight;
            }

            // possible length range (text only)
            if (fish.MinPossibleSize > 0 && fish.MaxPossibleSize > fish.MinPossibleSize)
            {
                this.DrawTextFit(b, this.I18n.Get("menu.journal.detail.size-range", new { min = (int)Math.Round(fish.MinPossibleSize * InchesToCm), max = (int)Math.Round(fish.MaxPossibleSize * InchesToCm) }), textX, y, contentWidth, Game1.textColor);
                y += lineHeight;
            }

            if (caught)
            {
                // personal record (text only) + "new record!" badge on the day a record is set
                string record = this.I18n.Get("menu.journal.detail.record", new { cm = (int)Math.Round(fish.MaxCaughtSize() * InchesToCm) });
                this.DrawTextFit(b, record, textX, y, contentWidth, Game1.textColor);
                if (progress.RecordDay == Game1.Date.TotalDays)
                {
                    float offset = Game1.smallFont.MeasureString(record).X + 16;
                    b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.detail.new-record"), new Vector2(textX + offset, y), new Color(178, 112, 15));
                }
                y += lineHeight;

                // records of other online players (text only, up to 3 best)
                if (Game1.getOnlineFarmers().Count > 1)
                {
                    var records = Game1.getOnlineFarmers()
                        .Where(farmer => farmer != Game1.player)
                        .Select(farmer => (farmer.Name, Size: fish.MaxCaughtSizeFor(farmer)))
                        .Where(r => r.Size > 0)
                        .OrderByDescending(r => r.Size)
                        .Take(3)
                        .ToList();
                    foreach ((string playerName, int size) in records)
                    {
                        b.DrawString(Game1.smallFont, this.FitToWidth($"• {playerName} — {(int)Math.Round(size * InchesToCm)} {this.I18n.Get("menu.journal.cm")}", contentWidth), new Vector2(textX + 12, y), Game1.textColor * 0.7f);
                        y += 27;
                    }
                }

                // big specimen
                this.DrawCheckboxLine(b, textX, y, fish.HasBigSpecimen(), this.I18n.Get("menu.journal.detail.big-specimen"), contentWidth);
                y += lineHeight;

                // best quality (star icon, or text for normal/unknown)
                Rectangle? star = this.GetQualityStarRect(progress.BestQuality);
                string qualityLabel = this.I18n.Get("menu.journal.detail.best-quality", new { quality = star.HasValue ? "" : (string)this.GetQualityName(progress.BestQuality) }).ToString().TrimEnd();
                this.DrawTextFit(b, qualityLabel, textX, y, contentWidth, Game1.textColor);
                if (star.HasValue)
                    b.Draw(Game1.mouseCursors, new Vector2(textX + Game1.smallFont.MeasureString(qualityLabel).X + 10, y + 2), star.Value, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
                y += lineHeight;
            }

            // Willy's note, always pinned to the bottom of the page
            int noteHeight = 158;
            int noteY = this.yPositionOnScreen + this.height - noteHeight - 28;
            this.DrawWillyNote(b, fish, leftX + PagePadding - 16, noteY, pageWidth - 2 * (PagePadding - 16), noteHeight);
        }

        /// <summary>Draw Willy's note box with his portrait and a data-driven hint about the fish.</summary>
        private void DrawWillyNote(SpriteBatch b, FishEntry fish, int x, int y, int width, int height)
        {
            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, height, Color.White, 1f, drawShadow: false);

            int innerX = x + 20;
            int innerY = y + 18;
            int portraitSize = 0;

            if (this.WillyPortrait is not null)
            {
                portraitSize = 84;
                b.Draw(this.WillyPortrait, new Rectangle(innerX, innerY + 8, portraitSize, portraitSize), new Rectangle(0, 0, 64, 64), Color.White);
            }

            int noteX = innerX + (portraitSize > 0 ? portraitSize + 16 : 0);
            int noteWidth = x + width - 20 - noteX;

            Utility.drawTextWithShadow(b, this.I18n.Get("menu.journal.note.title"), Game1.smallFont, new Vector2(noteX, innerY - 4), new Color(120, 78, 43));

            string note = this.WrapAndClampLines(this.GetWillyNote(fish), noteWidth, maxLines: 3);
            b.DrawString(Game1.smallFont, note, new Vector2(noteX, innerY + 28), Game1.textColor * 0.9f);
        }

        /// <summary>Build Willy's short note: a fun fact for fish that have one, otherwise one compact data-driven tip (place + best conditions).</summary>
        private string GetWillyNote(FishEntry fish)
        {
            if (fish.IsTrapFish)
                return this.I18n.Get("menu.journal.note.trap");

            // curated fun fact, if this fish has one
            string baseId = fish.QualifiedId.StartsWith("(O)") ? fish.QualifiedId[3..] : fish.QualifiedId;
            Translation fact = this.I18n.Get($"menu.journal.note.fact.{baseId}");
            if (fact.HasValue())
                return fact;

            // stable per-fish hash (string.GetHashCode is randomized per game launch)
            int hash = 0;
            foreach (char c in fish.QualifiedId)
                hash = (hash * 31 + c) & 0x7FFFFFFF;

            // one compact tip: place + the most useful condition (weather > time > season)
            string? location = fish.Locations.Keys.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault();
            string when;
            bool allDay = fish.TimeRanges.Count == 0 || fish.TimeRanges.Any(r => r.X <= 600 && r.Y >= 2600);
            HashSet<string> seasons = this.GetActiveSeasons(fish);
            if (fish.Weather == "rainy")
                when = this.I18n.Get("menu.journal.note.when.rain");
            else if (!allDay && fish.TimeRanges.Count > 0)
                when = this.I18n.Get("menu.journal.note.when.time", new { from = FormatTime(fish.TimeRanges[0].X), to = FormatTime(fish.TimeRanges[0].Y) });
            else if (seasons.Count < 4)
                when = string.Join(", ", seasons
                    .OrderBy(s => Array.IndexOf(new[] { "spring", "summer", "fall", "winter" }, s.ToLowerInvariant()))
                    .Select(s => (string)this.I18n.Get($"menu.journal.note.when.season.{s.ToLowerInvariant()}")));
            else
                when = this.I18n.Get("menu.journal.note.when.all-year");

            return location is not null
                ? this.I18n.Get($"menu.journal.note.tip.{1 + hash % 3}", new { location, when })
                : this.I18n.Get("menu.journal.note.tip.no-location", new { when });
        }

        /// <summary>Draw the detail spread's right page: world map with catch markers, locations, season/time/weather.</summary>
        private void DrawDetailRightPage(SpriteBatch b, FishEntry fish, int pageWidth)
        {
            int rightX = this.xPositionOnScreen + pageWidth;
            int contentWidth = pageWidth - 2 * PagePadding;
            int textX = rightX + PagePadding;
            int ry = this.yPositionOnScreen + 32;

            // header (the map starts below its real bottom edge, which varies by language/font)
            int headerBottom = this.DrawPageHeader(b, this.I18n.Get("menu.journal.detail.where-when-title"), rightX, pageWidth);
            ry = headerBottom + 26;

            // world map with catch markers
            if (this.WorldMap is not null)
            {
                int mapWidth = contentWidth - 40;
                int mapHeight = this.WorldMap.SourceRect.Height * mapWidth / Math.Max(1, this.WorldMap.SourceRect.Width);
                int mapX = rightX + (pageWidth - mapWidth) / 2;
                drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), mapX - 16, ry - 16, mapWidth + 32, mapHeight + 32, Color.White, 1f, drawShadow: false);
                b.Draw(this.WorldMap.Texture, new Rectangle(mapX, ry, mapWidth, mapHeight), this.WorldMap.SourceRect, Color.White);

                foreach (Vector2 marker in this.GetMapMarkers(fish))
                    this.DrawMapCross(b, mapX + (int)(marker.X * mapWidth), ry + (int)(marker.Y * mapHeight));

                ry += mapHeight + 28;
            }

            // trap fish: simple text instead of season/time/weather rows
            if (fish.IsTrapFish)
            {
                string water = this.I18n.Get(fish.TrapWaterType == "ocean" ? "menu.journal.detail.water.ocean" : "menu.journal.detail.water.freshwater");
                string trap = this.WrapAndClampLines(this.I18n.Get("menu.journal.detail.trap-fish", new { water }), contentWidth, maxLines: 3);
                b.DrawString(Game1.smallFont, trap, new Vector2(textX, ry), Game1.textColor * 0.9f);
                return;
            }

            // locations: a simple comma-separated list (no per-location seasons)
            if (fish.Locations.Count > 0)
            {
                this.DrawTextFit(b, this.I18n.Get("menu.journal.detail.locations-label"), textX, ry, contentWidth, Game1.textColor);
                ry += 30;
                string list = string.Join(", ", fish.Locations.Keys.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase));
                string wrappedList = this.WrapAndClampLines(list, contentWidth, maxLines: 2);
                b.DrawString(Game1.smallFont, wrappedList, new Vector2(textX, ry), Game1.textColor * 0.9f);
                ry += (int)Game1.smallFont.MeasureString(wrappedList).Y + 12;
            }

            // season icons (own row, so labels never collide in any language)
            HashSet<string> activeSeasons = this.GetActiveSeasons(fish);
            {
                string label = this.I18n.Get("menu.journal.detail.season-label");
                this.DrawTextFit(b, label, textX, ry, contentWidth - 4 * 46 - 16, Game1.textColor);
                int iconX = textX + (int)Math.Min(Game1.smallFont.MeasureString(label).X, contentWidth - 4 * 46 - 16) + 16;
                string[] seasonKeys = { "spring", "summer", "fall", "winter" };
                for (int i = 0; i < 4; i++)
                {
                    bool active = activeSeasons.Contains(seasonKeys[i]);
                    b.Draw(Game1.mouseCursors, new Vector2(iconX, ry + 4), new Rectangle(406, 441 + i * 8, 12, 8), active ? Color.White : Color.Black * 0.2f, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
                    iconX += 46;
                }
            }
            ry += 38;

            // weather icons (own row)
            {
                string label = this.I18n.Get("menu.journal.detail.weather-label");
                this.DrawTextFit(b, label, textX, ry, contentWidth - 2 * 46 - 16, Game1.textColor);
                int iconX = textX + (int)Math.Min(Game1.smallFont.MeasureString(label).X, contentWidth - 2 * 46 - 16) + 16;
                bool sunny = fish.Weather is "sunny" or "both";
                bool rainy = fish.Weather is "rainy" or "both";
                b.Draw(Game1.mouseCursors, new Vector2(iconX, ry + 4), new Rectangle(341, 421, 12, 8), sunny ? Color.White : Color.Black * 0.2f, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
                b.Draw(Game1.mouseCursors, new Vector2(iconX + 46, ry + 4), new Rectangle(365, 421, 12, 8), rainy ? Color.White : Color.Black * 0.2f, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
            }
            ry += 38;

            // time (text only)
            bool allDay = fish.TimeRanges.Count == 0 || fish.TimeRanges.Any(r => r.X <= 600 && r.Y >= 2600);
            string timeText = allDay
                ? this.I18n.Get("menu.journal.detail.time.all-day")
                : string.Join(", ", fish.TimeRanges.Select(r => $"{FormatTime(r.X)}–{FormatTime(r.Y)}"));
            this.DrawTextWrapped(b, this.I18n.Get("menu.journal.detail.time-line", new { time = timeText }), textX, ry, contentWidth, Game1.textColor, maxLines: 2);
        }

        /// <summary>Get the catch marker positions for a fish, as 0–1 fractions of the world map texture (cached).</summary>
        private List<Vector2> GetMapMarkers(FishEntry fish)
        {
            if (this.MapMarkerCache.TryGetValue(fish.QualifiedId, out List<Vector2>? cached))
                return cached;

            var markers = new List<Vector2>();
            if (this.WorldMap is not null && this.MapRegion is not null)
            {
                Rectangle area = this.WorldMap.MapPixelArea;
                foreach (string internalName in fish.LocationInternalNames.Values.Distinct().Take(8))
                {
                    try
                    {
                        GameLocation? location = Game1.getLocationFromName(internalName);
                        if (location is null)
                            continue;

                        var layer = location.Map?.Layers[0];
                        Point tile = layer is not null ? new Point(layer.LayerWidth / 2, layer.LayerHeight / 2) : Point.Zero;

                        MapAreaPositionWithContext? position = WorldMapManager.GetPositionData(location, tile);
                        if (position is null || position.Value.Data.Region.Id != this.MapRegion.Id)
                            continue;

                        Vector2 pixel = position.Value.GetMapPixelPosition();
                        var marker = new Vector2(
                            Math.Clamp((pixel.X - area.X) / Math.Max(1, area.Width), 0.02f, 0.98f),
                            Math.Clamp((pixel.Y - area.Y) / Math.Max(1, area.Height), 0.02f, 0.98f));
                        if (!markers.Any(m => Vector2.Distance(m, marker) < 0.03f))
                            markers.Add(marker);
                    }
                    catch
                    {
                        // skip locations that can't be resolved to a map position
                    }
                }
            }

            this.MapMarkerCache[fish.QualifiedId] = markers;
            return markers;
        }

        /// <summary>Draw a red X marker (with a light outline) centered on a map position.</summary>
        private void DrawMapCross(SpriteBatch b, int x, int y)
        {
            var outline = new Color(255, 240, 210);
            var red = new Color(205, 45, 35);
            foreach ((Color color, int length, int thickness) in new[] { (outline, 22, 8), (red, 18, 4) })
            {
                b.Draw(Game1.staminaRect, new Rectangle(x, y, length, thickness), null, color, MathF.PI / 4f, new Vector2(0.5f, 0.5f), SpriteEffects.None, 1f);
                b.Draw(Game1.staminaRect, new Rectangle(x, y, length, thickness), null, color, -MathF.PI / 4f, new Vector2(0.5f, 0.5f), SpriteEffects.None, 1f);
            }
        }

        /// <summary>Get the seasons in which a fish can be caught (for the season icon row).</summary>
        private HashSet<string> GetActiveSeasons(FishEntry fish)
        {
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "spring", "summer", "fall", "winter" };

            // union of location seasons, if known
            if (fish.Locations.Count > 0)
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (HashSet<string> seasons in fish.Locations.Values)
                {
                    if (seasons.Contains("*") || seasons.Count == 0)
                        return all;
                    result.UnionWith(seasons);
                }
                return result.Count > 0 ? result : all;
            }

            // fallback: seasons from Data/Fish
            return fish.Seasons.Count > 0 ? new HashSet<string>(fish.Seasons, StringComparer.OrdinalIgnoreCase) : all;
        }


        /*********
        ** Private methods — diary spread
        *********/
        /// <summary>Draw the diary spread: recent catches across both pages.</summary>
        private void DrawDiarySpread(SpriteBatch b, int pageWidth)
        {
            const int lineHeight = 34;
            int contentTop = this.yPositionOnScreen + 24 + HeaderHeight + 8;
            int linesPerPage = (this.height - (contentTop - this.yPositionOnScreen) - 100) / lineHeight;

            this.DrawPageHeader(b, this.I18n.Get("menu.journal.diary.title"), this.xPositionOnScreen, pageWidth);

            IReadOnlyList<CatchLogEntry> log = this.Progress.Log;
            var entries = log.Reverse().Take(linesPerPage * 2).ToList();

            if (entries.Count == 0)
            {
                string empty = this.I18n.Get("menu.journal.diary.empty");
                Vector2 size = Game1.smallFont.MeasureString(empty);
                b.DrawString(Game1.smallFont, empty, new Vector2(this.xPositionOnScreen + (pageWidth - size.X) / 2, contentTop + 40), Game1.textColor * 0.6f);
            }
            else
            {
                this.DrawDiaryEntries(b, entries.Take(linesPerPage), this.xPositionOnScreen + PagePadding, contentTop, pageWidth - 2 * PagePadding, lineHeight);
                this.DrawDiaryEntries(b, entries.Skip(linesPerPage), this.xPositionOnScreen + pageWidth + PagePadding, contentTop, pageWidth - 2 * PagePadding, lineHeight);
            }

            // today's catches at the bottom of the left page
            WorldDate today = Game1.Date;
            int caughtToday = log.Count(e => e.Year == today.Year && e.Season == today.SeasonKey && e.Day == today.DayOfMonth);
            this.DrawTextCentered(b, this.I18n.Get("menu.journal.diary.today", new { count = caughtToday }), this.xPositionOnScreen + pageWidth / 2, this.yPositionOnScreen + this.height - 88, pageWidth - 2 * PagePadding);
        }

        /// <summary>Draw a column of diary entries.</summary>
        private void DrawDiaryEntries(SpriteBatch b, IEnumerable<CatchLogEntry> entries, int x, int y, int width, int lineHeight)
        {
            foreach (CatchLogEntry entry in entries)
            {
                string fishName = this.FishById.TryGetValue(entry.QualifiedId, out FishEntry? fishEntry)
                    ? fishEntry.Data.DisplayName
                    : ItemRegistry.GetDataOrErrorItem(entry.QualifiedId).DisplayName;
                string date = this.I18n.Get("menu.journal.diary.date", new { season = this.GetSeasonDisplayName(entry.Season), day = entry.Day, year = entry.Year });
                string line = $"{date} — {fishName} — {(int)Math.Round(entry.SizeInches * InchesToCm)} {this.I18n.Get("menu.journal.cm")}";
                b.DrawString(Game1.smallFont, this.WrapAndClampLines(line, width, maxLines: 2), new Vector2(x, y), Game1.textColor * 0.9f);
                y += Game1.smallFont.MeasureString(line).X > width ? lineHeight * 2 - 8 : lineHeight;
            }
        }


        /*********
        ** Private methods — shared helpers
        *********/
        /// <summary>Draw a checkbox with a text label.</summary>
        private void DrawCheckboxLine(SpriteBatch b, int x, int y, bool isChecked, string text, int maxWidth)
        {
            b.Draw(Game1.mouseCursors, new Vector2(x, y), new Rectangle(isChecked ? 236 : 227, 425, 9, 9), Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
            this.DrawTextFit(b, text, x + 36, y, maxWidth - 36, Game1.textColor);
        }

        /// <summary>Draw a horizontal progress bar.</summary>
        private void DrawProgressBar(SpriteBatch b, int x, int y, int width, int height, float fraction)
        {
            fraction = Math.Clamp(fraction, 0f, 1f);
            b.Draw(Game1.staminaRect, new Rectangle(x - 2, y - 2, width + 4, height + 4), Game1.textColor * 0.35f);
            b.Draw(Game1.staminaRect, new Rectangle(x, y, width, height), new Color(228, 208, 168));
            if (fraction > 0f)
                b.Draw(Game1.staminaRect, new Rectangle(x, y, (int)(width * fraction), height), new Color(48, 128, 211));
        }

        /// <summary>Draw a single line of text with the standard SDV text shadow, shrinking it slightly (down to <see cref="MinTextScale"/>) if it doesn't fit, then truncating with an ellipsis as a last resort.</summary>
        private void DrawTextFit(SpriteBatch b, string text, int x, int y, int maxWidth, Color color)
        {
            float width = Game1.smallFont.MeasureString(text).X;
            float scale = width <= maxWidth ? 1f : Math.Max(MinTextScale, maxWidth / width);
            if (width * scale > maxWidth)
                text = this.FitToWidth(text, (int)(maxWidth / scale));
            Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(x, y), color, scale);
        }

        /// <summary>Draw text with the standard SDV text shadow, word-wrapped to at most <paramref name="maxLines"/> lines. Returns the drawn height in pixels.</summary>
        private int DrawTextWrapped(SpriteBatch b, string text, int x, int y, int maxWidth, Color color, int maxLines = 2)
        {
            string wrapped = this.WrapAndClampLines(text, maxWidth, maxLines);
            Utility.drawTextWithShadow(b, wrapped, Game1.smallFont, new Vector2(x, y), color);
            return (int)Game1.smallFont.MeasureString(wrapped).Y;
        }

        /// <summary>Draw a horizontally centered line of text with the standard SDV text shadow, shrinking/truncating it to fit.</summary>
        private void DrawTextCentered(SpriteBatch b, string text, int centerX, int y, int maxWidth)
        {
            float width = Game1.smallFont.MeasureString(text).X;
            float scale = width <= maxWidth ? 1f : Math.Max(MinTextScale, maxWidth / width);
            if (width * scale > maxWidth)
                text = this.FitToWidth(text, (int)(maxWidth / scale));
            float finalWidth = Game1.smallFont.MeasureString(text).X * scale;
            Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2((int)(centerX - finalWidth / 2), y), Game1.textColor, scale);
        }

        /// <summary>Truncate a string with an ellipsis so it fits the given pixel width.</summary>
        private string FitToWidth(string text, int maxWidth, SpriteFont? font = null)
        {
            font ??= Game1.smallFont;
            if (font.MeasureString(text).X <= maxWidth)
                return text;

            while (text.Length > 1 && font.MeasureString(text + "…").X > maxWidth)
                text = text[..^1].TrimEnd();
            return text + "…";
        }

        /// <summary>Word-wrap text to a pixel width and clamp it to a maximum number of lines (with an ellipsis).</summary>
        private string WrapAndClampLines(string text, int maxWidth, int maxLines)
        {
            string[] lines = Game1.parseText(text, Game1.smallFont, maxWidth).Split('\n');
            if (lines.Length <= maxLines)
                return string.Join("\n", lines);
            lines[maxLines - 1] = this.FitToWidth(lines[maxLines - 1] + "…", maxWidth);
            return string.Join("\n", lines.Take(maxLines));
        }

        /// <summary>Get the sprite for a quality star, or null for normal/unknown quality.</summary>
        private Rectangle? GetQualityStarRect(int quality)
        {
            return quality switch
            {
                1 => new Rectangle(338, 400, 8, 8), // silver
                2 => new Rectangle(346, 400, 8, 8), // gold
                4 => new Rectangle(346, 392, 8, 8), // iridium
                _ => null,
            };
        }

        /// <summary>Get the translated name for a quality level.</summary>
        private string GetQualityName(int quality)
        {
            return quality switch
            {
                0 => this.I18n.Get("menu.journal.detail.quality.normal"),
                1 => this.I18n.Get("menu.journal.detail.quality.silver"),
                2 => this.I18n.Get("menu.journal.detail.quality.gold"),
                4 => this.I18n.Get("menu.journal.detail.quality.iridium"),
                _ => this.I18n.Get("menu.journal.detail.quality.unknown"),
            };
        }

        /// <summary>Get a season's display name with a capitalized first letter.</summary>
        private string GetSeasonDisplayName(string seasonKey)
        {
            string name = this.I18n.Get($"menu.journal.detail.season.{seasonKey}").ToString();
            return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;
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
    }
}
