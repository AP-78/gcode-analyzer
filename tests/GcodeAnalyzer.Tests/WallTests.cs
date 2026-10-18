using System.Text;
using GcodeAnalyzer.Core;

namespace GcodeAnalyzer.Tests;

public class WallTests
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
    public void ReferenceTarget()
    {
        Assert.Equal(3, Ref.Walls.TargetLoops);
        Assert.Equal(1.32, Ref.Walls.TargetThickness, 3);
    }

    [Fact]
    public void ReferenceHasNoUnderrunAndThreeExpectedDeviations()
    {
        Assert.Equal(146, Ref.Walls.Layers.Count);
        Assert.Equal(143, Ref.Walls.Layers.Count(w => w.Status == WallStatus.Ok && w.Loops == 3));
        Assert.Equal([62, 144, 145], Ref.Walls.Layers.Where(w => w.Status == WallStatus.ExpectedDeviation).Select(w => w.Layer));
        Assert.DoesNotContain(Ref.Walls.Layers, w => w.Status == WallStatus.Underrun);
    }

    // Quadrat 10x10 als Außenwand, eine Innenwand-Schleife, Soll 3 Schleifen
    const string TwoLoops = """
        ; wall_loops = 3
        ; outer_wall_line_width = 0.42
        ; inner_wall_line_width = 0.45
        M83
        ;LAYER_CHANGE
        ;Z:0.2
        G1 X1 Y1 Z0.2
        ;TYPE:Inner wall
        G1 X9 Y1 E1
        G1 X9 Y9 E1
        G1 X1 Y9 E1
        G1 X1 Y1 E1
        G1 X0 Y0
        ;TYPE:Outer wall
        G1 X10 Y0 E1
        G1 X10 Y10 E1
        G1 X0 Y10 E1
        G1 X0 Y0 E1
        """;

    [Fact]
    public void MissingLoopIsUnderrun()
    {
        var w = Snippet(TwoLoops).Walls.Layers.Single();
        Assert.Equal(2, w.Loops);
        Assert.Equal(0.87, w.Thickness, 3);
        Assert.Equal(WallStatus.Underrun, w.Status);
    }

    [Fact]
    public void GapFillMakesItExpected()
    {
        var w = Snippet(TwoLoops + "\n;TYPE:Gap infill\nG1 X5 Y5\nG1 X6 Y5 E0.1\n").Walls.Layers.Single();
        Assert.Equal(WallStatus.ExpectedDeviation, w.Status);
    }
}
