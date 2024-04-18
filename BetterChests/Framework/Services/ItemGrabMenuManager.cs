namespace StardewMods.BetterChests.Framework.Services;

using System.Globalization;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewMods.BetterChests.Framework.Interfaces;
using StardewMods.BetterChests.Framework.Models.Events;
using StardewMods.BetterChests.Framework.Services.Factory;
using StardewMods.Common.Enums;
using StardewMods.Common.Interfaces;
using StardewMods.Common.Models;
using StardewMods.Common.Services;
using StardewMods.Common.Services.Integrations.BetterChests.Enums;
using StardewMods.Common.Services.Integrations.FauxCore;
using StardewValley.Buildings;
using StardewValley.Menus;
using StardewValley.Objects;

/// <summary>Manages the item grab menu in the game.</summary>
internal sealed class ItemGrabMenuManager : BaseService<ItemGrabMenuManager>
{
    private static ItemGrabMenuManager instance = null!;

    private readonly AssetHandler assetHandler;
    private readonly PerScreen<InventoryMenuManager> bottomMenu;
    private readonly ContainerFactory containerFactory;
    private readonly ContainerHandler containerHandler;
    private readonly PerScreen<IClickableMenu?> currentMenu = new();
    private readonly IEventManager eventManager;
    private readonly PerScreen<ServiceLock?> focus = new();
    private readonly IInputHelper inputHelper;
    private readonly IModConfig modConfig;
    private readonly PerScreen<InventoryMenuManager> topMenu;

    /// <summary>Initializes a new instance of the <see cref="ItemGrabMenuManager" /> class.</summary>
    /// <param name="assetHandler">Dependency used for handling assets.</param>
    /// <param name="containerFactory">Dependency used for accessing containers.</param>
    /// <param name="containerHandler">Dependency used for handling operations between containers.</param>
    /// <param name="eventManager">Dependency used for managing events.</param>
    /// <param name="log">Dependency used for logging debug information to the console.</param>
    /// <param name="manifest">Dependency for accessing mod manifest.</param>
    /// <param name="modConfig">Dependency used for accessing config data.</param>
    /// <param name="modRegistry">Dependency used for fetching metadata about loaded mods.</param>
    /// <param name="inputHelper">Dependency used for checking and changing input state.</param>
    /// <param name="patchManager">Dependency used for managing patches.</param>
    public ItemGrabMenuManager(
        AssetHandler assetHandler,
        ContainerFactory containerFactory,
        ContainerHandler containerHandler,
        IEventManager eventManager,
        ILog log,
        IManifest manifest,
        IModConfig modConfig,
        IModRegistry modRegistry,
        IInputHelper inputHelper,
        IPatchManager patchManager)
        : base(log, manifest)
    {
        // Init
        ItemGrabMenuManager.instance = this;
        this.assetHandler = assetHandler;
        this.containerFactory = containerFactory;
        this.containerHandler = containerHandler;
        this.eventManager = eventManager;
        this.inputHelper = inputHelper;
        this.modConfig = modConfig;

        this.topMenu = new PerScreen<InventoryMenuManager>(
            () => new InventoryMenuManager(eventManager, inputHelper, log, manifest, modConfig));

        this.bottomMenu = new PerScreen<InventoryMenuManager>(
            () => new InventoryMenuManager(eventManager, inputHelper, log, manifest, modConfig));

        // Events
        eventManager.Subscribe<RenderingActiveMenuEventArgs>(this.OnRenderingActiveMenu);
        eventManager.Subscribe<RenderedActiveMenuEventArgs>(this.OnRenderedActiveMenu);
        eventManager.Subscribe<UpdateTickingEventArgs>(this.OnUpdateTicking);
        eventManager.Subscribe<UpdateTickedEventArgs>(this.OnUpdateTicked);
        eventManager.Subscribe<ButtonPressedEventArgs>(this.OnButtonPressed);

        // Patches
        patchManager.Add(
            this.UniqueId,
            new SavedPatch(
                AccessTools.DeclaredMethod(typeof(IClickableMenu), nameof(IClickableMenu.SetChildMenu)),
                AccessTools.DeclaredMethod(
                    typeof(ItemGrabMenuManager),
                    nameof(ItemGrabMenuManager.IClickableMenu_SetChildMenu_postfix)),
                PatchType.Postfix),
            new SavedPatch(
                AccessTools.DeclaredMethod(typeof(InventoryMenu), nameof(InventoryMenu.draw), [typeof(SpriteBatch)]),
                AccessTools.DeclaredMethod(
                    typeof(ItemGrabMenuManager),
                    nameof(ItemGrabMenuManager.InventoryMenu_draw_prefix)),
                PatchType.Prefix),
            new SavedPatch(
                AccessTools.DeclaredMethod(typeof(InventoryMenu), nameof(InventoryMenu.draw), [typeof(SpriteBatch)]),
                AccessTools.DeclaredMethod(
                    typeof(ItemGrabMenuManager),
                    nameof(ItemGrabMenuManager.InventoryMenu_draw_postfix)),
                PatchType.Postfix),
            new SavedPatch(
                AccessTools
                    .GetDeclaredConstructors(typeof(ItemGrabMenu))
                    .Single(ctor => ctor.GetParameters().Length > 5),
                AccessTools.DeclaredMethod(
                    typeof(ItemGrabMenuManager),
                    nameof(ItemGrabMenuManager.ItemGrabMenu_constructor_transpiler)),
                PatchType.Transpiler));

        if (!modRegistry.IsLoaded("PathosChild.ChestsAnywhere"))
        {
            patchManager.Patch(this.UniqueId);
            return;
        }

        var ctorBaseChestOverlay = AccessTools.FirstConstructor(
            Type.GetType("Pathoschild.Stardew.ChestsAnywhere.Menus.Overlays.BaseChestOverlay, ChestsAnywhere"),
            c => c.GetParameters().Any(p => p.Name == "menu"));

        if (ctorBaseChestOverlay is not null)
        {
            patchManager.Add(
                this.UniqueId,
                new SavedPatch(
                    ctorBaseChestOverlay,
                    AccessTools.DeclaredMethod(
                        typeof(ItemGrabMenuManager),
                        nameof(ItemGrabMenuManager.ChestsAnywhere_BaseChestOverlay_prefix)),
                    PatchType.Prefix));
        }

        patchManager.Patch(this.UniqueId);
    }

