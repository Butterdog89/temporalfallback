using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TemporalRefuge;

internal static class BedSpawnRegistration
{
    internal const string ModId = "palmiersamasimplebedspawn";
    private const string SystemName = "SimpleBedSpawn.SimpleBedSpawnSystem";

    internal static IReadOnlyDictionary<string, BlockPos> Bind(IModLoader loader)
    {
        var mod = loader.GetMod(ModId);
        var system = mod?.Systems.SingleOrDefault(system => system.GetType().FullName == SystemName);
        if (system == null)
        {
            throw new InvalidOperationException("Simple Bed Spawn's server system was not loaded.");
        }

        // Simple Bed Spawn 1.4.0 has no public API for its registry.
        var type = system.GetType();
        var config = AccessTools.Field(type, "config")?.GetValue(system);
        var enabled = config?.GetType().GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
        if (enabled?.GetValue(config) is not true)
        {
            throw new InvalidOperationException("Simple Bed Spawn is disabled or its configuration interface has changed.");
        }

        return AccessTools.Field(type, "bedPosByPlayerUid")?.GetValue(system)
            as IReadOnlyDictionary<string, BlockPos>
            ?? throw new InvalidOperationException("Simple Bed Spawn's bed registry has changed.");
    }
}
