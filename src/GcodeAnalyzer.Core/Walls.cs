namespace GcodeAnalyzer.Core;

public enum WallStatus { Ok, ExpectedDeviation, Underrun }

/// <summary>Ist-Wandschleifen eines Layers: die Kontur mit den wenigsten Schleifen; Status ist der schlechteste aller Konturen.</summary>
public sealed record LayerWalls(int Layer, double Loops, double Thickness, WallStatus Status);

/// <summary>Soll-Wandstärke laut Slicer-Einstellungen und Ist-Werte je Layer (nur Layer mit Außenwand).</summary>
public sealed record WallAnalysis(int TargetLoops, double OuterWidth, double InnerWidth, IReadOnlyList<LayerWalls> Layers)
{
    public double TargetThickness => Thickness(TargetLoops);
    public double Thickness(double loops) => loops <= 0 ? 0 : OuterWidth + (loops - 1) * InnerWidth;

    internal static WallAnalysis From(IReadOnlyList<Layer> layers, IReadOnlyDictionary<string, string> settings,
        Func<string, double?> num, Func<string, double?> width)
    {
        double lineWidth = width("line_width") ?? width("extrusion_width") ?? num("nozzle_diameter") ?? 0.4;
        double Width(params string[] keys) => keys.Select(width).FirstOrDefault(v => v > 0) ?? lineWidth;
        int target = (int)(num("wall_loops") ?? num("perimeters") ?? 0);
        // PrusaSlicer: top_one_perimeter_type = none | top | topmost
        bool oneWallTop = num("only_one_wall_top") == 1 || settings.GetValueOrDefault("top_one_perimeter_type") is "top" or "topmost";
        bool oneWallFirst = num("only_one_wall_first_layer") == 1;
        var w = new WallAnalysis(target, Width("outer_wall_line_width", "external_perimeter_extrusion_width"),
            Width("inner_wall_line_width", "perimeter_extrusion_width"), []);

        double nearContour = w.TargetThickness + lineWidth; // Lückenfüllung so nah an der Außenwand ersetzt Wandschleifen
        var result = new List<LayerWalls>();
        foreach (var l in layers)
        {
            var chains = Chains(l.Segments);
            var contours = chains.Where(c => c[0].Category == PathCategory.OuterWall).ToList();
            if (contours.Count == 0) continue;
            // Innenwand-Schleife gehört zur nächstgelegenen Außenwand; offene Innenwandstücke zählen nicht als Schleife
            var loops = contours.Select(_ => 1).ToArray();
            foreach (var inner in chains.Where(c => c[0].Category == PathCategory.InnerWall && IsClosed(c)))
            {
                var samples = Samples(inner);
                int best = 0;
                double bestDist = double.MaxValue;
                for (int k = 0; k < contours.Count; k++)
                {
                    double d = samples.Average(pt => Distance(pt, contours[k]));
                    if (d < bestDist) { bestDist = d; best = k; }
                }
                loops[best]++;
            }
            var gaps = l.Segments.Where(s => s.Category == PathCategory.GapFill).Select(s => (s.From + s.To) / 2).ToList();
            bool layerExpected = (oneWallTop && l.Segments.Any(s => s.Category == PathCategory.TopSurface)) || (oneWallFirst && l.Index == 0);

            var status = WallStatus.Ok;
            for (int k = 0; k < contours.Count; k++)
            {
                if (loops[k] >= target) continue;
                bool expected = layerExpected || gaps.Any(g => Distance(g, contours[k]) <= nearContour);
                var st = expected ? WallStatus.ExpectedDeviation : WallStatus.Underrun;
                if (st > status) status = st;
            }
            int min = loops.Min();
            result.Add(new LayerWalls(l.Index, min, w.Thickness(min), status));
        }
        return w with { Layers = result };
    }

    /// <summary>Zusammenhängende Wandpfade gleicher Art (Außen- bzw. Innenwand) eines Layers.</summary>
    static List<List<Segment>> Chains(Segment[] segments)
    {
        var chains = new List<List<Segment>>();
        List<Segment>? current = null;
        foreach (var s in segments)
        {
            bool wall = s.Category is PathCategory.OuterWall or PathCategory.InnerWall;
            if (!wall) { current = null; continue; }
            if (current is null || current[^1].Category != s.Category || current[^1].To != s.From)
                chains.Add(current = []);
            current.Add(s);
        }
        return chains;
    }

    /// <summary>Geschlossen, wenn Anfang und Ende höchstens 1 mm auseinanderliegen (Naht).</summary>
    static bool IsClosed(List<Segment> chain) => System.Numerics.Vector3.Distance(chain[0].From, chain[^1].To) <= 1f;

    static List<System.Numerics.Vector3> Samples(List<Segment> chain)
    {
        int step = Math.Max(1, chain.Count / 8);
        return chain.Where((_, i) => i % step == 0).Select(s => s.From).ToList();
    }

    /// <summary>Kleinster Abstand eines Punkts zu einem Wandpfad in der XY-Ebene.</summary>
    static double Distance(System.Numerics.Vector3 p, List<Segment> chain)
    {
        double best = double.MaxValue;
        foreach (var s in chain)
        {
            var a = new System.Numerics.Vector2(s.From.X, s.From.Y);
            var ab = new System.Numerics.Vector2(s.To.X, s.To.Y) - a;
            var q = new System.Numerics.Vector2(p.X, p.Y);
            float len2 = ab.LengthSquared();
            float t = len2 > 0 ? Math.Clamp(System.Numerics.Vector2.Dot(q - a, ab) / len2, 0, 1) : 0;
            best = Math.Min(best, System.Numerics.Vector2.Distance(q, a + ab * t));
        }
        return best;
    }
}
