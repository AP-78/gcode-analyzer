using GcodeAnalyzer.Core;

namespace GcodeAnalyzer.Tests;

public class InfillTests
{
    static readonly AnalysisResult Ref = Load();

    static AnalysisResult Load()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "input"), "*.gcode").Single();
        using var s = File.OpenRead(path);
        return GcodeAnalysis.Analyze(s, "ref");
    }

    [Fact]
    public void ReferenceInfillSettings()
    {
        Assert.Equal("gyroid", Ref.InfillPattern);
        Assert.Equal(10, Ref.InfillDensity);
        Assert.Equal(0.45, Ref.InfillLineWidth);
    }

    [Fact]
    public void FirstLayerIsSolidMiddleLayerIsSparse()
    {
        Assert.True(Ref.Layers[0].SolidFillMm > 0);
        Assert.Equal(0, Ref.Layers[0].SparseFillMm);
        Assert.True(Ref.Layers[29].SparseFillMm > 0);
    }
}
