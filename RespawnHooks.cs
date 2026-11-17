using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace TemporalRefuge;

internal static class RespawnHooks
{
    [ThreadStatic] private static EntityAgent? gearUser;

    internal static MethodInfo BindRespawn() => Require(
        typeof(ServerSystemEntitySimulation), "OnPlayerRespawn", typeof(void), typeof(IServerPlayer));

    internal static void Install(Harmony harmony, MethodInfo respawn)
    {
        var gear = Require(typeof(ItemTemporalGear), nameof(ItemTemporalGear.OnHeldInteractStop), typeof(void),
            typeof(float), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection));
        var setSpawn = Require(typeof(ServerPlayer), nameof(ServerPlayer.SetSpawnPosition), typeof(void), typeof(PlayerSpawnPos));
        var getSpawn = Require(typeof(ServerPlayer), nameof(ServerPlayer.GetSpawnPosition), typeof(FuzzyEntityPos), typeof(bool));

        var revive = Require(typeof(EntityPlayer), nameof(EntityPlayer.Revive), typeof(void));

        harmony.Patch(revive, postfix: Patch(nameof(AfterRevive)));
        harmony.Patch(gear, prefix: Patch(nameof(BeginGearUse)), finalizer: Patch(nameof(EndGearUse)));
        harmony.Patch(setSpawn, prefix: Patch(nameof(SetSpawn)));
        harmony.Patch(getSpawn, prefix: Patch(nameof(GetSpawn)));
        harmony.Patch(respawn, prefix: Patch(nameof(BeforeRespawn)));
    }

    private static MethodInfo Require(Type type, string name, Type result, params Type[] parameters)
    {
        var method = AccessTools.DeclaredMethod(type, name, parameters);
        return method?.ReturnType == result
            ? method
            : throw new MissingMethodException(type.FullName, name);
    }

    private static HarmonyMethod Patch(string name) => new(typeof(RespawnHooks), name);

    private static void BeginGearUse(EntityAgent __2, ref EntityAgent? __state)
    {
        __state = gearUser;
        gearUser = __2;
    }

    private static Exception? EndGearUse(Exception? __exception, EntityAgent? __state)
    {
        gearUser = __state;
        return __exception;
    }

    private static bool SetSpawn(ServerPlayer __instance, PlayerSpawnPos? __0) =>
        __0 == null || gearUser == null || !ReferenceEquals(gearUser, __instance.Entity) ||
        (TemporalRefugeMod.Active?.SetGearAnchor(__instance, __0) ?? true);

    private static bool GetSpawn(ServerPlayer __instance, bool __0, ref FuzzyEntityPos __result)
    {
        var landing = TemporalRefugeMod.Active?.GetRefugeOverride(__instance, __0);
        if (landing == null) return true;
        __result = landing;
        return false;
    }

    // On 1.22.7 the server event dispatches the request, before the entity actually revives.
    private static void AfterRevive(EntityPlayer __instance) =>
        TemporalRefugeMod.Active?.AfterRevive(__instance);

    private static bool BeforeRespawn(ServerSystemEntitySimulation __instance, IServerPlayer __0) =>
        TemporalRefugeMod.Active?.BeforeRespawn(__instance, __0) ?? true;
}
