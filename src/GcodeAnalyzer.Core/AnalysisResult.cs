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

    public required IReadOnlyList<Layer> Layers { get; init; }
    public int LayerCount => Layers.Count;
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

    /// <summary>Filament- und Zeitanteil je Pfad-Kategorie (nur vorkommende).</summary>
    public required IReadOnlyDictionary<PathCategory, CategoryShare> Shares { get; init; }

    /// <summary>Mindest-Layerzeit in Sekunden (aus der Datei, sonst 8 s) und die Layer-Indizes darunter.</summary>
    public double MinLayerTime { get; init; }
    public required IReadOnlyList<int> ShortLayers { get; init; }

    /// <summary>Infill-Einstellungen; Dichte in Prozent, Linienbreite in mm.</summary>
    public string? InfillPattern { get; init; }
    public double? InfillDensity { get; init; }
    public double? InfillLineWidth { get; init; }

    /// <summary>Druckbett aus <c>printable_area</c> (Ecken min/max), sonst null.</summary>
    public (float MinX, float MinY, float MaxX, float MaxY)? Bed { get; init; }
}
