using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class CompProperties_InfectedCorpse : CompProperties
{
    public CompProperties_InfectedCorpse()
    {
        compClass = typeof(Comp_InfectedCorpse);
    }
}

[StaticConstructorOnStartup]
public sealed class Comp_InfectedCorpse : ThingComp
{
    private static readonly Material OverlayMaterial = MaterialPool.MatFrom(
        BaseContent.WhiteTex,
        ShaderDatabase.Transparent,
        new Color(0.42f, 1f, 0.36f, 0.18f),
        3600);

    private HediffDef _infectedDiseaseDef;

    private int _infectionTick = -1;

    public bool IsInfected => _infectedDiseaseDef != null;

    public HediffDef InfectedDiseaseDef => _infectedDiseaseDef;

    public void SetInfection(HediffDef diseaseDef)
    {
        if (diseaseDef == null)
        {
            return;
        }

        _infectedDiseaseDef = diseaseDef;
        _infectionTick = Find.TickManager?.TicksGame ?? -1;
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);

        if (respawningAfterLoad || IsInfected || parent is not Corpse corpse)
        {
            return;
        }

        if (ContagionCorpseUtility.TryGetCorpseContagiousDiseaseFromInnerPawn(corpse.InnerPawn, out HediffDef diseaseDef))
        {
            SetInfection(diseaseDef);
        }
    }

    public override string CompInspectStringExtra()
    {
        if (!TryGetDiseaseForDisplay(out HediffDef diseaseDef))
        {
            return null;
        }

        return "Contagion_InfectedCorpseInspect".Translate(diseaseDef.LabelCap);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Defs.Look(ref _infectedDiseaseDef, "infectedDiseaseDef");
        Scribe_Values.Look(ref _infectionTick, "infectionTick", -1);
    }

    public void DrawInfectionOverlay(Vector3 drawLoc)
    {
        if (!TryGetDiseaseForDisplay(out HediffDef _) || parent is not Corpse corpse || corpse.InnerPawn == null)
        {
            return;
        }

        Vector3 scale = new Vector3(
            Mathf.Clamp(corpse.InnerPawn.BodySize * 1.15f, 0.75f, 2.35f),
            1f,
            Mathf.Clamp(corpse.InnerPawn.BodySize * 1.15f, 0.75f, 2.35f));
        Vector3 location = drawLoc.WithYOffset(0.036f);
        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(location, Quaternion.identity, scale), OverlayMaterial, 0);
    }

    private bool TryGetDiseaseForDisplay(out HediffDef diseaseDef)
    {
        diseaseDef = _infectedDiseaseDef;
        if (diseaseDef != null)
        {
            return true;
        }

        return ContagionCorpseUtility.TryGetCorpseContagiousDiseaseFromInnerPawn((parent as Corpse)?.InnerPawn, out diseaseDef);
    }
}
