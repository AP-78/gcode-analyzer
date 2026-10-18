using System.Text;
using GcodeAnalyzer.Core;
using Xunit.Abstractions;

namespace GcodeAnalyzer.Tests;

public class MotionTests(ITestOutputHelper o)
{
    static AnalysisResult Snippet(string gcode) =>
        GcodeAnalysis.Analyze(new MemoryStream(Encoding.UTF8.GetBytes(gcode)), "snippet.gcode");

    [Fact]
    public void FlowRetractsAndZHops()
    {
        // 10 mm bei 100 mm/s mit 2 mm Filament (1,75 mm): 2 * 2,405 * 10 = 48,1 mm³/s > 20
        var r = Snippet("""
            M83
            ;LAYER_CHANGE
            ;Z:0.2
            G1 X0 Y0 Z0.2 F6000
            ;TYPE:Outer wall
            G1 X10 Y0 E2
            G1 E-0.8 F2400
            G1 X15 Y0 E-0.2
            G1 Z0.6
            G1 X40 Y0 F12000
            G1 Z0.2
            G1 E1 F2400
            G1 X50 Y0 E0.1 F600
            G10
            """);
        Assert.Equal(48.1, r.MaxVolumetricFlow, 1);
        Assert.Equal(20, r.VolumetricFlowLimit);
        Assert.Equal([0], r.OverFlowLayers);
        Assert.Equal(2, r.RetractCount);
        Assert.Equal(1, r.ZHopCount);
        Assert.Equal(30.8, r.TravelDistanceMm, 1); // Wipe 5 + Z-Hop 2 × 0,4 + Fahrt 25
    }

    [Fact]
    public void FirmwareRetractFollowedByERetractCountsOnce() =>
        Assert.Equal(2, Snippet("M83\nG1 X0 Y0 Z0.2\nG10\nG1 E-0.5\nG11\nG1 X5 Y0 E1\nG10\n").RetractCount);

    [Fact]
    public void Reference()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "input"), "*.gcode").Single();
        using var s = File.OpenRead(path);
        var r = GcodeAnalysis.Analyze(s, "ref");
        o.WriteLine($"flow {r.MaxVolumetricFlow} over {r.OverFlowLayers.Count}");
        Assert.Equal(20, r.VolumetricFlowLimit);
        Assert.Empty(r.OverFlowLayers); // Orca hält das Limit selbst ein
        Assert.Equal(852, r.RetractCount);
        Assert.Equal(852, r.ZHopCount);
    }
}
