using UnityEngine;
using Verse;

namespace Contagion;

public sealed class Hediff_ContagionCorpseFleas : Hediff
{
    private float _viability = 1f;

    public HediffDef FleaDiseaseDef;

    public override bool Visible => false;

    public float Viability => _viability;

    public void Configure(HediffDef diseaseDef)
    {
        FleaDiseaseDef = diseaseDef;
        _viability = Mathf.Clamp01(_viability <= 0f ? 1f : _viability);
        Severity = Mathf.Max(Severity, 0.01f);
    }

    public void UpdateFromCorpse(Corpse corpse, Vector_CorpseFlea vector, int deltaTicks)
    {
        if (corpse == null || vector == null)
        {
            return;
        }

        if (corpse.AmbientTemperature <= vector.frozenTemperature && vector.frozenViabilityLossPerDay > 0f)
        {
            _viability = Mathf.Max(0f, _viability - vector.frozenViabilityLossPerDay * deltaTicks / 60000f);
        }

        float ageDays = Mathf.Max(0f, corpse.Age / 60000f);
        float agePotency = ContagionCorpseExposureUtility.EvaluateCorpseFleaAgePotency(vector, ageDays);
        Severity = Mathf.Max(0f, agePotency * _viability);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref FleaDiseaseDef, "fleaDiseaseDef");
        Scribe_Values.Look(ref _viability, "viability", 1f);
    }
}
