﻿namespace XSPlus.Features
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using HarmonyLib;
    using StardewModdingAPI.Events;
    using StardewValley;
    using StardewValley.Objects;

    /// <inheritdoc />
    internal class CapacityFeature : FeatureWithParam<int>
    {
        private static CapacityFeature Instance = null!;
        private readonly Func<int> _getConfigCapacity;

        /// <summary>Initializes a new instance of the <see cref="CapacityFeature"/> class.</summary>
        /// <param name="getConfigCapacity">Get method for configured default capacity.</param>
        public CapacityFeature(Func<int> getConfigCapacity)
            : base("Capacity")
        {
            CapacityFeature.Instance = this;
            this._getConfigCapacity = getConfigCapacity;
        }

        /// <inheritdoc/>
        public override void Activate(IModEvents modEvents, Harmony harmony)
        {
            // Patches
            harmony.Patch(
                original: AccessTools.Method(typeof(Chest), nameof(Chest.GetActualCapacity)),
                postfix: new HarmonyMethod(typeof(CapacityFeature), nameof(CapacityFeature.Chest_GetActualCapacity_postfix)));
        }

        /// <inheritdoc/>
        public override void Deactivate(IModEvents modEvents, Harmony harmony)
        {
            // Patches
            harmony.Unpatch(
                original: AccessTools.Method(typeof(Chest), nameof(Chest.GetActualCapacity)),
                patch: AccessTools.Method(typeof(CapacityFeature), nameof(CapacityFeature.Chest_GetActualCapacity_postfix)));
        }

        /// <inheritdoc/>
        protected override bool TryGetValueForItem(Item item, out int param)
        {
            if (base.TryGetValueForItem(item, out param))
            {
                return true;
            }

            param = this._getConfigCapacity();
            return param == 0;
        }

        [SuppressMessage("ReSharper", "SA1313", Justification = "Naming is determined by Harmony.")]
        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Naming is determined by Harmony.")]
        [SuppressMessage("ReSharper", "SuggestBaseTypeForParameter", Justification = "Type is determined by Harmony.")]
        private static void Chest_GetActualCapacity_postfix(Chest __instance, ref int __result)
        {
            if (!CapacityFeature.Instance.IsEnabledForItem(__instance) || !CapacityFeature.Instance.TryGetValueForItem(__instance, out int capacity))
            {
                return;
            }

            __result = capacity switch
            {
                -1 => int.MaxValue,
                > 0 => capacity,
                _ => __result,
            };
        }
    }
}