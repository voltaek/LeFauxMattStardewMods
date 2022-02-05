﻿namespace StardewMods.BetterChests.Features;

using System;
using System.Linq;
using Common.Helpers;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewMods.BetterChests.Enums;
using StardewMods.BetterChests.Interfaces;
using StardewMods.FuryCore.Interfaces;
using StardewValley;

/// <inheritdoc />
internal class StashToChest : Feature
{
    private readonly Lazy<SlotLock> _slotLock;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StashToChest" /> class.
    /// </summary>
    /// <param name="config">Data for player configured mod options.</param>
    /// <param name="helper">SMAPI helper for events, input, and content.</param>
    /// <param name="services">Provides access to internal and external services.</param>
    public StashToChest(IConfigModel config, IModHelper helper, IModServices services)
        : base(config, helper, services)
    {
        this._slotLock = services.Lazy<SlotLock>();
    }

    private SlotLock SlotLock
    {
        get => this._slotLock.Value;
    }

    /// <inheritdoc />
    protected override void Activate()
    {
        this.Helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
    }

    /// <inheritdoc />
    protected override void Deactivate()
    {
        this.Helper.Events.Input.ButtonsChanged -= this.OnButtonsChanged;
    }

    private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
    {
        if (Context.IsPlayerFree && this.Config.ControlScheme.StashItems.JustPressed() && this.StashItems())
        {
            this.Helper.Input.SuppressActiveKeybinds(this.Config.ControlScheme.StashItems);
        }
    }

    private bool StashItems()
    {
        var eligibleChests = (
            from managedChest in this.ManagedChests.PlayerChests
            where managedChest.StashToChest >= FeatureOptionRange.Inventory
            select managedChest).ToList();

        foreach (var (placedObject, managedChest) in this.ManagedChests.PlacedChests)
        {
            if (managedChest.StashToChest == FeatureOptionRange.Disabled)
            {
                continue;
            }

            var (location, (x, y)) = placedObject;
            if (managedChest.StashToChest == FeatureOptionRange.World
                || managedChest.StashToChest == FeatureOptionRange.Location && managedChest.StashToChestDistance == -1
                || managedChest.StashToChest == FeatureOptionRange.Location && location.Equals(Game1.currentLocation) && Utility.withinRadiusOfPlayer((int)x * 64, (int)y * 64, managedChest.StashToChestDistance, Game1.player))
            {
                eligibleChests.Add(managedChest);
            }
        }

        if (!eligibleChests.Any())
        {
            Log.Trace("No eligible chests found to stash items into");
            return false;
        }

        Log.Trace("Stashing items into chests");
        var lockedSlots = this.SlotLock.LockedSlots;
        for (var index = Game1.player.Items.Count - 1; index >= 0; index--)
        {
            if (this.Config.SlotLock && lockedSlots[index])
            {
                continue;
            }

            var item = Game1.player.Items[index];
            if (item is null)
            {
                continue;
            }

            foreach (var eligibleChest in eligibleChests)
            {
                item = eligibleChest.StashItem(item);
                if (item is null)
                {
                    eligibleChest.Chest.shakeTimer = 100;
                    Game1.player.Items[index] = null;
                    break;
                }
            }
        }

        Game1.playSound("Ship");
        return true;
    }
}