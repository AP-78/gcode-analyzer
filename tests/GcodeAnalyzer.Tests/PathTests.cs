using System.Text;
using GcodeAnalyzer.Core;

namespace GcodeAnalyzer.Tests;

public class PathTests
{
    static AnalysisResult Snippet(string gcode) =>
        GcodeAnalysis.Analyze(new MemoryStream(Encoding.UTF8.GetBytes(gcode)), "snippet.gcode");

    static AnalysisResult Reference()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "input"), "*.gcode").Single();
        using var s = File.OpenRead(path);
        return GcodeAnalysis.Analyze(s, "ref");
    }

    [Fact]
    public void ReferenceHasAllExpectedCategories()
    {
        var cats = Reference().Layers.SelectMany(l => l.Segments).Select(s => s.Category).ToHashSet();
        foreach (var c in new[] { PathCategory.OuterWall, PathCategory.InnerWall, PathCategory.SparseInfill, PathCategory.SolidInfill,
                     PathCategory.TopSurface, PathCategory.BottomSurface, PathCategory.GapFill, PathCategory.Bridge, PathCategory.Travel })
            Assert.Contains(c, cats);
        Assert.DoesNotContain(PathCategory.Support, cats);
    }

    [Fact]
    public void ReferenceLayersHaveZAndHeight()
    {
        var r = Reference();
        Assert.Equal(0.2f, r.Layers[0].Z, 3);
        Assert.Equal(0.2f, r.Layers[0].Height, 3);
        Assert.Equal(35.0f, r.Layers[^1].Z, 3);
        Assert.Equal(0.24f, r.Layers[1].Height, 3);
        Assert.Equal((0f, 0f, 220f, 220f), r.Bed);
    }

    [Fact]
    public void SegmentsGetCategoryAndTravel()
    {
        var r = Snippet("""
            ;LAYER_CHANGE
            ;Z:0.2
            ;HEIGHT:0.2
            M83
            G1 X0 Y0 Z0.2 F3000
            ;TYPE:Outer wall
            G1 X10 Y0 E1
            G1 X20 Y0
            ;TYPE:Sparse infill
            G1 X20 Y10 E1
            """);
        var s = r.Layers.Single().Segments;
        Assert.Equal([PathCategory.OuterWall, PathCategory.Travel, PathCategory.SparseInfill], s.Select(x => x.Category));
    }

    [Fact]
    public void NoTravelFromUnknownStartPosition()
    {
        var r = Snippet("""
            M83
            G1 Z3
            G1 X100 Y100 F3000
            G1 X110 Y100 E1
            """);
        var s = r.Layers.Single().Segments;
        Assert.Single(s);
        Assert.Equal(new System.Numerics.Vector3(100, 100, 3), s[0].From);
    }

    [Fact]
    public void StartCodeBelongsToFirstLayer()
    {
        var r = Snippet("""
            M83
            G1 X0 Y0 Z0.2
            G1 X50 Y0 E5
            ;LAYER_CHANGE
            ;Z:0.2
            G1 X50 Y10 E1
            ;LAYER_CHANGE
            ;Z:0.4
            G1 Z0.4
            G1 X50 Y20 E1
            """);
        Assert.Equal(2, r.LayerCount);
        Assert.Equal(2, r.Layers[0].Segments.Length);
    }

    [Fact]
    public void ArcIsInterpolatedAndCountsForDimensions()
    {
        // Halbkreis im Uhrzeigersinn von (10,0) über (0,-10) nach (-10,0)
        var r = Snippet("""
            M83
            G1 X10 Y0 Z0.2
            G2 X-10 Y0 I-10 J0 E5
            """);
        Assert.Equal(10.0, r.Depth, 1);
        Assert.Equal(20.0, r.Width, 1);
        var arc = r.Layers.Single().Segments.Where(s => s.Category != PathCategory.Travel).ToList();
        Assert.True(arc.Count > 50);
        Assert.Equal(-10f, arc.Min(s => s.To.Y), 1);
    }
}
