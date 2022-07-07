﻿namespace StardewMods.BetterChests.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewMods.BetterChests.Features;
using StardewMods.Common.Helpers;
using StardewMods.Common.Helpers.ItemRepository;
using StardewValley;
using StardewValley.Menus;

/// <summary>
///     Menu for selecting <see cref="Item" /> based on their context tags.
/// </summary>
internal class ItemSelectionMenu : ItemGrabMenu
{
    private const int HorizontalTagSpacing = 10;
    private const int VerticalTagSpacing = 5;
    private static List<Item>? CachedItems;
    private static int? CachedLineHeight;
    private static List<ClickableComponent>? CachedTags;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ItemSelectionMenu" /> class.
    /// </summary>
    /// <param name="context">The source object.</param>
    /// <param name="matcher">ItemMatcher for holding the selected item tags.</param>
    public ItemSelectionMenu(object? context, ItemMatcher matcher)
        : base(
            new List<Item>(),
            false,
            true,
            null,
            (_, _) => { },
            null,
            (_, _) => { },
            canBeExitedWithKey: false,
            source: ItemSelectionMenu.source_none,
            context: context)
    {
        this.Selected = new(matcher);
        this.Selection = matcher;

        this.ItemsToGrabMenu.actualInventory = ItemSelectionMenu.Items;
        this.DisplayedItems = BetterItemGrabMenu.ItemsToGrabMenu!;
        this.DisplayedItems.AddHighlighter(this.Selection);
        this.DisplayedItems.AddTransformer(this.SortBySelection);
        this.DisplayedItems.ItemsRefreshed += this.OnItemsRefreshed;
        this.DisplayedItems.RefreshItems();
    }

    private static IEnumerable<ClickableComponent> AllTags
    {
        get => ItemSelectionMenu.CachedTags ??= (
                                                    from item in ItemSelectionMenu.Items
                                                    from tag in item.GetContextTags()
                                                    where !tag.StartsWith("id_") && !tag.StartsWith("item_") && !tag.StartsWith("preserve_")
                                                    orderby tag
                                                    select tag)
                                                .Distinct()
                                                .Select(tag =>
                                                {
                                                    var (width, height) = Game1.smallFont.MeasureString(tag).ToPoint();
                                                    return new ClickableComponent(new(0, 0, width, height), tag);
                                                })
                                                .OrderBy(cc => cc.name)
                                                .ToList();
    }

    private static List<Item> Items
    {
        get => ItemSelectionMenu.CachedItems ??= new(from item in new ItemRepository().GetAll() select item.Item);
    }

    private static int LineHeight
    {
        get => ItemSelectionMenu.CachedLineHeight ??= ItemSelectionMenu.AllTags.Max(tag => tag.bounds.Height) + ItemSelectionMenu.VerticalTagSpacing;
    }

    private DisplayedItems DisplayedItems { get; }

    private List<ClickableComponent> DisplayedTags { get; } = new();

    private DropDownList? DropDown { get; set; }

    private int Offset { get; set; }

    private bool RefreshItems { get; set; }

    private HashSet<string> Selected { get; }

    private ItemMatcher Selection { get; }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        Game1.drawDialogueBox(
            this.ItemsToGrabMenu.xPositionOnScreen - ItemSelectionMenu.borderWidth - ItemSelectionMenu.spaceToClearSideBorder,
            this.ItemsToGrabMenu.yPositionOnScreen - ItemSelectionMenu.borderWidth - ItemSelectionMenu.spaceToClearTopBorder - 24,
            this.ItemsToGrabMenu.width + ItemSelectionMenu.borderWidth * 2 + ItemSelectionMenu.spaceToClearSideBorder * 2,
            this.ItemsToGrabMenu.height + ItemSelectionMenu.spaceToClearTopBorder + ItemSelectionMenu.borderWidth * 2 + 24,
            false,
            true);

        Game1.drawDialogueBox(
            this.inventory.xPositionOnScreen - ItemSelectionMenu.borderWidth - ItemSelectionMenu.spaceToClearSideBorder,
            this.inventory.yPositionOnScreen - ItemSelectionMenu.borderWidth - ItemSelectionMenu.spaceToClearTopBorder + 24,
            this.inventory.width + ItemSelectionMenu.borderWidth * 2 + ItemSelectionMenu.spaceToClearSideBorder * 2,
            this.inventory.height + ItemSelectionMenu.spaceToClearTopBorder + ItemSelectionMenu.borderWidth * 2 - 24,
            false,
            true);

