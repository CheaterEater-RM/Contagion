using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class Hediff_ContagionAnimalHiddenDisease : HediffWithComps
{
    // Half a game-day between passive symptom checks.
    private const int SymptomCheckIntervalTicks = 30000;

    // Used for any showsSickSignal disease whose profile doesn't override the curve.
    // Cumulative chance over a typical untreated course: ~25%.
    private static readonly SimpleCurve DefaultPassiveSymptomCurve = BuildDefaultCurve();

    private bool _diagnosed;

    private int _nextSymptomCheckTick = -1;

    public bool Diagnosed => _diagnosed;

    public override bool Visible => _diagnosed && base.Visible;

    public void MarkDiagnosed()
    {
        _diagnosed = true;
    }

    public override void PostTickInterval(int delta)
    {
        base.PostTickInterval(delta);
        TryPassiveSymptomPresentation();
    }

    private void TryPassiveSymptomPresentation()
    {
        if (_diagnosed || pawn == null || !pawn.Spawned || pawn.Dead)
        {
            return;
        }

        int currentTick = Find.TickManager.TicksGame;
        if (_nextSymptomCheckTick < 0)
        {
            _nextSymptomCheckTick = currentTick + SymptomCheckIntervalTicks;
            return;
        }

        if (currentTick < _nextSymptomCheckTick)
        {
            return;
        }

        _nextSymptomCheckTick = currentTick + SymptomCheckIntervalTicks;

        if (!DiseaseProfileCache.TryGetResolvedProfile(def, out ResolvedTransmissionProfile resolvedProfile))
        {
            return;
        }

        // Use the profile-specific curve, or fall back to the default for showsSickSignal diseases.
        SimpleCurve curve = resolvedProfile.Profile.passiveSymptomPresentationCurve
            ?? (resolvedProfile.Profile.showsSickSignal ? DefaultPassiveSymptomCurve : null);
        if (curve == null)
        {
            return;
        }

        float chance = curve.Evaluate(Severity);
        bool passed = chance > 0f && Rand.Chance(chance);
        ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.Environmental, pawn, pawn, def, Mathf.Clamp01(chance), passed);
        if (!passed)
        {
            return;
        }

        if (!ContagionAnimalDiseaseUtility.CanShowAnimalSickSignal(pawn))
        {
            return;
        }

        pawn.health.AddHediff(ContagionDefOf.Contagion_AnimalSick);
        ContagionDiagnostics.Trace($"Animal sick signal presented: {def.defName} hidden disease became visible on {pawn.LabelShortCap}.");

        // Notify for colony animals only — wild animals show the sick signal on their health bar
        // but don't warrant a message the player may have no context for.
        if (PawnUtility.ShouldSendNotificationAbout(pawn))
        {
            Messages.Message(
                "Contagion_AnimalSickPresented".Translate(pawn.LabelShortCap),
                pawn,
                MessageTypeDefOf.CautionInput,
                historical: false);
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref _diagnosed, "diagnosed");
        Scribe_Values.Look(ref _nextSymptomCheckTick, "nextSymptomCheckTick", -1);
    }

    private static SimpleCurve BuildDefaultCurve()
    {
        SimpleCurve curve = new SimpleCurve();
        curve.Add(0.0f, 0.00f, sort: false);
        curve.Add(0.3f, 0.01f, sort: false);
        curve.Add(0.6f, 0.03f, sort: false);
        curve.Add(1.0f, 0.05f, sort: false);
        return curve;
    }
}
