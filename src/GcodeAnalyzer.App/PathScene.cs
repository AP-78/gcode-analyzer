using System.Numerics;
using System.Windows.Media;
using GcodeAnalyzer.Core;
using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using Media = System.Windows.Media;

namespace GcodeAnalyzer.App;

public enum ColorMode { Category, Speed, Flow, LayerTime }

/// <summary>3D-Pfade; Layer-Regler und Legende schalten nur Sichtbarkeit, Einfärbung tauscht nur Farben.</summary>
public sealed class PathScene
{
    public static readonly IReadOnlyDictionary<PathCategory, (string Name, Media.Color Color)> Styles = new Dictionary<PathCategory, (string, Media.Color)>
    {
        [PathCategory.OuterWall] = ("Außenwand", Rgb(0xFF7D38)),
        [PathCategory.InnerWall] = ("Innenwand", Rgb(0xFFE14D)),
        [PathCategory.SparseInfill] = ("Sparse-Infill", Rgb(0xB03029)),
        [PathCategory.SolidInfill] = ("Massiv-Infill", Rgb(0x9654CC)),
        [PathCategory.TopSurface] = ("Oberfläche", Rgb(0xF04040)),
        [PathCategory.BottomSurface] = ("Unterseite", Rgb(0x6A6AFF)),
        [PathCategory.GapFill] = ("Lückenfüllung", Rgb(0x3EA8A8)),
        [PathCategory.Bridge] = ("Brücke", Rgb(0x4D80BA)),
        [PathCategory.Support] = ("Support", Rgb(0x33CC33)),
        [PathCategory.SupportInterface] = ("Support-Interface", Rgb(0x127F12)),
        [PathCategory.Skirt] = ("Skirt/Brim", Rgb(0x00876E)),
        [PathCategory.Other] = ("Sonstiges", Rgb(0x888888)),
        [PathCategory.Travel] = ("Fahrweg", Rgb(0x9DB4FF)),
    };

    static readonly Vector4 Background = new(0.19f, 0.19f, 0.19f, 1);

    readonly AnalysisResult _result;

    /// <summary>Ein Linienmodell je Layer und Pfad-Kategorie; Positionen werden nur einmal aufgebaut.</summary>
    // ponytail: Zahl der Modelle = Layer × Kategorien (hier ~1.300); bei sehr vielen Layern Draw-Calls bündeln, falls es ruckelt
    sealed class Part(int layer, PathCategory category, Segment[] segments, LineGeometryModel3D model)
    {
        public int Layer { get; } = layer;
        public PathCategory Category { get; } = category;
        public Segment[] Segments { get; } = segments;
        public LineGeometryModel3D Model { get; } = model;
        public Color4Collection Bright = [], Dim = [];
    }

    readonly List<Part> _parts = [];
    readonly List<Part>[] _byLayer;
    int _current = -1;

    public GroupModel3D Root { get; } = new();
    public LineGeometryModel3D Grid { get; }
    /// <summary>Kreuze an dünnen Schalenstellen.</summary>
    public LineGeometryModel3D ThinShells { get; }
    public HashSet<PathCategory> Visible { get; }
    /// <summary>Wandpfade von Layern mit Wand-Unterschreitung rot, mit erwarteter Abweichung grau zeichnen.</summary>
    public bool MarkWalls { get; set; } = true;
    readonly Dictionary<int, WallStatus> _wallStatus;
    public ColorMode Mode { get; set; } = ColorMode.Category;
    readonly double _maxSpeed, _maxFlow, _minTime, _maxTime;

    /// <summary>Wertebereich der aktuellen Einfärbung für die Legende.</summary>
    public (double Min, double Max, string Unit) Range => Mode switch
    {
        ColorMode.Speed => (0, _maxSpeed, "mm/s"),
        ColorMode.Flow => (0, _maxFlow, "mm³/s"),
        ColorMode.LayerTime => (_minTime, _maxTime, "s"),
        _ => (0, 0, ""),
    };
    public static readonly Media.Color UnderrunColor = Rgb(0xFF2020), ExpectedColor = Rgb(0xA0A0A0), ThinShellColor = Rgb(0xFF00FF);

