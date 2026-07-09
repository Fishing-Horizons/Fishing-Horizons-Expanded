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
    /// Spread 0 is the title page (with the collection progress).
    /// The following spreads list all fish grouped into sections by source mod; every list page is
    /// a 2×2 grid (4 fish) at the same position on every page, with section titles at the top edge
    /// of a section's first page. Uncaught fish are shown as black silhouettes with hidden names.
    /// Clicking a fish opens its detail spread: stats, length slider and Willy's note on the left
    /// page; the world map, locations, season/time/weather info on the right page.
    /// The last spread is the catch diary (recent catches) and overall statistics.
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


        /*********
        ** Fields
        *********/
        /// <summary>The mod translations.</summary>
        private readonly ITranslationHelper I18n;

        /// <summary>The tracked journal progress (sold/gifted/best quality/catch diary).</summary>
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

        /// <summary>The world map base texture, if available.</summary>
        private readonly MapAreaTexture? WorldMap;

        /// <summary>Willy's portrait texture, if available.</summary>
        private readonly Texture2D? WillyPortrait;

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
            foreach (JournalSection section in FishRegistry.BuildSections(modRegistry, i18n.Get("menu.journal.section.vanilla")))
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
                this.WorldMap = WorldMapManager.GetMapRegions().FirstOrDefault()?.GetBaseTexture();
            }
            catch
            {
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

            // detail spread: any click goes back to the list
            if (this.DetailFish is not null)
            {
                this.DetailFish = null;
                Game1.playSound("shwip");
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
        }

        /// <inheritdoc/>
        public override void draw(SpriteBatch b)
        {
            // dim the game behind the menu
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

            // book background (placeholder: standard menu texture box split into two pages)
            int pageWidth = this.width / 2;
            drawTextureBox(b, this.xPositionOnScreen, this.yPositionOnScreen, pageWidth, this.height, Color.White);
            drawTextureBox(b, this.xPositionOnScreen + pageWidth, this.yPositionOnScreen, pageWidth, this.height, Color.White);

            if (this.DetailFish is not null)
            {
                this.DrawDetailSpread(b, this.DetailFish, pageWidth);
                this.BackButton.draw(b);
            }
            else if (this.Spread == 0)
            {
                this.DrawTitleSpread(b, pageWidth);
                if (this.Spread < this.SpreadCount - 1)
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

            base.draw(b);
            this.drawMouse(b);
        }


        /*********
        ** Private methods — title spread
        *********/
        /// <summary>Draw the title spread (spread 0): book title, collection progress and a hint.</summary>
        private void DrawTitleSpread(SpriteBatch b, int pageWidth)
        {
            int contentWidth = pageWidth - 2 * PagePadding;

            string title = this.I18n.Get("menu.journal.title");
            SpriteText.drawStringHorizontallyCenteredAt(
                b,
                title,
                this.xPositionOnScreen + pageWidth / 2,
                this.yPositionOnScreen + 96
            );

            // collection progress
            int caught = this.AllFish.Count(f => f.IsCaught());
            int total = this.AllFish.Count;
            int percent = total > 0 ? (int)Math.Round(100.0 * caught / total) : 0;
            string collection = this.I18n.Get("menu.journal.collection.progress", new { caught, total, percent });
            Vector2 collectionSize = Game1.smallFont.MeasureString(collection);
            int cy = this.yPositionOnScreen + 220;
            b.DrawString(Game1.smallFont, collection, new Vector2(this.xPositionOnScreen + (pageWidth - collectionSize.X) / 2, cy), Game1.textColor);
            this.DrawProgressBar(b, this.xPositionOnScreen + PagePadding + 32, cy + 40, contentWidth - 64, 16, total > 0 ? (float)caught / total : 0f);

            string hint = this.I18n.Get("menu.journal.turn-page-hint");
            Vector2 hintSize = Game1.smallFont.MeasureString(hint);
            b.DrawString(
                Game1.smallFont,
                hint,
                new Vector2(this.xPositionOnScreen + (pageWidth - hintSize.X) / 2, this.yPositionOnScreen + this.height - 120),
                Game1.textColor * 0.75f
            );
        }


        /*********
        ** Private methods — list view
        *********/
        /// <summary>The height reserved at the top of every page for a section title, so the grid is identical on all pages.</summary>
        private const int HeaderHeight = 56;

        /// <summary>Get the bounds of a fish slot on a page (2×2 grid). The grid position is the same on every page, whether or not a section title is shown.</summary>
        /// <param name="pageX">The page's left screen coordinate.</param>
        /// <param name="pageWidth">The page width.</param>
        /// <param name="slot">The slot index (0–3: left-to-right, top-to-bottom).</param>
        private Rectangle GetSlotBounds(int pageX, int pageWidth, int slot)
        {
            int topMargin = 24 + HeaderHeight;
            int gridWidth = pageWidth - 64;
            int gridHeight = this.height - topMargin - 32;
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
                if (pageIndex >= this.Pages.Count)
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
                this.DrawPageHeader(b, page.Header, pageX, pageWidth);

            for (int slot = 0; slot < page.Fish.Count; slot++)
                this.DrawFishCell(b, page.Fish[slot], this.GetSlotBounds(pageX, pageWidth, slot));
        }

        /// <summary>Draw a section title at the top edge of a page.</summary>
        private void DrawPageHeader(SpriteBatch b, string title, int pageX, int pageWidth)
        {
            Vector2 size = Game1.dialogueFont.MeasureString(title);
            Vector2 position = new(pageX + (pageWidth - size.X) / 2, this.yPositionOnScreen + 24);
            b.DrawString(Game1.dialogueFont, title, position, Game1.textColor);

            // divider line under the title
            int lineY = (int)(position.Y + size.Y + 4);
            b.Draw(Game1.staminaRect, new Rectangle(pageX + 48, lineY, pageWidth - 96, 2), Game1.textColor * 0.35f);
        }

        /// <summary>Draw a fish cell: sprite on top, name and brief info below, centered in its grid slot.</summary>
        private void DrawFishCell(SpriteBatch b, FishEntry fish, Rectangle bounds)
        {
            bool caught = fish.IsCaught();
            int spriteSize = (int)(16 * SpriteScale);

            string name = caught ? fish.Data.DisplayName : this.I18n.Get("menu.journal.unknown-fish");
            string info = caught
                ? this.I18n.Get("menu.journal.caught-count", new { count = fish.TimesCaught() })
                : this.I18n.Get("menu.journal.not-caught-hint");
            Vector2 nameSize = Game1.smallFont.MeasureString(name);
            Vector2 infoSize = Game1.smallFont.MeasureString(info);

            // center the content block vertically in the slot
            int contentHeight = spriteSize + 2 + (int)nameSize.Y + (int)infoSize.Y;
            int top = bounds.Y + Math.Max(0, (bounds.Height - contentHeight) / 2);

            // sprite (black silhouette if not caught yet)
            b.Draw(
                fish.Data.GetTexture(),
                new Vector2(bounds.X + (bounds.Width - spriteSize) / 2f, top),
                fish.Data.GetSourceRect(),
                caught ? Color.White : Color.Black,
                0f, Vector2.Zero, SpriteScale, SpriteEffects.None, 1f);

            // best quality star in the cell's top-right corner (like the mockup)
            if (caught)
            {
                int quality = this.Progress.Get(fish.QualifiedId).BestQuality;
                Rectangle? star = this.GetQualityStarRect(quality);
                if (star.HasValue)
                    b.Draw(Game1.mouseCursors, new Vector2(bounds.Right - 40, bounds.Y + 8), star.Value, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
            }

            // name (hidden until caught)
            b.DrawString(Game1.smallFont, name, new Vector2(bounds.X + (bounds.Width - nameSize.X) / 2, top + spriteSize + 2), Game1.textColor);

            // brief info
            b.DrawString(Game1.smallFont, info, new Vector2(bounds.X + (bounds.Width - infoSize.X) / 2, top + spriteSize + 2 + nameSize.Y), Game1.textColor * 0.6f);
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

        /// <summary>Draw the detail spread's left page: name, sprite, description, stats, length slider and Willy's note.</summary>
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

            // description (hidden until caught)
            string description = caught ? (fish.Data.Description ?? "") : this.I18n.Get("menu.journal.detail.hidden-description");
            string wrapped = Game1.parseText(description, Game1.smallFont, contentWidth);
            b.DrawString(Game1.smallFont, wrapped, new Vector2(textX, y), Game1.textColor * 0.9f);
            y += (int)Game1.smallFont.MeasureString(wrapped).Y + 12;

            const int lineHeight = 31;

            // caught status
            this.DrawCheckboxLine(b, textX, y, caught, this.I18n.Get(caught ? "menu.journal.detail.caught" : "menu.journal.not-caught-hint"));
            y += lineHeight;

            if (caught)
            {
                // times caught
                b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.caught-count", new { count = fish.TimesCaught() }), new Vector2(textX, y), Game1.textColor);
                y += lineHeight;

                // max caught size (+ "new record!" badge on the day a record is set)
                string maxSize = this.I18n.Get("menu.journal.detail.max-size", new { cm = (int)Math.Round(fish.MaxCaughtSize() * InchesToCm) });
                b.DrawString(Game1.smallFont, maxSize, new Vector2(textX, y), Game1.textColor);
                if (progress.RecordDay == Game1.Date.TotalDays)
                {
                    float offset = Game1.smallFont.MeasureString(maxSize).X + 16;
                    b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.detail.new-record"), new Vector2(textX + offset, y), new Color(178, 112, 15));
                }
                y += lineHeight;

                // big specimen
                this.DrawCheckboxLine(b, textX, y, fish.HasBigSpecimen(), this.I18n.Get("menu.journal.detail.big-specimen"));
                y += lineHeight;

                // sold / gifted (tracked since the mod was installed)
                this.DrawCheckboxLine(b, textX, y, progress.Sold, this.I18n.Get("menu.journal.detail.sold"));
                y += lineHeight;
                this.DrawCheckboxLine(b, textX, y, progress.Gifted, this.I18n.Get("menu.journal.detail.gifted"));
                y += lineHeight;

                // best quality (star icon, or text for normal/unknown)
                Rectangle? star = this.GetQualityStarRect(progress.BestQuality);
                string qualityLabel = this.I18n.Get("menu.journal.detail.best-quality", new { quality = star.HasValue ? "" : (string)this.GetQualityName(progress.BestQuality) }).ToString().TrimEnd();
                b.DrawString(Game1.smallFont, qualityLabel, new Vector2(textX, y), Game1.textColor);
                if (star.HasValue)
                    b.Draw(Game1.mouseCursors, new Vector2(textX + Game1.smallFont.MeasureString(qualityLabel).X + 10, y + 2), star.Value, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
                y += lineHeight;
            }

            // possible length range + slider showing the best catch
            if (fish.MinPossibleSize > 0 && fish.MaxPossibleSize > fish.MinPossibleSize)
            {
                int minCm = (int)Math.Round(fish.MinPossibleSize * InchesToCm);
                int maxCm = (int)Math.Round(fish.MaxPossibleSize * InchesToCm);
                b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.detail.size-range", new { min = minCm, max = maxCm }), new Vector2(textX, y), Game1.textColor);
                y += lineHeight + 2;

                float fraction = caught
                    ? Math.Clamp((fish.MaxCaughtSize() - fish.MinPossibleSize) / (float)(fish.MaxPossibleSize - fish.MinPossibleSize), 0f, 1f)
                    : 0f;
                this.DrawProgressBar(b, textX, y, contentWidth, 12, fraction);
                b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.detail.size-cm", new { cm = minCm }), new Vector2(textX, y + 16), Game1.textColor * 0.7f, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 1f);
                string maxLabel = this.I18n.Get("menu.journal.detail.size-cm", new { cm = maxCm });
                b.DrawString(Game1.smallFont, maxLabel, new Vector2(textX + contentWidth - Game1.smallFont.MeasureString(maxLabel).X * 0.85f, y + 16), Game1.textColor * 0.7f, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 1f);
                y += 48;
            }

            // Willy's note, pinned to the bottom of the page
            int noteHeight = 124;
            int noteY = this.yPositionOnScreen + this.height - noteHeight - 28;
            if (noteY >= y - 6)
                this.DrawWillyNote(b, fish, leftX + PagePadding - 16, noteY, pageWidth - 2 * (PagePadding - 16), noteHeight);
        }

        /// <summary>Draw Willy's note box with his portrait and a short hint about the fish.</summary>
        private void DrawWillyNote(SpriteBatch b, FishEntry fish, int x, int y, int width, int height)
        {
            drawTextureBox(b, x, y, width, height, Color.White);

            int innerX = x + 20;
            int innerY = y + 20;
            int portraitSize = 0;

            if (this.WillyPortrait is not null)
            {
                portraitSize = 84;
                b.Draw(this.WillyPortrait, new Rectangle(innerX, innerY, portraitSize, portraitSize), new Rectangle(0, 0, 64, 64), Color.White);
            }

            int noteX = innerX + (portraitSize > 0 ? portraitSize + 16 : 0);
            int noteWidth = x + width - 20 - noteX;

            string title = this.I18n.Get("menu.journal.note.title");
            b.DrawString(Game1.smallFont, title, new Vector2(noteX, innerY - 4), new Color(120, 78, 43));

            string note = Game1.parseText(this.GetWillyNote(fish), Game1.smallFont, noteWidth);
            b.DrawString(Game1.smallFont, note, new Vector2(noteX, innerY + 24), Game1.textColor * 0.9f, 0f, Vector2.Zero, 0.9f, SpriteEffects.None, 1f);
        }

        /// <summary>Pick a short note from Willy about a fish, based on its data.</summary>
        private string GetWillyNote(FishEntry fish)
        {
            if (fish.IsTrapFish)
                return this.I18n.Get("menu.journal.note.trap");
            if (fish.Weather == "rainy")
                return this.I18n.Get("menu.journal.note.rain");
            if (fish.Weather == "sunny")
                return this.I18n.Get("menu.journal.note.sun");

            bool allDay = fish.TimeRanges.Count == 0 || fish.TimeRanges.Any(r => r.X <= 600 && r.Y >= 2600);
            if (!allDay && fish.TimeRanges.Count > 0)
            {
                if (fish.TimeRanges[0].X >= 1600)
                    return this.I18n.Get("menu.journal.note.night");
                if (fish.TimeRanges[0].Y <= 1500)
                    return this.I18n.Get("menu.journal.note.morning");
            }

            // stable per-fish hash (string.GetHashCode is randomized per game launch)
            int hash = 0;
            foreach (char c in fish.QualifiedId)
                hash = (hash * 31 + c) & 0x7FFFFFFF;
            int variant = 1 + hash % 3;
            return this.I18n.Get($"menu.journal.note.generic.{variant}");
        }

        /// <summary>Draw the detail spread's right page: world map, locations, season/time/weather.</summary>
        private void DrawDetailRightPage(SpriteBatch b, FishEntry fish, int pageWidth)
        {
            int rightX = this.xPositionOnScreen + pageWidth;
            int contentWidth = pageWidth - 2 * PagePadding;
            int textX = rightX + PagePadding;
            int ry = this.yPositionOnScreen + 32;

            // header
            string title = this.I18n.Get("menu.journal.detail.where-when-title");
            Vector2 titleSize = Game1.dialogueFont.MeasureString(title);
            b.DrawString(Game1.dialogueFont, title, new Vector2(rightX + (pageWidth - titleSize.X) / 2, ry), Game1.textColor);
            ry += (int)titleSize.Y + 12;

            // world map
            if (this.WorldMap is not null)
            {
                int mapWidth = contentWidth - 40;
                int mapHeight = this.WorldMap.SourceRect.Height * mapWidth / Math.Max(1, this.WorldMap.SourceRect.Width);
                int mapX = rightX + (pageWidth - mapWidth) / 2;
                drawTextureBox(b, mapX - 16, ry - 16, mapWidth + 32, mapHeight + 32, Color.White);
                b.Draw(this.WorldMap.Texture, new Rectangle(mapX, ry, mapWidth, mapHeight), this.WorldMap.SourceRect, Color.White);
                ry += mapHeight + 28;
            }

            // trap fish: simple text instead of season/time/weather rows
            if (fish.IsTrapFish)
            {
                string water = this.I18n.Get(fish.TrapWaterType == "ocean" ? "menu.journal.detail.water.ocean" : "menu.journal.detail.water.freshwater");
                string trap = Game1.parseText(this.I18n.Get("menu.journal.detail.trap-fish", new { water }), Game1.smallFont, contentWidth);
                b.DrawString(Game1.smallFont, trap, new Vector2(textX, ry), Game1.textColor * 0.9f);
                return;
            }

            // locations (up to 3, then "+N more")
            HashSet<string> activeSeasons = this.GetActiveSeasons(fish);
            if (fish.Locations.Count > 0)
            {
                var locations = fish.Locations.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
                foreach ((string location, HashSet<string> seasons) in locations.Take(3))
                {
                    string seasonText = seasons.Contains("*") || seasons.Count == 0 || seasons.Count >= 4
                        ? this.I18n.Get("menu.journal.detail.season.any")
                        : string.Join(", ", seasons
                            .OrderBy(s => Array.IndexOf(new[] { "spring", "summer", "fall", "winter" }, s))
                            .Select(s => (string)this.I18n.Get($"menu.journal.detail.season.{s}")));
                    string line = $"• {location} — {seasonText}";

                    // shrink very long lines so they fit the page
                    float scale = Math.Min(1f, contentWidth / Math.Max(1f, Game1.smallFont.MeasureString(line).X));
                    b.DrawString(Game1.smallFont, line, new Vector2(textX, ry), Game1.textColor * 0.9f, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
                    ry += 28;
                }
                if (locations.Count > 3)
                {
                    b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.detail.locations-more", new { count = locations.Count - 3 }), new Vector2(textX, ry), Game1.textColor * 0.6f);
                    ry += 28;
                }
            }
            ry += 8;

            // season icons row
            b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.detail.season-label"), new Vector2(textX, ry), Game1.textColor);
            {
                int iconX = textX + 130;
                string[] seasonKeys = { "spring", "summer", "fall", "winter" };
                for (int i = 0; i < 4; i++)
                {
                    bool active = activeSeasons.Contains(seasonKeys[i]);
                    b.Draw(Game1.mouseCursors, new Vector2(iconX, ry), new Rectangle(406, 441 + i * 8, 12, 8), active ? Color.White : Color.Black * 0.2f, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
                    iconX += 48;
                }
            }
            ry += 40;

            // time bar
            bool allDay = fish.TimeRanges.Count == 0 || fish.TimeRanges.Any(r => r.X <= 600 && r.Y >= 2600);
            string timeText = allDay
                ? this.I18n.Get("menu.journal.detail.time.all-day")
                : string.Join(", ", fish.TimeRanges.Select(r => $"{FormatTime(r.X)}–{FormatTime(r.Y)}"));
            b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.detail.time-line", new { time = timeText }), new Vector2(textX, ry), Game1.textColor);
            ry += 30;
            this.DrawTimeBar(b, fish, textX, ry, contentWidth, 14, allDay);
            ry += 52;

            // weather icons row
            b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.detail.weather-label"), new Vector2(textX, ry), Game1.textColor);
            {
                bool sunny = fish.Weather is "sunny" or "both";
                bool rainy = fish.Weather is "rainy" or "both";
                b.Draw(Game1.mouseCursors, new Vector2(textX + 130, ry), new Rectangle(341, 421, 12, 8), sunny ? Color.White : Color.Black * 0.2f, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
                b.Draw(Game1.mouseCursors, new Vector2(textX + 178, ry), new Rectangle(365, 421, 12, 8), rainy ? Color.White : Color.Black * 0.2f, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
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

        /// <summary>Draw the day timeline bar (6:00–2:00) with the fish's active time ranges highlighted.</summary>
        private void DrawTimeBar(SpriteBatch b, FishEntry fish, int x, int y, int width, int height, bool allDay)
        {
            const int dayStart = 600;
            const int dayEnd = 2600;

            // colored segments (morning/day/evening/night), dimmed outside the active ranges
            for (int px = 0; px < width; px += 3)
            {
                int time = dayStart + (dayEnd - dayStart) * px / width;
                Color color =
                    time < 1100 ? new Color(240, 214, 108)
                    : time < 1700 ? new Color(154, 200, 96)
                    : time < 2000 ? new Color(233, 152, 82)
                    : new Color(120, 133, 191);
                bool active = allDay || fish.TimeRanges.Any(r => time >= r.X && time < r.Y);
                b.Draw(Game1.staminaRect, new Rectangle(x + px, y, Math.Min(3, width - px), height), color * (active ? 1f : 0.22f));
            }

            // border
            b.Draw(Game1.staminaRect, new Rectangle(x, y - 2, width, 2), Game1.textColor * 0.4f);
            b.Draw(Game1.staminaRect, new Rectangle(x, y + height, width, 2), Game1.textColor * 0.4f);

            // tick labels
            (float Fraction, string Label)[] ticks = { (0f, "6:00"), (0.3f, "12:00"), (0.6f, "18:00"), (1f, "2:00") };
            foreach ((float fraction, string label) in ticks)
            {
                float labelWidth = Game1.smallFont.MeasureString(label).X * 0.75f;
                float labelX = x + fraction * width - (fraction >= 1f ? labelWidth : fraction <= 0f ? 0 : labelWidth / 2);
                b.DrawString(Game1.smallFont, label, new Vector2(labelX, y + height + 6), Game1.textColor * 0.7f, 0f, Vector2.Zero, 0.75f, SpriteEffects.None, 1f);
            }
        }


        /*********
        ** Private methods — diary spread
        *********/
        /// <summary>Draw the diary spread: recent catches on the left page, statistics on the right page.</summary>
        private void DrawDiarySpread(SpriteBatch b, int pageWidth)
        {
            this.DrawDiaryLeftPage(b, pageWidth);
            this.DrawDiaryRightPage(b, pageWidth);
        }

        /// <summary>Draw the diary's left page: the recent catch log.</summary>
        private void DrawDiaryLeftPage(SpriteBatch b, int pageWidth)
        {
            int leftX = this.xPositionOnScreen;
            int contentWidth = pageWidth - 2 * PagePadding;
            int textX = leftX + PagePadding;

            this.DrawPageHeader(b, this.I18n.Get("menu.journal.diary.title"), leftX, pageWidth);
            int y = this.yPositionOnScreen + 24 + HeaderHeight + 8;

            IReadOnlyList<CatchLogEntry> log = this.Progress.Log;
            if (log.Count == 0)
            {
                string empty = this.I18n.Get("menu.journal.diary.empty");
                Vector2 size = Game1.smallFont.MeasureString(empty);
                b.DrawString(Game1.smallFont, empty, new Vector2(leftX + (pageWidth - size.X) / 2, y + 40), Game1.textColor * 0.6f);
            }
            else
            {
                const int lineHeight = 34;
                int maxLines = (this.height - (y - this.yPositionOnScreen) - 90) / lineHeight;
                foreach (CatchLogEntry entry in log.Reverse().Take(maxLines))
                {
                    string fishName = this.FishById.TryGetValue(entry.QualifiedId, out FishEntry? fishEntry)
                        ? fishEntry.Data.DisplayName
                        : ItemRegistry.GetDataOrErrorItem(entry.QualifiedId).DisplayName;
                    string date = this.I18n.Get("menu.journal.diary.date", new { season = this.GetSeasonDisplayName(entry.Season), day = entry.Day, year = entry.Year });
                    string line = $"{date} — {fishName} — {(int)Math.Round(entry.SizeInches * InchesToCm)} {this.I18n.Get("menu.journal.cm")}";

                    float scale = Math.Min(1f, contentWidth / Math.Max(1f, Game1.smallFont.MeasureString(line).X));
                    b.DrawString(Game1.smallFont, line, new Vector2(textX, y), Game1.textColor * 0.9f, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
                    y += lineHeight;
                }
            }

            // total catches at the bottom
            int totalCatches = this.AllFish.Sum(f => f.TimesCaught());
            string total = this.I18n.Get("menu.journal.diary.total", new { count = totalCatches });
            Vector2 totalSize = Game1.smallFont.MeasureString(total);
            b.DrawString(Game1.smallFont, total, new Vector2(leftX + (pageWidth - totalSize.X) / 2, this.yPositionOnScreen + this.height - 64), Game1.textColor);
        }

        /// <summary>Draw the diary's right page: overall statistics and collection progress.</summary>
        private void DrawDiaryRightPage(SpriteBatch b, int pageWidth)
        {
            int rightX = this.xPositionOnScreen + pageWidth;
            int contentWidth = pageWidth - 2 * PagePadding;
            int textX = rightX + PagePadding;

            this.DrawPageHeader(b, this.I18n.Get("menu.journal.stats.title"), rightX, pageWidth);
            int y = this.yPositionOnScreen + 24 + HeaderHeight + 16;

            // statistics
            int totalCatches = this.AllFish.Sum(f => f.TimesCaught());
            int species = this.AllFish.Count(f => f.IsCaught());
            int bigSpecimens = this.AllFish.Count(f => f.IsCaught() && f.HasBigSpecimen());
            int maxSizeCm = (int)Math.Round(this.AllFish.Select(f => f.MaxCaughtSize()).DefaultIfEmpty(0).Max() * InchesToCm);
            IReadOnlyList<CatchLogEntry> log = this.Progress.Log;
            string avgSize = log.Count > 0
                ? ((int)Math.Round(log.Average(e => e.SizeInches) * InchesToCm)).ToString()
                : "—";

            const int lineHeight = 38;
            var lines = new List<string>
            {
                this.I18n.Get("menu.journal.stats.total-caught", new { count = totalCatches }),
                this.I18n.Get("menu.journal.stats.species", new { count = species }),
                this.I18n.Get("menu.journal.stats.big", new { count = bigSpecimens }),
                this.I18n.Get("menu.journal.stats.max-size", new { cm = maxSizeCm }),
                this.I18n.Get("menu.journal.stats.avg-size", new { cm = avgSize }),
            };
            foreach (string line in lines)
            {
                b.DrawString(Game1.smallFont, line, new Vector2(textX, y), Game1.textColor);
                y += lineHeight;
            }
            y += 16;

            // collection progress
            string collectionTitle = this.I18n.Get("menu.journal.collection.title");
            Vector2 collectionTitleSize = Game1.dialogueFont.MeasureString(collectionTitle);
            b.DrawString(Game1.dialogueFont, collectionTitle, new Vector2(rightX + (pageWidth - collectionTitleSize.X) / 2, y), Game1.textColor);
            y += (int)collectionTitleSize.Y + 12;

            int caught = species;
            int total = this.AllFish.Count;
            int percent = total > 0 ? (int)Math.Round(100.0 * caught / total) : 0;
            b.DrawString(Game1.smallFont, this.I18n.Get("menu.journal.collection.progress", new { caught, total, percent }), new Vector2(textX, y), Game1.textColor);
            y += 36;
            this.DrawProgressBar(b, textX, y, contentWidth, 16, total > 0 ? (float)caught / total : 0f);
        }


        /*********
        ** Private methods — shared helpers
        *********/
        /// <summary>Draw a checkbox with a text label.</summary>
        private void DrawCheckboxLine(SpriteBatch b, int x, int y, bool isChecked, string text)
        {
            b.Draw(Game1.mouseCursors, new Vector2(x, y), new Rectangle(isChecked ? 236 : 227, 425, 9, 9), Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
            b.DrawString(Game1.smallFont, text, new Vector2(x + 36, y), Game1.textColor);
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
