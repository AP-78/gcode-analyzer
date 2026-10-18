using System.Text;
using GcodeAnalyzer.Core;

namespace GcodeAnalyzer.Tests;

public class ShellTests
{
    static AnalysisResult Snippet(string gcode) =>
        GcodeAnalysis.Analyze(new MemoryStream(Encoding.UTF8.GetBytes(gcode)), "snippet.gcode");

    static readonly AnalysisResult Ref = Load();

    static AnalysisResult Load()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "input"), "*.gcode").Single();
        using var s = File.OpenRead(path);
        return GcodeAnalysis.Analyze(s, "ref");
    }

    [Fact]
    public void ReferenceTop()
    {
        var t = Ref.Shells.Top;
        Assert.Equal((2, 0.8, 4), (t.SetLayers, t.SetThickness, t.Effective));
        Assert.Equal(4, t.MeasuredMin);
        Assert.Equal(0, t.ThinSpots);
    }

    [Fact]
    public void ReferenceBottom()
    {
        var b = Ref.Shells.Bottom;
        Assert.Equal((2, 2), (b.SetLayers, b.Effective));
        Assert.Equal(2, b.MeasuredMin);
        Assert.Equal(0, b.ThinSpots);
    }

    static string Stack(string bottomLayerType) => $"""
        ; top_shell_layers = 3
        ; layer_height = 0.2
        ; sparse_infill_density = 20%
        M83
        ;LAYER_CHANGE
        ;Z:0.2
        G1 X0 Y5 Z0.2
        ;TYPE:{bottomLayerType}
        G1 X10 Y5 E1
        ;LAYER_CHANGE
        ;Z:0.4
        G1 X0 Y5 Z0.4
        ;TYPE:Internal solid infill
        G1 X10 Y5 E1
        ;LAYER_CHANGE
        ;Z:0.6
        G1 X0 Y5 Z0.6
        ;TYPE:Top surface
        G1 X10 Y5 E1
        """;

    [Fact]
    public void TwoSolidLayersOverSparseAreThin()
    {
        var t = Snippet(Stack("Sparse infill")).Shells;
        Assert.Equal(3, t.Top.Effective);
        Assert.Equal(2, t.Top.MeasuredMin);
        Assert.True(t.Top.ThinSpots > 0);
        Assert.All(t.ThinSpots, s => Assert.Equal((2, 2, true), (s.Layer, s.Measured, s.Top)));
    }

    [Fact]
    public void FullySolidRegionHasNoShellMeasurement()
    {
        var t = Snippet(Stack("Internal solid infill")).Shells.Top;
        Assert.Null(t.MeasuredMin);
        Assert.Equal(0, t.ThinSpots);
    }
}
