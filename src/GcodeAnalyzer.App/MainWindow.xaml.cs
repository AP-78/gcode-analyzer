using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using GcodeAnalyzer.Core;
using Microsoft.Win32;

namespace GcodeAnalyzer.App;

public record Row(string Label, string Value);
public record Section(string Title, IReadOnlyList<Row> Rows);

public partial class MainWindow : Window
{
    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public MainWindow()
    {
        InitializeComponent();
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
        var thumb = ToImage(r.Thumbnail);
        Preview.Source = thumb;
        DropHint.Visibility = thumb == null ? Visibility.Visible : Visibility.Collapsed;
        if (thumb == null) DropHint.Text = "Kein Vorschaubild in der Datei";

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
        };
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
