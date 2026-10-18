using System.Numerics;

namespace GcodeAnalyzer.Core;

/// <summary>Pfad-Kategorie laut CONTEXT.md; Travel ist der Fahrweg.</summary>
public enum PathCategory : byte
{
    OuterWall, InnerWall, SparseInfill, SolidInfill, TopSurface, BottomSurface,
    GapFill, Bridge, Support, SupportInterface, Skirt, Other, Travel,
}

/// <summary>Gerade Bewegung; Speed in mm/s, Extrusion in mm Filament.</summary>
public readonly record struct Segment(Vector3 From, Vector3 To, PathCategory Category, float Speed, float Extrusion)
{
    public float Length => Vector3.Distance(From, To);

    /// <summary>Volumenstrom in mm³/s bei Nenngeschwindigkeit; filamentArea in mm².</summary>
    public float VolumetricFlow(float filamentArea) => Length > 0 ? Extrusion * filamentArea * Speed / Length : 0;
}

/// <summary>Time: Druckzeit des Layers in Sekunden, auf die Slicer-Gesamtzeit skaliert.</summary>
public sealed record Layer(int Index, float Z, float Height, Segment[] Segments, double Time = 0)
{
    /// <summary>Filament der massiven Füllung (Massiv-Infill, Oberfläche, Unterseite, Brücke) in mm.</summary>
    public double SolidFillMm => Segments.Where(s => s.Category.IsSolidFill()).Sum(s => (double)s.Extrusion);
    public double SparseFillMm => Segments.Where(s => s.Category == PathCategory.SparseInfill).Sum(s => (double)s.Extrusion);
}

/// <summary>Anteil einer Pfad-Kategorie an Filament und Druckzeit.</summary>
public sealed record CategoryShare(double FilamentMm, double Seconds, double FilamentPercent, double TimePercent);

public static class PathCategories
{
    public static bool IsSolidFill(this PathCategory c) =>
        c is PathCategory.SolidInfill or PathCategory.TopSurface or PathCategory.BottomSurface or PathCategory.Bridge;

    // Markierungen von OrcaSlicer/Bambu Studio und PrusaSlicer.
    static readonly Dictionary<string, PathCategory> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Outer wall"] = PathCategory.OuterWall, ["External perimeter"] = PathCategory.OuterWall,
        ["Overhang wall"] = PathCategory.OuterWall, ["Overhang perimeter"] = PathCategory.OuterWall,
        ["Inner wall"] = PathCategory.InnerWall, ["Perimeter"] = PathCategory.InnerWall,
        ["Sparse infill"] = PathCategory.SparseInfill, ["Internal infill"] = PathCategory.SparseInfill,
        ["Internal solid infill"] = PathCategory.SolidInfill, ["Solid infill"] = PathCategory.SolidInfill,
        ["Top surface"] = PathCategory.TopSurface, ["Top solid infill"] = PathCategory.TopSurface, ["Ironing"] = PathCategory.TopSurface,
        ["Bottom surface"] = PathCategory.BottomSurface, ["Bottom solid infill"] = PathCategory.BottomSurface,
        ["Gap infill"] = PathCategory.GapFill, ["Gap fill"] = PathCategory.GapFill,
        ["Bridge"] = PathCategory.Bridge, ["Internal Bridge"] = PathCategory.Bridge, ["Bridge infill"] = PathCategory.Bridge,
        ["Support"] = PathCategory.Support, ["Support material"] = PathCategory.Support, ["Support transition"] = PathCategory.Support,
        ["Support interface"] = PathCategory.SupportInterface, ["Support material interface"] = PathCategory.SupportInterface,
        ["Skirt"] = PathCategory.Skirt, ["Brim"] = PathCategory.Skirt, ["Skirt/Brim"] = PathCategory.Skirt,
    };

    public static PathCategory FromMarker(string marker) =>
        Map.TryGetValue(marker.Trim(), out var c) ? c : PathCategory.Other;
}