    /// <summary>Gets the current item grab menu.</summary>
    public ItemGrabMenu? CurrentMenu =>
        Game1.activeClickableMenu?.Equals(this.currentMenu.Value) == true
            ? this.currentMenu.Value as ItemGrabMenu
            : null;

    /// <summary>Gets the inventory menu manager for the top inventory menu.</summary>
    public IInventoryMenuManager Top => this.topMenu.Value;

    /// <summary>Gets the inventory menu manager for the bottom inventory menu.</summary>
    public IInventoryMenuManager Bottom => this.bottomMenu.Value;

    /// <summary>Determines if the specified source object can receive focus.</summary>
    /// <param name="source">The object to check if it can receive focus.</param>
    /// <returns>true if the source object can receive focus; otherwise, false.</returns>
    public bool CanFocus(object source) => this.focus.Value is null || this.focus.Value.Source == source;

    /// <summary>Tries to request focus for a specific object.</summary>
    /// <param name="source">The object that needs focus.</param>
    /// <param name="serviceLock">
    /// An optional output parameter representing the acquired service lock, or null if failed to
    /// acquire.
    /// </param>
    /// <returns>true if focus was successfully acquired; otherwise, false.</returns>
    public bool TryGetFocus(object source, [NotNullWhen(true)] out IServiceLock? serviceLock)
    {
        serviceLock = null;
        if (this.focus.Value is not null && this.focus.Value.Source != source)
        {
            return false;
        }

        if (this.focus.Value is not null && this.focus.Value.Source == source)
        {
            serviceLock = this.focus.Value;
            return true;
        }

        this.focus.Value = new ServiceLock(source, this);
        serviceLock = this.focus.Value;
        return true;
    }

