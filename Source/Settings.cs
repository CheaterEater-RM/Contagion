using UnityEngine;
using Verse;

namespace Contagion;

public sealed class Contagion_Settings : ModSettings
{
    public void Reset()
    {
    }

    public override void ExposeData()
    {
        base.ExposeData();
    }
}

public sealed class Contagion_Mod : Mod
{
    public static Contagion_Settings Settings { get; private set; }

    public Contagion_Mod(ModContentPack content)
        : base(content)
    {
        Settings = GetSettings<Contagion_Settings>();
    }

    public override string SettingsCategory()
    {
        return "Contagion_SettingsCategory".Translate();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.Label("Contagion_SettingsEmpty".Translate());
        listing.Gap();

        if (listing.ButtonText("Contagion_ResetDefaults".Translate()))
        {
            Settings.Reset();
        }

        listing.End();
    }
}