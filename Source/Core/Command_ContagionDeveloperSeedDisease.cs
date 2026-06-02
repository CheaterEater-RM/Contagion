using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class Command_ContagionDeveloperSeedDisease : Command_Action
{
    private readonly Pawn _pawn;

    public Command_ContagionDeveloperSeedDisease(Pawn pawn)
    {
        _pawn = pawn;
        defaultLabel = "Contagion_DeveloperSeedDisease".Translate();
        defaultDesc = "Contagion_DeveloperSeedDiseaseDesc".Translate();
        icon = HasIncubation(pawn) ? TexCommand.FireAtWill : TexCommand.Attack;
        Order = -94f;
        action = ShowMenu;
    }

    public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions => BuildOptions();

    private IEnumerable<FloatMenuOption> BuildOptions()
    {
        List<ResolvedTransmissionProfile> profiles = new List<ResolvedTransmissionProfile>();
        foreach (ResolvedTransmissionProfile resolvedProfile in DiseaseProfileCache.AllProfiles)
        {
            if (resolvedProfile?.DiseaseDef != null && resolvedProfile.Profile?.CanAffect(_pawn) == true)
            {
                profiles.Add(resolvedProfile);
            }
        }

        profiles.Sort((left, right) => string.Compare(left.DiseaseDef.label, right.DiseaseDef.label, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < profiles.Count; i++)
        {
            ResolvedTransmissionProfile resolvedProfile = profiles[i];
            yield return new FloatMenuOption(
                BuildOptionLabel(resolvedProfile),
                delegate
                {
                    TrySeedDisease(resolvedProfile);
                });
        }
    }

    private void ShowMenu()
    {
        List<FloatMenuOption> options = new List<FloatMenuOption>(RightClickFloatMenuOptions);
        if (options.Count > 0)
        {
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    private void TrySeedDisease(ResolvedTransmissionProfile resolvedProfile)
    {
        if (_pawn == null || resolvedProfile?.DiseaseDef == null)
        {
            return;
        }

        if (ContagionDiseaseUtility.TrySeedIncubation(
            _pawn,
            resolvedProfile.DiseaseDef,
            resolvedProfile.PartsToAffect,
            ContagionDiagnosticOrigin.Unknown,
            ContagionSeedSource.Developer,
            out HediffDef _))
        {
            ContagionTrace.SourceAtCell(_pawn.PositionHeld, _pawn, resolvedProfile.DiseaseDef, ContagionDebugVectorKind.Developer);
            return;
        }

        Messages.Message(
            "Contagion_DeveloperSeedDiseaseFailed".Translate(_pawn.LabelShortCap, resolvedProfile.DiseaseDef.LabelCap),
            _pawn,
            MessageTypeDefOf.RejectInput,
            historical: false);
    }

    private static bool HasIncubation(Pawn pawn)
    {
        return pawn?.health?.hediffSet?.HasHediff(ContagionDefOf.Contagion_Incubation) == true;
    }

    private static string BuildOptionLabel(ResolvedTransmissionProfile resolvedProfile)
    {
        string label = resolvedProfile.DiseaseDef.LabelCap.Resolve();
        bool affectsHumans = resolvedProfile.Profile?.affectsHumans == true;
        bool affectsAnimals = resolvedProfile.Profile?.affectsAnimals == true;
        if (affectsHumans == affectsAnimals)
        {
            return label;
        }

        return affectsHumans
            ? "Contagion_DeveloperDiseaseLabelHuman".Translate(label).Resolve()
            : "Contagion_DeveloperDiseaseLabelAnimal".Translate(label).Resolve();
    }
}
