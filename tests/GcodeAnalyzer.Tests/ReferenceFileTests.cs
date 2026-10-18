using System.Text;
using GcodeAnalyzer.Core;

namespace GcodeAnalyzer.Tests;

// Referenzdatei: OrcaSlicer 2.4.2, Creality K1, PLA, 0,24 mm (input/).
public class ReferenceFileTests
{
    static readonly AnalysisResult R = Load();

    static AnalysisResult Load()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "input"), "*.gcode").Single();
        using var s = File.OpenRead(path);
        return GcodeAnalysis.Analyze(s, Path.GetFileName(path));
    }

    [Fact] public void Slicer() { Assert.Equal("OrcaSlicer", R.SlicerName); Assert.Equal("2.4.2", R.SlicerVersion); }
    [Fact] public void LayerCount() => Assert.Equal(146, R.LayerCount);
    [Fact] public void LineCount() => Assert.Equal(119813, R.LineCount);
    [Fact] public void FileSize() => Assert.Equal(5480050, R.FileSize);
    [Fact] public void EstimatedTime() => Assert.Equal(new TimeSpan(2, 35, 50), R.EstimatedTime);
    [Fact] public void FilamentWeight() => Assert.Equal(76.65, R.FilamentWeightG);
    [Fact] public void FilamentLength() => Assert.Equal(25908.58, R.FilamentLengthMm);
    [Fact] public void FilamentCost() => Assert.Equal(1.46, R.FilamentCost);

    [Fact]
    public void Dimensions()
    {
        Assert.Equal(187.5, R.Width, 1);
        Assert.Equal(121.7, R.Depth, 1);
        Assert.Equal(35.0, R.Height, 1);
    }

    [Fact]
    public void DetectedSettings()
    {
        Assert.Equal(0.24, R.LayerHeight);
        Assert.Equal(209, R.MaxPrintSpeed, 0);
        Assert.Equal(230, R.NozzleTemp);
        Assert.Equal(65, R.BedTemp);
        Assert.Equal("Creality K1", R.PrinterModel);
        Assert.Equal("PLA", R.FilamentType);
    }

    [Fact] public void SlicerSettings() => Assert.Equal("3", R.Settings["wall_loops"]);

    [Fact]
    public void ThumbnailIsLargestPng()
    {
        Assert.NotNull(R.Thumbnail);
        Assert.Equal(0x89, R.Thumbnail![0]); // PNG-Signatur
        Assert.Equal("PNG", Encoding.ASCII.GetString(R.Thumbnail, 1, 3));
        Assert.True(R.Thumbnail.Length > 3000); // 300x300 statt 96x96
    }

    [Fact]
    public void ReportsProgressUpToOne()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "input"), "*.gcode").Single();
        var seen = new List<double>();
        using var s = File.OpenRead(path);
        GcodeAnalysis.Analyze(s, "x", new SyncProgress(seen.Add));
        Assert.NotEmpty(seen);
        Assert.Equal(1.0, seen[^1]);
    }

    sealed class SyncProgress(Action<double> a) : IProgress<double> { public void Report(double v) => a(v); }
}
