using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GcodeAnalyzer.Core;
using HelixToolkit.Wpf.SharpDX;
using Microsoft.Win32;

namespace GcodeAnalyzer.App;

public record Row(string Label, string Value, Brush? Brush = null);
public record Section(string Title, IReadOnlyList<Row> Rows);
public record LegendItem(PathCategory Category, string Name, Brush Brush) { public bool IsVisible { get; set; } }

public partial class MainWindow : Window
{
    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    PathScene? _scene;
    AnalysisResult? _result;
    readonly System.Collections.ObjectModel.ObservableCollection<Row> _layerRows = [];

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

    void OnAbout(object sender, RoutedEventArgs e)
    {
        var v = typeof(MainWindow).Assembly.GetName().Version;
        MessageBox.Show(this,
            $"G-Code-Analyzer {v?.ToString(3)}\n\n" +
            "Liest G-Code aus OrcaSlicer, Bambu Studio und PrusaSlicer und wertet Wände, Schalen, Infill und Support aus.\n\n" +
            "Icons: Font Awesome Free 7.3.1 von Fonticons, Inc. (fontawesome.com), lizenziert unter CC BY 4.0 " +
            "(creativecommons.org/licenses/by/4.0).\n" +
            "3D-Darstellung: Helix Toolkit (MIT-Lizenz).",
            "Über G-Code-Analyzer", MessageBoxButton.OK, MessageBoxImage.Information);
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
        _result = r;

        var hints = r.Warnings.Select(w => new Row(w, "", new SolidColorBrush(Color.FromRgb(0xE0, 0x80, 0x00)))).ToList();
        if (r.ShortLayers.Count > 0)
            hints.Add(new($"{r.ShortLayers.Count} Layer unter {N(r.MinLayerTime, 0)} s",
                string.Join(", ", r.ShortLayers.Take(8).Select(i => i + 1)) + (r.ShortLayers.Count > 8 ? " …" : "")));

        var walls = r.Walls;
        var under = walls.Layers.Where(w => w.Status == WallStatus.Underrun).ToList();
        var expected = walls.Layers.Where(w => w.Status == WallStatus.ExpectedDeviation).ToList();
        if (under.Count > 0)
            hints.Add(new($"Wand-Unterschreitung in {under.Count} Layern",
                string.Join(", ", under.Take(8).Select(w => w.Layer + 1)) + (under.Count > 8 ? " …" : ""), StatusBrush(WallStatus.Underrun)));
        var minWall = walls.Layers.MinBy(w => w.Loops);
        if (r.OverFlowLayers.Count > 0)
            hints.Add(new($"Volumenstrom > {N(r.VolumetricFlowLimit, 0)} mm³/s in {r.OverFlowLayers.Count} Layern",
                string.Join(", ", r.OverFlowLayers.Take(8).Select(i => i + 1)) + (r.OverFlowLayers.Count > 8 ? " …" : ""),
                new SolidColorBrush(PathScene.UnderrunColor)));
        var sup = r.Support;
        var warn = new SolidColorBrush(Color.FromRgb(0xE0, 0x80, 0x00));
        if (sup.Contradiction)
            hints.Add(new("Support aktiviert, aber", "keine Support-Pfade", warn));
        var shells = r.Shells;
        if (shells.ThinSpots.Count > 0)
            hints.Add(new("Dünne Schalenstellen", $"{N(shells.ThinSpots.Count, 0)} Stellen", new SolidColorBrush(PathScene.ThinShellColor)));
        Row[] ShellRows(string side, ShellSide s) => [
            new($"{side} eingestellt", s.SetThickness > 0 ? $"{s.SetLayers} Layer | {N(s.SetThickness, 2)} mm" : $"{s.SetLayers} Layer"),
            new($"{side} effektiv", $"{s.Effective} Layer"),
            new($"{side} gemessen", s.MeasuredMin is { } mn ? $"min. {mn} | Ø {N(s.MeasuredAvg ?? 0, 1)}" : "–",
                s.MeasuredMin < s.Effective ? new SolidColorBrush(PathScene.ThinShellColor) : null),
        ];

        Sections.ItemsSource = new[]
        {
            new Section("Druckstatistik", [
                new("Geschätzte Druckzeit", r.EstimatedTime is { } t ? Duration(t) : "–"),
                new("Filament", r.FilamentLengthMm is { } mm ? $"{N(mm / 1000, 2)} m" : "–"),
                new("Filamentgewicht", r.FilamentWeightG is { } g ? $"{N(g, 2)} g" : "–"),
                new("Filamentkosten", r.FilamentCost is { } c ? $"{N(c, 2)} €" : "–"),
                new("Layer", N(r.LayerCount, 0)),
                new("Max. Volumenstrom", $"{N(r.MaxVolumetricFlow, 1)} mm³/s (Grenze {N(r.VolumetricFlowLimit, 0)})",
                    r.OverFlowLayers.Count > 0 ? new SolidColorBrush(PathScene.UnderrunColor) : null),
                new("Retracts | Z-Hops", $"{N(r.RetractCount, 0)} | {N(r.ZHopCount, 0)}"),
                new("Fahrweg (Strecke | Zeit)", $"{N(r.TravelDistanceMm / 1000, 1)} m | {N(r.Shares.GetValueOrDefault(PathCategory.Travel)?.TimePercent ?? 0, 1)} %"),
                new("Zeilen", N(r.LineCount, 0)),
            ]),
            new Section("Anteile (Filament | Zeit)", r.Shares.OrderByDescending(kv => kv.Value.TimePercent)
                .Select(kv => new Row(PathScene.Styles[kv.Key].Name, $"{N(kv.Value.FilamentPercent, 1)} % | {N(kv.Value.TimePercent, 1)} %"))
                .ToList()),
            new Section("Wände", [
                new("Soll-Wandstärke", walls.TargetLoops > 0 ? $"{walls.TargetLoops} Schleifen | {N(walls.TargetThickness, 2)} mm" : "–"),
                new("Ist-Wandstärke min.", minWall is null ? "–" : $"{N(minWall.Loops, 1)} Schleifen | {N(minWall.Thickness, 2)} mm",
                    minWall is null ? null : StatusBrush(minWall.Status)),
                new("Layer wie Soll", N(walls.Layers.Count(w => w.Status == WallStatus.Ok), 0)),
                new("Erwartete Abweichung", N(expected.Count, 0), StatusBrush(WallStatus.ExpectedDeviation)),
                new("Wand-Unterschreitung", N(under.Count, 0), under.Count > 0 ? StatusBrush(WallStatus.Underrun) : null),
            ]),
            new Section("Schalen", [.. ShellRows("Oben", shells.Top), .. ShellRows("Unten", shells.Bottom),
                new("Dünne Schalenstellen", N(shells.ThinSpots.Count, 0), shells.ThinSpots.Count > 0 ? new SolidColorBrush(PathScene.ThinShellColor) : null),
            ]),
            new Section("Support", [
                new("Aktiviert", sup.Enabled ? "ja" : "nein"),
                new("Typ", sup.Type ?? "–"),
                new("Nur auf Druckbett", sup.BuildPlateOnly is { } bp ? (bp ? "ja" : "nein") : "–"),
                new("Support-Pfade", sup.HasPaths ? "vorhanden" : "keine", sup.Contradiction ? warn : null),
                new("Anteil (Filament | Zeit)", sup.HasPaths ? $"{N(sup.FilamentPercent, 1)} % | {N(sup.TimePercent, 1)} %" : "–"),
            ]),
            new Section("Infill", [
                new("Muster", r.InfillPattern is { Length: > 0 } pat ? char.ToUpper(pat[0]) + pat[1..] : "–"),
                new("Dichte", r.InfillDensity is { } dens ? $"{N(dens, 0)} %" : "–"),
                new("Linienbreite", r.InfillLineWidth is { } lw ? $"{N(lw, 2)} mm" : "–"),
                new("Sparse-Infill (Filament | Zeit)", r.Shares.TryGetValue(PathCategory.SparseInfill, out var sp)
                    ? $"{N(sp.FilamentPercent, 1)} % | {N(sp.TimePercent, 1)} %" : "–"),
                new("Layer mit massiver Füllung", N(r.Layers.Count(l => l.SolidFillMm > 0), 0)),
                new("Layer nur mit Sparse-Infill", N(r.Layers.Count(l => l.SolidFillMm == 0 && l.SparseFillMm > 0), 0)),
            ]),
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
        }.Prepend(new Section("Aktueller Layer", _layerRows))
         .Prepend(new Section("Hinweise", hints)).Where(sec => sec.Rows.Count > 0 || sec.Rows == _layerRows).ToList();
        ShowScene(r);
    }

