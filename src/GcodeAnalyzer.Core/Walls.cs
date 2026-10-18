namespace GcodeAnalyzer.Core;

public enum WallStatus { Ok, ExpectedDeviation, Underrun }

/// <summary>Ist-Wandschleifen eines Layers, gemittelt über alle Konturen.</summary>
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

        var result = new List<LayerWalls>();
        foreach (var l in layers)
        {
            int outer = 0, inner = 0;
            Segment? prev = null;
            foreach (var s in l.Segments)
            {
                bool wall = s.Category is PathCategory.OuterWall or PathCategory.InnerWall;
                // neue Wandschleife, wenn der Wandpfad nicht nahtlos am vorigen Wandpfad gleicher Art anschließt
                if (wall && !(prev is { } p && p.Category == s.Category && p.To == s.From))
                {
                    if (s.Category == PathCategory.OuterWall) outer++; else inner++;
                }
                prev = wall ? s : null;
            }
            if (outer == 0) continue;
            double loops = Math.Round((outer + inner) / (double)outer, 1);
            bool expected = l.Segments.Any(s => s.Category == PathCategory.GapFill)
                || (oneWallTop && l.Segments.Any(s => s.Category == PathCategory.TopSurface))
                || (oneWallFirst && l.Index == 0);
            var status = loops >= target - 0.05 ? WallStatus.Ok : expected ? WallStatus.ExpectedDeviation : WallStatus.Underrun;
            result.Add(new LayerWalls(l.Index, loops, w.Thickness(loops), status));
        }
        return w with { Layers = result };
    }
}