        this.ItemsToGrabMenu.draw(b);
        this.okButton.draw(b);

        foreach (var tag in this.DisplayedTags.Where(cc => this.inventory.isWithinBounds(cc.bounds.X, cc.bounds.Bottom - this.Offset * ItemSelectionMenu.LineHeight)))
        {
            if (this.hoverText == tag.name)
            {
                Utility.drawTextWithShadow(
                    b,
                    tag.name,
                    Game1.smallFont,
                    new(tag.bounds.X, tag.bounds.Y - this.Offset * ItemSelectionMenu.LineHeight),
                    this.Selected.Contains(tag.name) ? Game1.textColor : Game1.unselectedOptionColor,
                    1f,
                    0.1f);
            }
            else
            {
                b.DrawString(
                    Game1.smallFont,
                    tag.name,
                    new(tag.bounds.X, tag.bounds.Y - this.Offset * ItemSelectionMenu.LineHeight),
                    this.Selected.Contains(tag.name) ? Game1.textColor : Game1.unselectedOptionColor);
            }
        }
    }

    /// <inheritdoc />
    public override void performHoverAction(int x, int y)
    {
        this.okButton.scale = this.okButton.containsPoint(x, y)
            ? Math.Min(1.1f, this.okButton.scale + 0.05f)
            : Math.Max(1f, this.okButton.scale - 0.05f);

        var cc = this.ItemsToGrabMenu.inventory.FirstOrDefault(slot => slot.containsPoint(x, y));
        if (cc is not null && int.TryParse(cc.name, out var slotNumber))
        {
            this.hoveredItem = this.ItemsToGrabMenu.actualInventory.ElementAtOrDefault(slotNumber);
            this.hoverText = string.Empty;
            return;
        }

        cc = this.DisplayedTags.FirstOrDefault(slot => slot.containsPoint(x, y + this.Offset * ItemSelectionMenu.LineHeight));
        if (cc is not null)
        {
            this.hoveredItem = null;
            this.hoverText = cc.name ?? string.Empty;
            return;
        }

        this.hoveredItem = null;
        this.hoverText = string.Empty;
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.okButton.containsPoint(x, y) && this.readyToClose())
        {
            this.exitThisMenu();
            if (Game1.currentLocation.currentEvent is { CurrentCommand: > 0 })
            {
                Game1.currentLocation.currentEvent.CurrentCommand++;
            }

            Game1.playSound("bigDeSelect");
            return;
        }

        // Left click an item slot to add individual item tag to filters
        var itemSlot = this.ItemsToGrabMenu.inventory.FirstOrDefault(slot => slot.containsPoint(x, y));
        if (itemSlot is not null
            && int.TryParse(itemSlot.name, out var slotNumber)
            && this.ItemsToGrabMenu.actualInventory.ElementAtOrDefault(slotNumber) is { } item
            && item.GetContextTags().FirstOrDefault(contextTag => contextTag.StartsWith("item_")) is { } tag
            && !string.IsNullOrWhiteSpace(tag))
        {
            this.AddTag(tag);
            return;
        }

        // Left click a tag on bottom menu
        itemSlot = this.DisplayedTags.FirstOrDefault(slot => slot.containsPoint(x, y + this.Offset * ItemSelectionMenu.LineHeight));
        if (itemSlot is not null && !string.IsNullOrWhiteSpace(itemSlot.name))
        {
            this.AddOrRemoveTag(itemSlot.name);
        }
    }

    /// <inheritdoc />
    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        // Right click an item slot to display dropdown with item's context tags
        if (this.ItemsToGrabMenu.inventory.FirstOrDefault(slot => slot.containsPoint(x, y)) is { } itemSlot
            && int.TryParse(itemSlot.name, out var slotNumber)
            && this.ItemsToGrabMenu.actualInventory.ElementAtOrDefault(slotNumber) is { } item)
        {
            var tags = new HashSet<string>(item.GetContextTags().Where(tag => !(tag.StartsWith("id_") || tag.StartsWith("preserve_"))));

            // Add extra quality levels
            if (tags.Contains("quality_none"))
            {
                tags.Add("quality_silver");
                tags.Add("quality_gold");
                tags.Add("quality_iridium");
            }

            if (this.DropDown is not null)
            {
                BetterItemGrabMenu.RemoveOverlay();
            }

            this.DropDown = new(tags.ToList(), x, y, this.Callback);
            BetterItemGrabMenu.AddOverlay(this.DropDown);
        }
    }

    /// <inheritdoc />
    public override void receiveScrollWheelAction(int direction)
    {
        var (x, y) = Game1.getMousePosition(true);
        if (!this.inventory.isWithinBounds(x, y))
        {
            return;
        }

        switch (direction)
        {
            case > 0 when this.Offset >= 1:
                this.Offset--;
                return;
            case < 0 when this.DisplayedTags.Last().bounds.Bottom - this.Offset * ItemSelectionMenu.LineHeight - this.inventory.yPositionOnScreen >= this.inventory.height:
                this.Offset++;
                return;
            default:
                base.receiveScrollWheelAction(direction);
                return;
        }
    }

    /// <inheritdoc />
    public override void update(GameTime time)
    {
        if (this.RefreshItems)
        {
            this.RefreshItems = false;
            foreach (var tag in this.Selected.Where(tag => !ItemSelectionMenu.AllTags.Any(cc => cc.name.Equals(tag))))
            {
                var (textWidth, textHeight) = Game1.smallFont.MeasureString(tag).ToPoint();
                ItemSelectionMenu.CachedTags?.Add(new(new(0, 0, textWidth, textHeight), tag));
            }

            this.DisplayedTags.Clear();
            this.DisplayedTags.AddRange(this.Selected.Any()
                ?
                from tag in ItemSelectionMenu.AllTags
                where this.Selected.Contains(tag.name) || this.DisplayedItems.Items.Any(item => item.HasContextTag(tag.name))
                orderby this.Selected.Contains(tag.name) ? 0 : 1, tag.name
                select tag
                :
                from tag in ItemSelectionMenu.AllTags
                where this.DisplayedItems.Items.Any(item => item.HasContextTag(tag.name))
                select tag);
            var x = this.inventory.xPositionOnScreen;
            var y = this.inventory.yPositionOnScreen;
            var matched = this.Selection.Any();

            foreach (var tag in this.DisplayedTags)
            {
                if (matched && !this.Selected.Contains(tag.name))
                {
                    matched = false;
                    x = this.inventory.xPositionOnScreen;
                    y += ItemSelectionMenu.LineHeight;
                }
                else if (x + tag.bounds.Width + ItemSelectionMenu.HorizontalTagSpacing >= this.inventory.xPositionOnScreen + this.inventory.width)
                {
                    x = this.inventory.xPositionOnScreen;
                    y += ItemSelectionMenu.LineHeight;
                }

                tag.bounds.X = x;
                tag.bounds.Y = y;
                x += tag.bounds.Width + ItemSelectionMenu.HorizontalTagSpacing;
            }
        }

        if (!this.Selected.SetEquals(this.Selection))
        {
            var added = this.Selected.Except(this.Selection).ToList();
            var removed = this.Selection.Except(this.Selected).ToList();
            foreach (var tag in added)
            {
                this.Selection.Add(tag);
            }

            foreach (var tag in removed)
            {
                this.Selection.Remove(tag);
            }

            this.DisplayedItems.RefreshItems();
        }
    }

    private void AddOrRemoveTag(string tag)
    {
        if (this.Selected.Contains(tag))
        {
            this.Selected.Remove(tag);
        }
        else
        {
            this.Selected.Add(tag);
        }
    }

    private void AddTag(string tag)
    {
        if (!this.Selected.Contains(tag))
        {
            this.Selected.Add(tag);
        }
    }

    private void Callback(string? tag)
    {
        if (tag is not null)
        {
            this.AddTag(tag);
        }

        BetterItemGrabMenu.RemoveOverlay();
        this.DropDown = null;
    }

    private void OnItemsRefreshed(object? sender, EventArgs e)
    {
        this.RefreshItems = true;
    }

    private IEnumerable<Item> SortBySelection(IEnumerable<Item> items)
    {
        return this.Selection.Any() ? items.OrderBy(item => this.Selection.Matches(item) ? 0 : 1) : items;
    }
}