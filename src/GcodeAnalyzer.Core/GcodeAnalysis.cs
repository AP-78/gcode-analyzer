using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GcodeAnalyzer.Core;

public static partial class GcodeAnalysis
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static AnalysisResult Analyze(Stream stream, string fileName,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var settings = new Dictionary<string, string>();
        string? slicer = null, version = null;
        int lines = 0, layerChanges = 0;

        StringBuilder? thumb = null;
        string? bestThumb = null;
        int thumbArea = 0, bestArea = -1;

        bool relE = false, relXyz = false;
        double x = 0, y = 0, z = 0, e = 0, f = 0, maxF = 0;
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue, maxZ = 0;
        double? nozzle = null, bed = null;

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
                if (line.StartsWith(";LAYER_CHANGE", StringComparison.Ordinal)) { layerChanges++; continue; }
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
                    double? nx = null, ny = null, nz = null, ne = null, nf = null;
                    foreach (var r in args.Split(' '))
                    {
                        var t = args[r];
                        if (t.Length < 2) continue;
                        if (!double.TryParse(t[1..], NumberStyles.Float, Inv, out var v)) continue;
                        switch (t[0])
                        {
                            case 'X': nx = v; break;
                            case 'Y': ny = v; break;
                            case 'Z': nz = v; break;
                            case 'E': ne = v; break;
                            case 'F': nf = v; break;
                        }
                    }
                    if (nf is { } fv) f = fv;
                    if (nx is { } xv) x = relXyz ? x + xv : xv;
                    if (ny is { } yv) y = relXyz ? y + yv : yv;
                    if (nz is { } zv) z = relXyz ? z + zv : zv;
                    double de = 0;
                    if (ne is { } ev) { de = relE ? ev : ev - e; e = relE ? e + ev : ev; }
                    // ponytail: Bögen (G2/G3) zählen nur mit ihrem Endpunkt; reicht für die Ausdehnung, exakte Bögen kommen mit der 3D-Ansicht
                    if (de > 0 && (nx != null || ny != null))
                    {
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        maxZ = Math.Max(maxZ, z);
                        maxF = Math.Max(maxF, f);
                    }
                    break;
                }
                case "G90": relXyz = false; relE = false; break;
                case "G91": relXyz = true; relE = true; break;
                case "M82": relE = false; break;
                case "M83": relE = true; break;
                case "G92":
                    foreach (var r in args.Split(' '))
                        if (args[r] is ['E', .. var rest] && double.TryParse(rest, NumberStyles.Float, Inv, out var v)) e = v;
                    break;
                case "M104" or "M109": nozzle ??= SValue(args); break;
                case "M140" or "M190": bed ??= SValue(args); break;
            }
        }
        progress?.Report(1.0);

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
            LayerCount = layerChanges > 0 ? layerChanges : (int)(Num(settings, "total layer number") ?? 0),
            EstimatedTime = settings.TryGetValue("estimated printing time (normal mode)", out var et) ? ParseDuration(et) : null,
            FilamentLengthMm = Num(settings, "filament used [mm]"),
            FilamentWeightG = Num(settings, "total filament used [g]") ?? Num(settings, "filament used [g]"),
            FilamentCost = Num(settings, "total filament cost"),
            Width = any ? maxX - minX : 0,
            Depth = any ? maxY - minY : 0,
            Height = maxZ,
            LayerHeight = Num(settings, "layer_height"),
            MaxPrintSpeed = maxF / 60,
            NozzleTemp = nozzle,
            BedTemp = bed,
            PrinterModel = settings.GetValueOrDefault("printer_model"),
            FilamentType = settings.GetValueOrDefault("filament_type"),
        };
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

    [GeneratedRegex(@"^; generated by (\S+) (\S+)")] private static partial Regex GeneratedBy();
    [GeneratedRegex(@"^; ([^=:]+?)\s*[=:]\s*(.*)$")] private static partial Regex Setting();
    [GeneratedRegex(@"(\d+)x(\d+)")] private static partial Regex ThumbBegin();
    [GeneratedRegex(@"(\d+)\s*([dhms])")] private static partial Regex Duration();
}
