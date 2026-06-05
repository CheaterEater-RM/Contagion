using System;
using System.Collections.Generic;

// A Scenario is PURELY a pawn-placement layout: where the pawns stand. Indoor/outdoor, disease, PPE,
// suppression, and difficulty are all orthogonal toggles supplied at run time via RunConditions.
// BuildLayout is called once per Monte-Carlo trial with that trial's RNG so placement varies.

internal enum PlacementKind
{
    OpenCluster, // a rough disc of pawns (e.g. a trade caravan mingling)
    Pair,        // two pawns at a fixed distance (cross-check vs the pointwise audit)
    Room,        // an enclosed room of fixed size with a set occupancy
}

internal sealed class Layout
{
    public List<(float x, float z)> Positions = new();
    public bool Indoor;     // roofed/enclosed -> airborne enclosureFactor 1; else outdoorFactor penalty
    public int RoomCells;   // enclosing room size for room-air (0 = open / no room-air reservoir)

    public float Distance(int a, int b)
    {
        float dx = Positions[a].x - Positions[b].x;
        float dz = Positions[a].z - Positions[b].z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }
}

internal sealed class Scenario
{
    public string Name;
    public PlacementKind Kind;
    public int PawnCount;
    public int DefaultInitialInfected = 1;

    // OpenCluster
    public float ClusterRadius;

    // Pair
    public float PairDistance;

    // Room
    public int RoomWidth;
    public int RoomHeight;

    public Layout BuildLayout(RunConditions conditions, Random rng)
    {
        Layout layout = new();
        switch (Kind)
        {
            case PlacementKind.OpenCluster:
                for (int i = 0; i < PawnCount; i++)
                {
                    // Uniform point in a disc of ClusterRadius.
                    double angle = rng.NextDouble() * Math.PI * 2.0;
                    double r = ClusterRadius * Math.Sqrt(rng.NextDouble());
                    layout.Positions.Add(((float)(Math.Cos(angle) * r), (float)(Math.Sin(angle) * r)));
                }

                layout.Indoor = !conditions.Outdoor;
                // An indoor open area is treated as one room sized to the cluster footprint.
                layout.RoomCells = layout.Indoor ? (int)Math.Round(Math.PI * ClusterRadius * ClusterRadius) : 0;
                break;

            case PlacementKind.Pair:
                layout.Positions.Add((0f, 0f));
                layout.Positions.Add((PairDistance, 0f));
                layout.Indoor = !conditions.Outdoor;
                layout.RoomCells = layout.Indoor ? 25 : 0;
                break;

            case PlacementKind.Room:
                // Rooms are enclosed by definition; the indoor/outdoor toggle does not apply.
                HashSet<(int, int)> used = new();
                while (layout.Positions.Count < PawnCount)
                {
                    int cx = rng.Next(RoomWidth);
                    int cz = rng.Next(RoomHeight);
                    if (used.Add((cx, cz)))
                    {
                        layout.Positions.Add((cx + 0.5f, cz + 0.5f));
                    }
                }

                layout.Indoor = true;
                layout.RoomCells = RoomWidth * RoomHeight;
                break;
        }

        return layout;
    }

    public static Dictionary<string, Scenario> Catalog()
    {
        return new Dictionary<string, Scenario>(StringComparer.OrdinalIgnoreCase)
        {
            // A 20-pawn trade caravan mingling in a tight cluster (within plague proximity range 6 and
            // flu airborne range 10 of several neighbors). The reproduction case for the blow-up.
            ["caravan"] = new Scenario
            {
                Name = "caravan", Kind = PlacementKind.OpenCluster,
                PawnCount = 20, DefaultInitialInfected = 3, ClusterRadius = 4f,
            },
            ["two-pawn"] = new Scenario
            {
                Name = "two-pawn", Kind = PlacementKind.Pair,
                PawnCount = 2, DefaultInitialInfected = 1, PairDistance = 2f,
            },
            // Tightly-packed barracks: small room, high occupancy.
            ["barracks"] = new Scenario
            {
                Name = "barracks", Kind = PlacementKind.Room,
                PawnCount = 10, DefaultInitialInfected = 1, RoomWidth = 7, RoomHeight = 5,
            },
            // More open hospital: larger room, fewer occupants.
            ["hospital"] = new Scenario
            {
                Name = "hospital", Kind = PlacementKind.Room,
                PawnCount = 6, DefaultInitialInfected = 1, RoomWidth = 9, RoomHeight = 7,
            },
            // High-traffic dining/rec hall: large room near the room-air cell cap, busy.
            ["dining-rec"] = new Scenario
            {
                Name = "dining-rec", Kind = PlacementKind.Room,
                PawnCount = 12, DefaultInitialInfected = 1, RoomWidth = 11, RoomHeight = 9,
            },
            // Sparsely occupied workshop.
            ["workshop"] = new Scenario
            {
                Name = "workshop", Kind = PlacementKind.Room,
                PawnCount = 3, DefaultInitialInfected = 1, RoomWidth = 9, RoomHeight = 7,
            },
        };
    }
}
