using System.Numerics;

namespace GcodeAnalyzer.Core;

/// <summary>Stelle, an der weniger Schalenschichten gemessen als effektiv eingestellt sind.</summary>
public sealed record ThinShellSpot(int Layer, float X, float Y, int Measured, bool Top);

/// <summary>Eingestellte, effektive und gemessene Schalenschichten einer Seite (oben/unten).</summary>
public sealed record ShellSide(int SetLayers, double SetThickness, int Effective, int? MeasuredMin, double? MeasuredAvg, int ThinSpots);

public sealed record ShellAnalysis(ShellSide Top, ShellSide Bottom, IReadOnlyList<ThinShellSpot> ThinSpots)
{
    const float Cell = 1f;
    const byte Solid = 1, SparseRegion = 2, TopMark = 4, BottomMark = 8;

    internal static ShellAnalysis From(IReadOnlyList<Layer> layers, Func<string, double?> num, double? layerHeight)
    {
        int topSet = (int)(num("top_shell_layers") ?? num("top_solid_layers") ?? 0);
        int botSet = (int)(num("bottom_shell_layers") ?? num("bottom_solid_layers") ?? 0);
        double topThick = num("top_shell_thickness") ?? num("top_solid_min_thickness") ?? 0;
        double botThick = num("bottom_shell_thickness") ?? num("bottom_solid_min_thickness") ?? 0;
        int Effective(int n, double t) => layerHeight is > 0 && t > 0 ? Math.Max(n, (int)Math.Ceiling(t / layerHeight.Value - 1e-6)) : n;
        int topEff = Effective(topSet, topThick), botEff = Effective(botSet, botThick);

        // Raster über alle Extrusionen
        var ext = layers.SelectMany(l => l.Segments).Where(s => s.Extrusion > 0).ToList();
        if (ext.Count == 0)
            return new(new(topSet, topThick, topEff, null, null, 0), new(botSet, botThick, botEff, null, null, 0), []);
        float x0 = ext.Min(s => Math.Min(s.From.X, s.To.X)), y0 = ext.Min(s => Math.Min(s.From.Y, s.To.Y));
        int nx = (int)((ext.Max(s => Math.Max(s.From.X, s.To.X)) - x0) / Cell) + 1;
        int ny = (int)((ext.Max(s => Math.Max(s.From.Y, s.To.Y)) - y0) / Cell) + 1;

        // Sparse-Infill füllt die Fläche nur mit Linien im Abstand Linienbreite/Dichte; der Bereich dazwischen zählt mit
        double density = (num("sparse_infill_density") ?? num("fill_density") ?? 20) / 100;
        double spacing = (num("sparse_infill_line_width") ?? num("line_width") ?? 0.45) / Math.Max(density, 0.01);
        int dilate = Math.Clamp((int)Math.Ceiling(spacing / 2 / Cell), 1, 10);

        var grid = new byte[layers.Count][];
        for (int i = 0; i < layers.Count; i++)
        {
            var g = grid[i] = new byte[nx * ny];
            var sparse = new List<int>();
            foreach (var s in layers[i].Segments)
            {
                if (s.Extrusion <= 0) continue;
                byte mark = s.Category switch
                {
                    PathCategory.TopSurface => Solid | TopMark,
                    PathCategory.BottomSurface => Solid | BottomMark,
                    PathCategory.SparseInfill => 0,
                    PathCategory.Support or PathCategory.SupportInterface or PathCategory.Skirt or PathCategory.Other => 0,
                    _ => Solid, // Wände, Massiv-Infill, Brücke, Lückenfüllung
                };
                Rasterize(s, x0, y0, nx, ny, c => { if (s.Category == PathCategory.SparseInfill) sparse.Add(c); else g[c] |= mark; });
            }
            foreach (int c in sparse)
            {
                int cx = c % nx, cy = c / nx;
                for (int dy = -dilate; dy <= dilate; dy++)
                for (int dx = -dilate; dx <= dilate; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && y >= 0 && x < nx && y < ny) g[y * nx + x] |= SparseRegion;
                }
            }
        }

        var spots = new List<ThinShellSpot>();
        ShellSide Measure(bool top, int set, double thick, int eff)
        {
            var counts = new List<int>();
            int thin = 0;
            byte mark = top ? TopMark : BottomMark;
            for (int i = 0; i < layers.Count; i++)
            for (int c = 0; c < nx * ny; c++)
            {
                if ((grid[i][c] & mark) == 0) continue;
                int n = 0, j = i;
                while (j >= 0 && j < layers.Count && (grid[j][c] & Solid) != 0) { n++; j += top ? -1 : 1; }
                // nur Stellen, unter (bzw. über) denen Sparse-Infill liegt; durchgehend massive Bereiche haben keine Schale
                if (j < 0 || j >= layers.Count || (grid[j][c] & SparseRegion) == 0) continue;
                counts.Add(n);
                if (n < eff)
                {
                    thin++;
                    spots.Add(new ThinShellSpot(i, x0 + (c % nx + 0.5f) * Cell, y0 + (c / nx + 0.5f) * Cell, n, top));
                }
            }
            return new ShellSide(set, thick, eff, counts.Count > 0 ? counts.Min() : null, counts.Count > 0 ? counts.Average() : null, thin);
        }

        var topSide = Measure(true, topSet, topThick, topEff);
        var botSide = Measure(false, botSet, botThick, botEff);
        return new ShellAnalysis(topSide, botSide, spots);
    }

    static void Rasterize(Segment s, float x0, float y0, int nx, int ny, Action<int> mark)
    {
        var a = new Vector2(s.From.X, s.From.Y);
        var b = new Vector2(s.To.X, s.To.Y);
        int steps = Math.Max(1, (int)(Vector2.Distance(a, b) / (Cell / 2)));
        for (int k = 0; k <= steps; k++)
        {
            var p = Vector2.Lerp(a, b, (float)k / steps);
            int x = (int)((p.X - x0) / Cell), y = (int)((p.Y - y0) / Cell);
            if (x >= 0 && y >= 0 && x < nx && y < ny) mark(y * nx + x);
        }
    }
}
