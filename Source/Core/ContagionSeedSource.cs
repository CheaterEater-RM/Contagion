namespace Contagion;

// How a disease incubation was seeded. Stored on Hediff_ContagionIncubation and passed to
// ContagionDiseaseNotifier so outbreak letters can attribute the source to the player.
//
// Scribe_Values.Look serializes enums by name (Enum.ToString / Enum.Parse), so reordering
// members is safe. Never rename or remove a member — that breaks old saves. Append new values only.
public enum ContagionSeedSource
{
    Unknown,       // Catch-all fallback — attribution not tracked at this call site
    Environmental, // Map-based environmental vector (mosquitoes, tsetse, contaminated water/soil)
    Arrival,       // Arrived with visitors, raiders, wanderers, farm animals, or quest pawns
    Foodborne,     // Ingested contaminated food (cooked by infected cook or made from infected meat)
    Cooking,       // Cook contracted it while handling contaminated ingredients at the stove
    Corpse,        // Contracted from handling, carrying, or butchering an infected corpse
    CorpseIngestion, // Ate an infected corpse directly
    Contact,       // Person-to-person spread: airborne, proximity, social, or fomite
    Acausal,       // Storyteller window expired; source genuinely unknown
    Storyteller,   // Direct storyteller incidence seed (the initial index case)
    Developer,     // Forced via developer tool
}
