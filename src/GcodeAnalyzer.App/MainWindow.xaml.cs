using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GcodeAnalyzer.Core;
using HelixToolkit.Wpf.SharpDX;
using Microsoft.Win32;

namespace GcodeAnalyzer.App;

public record Row(string Label, string Value);
public record Section(string Title, IReadOnlyList<Row> Rows);
public record LegendItem(PathCategory Category, string Name, Brush Brush) { public bool IsVisible { get; set; } }

public partial class MainWindow : Window
{
    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    PathScene? _scene;

    public MainWindow()
    {
        InitializeComponent();
        Viewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        // "Öffnen mit" bzw. Start mit Dateipfad als Argument
        if (Environment.GetCommandLineArgs() is [_, var file, ..]) Loaded += (_, _) => _ = Load(file);
    }

    void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "G-Code (*.gcode;*.gco;*.g)|*.gcode;*.gco;*.g|Alle Dateien (*.*)|*.*" };
        if (dlg.ShowDialog(this) == true) _ = Load(dlg.FileName);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) _ = Load(files[0]);
    }

    async Task Load(string path)
    {
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        Status.Text = $"Lese {Path.GetFileName(path)} …";
        try
        {
            var progress = new Progress<double>(v => Progress.Value = v);
            var r = await Task.Run(() =>
            {
                using var s = File.OpenRead(path);
                return GcodeAnalysis.Analyze(s, Path.GetFileName(path), progress);
            });
            Show(r);
            Status.Text = path;
        }
        catch (Exception ex)
        {
            Status.Text = "";
            MessageBox.Show(this, $"Die Datei konnte nicht gelesen werden:\n{ex.Message}", "G-Code-Analyzer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { Progress.Visibility = Visibility.Collapsed; }
    }

    void Show(AnalysisResult r)
    {
        Title = $"{r.FileName} – G-Code-Analyzer";
        Preview.Source = ToImage(r.Thumbnail);
        ShowScene(r);

        var hints = new List<Row>();
        if (r.ShortLayers.Count > 0)
            hints.Add(new($"{r.ShortLayers.Count} Layer unter {N(r.MinLayerTime, 0)} s",
                string.Join(", ", r.ShortLayers.Take(8).Select(i => i + 1)) + (r.ShortLayers.Count > 8 ? " …" : "")));

        Sections.ItemsSource = new[]
        {
            new Section("Druckstatistik", [
                new("Geschätzte Druckzeit", r.EstimatedTime is { } t ? Duration(t) : "–"),
                new("Filament", r.FilamentLengthMm is { } mm ? $"{N(mm / 1000, 2)} m" : "–"),
                new("Filamentgewicht", r.FilamentWeightG is { } g ? $"{N(g, 2)} g" : "–"),
                new("Filamentkosten", r.FilamentCost is { } c ? $"{N(c, 2)} €" : "–"),
                new("Layer", N(r.LayerCount, 0)),
                new("Zeilen", N(r.LineCount, 0)),
            ]),
            new Section("Anteile (Filament | Zeit)", r.Shares.OrderByDescending(kv => kv.Value.TimePercent)
                .Select(kv => new Row(PathScene.Styles[kv.Key].Name, $"{N(kv.Value.FilamentPercent, 1)} % | {N(kv.Value.TimePercent, 1)} %"))
                .ToList()),
            new Section("Abmessungen", [
                new("Breite (X)", $"{N(r.Width, 1)} mm"),
                new("Tiefe (Y)", $"{N(r.Depth, 1)} mm"),
                new("Höhe (Z)", $"{N(r.Height, 1)} mm"),
            ]),
            new Section("Erkannte Einstellungen", [
                new("Layerhöhe", r.LayerHeight is { } lh ? $"{N(lh, 2)} mm" : "–"),
                new("Max. Druckgeschwindigkeit", $"{N(r.MaxPrintSpeed, 0)} mm/s"),
                new("Düsentemperatur", r.NozzleTemp is { } nt ? $"{N(nt, 0)} °C" : "–"),
                new("Betttemperatur", r.BedTemp is { } bt ? $"{N(bt, 0)} °C" : "–"),
                new("Drucker", r.PrinterModel ?? "–"),
                new("Filamenttyp", r.FilamentType ?? "–"),
            ]),
            new Section("Datei", [
                new("Name", r.FileName),
                new("Größe", $"{N(r.FileSize / 1024.0 / 1024.0, 1)} MB"),
                new("Slicer", r.SlicerName is null ? "unbekannt" : $"{r.SlicerName} {r.SlicerVersion}"),
            ]),
        }.Prepend(new Section("Hinweise", hints)).Where(sec => sec.Rows.Count > 0).ToList();
    }

    void ShowScene(AnalysisResult r)
    {
        SceneRoot.Children.Clear();
        _scene = new PathScene(r);
        SceneRoot.Children.Add(_scene.Root);
        _scene.Grid.Visibility = GridToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Legend.ItemsSource = _scene.Present
            .Select(c => new LegendItem(c, PathScene.Styles[c].Name, new SolidColorBrush(PathScene.Styles[c].Color)) { IsVisible = _scene.Visible.Contains(c) })
            .ToList();
        bool any = r.LayerCount > 0;
        DropHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        if (!any) DropHint.Text = "Keine Druckpfade in der Datei";
        LegendPanel.Visibility = LayerPanel.Visibility = ChartPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        Chart.SetData(r.Layers, r.MinLayerTime);
        LayerSlider.Maximum = Math.Max(1, r.LayerCount);
        LayerSlider.Value = LayerSlider.Maximum;
        OnLayerChanged(this, null!);
        if (any) FocusCamera(r);
    }

    /// <summary>Kamera schräg von vorne auf die Extrusionen richten.</summary>
    void FocusCamera(AnalysisResult r)
    {
        var pts = r.Layers.SelectMany(l => l.Segments).Where(s => s.Category != PathCategory.Travel).Select(s => s.To).ToList();
        if (pts.Count == 0) return;
        var min = pts.Aggregate(System.Numerics.Vector3.Min);
        var max = pts.Aggregate(System.Numerics.Vector3.Max);
        var c = (min + max) / 2;
        float d = Math.Max(Math.Max(max.X - min.X, max.Y - min.Y), max.Z - min.Z) * 1.6f + 10;
        var cam = (PerspectiveCamera)Viewport.Camera;
        cam.Position = new System.Windows.Media.Media3D.Point3D(c.X, c.Y - d, c.Z + d * 0.8);
        cam.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, d, -d * 0.8);
        cam.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
    }

    void OnLayerChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_scene == null) return;
        int n = (int)LayerSlider.Value;
        _scene.ShowUpTo(n - 1);
        Chart.Current = n - 1;
        LayerText.Text = $"{n} / {(int)LayerSlider.Maximum}";
    }

    void OnChartLayerClicked(int layer) => LayerSlider.Value = layer + 1;

    void OnLayerMinus(object sender, RoutedEventArgs e) => LayerSlider.Value--;
    void OnLayerPlus(object sender, RoutedEventArgs e) => LayerSlider.Value++;
    void OnAllLayers(object sender, RoutedEventArgs e) => LayerSlider.Value = LayerSlider.Maximum;

    void OnLegendClick(object sender, RoutedEventArgs e)
    {
        if (_scene == null || sender is not FrameworkElement { DataContext: LegendItem item }
            || item.IsVisible == _scene.Visible.Contains(item.Category)) return;
        if (item.IsVisible) _scene.Visible.Add(item.Category); else _scene.Visible.Remove(item.Category);
        _scene.Rebuild();
        _scene.ShowUpTo((int)LayerSlider.Value - 1);
    }

    void OnGridClick(object sender, RoutedEventArgs e)
    {
        if (_scene != null) _scene.Grid.Visibility = GridToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    static string N(double v, int decimals) => v.ToString("N" + decimals, De);

    static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{t.Minutes} min {t.Seconds} s";

    static BitmapImage? ToImage(byte[]? png)
    {
        if (png == null) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(png);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception) { return null; }
    }
}
