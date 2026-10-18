using System.Numerics;

namespace GcodeAnalyzer.Core;

/// <summary>Pfad-Kategorie laut CONTEXT.md; Travel ist der Fahrweg.</summary>
public enum PathCategory : byte
{
    OuterWall, InnerWall, SparseInfill, SolidInfill, TopSurface, BottomSurface,
    GapFill, Bridge, Support, SupportInterface, Skirt, Other, Travel,
}

public readonly record struct Segment(Vector3 From, Vector3 To, PathCategory Category);

public sealed record Layer(int Index, float Z, float Height, Segment[] Segments);

public static class PathCategories
{
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
