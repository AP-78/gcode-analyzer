namespace GcodeAnalyzer.Core;

/// <summary>Ergebnis der Analyse einer G-Code-Datei. Begriffe siehe CONTEXT.md.</summary>
public sealed record AnalysisResult
{
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public int LineCount { get; init; }
    public string? SlicerName { get; init; }
    public string? SlicerVersion { get; init; }

    /// <summary>Slicer-Einstellungen aus den Kommentaren der G-Code-Datei.</summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    /// <summary>Größtes eingebettetes PNG-Vorschaubild.</summary>
    public byte[]? Thumbnail { get; init; }

    public int LayerCount { get; init; }
    public TimeSpan? EstimatedTime { get; init; }
    public double? FilamentLengthMm { get; init; }
    public double? FilamentWeightG { get; init; }
    public double? FilamentCost { get; init; }

    /// <summary>Ausdehnung aller Extrusionen; Höhe ist die höchste Extrusion über dem Druckbett.</summary>
    public double Width { get; init; }
    public double Depth { get; init; }
    public double Height { get; init; }

    public double? LayerHeight { get; init; }
    /// <summary>Höchste Geschwindigkeit einer Extrusionsbewegung in mm/s.</summary>
    public double MaxPrintSpeed { get; init; }
    public double? NozzleTemp { get; init; }
    public double? BedTemp { get; init; }
    public string? PrinterModel { get; init; }
    public string? FilamentType { get; init; }
}
