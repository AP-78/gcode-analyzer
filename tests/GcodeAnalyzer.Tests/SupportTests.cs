using System.Text;
using GcodeAnalyzer.Core;

namespace GcodeAnalyzer.Tests;

public class SupportTests
{
    static AnalysisResult Snippet(string gcode) =>
        GcodeAnalysis.Analyze(new MemoryStream(Encoding.UTF8.GetBytes(gcode)), "snippet.gcode");

    [Fact]
    public void ReferenceHasSupportContradiction()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "input"), "*.gcode").Single();
        using var s = File.OpenRead(path);
        var sup = GcodeAnalysis.Analyze(s, "ref").Support;
        Assert.True(sup.Enabled);
        Assert.Equal("tree(manual)", sup.Type);
        Assert.True(sup.BuildPlateOnly);
        Assert.False(sup.HasPaths);
        Assert.True(sup.Contradiction);
    }

    [Fact]
    public void SupportPathsAreDetectedAndShared()
    {
        var sup = Snippet("""
            ; enable_support = 1
            ; support_type = normal(auto)
            M83
            ;LAYER_CHANGE
            ;Z:0.2
            G1 X0 Y0 Z0.2 F600
            ;TYPE:Outer wall
            G1 X10 Y0 E3
            ;TYPE:Support
            G1 X20 Y0 E0.5
            ;TYPE:Support interface
            G1 X30 Y0 E0.5
            """).Support;
        Assert.True(sup.HasPaths);
        Assert.False(sup.Contradiction);
        Assert.Equal(25, sup.FilamentPercent, 3);
        Assert.Equal(200.0 / 3, sup.TimePercent, 3);
    }

    [Fact]
    public void DisabledWithoutPathsIsNoContradiction() =>
        Assert.False(Snippet("; enable_support = 0\nG1 X1 Y1").Support.Contradiction);
}
