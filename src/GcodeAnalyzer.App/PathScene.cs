using System.Numerics;
using System.Windows.Media;
using GcodeAnalyzer.Core;
using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using Media = System.Windows.Media;

namespace GcodeAnalyzer.App;

/// <summary>Ein Linienmodell pro Layer; der Layer-Regler schaltet nur Sichtbarkeit und Farben um.</summary>
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
    readonly LineGeometryModel3D[] _models;
    Color4Collection?[] _bright = [], _dim = [];
    int _current = -1;

    public GroupModel3D Root { get; } = new();
    public LineGeometryModel3D Grid { get; }
    public HashSet<PathCategory> Visible { get; }

    public PathScene(AnalysisResult result)
    {
        _result = result;
        Visible = result.Layers.SelectMany(l => l.Segments).Select(s => s.Category).Where(c => c != PathCategory.Travel).ToHashSet();
        _models = result.Layers.Select(_ => new LineGeometryModel3D { Thickness = 1, Color = Colors.White }).ToArray();
        foreach (var m in _models) Root.Children.Add(m);
        Grid = BuildGrid(result);
        Root.Children.Add(Grid);
        Rebuild();
    }

    public IEnumerable<PathCategory> Present =>
        _result.Layers.SelectMany(l => l.Segments).Select(s => s.Category).Distinct().Order();

    /// <summary>Geometrie aller Layer neu aufbauen, z. B. nach Ein-/Ausblenden einer Pfad-Kategorie.</summary>
    // ponytail: baut auf dem UI-Thread; bei sehr großen Dateien in den Hintergrund verlagern, falls es hakt
    public void Rebuild()
    {
        _bright = new Color4Collection?[_models.Length];
        _dim = new Color4Collection?[_models.Length];
        for (int i = 0; i < _models.Length; i++)
        {
            var pos = new Vector3Collection();
            var idx = new IntCollection();
            var bright = new Color4Collection();
            var dim = new Color4Collection();
            foreach (var s in _result.Layers[i].Segments)
            {
                if (!Visible.Contains(s.Category)) continue;
                var v = ToVec(Styles[s.Category].Color);
                var c = ToColor4(v);
                var d = ToColor4(Vector4.Lerp(v, Background, 0.55f));
                idx.Add(pos.Count); pos.Add(s.From);
                idx.Add(pos.Count); pos.Add(s.To);
                bright.Add(c); bright.Add(c);
                dim.Add(d); dim.Add(d);
            }
            _bright[i] = bright;
            _dim[i] = dim;
            _models[i].Geometry = new LineGeometry3D { Positions = pos, Indices = idx, Colors = i == _current ? bright : dim };
        }
    }

    /// <summary>Layer 0..current zeigen, current hervorheben.</summary>
    public void ShowUpTo(int current)
    {
        if (_current >= 0 && _current < _models.Length) SetColors(_current, bright: false);
        _current = current;
        for (int i = 0; i < _models.Length; i++) _models[i].Visibility = i <= current ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        SetColors(current, bright: true);
    }

    void SetColors(int i, bool bright)
    {
        var m = _models[i];
        m.Thickness = bright ? 2 : 1;
        if (m.Geometry is LineGeometry3D g)
            m.Geometry = new LineGeometry3D { Positions = g.Positions, Indices = g.Indices, Colors = bright ? _bright[i] : _dim[i] };
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
    static Color4 ToColor4(Vector4 v) => new(v.X, v.Y, v.Z, v.W);
    static Vector4 ToVec(Media.Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, 1);
}
