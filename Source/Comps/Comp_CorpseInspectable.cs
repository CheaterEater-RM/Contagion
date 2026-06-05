using RimWorld;
using Verse;

namespace Contagion;

public sealed class CompProperties_CorpseInspectable : CompProperties_Interactable
{
    public CompProperties_CorpseInspectable()
    {
        compClass = typeof(Comp_CorpseInspectable);

        // Examination duration (matches the retired JobDriver_InspectCorpse's 400-tick wait).
        ticksToActivate = 400;

        // One-shot per corpse is enforced by Comp_InfectedCorpse.HasBeenInspected, so this comp
        // keeps no active/cooldown state of its own.
        cooldownTicks = 0;
        activeTicks = 0;
        requiresPower = false;

        // The player explicitly ordered the inspection, so allow it on a forbidden corpse and
        // keep the corpse's inspect panel free of "must be activated by a colonist" clutter.
        ignoreForbidden = true;
        showMustBeActivatedByColonist = false;

        // Required non-empty by CompProperties_Interactable.ConfigErrors. The interaction UI is
        // hidden (Comp_CorpseInspectable.HideInteraction), so this icon is never actually drawn.
        activateTexPath = "UI/Commands/Activate";

        // Job report line while examining. JobDriver_InteractThing.GetReport calls .Formatted()
        // (not .Translate()) on these, and the props are injected in code (no XML translation
        // injection), so we resolve the keys here. This ctor only runs from
        // ContagionCorpseDefInjector at [StaticConstructorOnStartup] — after LanguageData loads —
        // so .Translate() is safe. The unfilled {0} placeholder survives (no args) and is filled
        // by GetReport's .Formatted(corpseLabel). (A mid-session language change needs a restart to
        // refresh these, which is standard for code-injected props.)
        jobString = "Contagion_InspectCorpseReportJob".Translate();
        activatingString = "Contagion_InspectCorpseReportActive".Translate();
        activatingStringPending = "Contagion_InspectCorpseReportActive".Translate();
    }
}

// Makes a corpse examinable via the vanilla interaction job (JobDefOf.InteractThing /
// JobDriver_InteractThing). Inspection is a non-destructive, one-shot-per-corpse information
// action: a colonist walks to the corpse, examines it for ticksToActivate, then OnInteracted
// runs the post-mortem diagnosis (ContagionCorpseUtility.TryInspectCorpse).
//
// Why CompInteractable rather than a custom JobDriver: JobDriver_InteractThing is a vanilla
// concrete driver, so an in-flight inspection saved with this mod survives removal (the saved
// curJob/curDriver are vanilla types — no missing-class abstract-load failure that locks the
// pawn). The Contagion-specific logic lives here in the comp callback, which is dropped silently
// if the mod is removed.
//
// The interaction UI is hidden (HideInteraction): the single entry point is
// FloatMenuOptionProvider_InspectCorpse, which keeps all gating (flesh, not-yet-inspected, etc.)
// and issues the vanilla InteractThing job. CompProperties are configured by
// CompProperties_CorpseInspectable, injected onto every Corpse def by ContagionCorpseDefInjector.
public sealed class Comp_CorpseInspectable : CompInteractable
{
    // Suppress the comp's own gizmo / float-menu option / targeter. The float-menu provider is
    // the only entry point so it can apply Contagion's gating.
    public override bool HideInteraction => true;

    protected override void OnInteracted(Pawn caster)
    {
        if (parent is Corpse corpse)
        {
            ContagionCorpseUtility.TryInspectCorpse(corpse, caster);
        }
    }

    // Inspection state is surfaced by Comp_InfectedCorpse's inspect string; this comp adds no
    // cooldown/activation text of its own.
    public override string CompInspectStringExtra()
    {
        return null;
    }
}