    public PathScene(AnalysisResult result)
    {
        _result = result;
        _wallStatus = result.Walls.Layers.ToDictionary(w => w.Layer, w => w.Status);
        var ext = result.Layers.SelectMany(l => l.Segments).Where(sg => sg.Category != PathCategory.Travel && sg.Extrusion > 0).ToList();
        _maxSpeed = Math.Max(1, ext.Count > 0 ? ext.Max(sg => sg.Speed) : 1);
        _maxFlow = Math.Max(result.VolumetricFlowLimit, result.MaxVolumetricFlow);
        _minTime = result.Layers.Count > 0 ? result.Layers.Min(l => l.Time) : 0;
        _maxTime = Math.Max(_minTime + 1, result.Layers.Count > 0 ? result.Layers.Max(l => l.Time) : 1);
        Visible = result.Layers.SelectMany(l => l.Segments).Select(s => s.Category).Where(c => c != PathCategory.Travel).ToHashSet();

        _byLayer = new List<Part>[result.Layers.Count];
        foreach (var layer in result.Layers)
        {
            _byLayer[layer.Index] = [];
            foreach (var g in layer.Segments.GroupBy(s => s.Category))
            {
                var segs = g.ToArray();
                var pos = new Vector3Collection(segs.Length * 2);
                var idx = new IntCollection(segs.Length * 2);
                foreach (var s in segs) { idx.Add(pos.Count); pos.Add(s.From); idx.Add(pos.Count); pos.Add(s.To); }
                var model = new LineGeometryModel3D { Thickness = 1, Color = Colors.White, Geometry = new LineGeometry3D { Positions = pos, Indices = idx } };
                var part = new Part(layer.Index, g.Key, segs, model);
                _parts.Add(part);
                _byLayer[layer.Index].Add(part);
                Root.Children.Add(model);
            }
        }
        Grid = BuildGrid(result);
        Root.Children.Add(Grid);
        var tb = new LineBuilder();
        foreach (var t in result.Shells.ThinSpots)
        {
            var z = result.Layers[t.Layer].Z + 0.05f;
            tb.AddLine(new Vector3(t.X - 0.5f, t.Y - 0.5f, z), new Vector3(t.X + 0.5f, t.Y + 0.5f, z));
            tb.AddLine(new Vector3(t.X - 0.5f, t.Y + 0.5f, z), new Vector3(t.X + 0.5f, t.Y - 0.5f, z));
        }
        ThinShells = new LineGeometryModel3D { Geometry = result.Shells.ThinSpots.Count > 0 ? tb.ToLineGeometry3D() : null, Color = ThinShellColor, Thickness = 2 };
        Root.Children.Add(ThinShells);
        Recolor();
    }

    public IEnumerable<PathCategory> Present => _parts.Select(p => p.Category).Distinct().Order();

    /// <summary>Farben nach Einfärbung und Wand-Markierung neu berechnen; die Geometrie bleibt.</summary>
    public void Recolor()
    {
        foreach (var p in _parts)
        {
            var status = MarkWalls ? _wallStatus.GetValueOrDefault(p.Layer) : WallStatus.Ok;
            bool wall = p.Category is PathCategory.OuterWall or PathCategory.InnerWall;
            p.Bright = new Color4Collection(p.Segments.Length * 2);
            p.Dim = new Color4Collection(p.Segments.Length * 2);
            foreach (var s in p.Segments)
            {
                var v = Mode == ColorMode.Category || s.Category == PathCategory.Travel
                    ? ToVec(!wall || status == WallStatus.Ok ? Styles[s.Category].Color
                        : status == WallStatus.Underrun ? UnderrunColor : ExpectedColor)
                    : Heat(Mode switch
                    {
                        ColorMode.Speed => s.Speed / _maxSpeed,
                        ColorMode.Flow => s.VolumetricFlow(_result.FilamentArea) / _maxFlow,
                        _ => (_result.Layers[p.Layer].Time - _minTime) / (_maxTime - _minTime),
                    });
                var c = ToColor4(v);
                var d = ToColor4(Vector4.Lerp(v, Background, 0.55f));
                p.Bright.Add(c); p.Bright.Add(c);
                p.Dim.Add(d); p.Dim.Add(d);
            }
            SetColors(p, p.Layer == _current);
        }
    }

    /// <summary>Layer 0..current und eingeschaltete Pfad-Kategorien zeigen, current hervorheben.</summary>
    public void ShowUpTo(int current)
    {
        int previous = _current;
        _current = current;
        foreach (var p in _parts)
            p.Model.Visibility = p.Layer <= current && Visible.Contains(p.Category)
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (previous >= 0 && previous < _byLayer.Length) foreach (var p in _byLayer[previous]) SetColors(p, false);
        if (current >= 0 && current < _byLayer.Length) foreach (var p in _byLayer[current]) SetColors(p, true);
    }

    static void SetColors(Part p, bool bright)
    {
        p.Model.Thickness = bright ? 2 : 1;
        if (p.Model.Geometry is LineGeometry3D g)
            p.Model.Geometry = new LineGeometry3D { Positions = g.Positions, Indices = g.Indices, Colors = bright ? p.Bright : p.Dim };
    }

    static LineGeometryModel3D BuildGrid(AnalysisResult r)
    {
        var (x0, y0, x1, y1) = r.Bed ?? (0, 0, (float)Math.Max(100, r.Width), (float)Math.Max(100, r.Depth));
        var b = new LineBuilder();
        for (float x = x0; x <= x1 + 0.01f; x += 10) b.AddLine(new Vector3(x, y0, 0), new Vector3(x, y1, 0));
        for (float y = y0; y <= y1 + 0.01f; y += 10) b.AddLine(new Vector3(x0, y, 0), new Vector3(x1, y, 0));
        return new LineGeometryModel3D { Geometry = b.ToLineGeometry3D(), Color = Rgb(0x555555), Thickness = 0.7 };
    }

    static Media.Color Rgb(int v) => Media.Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    /// <summary>Farbverlauf blau → cyan → grün → gelb → rot für 0..1.</summary>
    public static readonly Media.Color[] HeatStops = [Rgb(0x2050FF), Rgb(0x20D0FF), Rgb(0x30D040), Rgb(0xFFE14D), Rgb(0xFF2020)];

    static Vector4 Heat(double t)
    {
        t = Math.Clamp(t, 0, 1) * (HeatStops.Length - 1);
        int k = Math.Min((int)t, HeatStops.Length - 2);
        return Vector4.Lerp(ToVec(HeatStops[k]), ToVec(HeatStops[k + 1]), (float)(t - k));
    }

    static Color4 ToColor4(Vector4 v) => new(v.X, v.Y, v.Z, v.W);
    static Vector4 ToVec(Media.Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, 1);
}
