using HarmonyLib;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff), new[] { typeof(Hediff) })]
internal static class Patch_Pawn_HealthTracker_RemoveHediff
{
    private const int TicksPerDay = 60000;

    public static void Prefix(Hediff hediff, out Pawn __state)
    {
        __state = hediff?.pawn;
    }

    public static void Postfix(Hediff hediff, Pawn __state)
    {
        if (__state == null || __state.Dead || hediff?.def == null)
        {
            return;
        }

        if (!DiseaseProfileCache.TryGetResolvedProfile(hediff.def, out ResolvedTransmissionProfile resolvedProfile))
        {
            return;
        }

        if (resolvedProfile.Profile.immunityDurationDays <= 0f)
        {
            return;
        }

        int durationTicks = Mathf.Max(1, Mathf.RoundToInt(resolvedProfile.Profile.immunityDurationDays * TicksPerDay));
        ContagionDiseaseUtility.GiveTemporaryImmunity(__state, hediff.def, durationTicks);
    }
}