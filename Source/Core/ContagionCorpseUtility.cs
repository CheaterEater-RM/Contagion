using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionCorpseUtility
{
    public static bool IsInfectedCorpse(Thing thing)
    {
        return TryGetInfectedDisease(thing as Corpse, out HediffDef _);
    }

    public static bool IsUninfectedCorpse(Thing thing)
    {
        return thing is Corpse corpse && !TryGetInfectedDisease(corpse, out HediffDef _);
    }

    public static bool TryGetInfectedDisease(Corpse corpse, out HediffDef diseaseDef)
    {
        diseaseDef = null;
        if (corpse == null)
        {
            return false;
        }

        Comp_InfectedCorpse comp = corpse.TryGetComp<Comp_InfectedCorpse>();
        if (comp?.IsInfected == true)
        {
            diseaseDef = comp.InfectedDiseaseDef;
            return diseaseDef != null;
        }

        return TryGetCorpseContagiousDiseaseFromInnerPawn(corpse.InnerPawn, out diseaseDef);
    }

    public static bool TryGetCorpseContagiousDiseaseFromInnerPawn(Pawn innerPawn, out HediffDef diseaseDef)
    {
        diseaseDef = null;
        if (innerPawn == null)
        {
            return false;
        }

        if (innerPawn.RaceProps?.Animal == true)
        {
            diseaseDef = ContagionAnimalDiseaseUtility.GetAnimalCorpseContagiousDisease(innerPawn);
        }
        else if (innerPawn.RaceProps?.Humanlike == true)
        {
            diseaseDef = ContagionAnimalDiseaseUtility.GetHumanCorpseContagiousDisease(innerPawn);
        }

        return diseaseDef != null;
    }

    public static void NotifyCorpseIngested(Corpse corpse, Pawn ingester)
    {
        if (corpse == null || ingester?.MapHeld == null || !TryGetInfectedDisease(corpse, out HediffDef diseaseDef))
        {
            return;
        }

        if (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
        {
            return;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.FoodborneAttempted);

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        float chance = ContagionTransmissionUtility.BuildSeederChance(
            1f,
            ingester,
            resolvedProfile,
            ingester.MapHeld,
            transmissionMultiplier,
            out HediffDef _);

        if (!Rand.Chance(Mathf.Clamp01(chance)))
        {
            return;
        }

        if (ContagionDiseaseUtility.TrySeedIncubation(
            ingester,
            resolvedProfile.DiseaseDef,
            resolvedProfile.PartsToAffect,
            ContagionDiagnosticOrigin.Spread,
            out HediffDef _))
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.FoodborneSeeded);
            ContagionDiagnostics.Trace($"Corpse ingestion transmission: {resolvedProfile.DiseaseDef.defName} to {ingester.LabelShortCap}.");
        }
    }
}