    private static void ChestsAnywhere_BaseChestOverlay_prefix(IClickableMenu menu, ref int topOffset)
    {
        if (menu is not ItemGrabMenu itemGrabMenu
            || !ItemGrabMenuManager.instance.containerFactory.TryGetOne(out var container))
        {
            return;
        }

        if (itemGrabMenu.ItemsToGrabMenu.capacity == 70)
        {
            topOffset = Game1.pixelZoom * -13;
        }

        if (container.Options.SearchItems is not FeatureOption.Disabled)
        {
            topOffset -= Game1.pixelZoom * 24;
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Harmony")]
    [SuppressMessage("StyleCop", "SA1313", Justification = "Harmony")]
    private static void IClickableMenu_SetChildMenu_postfix() => ItemGrabMenuManager.instance.UpdateMenu();

    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Harmony")]
    [SuppressMessage("ReSharper", "RedundantAssignment", Justification = "Harmony")]
    [SuppressMessage("StyleCop", "SA1313", Justification = "Harmony")]
    private static void InventoryMenu_draw_prefix(InventoryMenu __instance, ref InventoryMenuManager? __state)
    {
        __state = __instance.Equals(ItemGrabMenuManager.instance.topMenu.Value.Menu)
            ? ItemGrabMenuManager.instance.topMenu.Value
            : __instance.Equals(ItemGrabMenuManager.instance.bottomMenu.Value.Menu)
                ? ItemGrabMenuManager.instance.bottomMenu.Value
                : null;

        if (__state?.Container is null)
        {
            return;
        }

        // Apply operations
        var itemsDisplayingEventArgs = new ItemsDisplayingEventArgs(__state.Container);
        ItemGrabMenuManager.instance.eventManager.Publish(itemsDisplayingEventArgs);
        __instance.actualInventory = itemsDisplayingEventArgs.Items.ToList();

        var defaultName = int.MaxValue.ToString(CultureInfo.InvariantCulture);
        for (var index = 0; index < __instance.inventory.Count; ++index)
        {
            if (index >= __instance.actualInventory.Count)
            {
                __instance.inventory[index].name = defaultName;
                continue;
            }

            var actualIndex = __state.Container.Items.IndexOf(__instance.actualInventory[index]);
            __instance.inventory[index].name =
                actualIndex > -1 ? actualIndex.ToString(CultureInfo.InvariantCulture) : defaultName;
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Harmony")]
    [SuppressMessage("ReSharper", "RedundantAssignment", Justification = "Harmony")]
    [SuppressMessage("StyleCop", "SA1313", Justification = "Harmony")]
    private static void InventoryMenu_draw_postfix(InventoryMenu __instance, ref InventoryMenuManager? __state)
    {
        __state = __instance.Equals(ItemGrabMenuManager.instance.topMenu.Value.Menu)
            ? ItemGrabMenuManager.instance.topMenu.Value
            : __instance.Equals(ItemGrabMenuManager.instance.bottomMenu.Value.Menu)
                ? ItemGrabMenuManager.instance.bottomMenu.Value
                : null;

        if (__state?.Container is null)
        {
            return;
        }

        // Restore original
        __instance.actualInventory = __state.Container.Items;
    }

    private static IEnumerable<CodeInstruction>
        ItemGrabMenu_constructor_transpiler(IEnumerable<CodeInstruction> instructions) =>
        new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(OpCodes.Stloc_1))
            .Advance(-1)
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldarg_S, (short)16),
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.DeclaredMethod(
                        typeof(ItemGrabMenuManager),
                        nameof(ItemGrabMenuManager.GetChestContext))))
            .MatchStartForward(
                new CodeMatch(
                    instruction => instruction.Calls(
                        AccessTools.DeclaredMethod(typeof(Chest), nameof(Chest.GetActualCapacity)))))
            .Advance(1)
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldarg_S, (short)16),
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.DeclaredMethod(
                        typeof(ItemGrabMenuManager),
                        nameof(ItemGrabMenuManager.GetMenuCapacity))))
            .MatchStartForward(
                new CodeMatch(
                    instruction => instruction.Calls(
                        AccessTools.DeclaredMethod(typeof(Chest), nameof(Chest.GetActualCapacity)))))
            .Advance(1)
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldarg_S, (short)16),
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.DeclaredMethod(
                        typeof(ItemGrabMenuManager),
                        nameof(ItemGrabMenuManager.GetMenuCapacity))))
            .InstructionEnumeration();

    private static object? GetChestContext(Item? sourceItem, object? context) =>
        context switch
        {
            Chest chest => chest,
            SObject
            {
                heldObject.Value: Chest heldChest,
            } => heldChest,
            Building building when building.buildingChests.Any() => building.buildingChests.First(),
            GameLocation location when location.GetFridge() is Chest fridge => fridge,
            _ => sourceItem,
        };

    private static int GetMenuCapacity(int capacity, object? context)
    {
        switch (context)
        {
            case Item item when ItemGrabMenuManager.instance.containerFactory.TryGetOne(item, out var container):
            case Building building
                when ItemGrabMenuManager.instance.containerFactory.TryGetOne(building, out container):
                return container.Options.ResizeChest switch
                {
                    ChestMenuOption.Small => 9,
                    ChestMenuOption.Medium => 36,
                    ChestMenuOption.Large => 70,
                    _ when capacity is > 70 or -1 => 70,
                    _ => capacity,
                };

            default: return capacity > 70 ? 70 : capacity;
        }
    }

    private void OnUpdateTicking(UpdateTickingEventArgs e) => this.UpdateMenu();

    private void OnUpdateTicked(UpdateTickedEventArgs e) => this.UpdateMenu();

    private void UpdateHighlightMethods()
    {
        if (this.CurrentMenu is null)
        {
            return;
        }

        if (this.CurrentMenu.ItemsToGrabMenu.highlightMethod != this.topMenu.Value.HighlightMethod)
        {
            this.topMenu.Value.OriginalHighlightMethod = this.CurrentMenu.ItemsToGrabMenu.highlightMethod;
            this.CurrentMenu.ItemsToGrabMenu.highlightMethod = this.topMenu.Value.HighlightMethod;
        }

        if (this.CurrentMenu.inventory.highlightMethod != this.bottomMenu.Value.HighlightMethod)
        {
            this.bottomMenu.Value.OriginalHighlightMethod = this.CurrentMenu.inventory.highlightMethod;
            this.CurrentMenu.inventory.highlightMethod = this.bottomMenu.Value.HighlightMethod;
        }
    }

    private void UpdateMenu()
    {
        var menu = Game1.activeClickableMenu?.GetChildMenu() ?? Game1.activeClickableMenu;
        if (menu == this.currentMenu.Value)
        {
            this.UpdateHighlightMethods();
            return;
        }

        this.currentMenu.Value = menu;
        this.focus.Value = null;
        if (menu is not ItemGrabMenu itemGrabMenu)
        {
            this.topMenu.Value.Reset(null, null);
            this.bottomMenu.Value.Reset(null, null);
            this.eventManager.Publish(new ItemGrabMenuChangedEventArgs());
            return;
        }

        // Update top menu
        this.topMenu.Value.Reset(itemGrabMenu, itemGrabMenu.ItemsToGrabMenu);
        if (this.containerFactory.TryGetOne(out var topContainer))
        {
            itemGrabMenu.behaviorFunction = topContainer.GrabItemFromInventory;
            itemGrabMenu.behaviorOnItemGrab = topContainer.GrabItemFromChest;
        }

        this.topMenu.Value.Container = topContainer;

        // Update bottom menu
        this.bottomMenu.Value.Reset(itemGrabMenu, itemGrabMenu.inventory);
        if (!itemGrabMenu.inventory.actualInventory.Equals(Game1.player.Items)
            || !this.containerFactory.TryGetOne(Game1.player, out var bottomContainer))
        {
            bottomContainer = null;
        }

        this.bottomMenu.Value.Container = bottomContainer;

        // Reset filters
        this.UpdateHighlightMethods();
        this.eventManager.Publish(new ItemGrabMenuChangedEventArgs());

        // Disable background fade
        itemGrabMenu.setBackgroundTransparency(false);
    }

    private void OnButtonPressed(ButtonPressedEventArgs e)
    {
        if (this.CurrentMenu is null || e.Button is not SButton.MouseLeft)
        {
            return;
        }

        var (mouseX, mouseY) = Game1.getMousePosition(true);
        if (this.Top.Container is null
            || this.Bottom.Container is null
            || !this.modConfig.Controls.TransferItems.IsDown()
            || this.CurrentMenu.fillStacksButton is null
            || !this.CurrentMenu.fillStacksButton.containsPoint(mouseX, mouseY))
        {
            return;
        }

        this.inputHelper.Suppress(e.Button);
        var (from, to) = this.modConfig.Controls.TransferItemsReverse.IsDown()
            ? (this.Top.Container, this.Bottom.Container)
            : (this.Bottom.Container, this.Top.Container);

        if (!this.containerHandler.Transfer(this.Bottom.Container, this.Top.Container, out var amounts, true))
        {
            return;
        }

        foreach (var (name, amount) in amounts)
        {
            if (amount > 0)
            {
                this.Log.Trace(
                    "{0}: {{ Item: {1}, Quantity: {2}, From: {3}, To: {4} }}",
                    this.Id,
                    name,
                    amount,
                    from,
                    to);
            }
        }
    }

    [Priority(int.MaxValue)]
    private void OnRenderingActiveMenu(RenderingActiveMenuEventArgs e)
    {
        if (this.CurrentMenu is null || Game1.options.showClearBackgrounds)
        {
            return;
        }

        // Redraw background
        e.SpriteBatch.Draw(
            Game1.fadeToBlackRect,
            new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height),
            Color.Black * 0.5f);

        Game1.mouseCursorTransparency = 0f;

        if (this.CurrentMenu.fillStacksButton is null)
        {
            return;
        }

        var (mouseX, mouseY) = Game1.getMousePosition(true);
        if (!this.modConfig.Controls.TransferItems.IsDown()
            || !this.CurrentMenu.fillStacksButton.containsPoint(mouseX, mouseY))
        {
            this.CurrentMenu.fillStacksButton.texture = Game1.mouseCursors;
            this.CurrentMenu.fillStacksButton.sourceRect = new Rectangle(103, 469, 16, 16);
            this.CurrentMenu.fillStacksButton.hoverText = Game1.content.LoadString("Strings\\UI:ItemGrab_FillStacks");
            return;
        }

        this.CurrentMenu.fillStacksButton.texture = this.assetHandler.Icons.Value;

        this.CurrentMenu.fillStacksButton.sourceRect = this.modConfig.Controls.TransferItemsReverse.IsDown()
            ? new Rectangle(96, 0, 16, 16)
            : new Rectangle(80, 0, 16, 16);

        this.CurrentMenu.fillStacksButton.hoverText = this.modConfig.Controls.TransferItemsReverse.IsDown()
            ? I18n.Button_TransferDown_Name()
            : I18n.Button_TransferUp_Name();
    }

    [Priority(int.MinValue)]
    private void OnRenderedActiveMenu(RenderedActiveMenuEventArgs e)
    {
        if (this.CurrentMenu is null)
        {
            return;
        }

        // Draw overlay
        this.topMenu.Value.Draw(e.SpriteBatch);
        this.bottomMenu.Value.Draw(e.SpriteBatch);

        // Redraw foreground
        if (this.focus.Value is null)
        {
            if (this.CurrentMenu.hoverText != null
                && (this.CurrentMenu.hoveredItem == null || this.CurrentMenu.ItemsToGrabMenu == null))
            {
                if (this.CurrentMenu.hoverAmount > 0)
                {
                    IClickableMenu.drawToolTip(
                        e.SpriteBatch,
                        this.CurrentMenu.hoverText,
                        string.Empty,
                        null,
                        true,
                        -1,
                        0,
                        null,
                        -1,
                        null,
                        this.CurrentMenu.hoverAmount);
                }
                else
                {
                    IClickableMenu.drawHoverText(e.SpriteBatch, this.CurrentMenu.hoverText, Game1.smallFont);
                }
            }

            if (this.CurrentMenu.hoveredItem != null)
            {
                IClickableMenu.drawToolTip(
                    e.SpriteBatch,
                    this.CurrentMenu.hoveredItem.getDescription(),
                    this.CurrentMenu.hoveredItem.DisplayName,
                    this.CurrentMenu.hoveredItem,
                    this.CurrentMenu.heldItem != null);
            }
            else if (this.CurrentMenu.hoveredItem != null && this.CurrentMenu.ItemsToGrabMenu != null)
            {
                IClickableMenu.drawToolTip(
                    e.SpriteBatch,
                    this.CurrentMenu.ItemsToGrabMenu.descriptionText,
                    this.CurrentMenu.ItemsToGrabMenu.descriptionTitle,
                    this.CurrentMenu.hoveredItem,
                    this.CurrentMenu.heldItem != null);
            }

            this.CurrentMenu.heldItem?.drawInMenu(
                e.SpriteBatch,
                new Vector2(Game1.getOldMouseX() + 8, Game1.getOldMouseY() + 8),
                1f);
        }

        Game1.mouseCursorTransparency = 1f;
        this.CurrentMenu.drawMouse(e.SpriteBatch);
    }

    private sealed class ServiceLock(object source, ItemGrabMenuManager itemGrabMenuManager) : IServiceLock
    {
        public object Source => source;

        public void Release()
        {
            if (itemGrabMenuManager.focus.Value == this)
            {
                itemGrabMenuManager.focus.Value = null;
            }
        }
    }
}