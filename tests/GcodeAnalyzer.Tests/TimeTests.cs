using System.Text;
using GcodeAnalyzer.Core;

namespace GcodeAnalyzer.Tests;

public class TimeTests
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
    public void ReferenceLayerTimesSumToSlicerTotal() =>
        Assert.Equal(new TimeSpan(2, 35, 50).TotalSeconds, Ref.Layers.Sum(l => l.Time), 0.5);

    [Fact]
    public void ReferenceSharesSumToHundred()
    {
        Assert.Equal(100, Ref.Shares.Values.Sum(s => s.TimePercent), 3);
        Assert.Equal(100, Ref.Shares.Values.Sum(s => s.FilamentPercent), 3);
        Assert.Equal(0, Ref.Shares[PathCategory.Travel].FilamentMm);
        Assert.False(Ref.Shares.ContainsKey(PathCategory.Support));
    }

    [Fact]
    public void ReferenceFilamentMatchesSlicer() =>
        Assert.Equal(25908.58, Ref.Shares.Values.Sum(s => s.FilamentMm), 0); // ±1 mm

    [Fact]
    public void ReferenceMinLayerTimeFromFile() => Assert.Equal(4, Ref.MinLayerTime);

    [Fact]
    public void RawTimeWithoutSlicerTotal()
    {
        // 10 mm bei 600 mm/min = 1 s Außenwand, 20 mm Fahrweg bei 1200 mm/min = 1 s, Retract 1 mm bei 60 mm/min = 1 s
        var r = Snippet("""
            M83
            ;LAYER_CHANGE
            ;Z:0.2
            G1 X0 Y0 Z0.2 F600
            ;TYPE:Outer wall
            G1 X10 Y0 E2
            G1 X30 Y0 F1200
            G1 E-1 F60
            """);
        Assert.Equal(3, r.Layers.Single().Time, 3);
        Assert.Equal(1, r.Shares[PathCategory.OuterWall].Seconds, 3);
        Assert.Equal(2, r.Shares[PathCategory.Travel].Seconds, 3);
        Assert.Equal(100, r.Shares[PathCategory.OuterWall].FilamentPercent);
        Assert.Equal(10f, r.Layers.Single().Segments[0].Speed);
        Assert.Equal(2f, r.Layers.Single().Segments[0].Extrusion);
    }

    [Fact]
    public void ShortLayersBelowMinimum()
    {
        var r = Snippet("""
            ; slow_down_layer_time = 4
            M83
            ;LAYER_CHANGE
            ;Z:0.2
            G1 X0 Y0 Z0.2 F600
            ;TYPE:Outer wall
            G1 X50 Y0 E2
            ;LAYER_CHANGE
            ;Z:0.4
            G1 X60 Y0 E1
            """);
        Assert.Equal(4, r.MinLayerTime);
        Assert.Equal([1], r.ShortLayers);
    }

    [Fact]
    public void DefaultMinLayerTime() => Assert.Equal(8, Snippet("G1 X1 Y1").MinLayerTime);
}
