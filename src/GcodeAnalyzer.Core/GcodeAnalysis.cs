using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace GcodeAnalyzer.Core;

public static partial class GcodeAnalysis
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const float ArcStepMm = 0.5f;

    public static AnalysisResult Analyze(Stream stream, string fileName,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var settings = new Dictionary<string, string>();
        string? slicer = null, version = null;
        int lines = 0;

        StringBuilder? thumb = null;
        string? bestThumb = null;
        int thumbArea = 0, bestArea = -1;

        var layers = new List<Layer>();
        var segs = new List<Segment>();          // Segmente des laufenden Layers (vor dem ersten Layer-Wechsel: Startcode)
        float layerZ = float.NaN, layerH = 0;
        bool explicitZ = false;                  // Z aus ;Z:-Kommentar, sonst höchste Extrusion des Layers
        var tools = new HashSet<int>();
        var category = PathCategory.Other;

        bool relE = false, relXyz = false;
        var pos = Vector3.Zero;
        bool knownXY = false; // bis zur ersten Bewegung mit X und Y ist die Startposition unbekannt
        double e = 0, f = 0, maxF = 0;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, maxZ = 0;
        double? nozzle = null, bed = null;
        var pts = new List<Vector3>();
        double layerTime = 0;                    // ungeskaliert, ohne Beschleunigung
        int retracts = 0, zHops = 0;
        bool retracted = false;
        var catTime = new double[Enum.GetValues<PathCategory>().Length];
        var catE = new double[catTime.Length];

        void FlushLayer()
        {
            if (float.IsNaN(layerZ)) return; // Startcode wandert in den ersten Layer
            if (segs.Count == 0 && layerTime == 0) return; // doppelte Layer-Markierung
            var z = explicitZ || !segs.Any(sg => sg.Extrusion > 0) ? layerZ : segs.Where(sg => sg.Extrusion > 0).Max(sg => sg.To.Z);
            layers.Add(new Layer(layers.Count, z, layerH, segs.ToArray(), layerTime));
            segs.Clear();
            layerTime = 0;
        }

        long length = stream.CanSeek ? stream.Length : 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1 << 16);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (++lines % 50_000 == 0)
            {
                ct.ThrowIfCancellationRequested();
                if (length > 0) progress?.Report(Math.Min(0.99, (double)stream.Position / length));
            }
            if (line.Length == 0) continue;

            if (line[0] == ';')
            {
                if (thumb != null)
                {
                    if (line.StartsWith("; thumbnail end", StringComparison.Ordinal))
                    {
                        if (thumbArea > bestArea) { bestArea = thumbArea; bestThumb = thumb.ToString(); }
                        thumb = null;
                    }
                    else thumb.Append(line.AsSpan(1).Trim());
                    continue;
                }
                // OrcaSlicer/PrusaSlicer: ;TYPE: ;LAYER_CHANGE ;Z: ;HEIGHT:  Bambu Studio: ; FEATURE: ; CHANGE_LAYER ; Z_HEIGHT: ; LAYER_HEIGHT:  Cura: ;LAYER:
                if (Marker(line, ";TYPE:", "; FEATURE:") is { } type) { category = PathCategories.FromMarker(type); continue; }
                if (line.StartsWith(";LAYER_CHANGE", StringComparison.Ordinal) || line.StartsWith("; CHANGE_LAYER", StringComparison.Ordinal)
                    || line.StartsWith(";LAYER:", StringComparison.Ordinal))
                {
                    FlushLayer();
                    layerZ = pos.Z; layerH = 0; explicitZ = false;
                    continue;
                }
                if (Marker(line, ";Z:", "; Z_HEIGHT:") is { } zs) { if (float.TryParse(zs, NumberStyles.Float, Inv, out var lz)) { layerZ = lz; explicitZ = true; } continue; }
                if (Marker(line, ";HEIGHT:", "; LAYER_HEIGHT:") is { } hs) { if (float.TryParse(hs, NumberStyles.Float, Inv, out var lh)) layerH = lh; continue; }
                if (line.StartsWith("; thumbnail begin ", StringComparison.Ordinal))
                {
                    var m = ThumbBegin().Match(line);
                    thumbArea = m.Success ? int.Parse(m.Groups[1].Value) * int.Parse(m.Groups[2].Value) : 0;
                    thumb = new StringBuilder();
                    continue;
                }
                if (slicer == null && GeneratedBy().Match(line) is { Success: true } g)
                {
                    slicer = g.Groups[1].Value; version = g.Groups[2].Value;
                    continue;
                }
                if (Setting().Match(line) is { Success: true } s)
                    settings[s.Groups[1].Value.Trim()] = s.Groups[2].Value.Trim().Trim('"');
                continue;
            }

            var code = line.AsSpan();
            int semi = code.IndexOf(';');
            if (semi >= 0) code = code[..semi];
            code = code.Trim();
            if (code.IsEmpty) continue;
            int sp = code.IndexOf(' ');
            var cmd = sp < 0 ? code : code[..sp];
            var args = sp < 0 ? ReadOnlySpan<char>.Empty : code[(sp + 1)..];

            switch (cmd)
            {
                case "G0" or "G1" or "G2" or "G3":
                {
                    float? nx = null, ny = null, nz = null, ci = null, cj = null;
                    double? ne = null;
                    foreach (var r in args.Split(' '))
                    {
                        var t = args[r];
                        if (t.Length < 2) continue;
                        if (!double.TryParse(t[1..], NumberStyles.Float, Inv, out var v)) continue;
                        switch (t[0])
                        {
                            case 'X': nx = (float)v; break;
                            case 'Y': ny = (float)v; break;
                            case 'Z': nz = (float)v; break;
                            case 'E': ne = v; break;
                            case 'F': f = v; break;
                            case 'I': ci = (float)v; break;
                            case 'J': cj = (float)v; break;
                        }
                    }
                    var to = pos;
                    if (nx is { } xv) to.X = relXyz ? to.X + xv : xv;
                    if (ny is { } yv) to.Y = relXyz ? to.Y + yv : yv;
                    if (nz is { } zv) to.Z = relXyz ? to.Z + zv : zv;
                    double de = 0;
                    if (ne is { } ev) { de = relE ? ev : ev - e; e = relE ? e + ev : ev; }
                    if (de < 0 && !retracted) { retracts++; retracted = true; }
                    else if (de > 0) retracted = false;
                    if (de <= 0 && to.Z < pos.Z && knownXY) zHops++; // Z-Hop endet mit Absenken ohne Extrusion
                    float speed = (float)(f / 60);
                    if (to == pos)
                    {
                        // Retract/Unretract: kostet Zeit, zählt zum Fahrweg
                        if (de != 0 && speed > 0) { double t = Math.Abs(de) / speed; layerTime += t; catTime[(int)PathCategory.Travel] += t; }
                        break;
                    }
                    if (!knownXY)
                    {
                        pos = to;
                        knownXY = nx != null && ny != null;
                        if (!knownXY || de <= 0) break;
                    }

                    bool extruding = de > 0 && (nx != null || ny != null);
                    var cat = extruding ? category : PathCategory.Travel;
                    pts.Clear();
                    if (cmd is "G2" or "G3" && (ci != null || cj != null))
                        Arc(pos, to, new Vector2(ci ?? 0, cj ?? 0), cmd is "G2", pts);
                    else pts.Add(to);

                    var from = pos;
                    float total = 0;
                    var prev = pos;
                    foreach (var p in pts) { total += Vector3.Distance(prev, p); prev = p; }
                    if (speed > 0) { double t = total / speed; layerTime += t; catTime[(int)cat] += t; }
                    if (extruding) catE[(int)cat] += de;
                    foreach (var p in pts)
                    {
                        float part = total > 0 ? Vector3.Distance(from, p) / total : 1;
                        segs.Add(new Segment(from, p, cat, speed, extruding ? (float)(de * part) : 0));
                        if (extruding)
                        {
                            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                            maxZ = Math.Max(maxZ, p.Z);
                        }
                        from = p;
                    }
                    if (extruding) maxF = Math.Max(maxF, f);
                    pos = to;
                    break;
                }
                case "G90": relXyz = false; relE = false; break;
                case "G91": relXyz = true; relE = true; break;
                case "G10": if (!retracted) { retracts++; retracted = true; } break;
                case "G11": retracted = false; break;
                case "M82": relE = false; break;
                case "M83": relE = true; break;
                case "G92":
                    foreach (var r in args.Split(' '))
                        if (args[r] is ['E', .. var rest] && double.TryParse(rest, NumberStyles.Float, Inv, out var v)) e = v;
                    break;
                case "M104" or "M109": nozzle ??= SValue(args); break;
                case "M140" or "M190": bed ??= SValue(args); break;
            }
            if (cmd.Length > 1 && cmd[0] == 'T' && int.TryParse(cmd[1..], out var tool)) tools.Add(tool);
        }
        FlushLayer();
        if (layers.Count == 0 && segs.Count > 0) layers.Add(new Layer(0, pos.Z, 0, segs.ToArray(), layerTime));
        progress?.Report(1.0);

        // ponytail: Zeit ohne Beschleunigung, linear auf die Slicer-Gesamtzeit skaliert; echte Planer-Simulation, falls Verhältnisse zwischen Layern nicht reichen
        // Bambu Studio: "; model printing time: 1h 10m 41s; total estimated time: 1h 17m 20s"
        var estimated = settings.TryGetValue("estimated printing time (normal mode)", out var et) ? ParseDuration(et)
            : settings.TryGetValue("model printing time", out var mt) ? ParseDuration(mt.Split("total estimated time:").Last()) : null;
        double raw = layers.Sum(l => l.Time);
        double scale = estimated is { } est && raw > 0 ? est.TotalSeconds / raw : 1;
        for (int i = 0; i < layers.Count; i++) layers[i] = layers[i] with { Time = layers[i].Time * scale };

        double sumT = catTime.Sum() * scale, sumE = catE.Sum();
        var shares = new Dictionary<PathCategory, CategoryShare>();
        for (int c = 0; c < catTime.Length; c++)
            if (catTime[c] > 0 || catE[c] > 0)
                shares[(PathCategory)c] = new CategoryShare(catE[c], catTime[c] * scale,
                    sumE > 0 ? catE[c] / sumE * 100 : 0, sumT > 0 ? catTime[c] * scale / sumT * 100 : 0);

        double diameter = Num(settings, "filament_diameter") ?? 1.75;
        float area = (float)(Math.PI * diameter * diameter / 4);
        double flowLimit = Num(settings, "filament_max_volumetric_speed") ?? 20;
        double maxFlow = 0, travel = 0;
        var overFlow = new List<int>();
        foreach (var l in layers)
        {
            bool over = false;
            foreach (var sg in l.Segments)
            {
                if (sg.Category == PathCategory.Travel) { travel += sg.Length; continue; }
                double fl = sg.VolumetricFlow(area);
                maxFlow = Math.Max(maxFlow, fl);
                over |= fl > flowLimit * 1.02; // Rundung der E-Werte im G-Code
            }
            if (over) overFlow.Add(l.Index);
        }

        double minLayerTime = Num(settings, "slow_down_layer_time") ?? Num(settings, "slowdown_below_layer_time") ?? 8;

        bool any = maxX >= minX;
        return new AnalysisResult
        {
            FileName = fileName,
            FileSize = length,
            LineCount = lines,
            SlicerName = slicer,
            SlicerVersion = version,
            Settings = settings,
            Thumbnail = bestThumb == null ? null : TryBase64(bestThumb),
            Layers = layers,
            EstimatedTime = estimated,
            Shares = shares,
            FilamentDiameter = diameter,
            MaxVolumetricFlow = maxFlow,
            VolumetricFlowLimit = flowLimit,
            OverFlowLayers = overFlow,
            RetractCount = retracts,
            ZHopCount = zHops,
            TravelDistanceMm = travel,
            MinLayerTime = minLayerTime,
            ShortLayers = layers.Where(l => l.Time < minLayerTime).Select(l => l.Index).ToList(),
            FilamentLengthMm = Num(settings, "filament used [mm]") ?? Num(settings, "total filament length [mm]"),
            FilamentWeightG = Num(settings, "total filament used [g]") ?? Num(settings, "filament used [g]") ?? Num(settings, "total filament weight [g]"),
            FilamentCost = Num(settings, "total filament cost") ?? Num(settings, "filament cost"),
            Warnings = Warnings(slicer, layers, tools),
            Width = any ? maxX - minX : 0,
            Depth = any ? maxY - minY : 0,
            Height = maxZ,
            LayerHeight = Num(settings, "layer_height"),
            MaxPrintSpeed = maxF / 60,
            NozzleTemp = nozzle,
            BedTemp = bed,
            PrinterModel = settings.GetValueOrDefault("printer_model"),
            FilamentType = settings.GetValueOrDefault("filament_type"),
            InfillPattern = settings.GetValueOrDefault("sparse_infill_pattern") ?? settings.GetValueOrDefault("fill_pattern"),
            InfillDensity = Num(settings, "sparse_infill_density") ?? Num(settings, "fill_density"),
            InfillLineWidth = Width(settings, "sparse_infill_line_width") ?? Width(settings, "infill_extrusion_width"),
            Support = SupportAnalysis.From(settings, shares),
            Walls = WallAnalysis.From(layers, settings, k => Num(settings, k), k => Width(settings, k)),
            Shells = ShellAnalysis.From(layers, k => Num(settings, k), Num(settings, "layer_height")),
            Bed = ParseBed(settings.GetValueOrDefault("printable_area") ?? settings.GetValueOrDefault("bed_shape")),
        };
    }

    /// <summary>Bogen um Mittelpunkt start+ij in Stücke von höchstens 0,5 mm und 10° zerlegen (Sehne ≈ Bogen); Z wird linear mitgeführt.</summary>
    static void Arc(Vector3 from, Vector3 to, Vector2 ij, bool clockwise, List<Vector3> pts)
    {
        var c = new Vector2(from.X, from.Y) + ij;
        float r = ij.Length();
        float a0 = MathF.Atan2(from.Y - c.Y, from.X - c.X);
        float a1 = MathF.Atan2(to.Y - c.Y, to.X - c.X);
        float sweep = a1 - a0;
        if (clockwise) { if (sweep >= 0) sweep -= 2 * MathF.PI; }
        else if (sweep <= 0) sweep += 2 * MathF.PI;
        int n = Math.Clamp((int)MathF.Ceiling(Math.Max(MathF.Abs(sweep) * r / ArcStepMm, MathF.Abs(sweep) / (MathF.PI / 18))), 1, 720);
        for (int i = 1; i < n; i++)
        {
            float t = (float)i / n, a = a0 + sweep * t;
            pts.Add(new Vector3(c.X + r * MathF.Cos(a), c.Y + r * MathF.Sin(a), from.Z + (to.Z - from.Z) * t));
        }
        pts.Add(to);
    }

    /// <summary>"0x0,220x0,220x220,0x220" → Ecken min/max.</summary>
    static (float, float, float, float)? ParseBed(string? s)
    {
        if (s == null) return null;
        var c = s.Split(',').Select(p => p.Split('x'))
            .Where(p => p.Length == 2)
            .Select(p => (float.TryParse(p[0], Inv, out var x) ? x : float.NaN, float.TryParse(p[1], Inv, out var y) ? y : float.NaN))
            .Where(p => !float.IsNaN(p.Item1) && !float.IsNaN(p.Item2)).ToList();
        if (c.Count < 3) return null;
        return (c.Min(p => p.Item1), c.Min(p => p.Item2), c.Max(p => p.Item1), c.Max(p => p.Item2));
    }

    static readonly string[] Supported = ["OrcaSlicer", "BambuStudio", "PrusaSlicer", "SuperSlicer"];

    static List<string> Warnings(string? slicer, List<Layer> layers, HashSet<int> tools)
    {
        var w = new List<string>();
        if (layers.Count == 0) w.Add("Keine Druckbewegungen gefunden. Ist das eine G-Code-Datei?");
        else if (slicer == null) w.Add("Slicer nicht erkannt; Wände, Schalen und Infill können fehlen.");
        else if (!Supported.Contains(slicer, StringComparer.OrdinalIgnoreCase))
            w.Add($"{slicer} wird nicht unterstützt; Wände, Schalen und Infill können fehlen.");
        if (tools.Count > 1) w.Add($"Mehrere Filamente ({tools.Count}); Verbrauch und Anteile gelten für alle zusammen.");
        return w;
    }

    static string? Marker(string line, string a, string b) =>
        line.StartsWith(a, StringComparison.Ordinal) ? line[a.Length..].Trim()
        : line.StartsWith(b, StringComparison.Ordinal) ? line[b.Length..].Trim() : null;

    /// <summary>Linienbreite in mm; PrusaSlicer erlaubt "112%" (der Düse) und 0 für automatisch.</summary>
    static double? Width(Dictionary<string, string> s, string key)
    {
        if (!s.TryGetValue(key, out var v)) return null;
        v = v.Split(',')[0].Trim();
        bool pct = v.EndsWith('%');
        if (!double.TryParse(v.TrimEnd('%'), NumberStyles.Float, Inv, out var d) || d <= 0) return null;
        return pct ? (Num(s, "nozzle_diameter") ?? 0.4) * d / 100 : d;
    }

    static double? SValue(ReadOnlySpan<char> args)
    {
        foreach (var r in args.Split(' '))
            if (args[r] is ['S', .. var rest] && double.TryParse(rest, NumberStyles.Float, Inv, out var v) && v > 0) return v;
        return null;
    }

    // Mehrere Filamente stehen kommagetrennt; hier zählt der erste Wert.
    static double? Num(Dictionary<string, string> s, string key) =>
        s.TryGetValue(key, out var v) && double.TryParse(v.Split(',')[0].Trim().TrimEnd('%'), NumberStyles.Float, Inv, out var d) ? d : null;

    static byte[]? TryBase64(string s)
    {
        try { return Convert.FromBase64String(s); } catch (FormatException) { return null; }
    }

    /// <summary>"1d 2h 35m 50s" → TimeSpan.</summary>
    public static TimeSpan? ParseDuration(string s)
    {
        var ms = Duration().Matches(s);
        if (ms.Count == 0) return null;
        var t = TimeSpan.Zero;
        foreach (Match m in ms)
        {
            int n = int.Parse(m.Groups[1].Value);
            t += m.Groups[2].Value switch { "d" => TimeSpan.FromDays(n), "h" => TimeSpan.FromHours(n), "m" => TimeSpan.FromMinutes(n), _ => TimeSpan.FromSeconds(n) };
        }
        return t;
    }

    // "; generated by OrcaSlicer 2.4.2 on …", Bambu Studio "; BambuStudio 01.09.00.70", Cura ";Generated with Cura_SteamEngine 5.7.0"
    [GeneratedRegex(@"^;\s*(?:generated by |Generated with )?(OrcaSlicer|BambuStudio|PrusaSlicer|SuperSlicer|Cura_SteamEngine) v?(\d[\w.\-+]*)")] private static partial Regex GeneratedBy();
    [GeneratedRegex(@"^; ([^=:]+?)\s*[=:]\s*(.*)$")] private static partial Regex Setting();
    [GeneratedRegex(@"(\d+)x(\d+)")] private static partial Regex ThumbBegin();
    [GeneratedRegex(@"(\d+)\s*([dhms])")] private static partial Regex Duration();
}
