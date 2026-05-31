using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Pawn_CarryTracker))]
internal static class Patch_Pawn_CarryTracker_CorpseExposure
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Pawn_CarryTracker.TryStartCarry), typeof(Thing))]
    public static void TryStartCarryThingPostfix(Pawn_CarryTracker __instance, Thing item, bool __result)
    {
        if (__result)
        {
            NotifyPickedUp(__instance);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Pawn_CarryTracker.TryStartCarry), typeof(Thing), typeof(int), typeof(bool))]
    public static void TryStartCarryCountPostfix(Pawn_CarryTracker __instance, int __result)
    {
        if (__result > 0)
        {
            NotifyPickedUp(__instance);
        }
    }

    private static void NotifyPickedUp(Pawn_CarryTracker carryTracker)
    {
        Pawn pawn = carryTracker?.pawn;
        if (pawn == null || carryTracker.CarriedThing is not Corpse corpse)
        {
            return;
        }

        if (ContagionCorpseExposureUtility.TryGetCorpseFluidVector(
            corpse,
            out ResolvedTransmissionProfile resolvedProfile,
            out Vector_CorpseFluid vector))
        {
            ContagionCorpseExposureUtility.TryApplyFluidExposure(
                pawn,
                corpse,
                resolvedProfile,
                vector,
                ContagionCorpseFluidExposureKind.Pickup);
        }
    }

    public static void NotifyPutDownFromDropPatch(Pawn_CarryTracker carryTracker, Corpse corpse)
    {
        NotifyPutDown(carryTracker, corpse);
    }

    private static void NotifyPutDown(Pawn_CarryTracker carryTracker, Corpse corpse)
    {
        Pawn pawn = carryTracker?.pawn;
        if (pawn == null || corpse == null)
        {
            return;
        }

        if (ContagionCorpseExposureUtility.TryGetCorpseFluidVector(
            corpse,
            out ResolvedTransmissionProfile resolvedProfile,
            out Vector_CorpseFluid vector))
        {
            ContagionCorpseExposureUtility.TryApplyFluidExposure(
                pawn,
                corpse,
                resolvedProfile,
                vector,
                ContagionCorpseFluidExposureKind.Putdown);
        }
    }
}

[HarmonyPatch]
internal static class Patch_Pawn_CarryTracker_DropCorpseExposure
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(
            typeof(Pawn_CarryTracker),
            nameof(Pawn_CarryTracker.TryDropCarriedThing),
            new[] { typeof(IntVec3), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>) });
        yield return AccessTools.Method(
            typeof(Pawn_CarryTracker),
            nameof(Pawn_CarryTracker.TryDropCarriedThing),
            new[] { typeof(IntVec3), typeof(int), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>) });
    }

    public static void Prefix(Pawn_CarryTracker __instance, out Corpse __state)
    {
        __state = __instance?.CarriedThing as Corpse;
    }

    public static void Postfix(Pawn_CarryTracker __instance, Corpse __state, bool __result)
    {
        if (__result)
        {
            Patch_Pawn_CarryTracker_CorpseExposure.NotifyPutDownFromDropPatch(__instance, __state);
        }
    }
}