    void UpdateLayerRows(int i)
    {
        _layerRows.Clear();
        if (_result is null || i < 0 || i >= _result.LayerCount) return;
        var l = _result.Layers[i];
        double fill = l.SolidFillMm + l.SparseFillMm;
        _layerRows.Add(new("Z | Höhe", $"{N(l.Z, 2)} mm | {N(l.Height, 2)} mm"));
        _layerRows.Add(new("Zeit", $"{N(l.Time, 1)} s"));
        if (_result.Walls.Layers.FirstOrDefault(w => w.Layer == i) is { } lw)
            _layerRows.Add(new("Wandschleifen | Ist-Wandstärke", $"{N(lw.Loops, 1)} | {N(lw.Thickness, 2)} mm", StatusBrush(lw.Status)));
        _layerRows.Add(new("Füllung massiv | Sparse", fill > 0 ? $"{N(l.SolidFillMm / fill * 100, 0)} % | {N(l.SparseFillMm / fill * 100, 0)} %" : "–"));
    }

    void ShowScene(AnalysisResult r)
    {
        SceneRoot.Children.Clear();
        _scene = new PathScene(r) { Mode = (ColorMode)Math.Max(0, ColorModeBox.SelectedIndex) };
        SceneRoot.Children.Add(_scene.Root);
        _scene.Grid.Visibility = GridToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        OnThinShellsClick(this, null!);
        UpdateHeatLegend();
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
        if (Viewport.Camera is not PerspectiveCamera cam) return;
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
        UpdateLayerRows(n - 1);
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
        _scene.ShowUpTo((int)LayerSlider.Value - 1);
    }

