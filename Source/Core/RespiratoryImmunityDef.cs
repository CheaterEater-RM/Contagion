using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Contagion;

// One whitelisted gene and how much respiratory protection it grants (0-1, where 1 is effectively
// immune to airway-based transmission). Referenced by defName string rather than a GeneDef direct
// reference so that listing a Biotech/Odyssey gene does not throw a cross-reference error when that
// DLC is absent — missing genes are simply skipped at resolve time.
public sealed class GeneRespiratoryProtection
{
    public string gene;

    public float protection = 1f;
}

// Player-facing, fully patchable config Def. The mod ships one instance; players can PatchOperation
// it or add their own. Multiple instances are merged.
//
// Default rule for airway-based protection (airborne / social / close contact):
//   - Worn apparel and body-part hediffs that grant ToxicEnvironmentResistance count at FULL effect.
//   - Genes do NOT count, UNLESS listed here with an explicit protection value.
public sealed class RespiratoryImmunityDef : Def
{
    public List<GeneRespiratoryProtection> geneProtections;
}

public static class RespiratoryProtectionCache
{
    private static Dictionary<GeneDef, float> _geneProtections;

    public static bool HasAnyGeneProtections
    {
        get
        {
            EnsureInitialized();
            return _geneProtections.Count > 0;
        }
    }

    public static void Reset()
    {
        _geneProtections = null;
    }

    // Highest respiratory protection (0-1) among the pawn's active whitelisted genes.
    public static float GetGeneProtection(Pawn pawn)
    {
        if (pawn?.genes == null)
        {
            return 0f;
        }

        EnsureInitialized();
        if (_geneProtections.Count == 0)
        {
            return 0f;
        }

        float best = 0f;
        foreach (KeyValuePair<GeneDef, float> entry in _geneProtections)
        {
            if (entry.Value > best && pawn.genes.HasActiveGene(entry.Key))
            {
                best = entry.Value;
            }
        }

        return best;
    }

    private static void EnsureInitialized()
    {
        if (_geneProtections != null)
        {
            return;
        }

        _geneProtections = new Dictionary<GeneDef, float>();
        foreach (RespiratoryImmunityDef def in DefDatabase<RespiratoryImmunityDef>.AllDefsListForReading)
        {
            if (def.geneProtections == null)
            {
                continue;
            }

            for (int i = 0; i < def.geneProtections.Count; i++)
            {
                GeneRespiratoryProtection entry = def.geneProtections[i];
                if (entry?.gene == null)
                {
                    continue;
                }

                GeneDef geneDef = DefDatabase<GeneDef>.GetNamedSilentFail(entry.gene);
                if (geneDef == null)
                {
                    // Gene from a DLC/mod that is not loaded; silently skip.
                    continue;
                }

                float protection = Mathf.Clamp01(entry.protection);
                if (!_geneProtections.TryGetValue(geneDef, out float existing) || protection > existing)
                {
                    _geneProtections[geneDef] = protection;
                }
            }
        }
    }
}
