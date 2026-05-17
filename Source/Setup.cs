using HarmonyLib;
using Verse;

namespace Contagion;

[StaticConstructorOnStartup]
public static class Contagion_Init
{
    static Contagion_Init()
    {
        Harmony harmony = new Harmony("com.cheatereater.contagion");
        harmony.PatchAll();

        if (Prefs.DevMode)
        {
            Log.Message("[Contagion] Harmony patches applied.");
        }
    }
}