    void OnMarkWallsClick(object sender, RoutedEventArgs e)
    {
        if (_scene == null) return;
        _scene.MarkWalls = MarkWallsToggle.IsChecked == true;
        _scene.Recolor();
    }

    static Brush? StatusBrush(WallStatus s) => s switch
    {
        WallStatus.Underrun => new SolidColorBrush(PathScene.UnderrunColor),
        WallStatus.ExpectedDeviation => Brushes.Gray,
        _ => null,
    };

    void OnColorModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_scene == null) return;
        _scene.Mode = (ColorMode)ColorModeBox.SelectedIndex;
        _scene.Recolor();
        UpdateHeatLegend();
    }

    void UpdateHeatLegend()
    {
        if (_scene == null) return;
        bool heat = _scene.Mode != ColorMode.Category;
        HeatLegend.Visibility = heat ? Visibility.Visible : Visibility.Collapsed;
        if (!heat) return;
        var stops = new GradientStopCollection(PathScene.HeatStops.Select((c, k) => new GradientStop(c, k / (double)(PathScene.HeatStops.Length - 1))));
        HeatBar.Fill = new LinearGradientBrush(stops, 0);
        var (min, max, unit) = _scene.Range;
        HeatMin.Text = $"{N(min, 0)} {unit}";
        HeatMax.Text = $"{N(max, 0)} {unit}";
    }

    void OnThinShellsClick(object sender, RoutedEventArgs e)
    {
        if (_scene != null) _scene.ThinShells.Visibility = ThinShellsToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
