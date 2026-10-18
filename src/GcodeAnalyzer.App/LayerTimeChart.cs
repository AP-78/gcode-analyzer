using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GcodeAnalyzer.Core;

namespace GcodeAnalyzer.App;

/// <summary>Balkendiagramm "Zeit pro Layer" mit Wurzelskala, damit kurze Layer neben sehr langen sichtbar bleiben; Klick wählt den Layer.</summary>
public sealed class LayerTimeChart : FrameworkElement
{
    static readonly Brush Bar = new SolidColorBrush(Color.FromRgb(0x4D, 0x80, 0xBA));
    static readonly Brush Short = new SolidColorBrush(Color.FromRgb(0xF0, 0x40, 0x40));
    static readonly Brush CurrentBar = new SolidColorBrush(Color.FromRgb(0xFF, 0xE1, 0x4D));
    static readonly Brush Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
    static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    static readonly Pen Limit = new(new SolidColorBrush(Color.FromRgb(0xF0, 0x40, 0x40)), 1) { DashStyle = DashStyles.Dash };
    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    IReadOnlyList<Layer> _layers = [];
    double _limit;
    int _current = -1, _hover = -1;

    public event Action<int>? LayerClicked;

    public void SetData(IReadOnlyList<Layer> layers, double minLayerTime)
    {
        _layers = layers;
        _limit = minLayerTime;
        InvalidateVisual();
    }

    public int Current { get => _current; set { _current = value; InvalidateVisual(); } }

    const double Left = 40, Bottom = 4, Top = 18;

    int LayerAt(Point p)
    {
        if (_layers.Count == 0 || p.X < Left) return -1;
        return Math.Clamp((int)((p.X - Left) / (ActualWidth - Left) * _layers.Count), 0, _layers.Count - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e) { _hover = LayerAt(e.GetPosition(this)); InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hover = -1; InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        int i = LayerAt(e.GetPosition(this));
        if (i >= 0) LayerClicked?.Invoke(i);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Background, null, new Rect(RenderSize)); // dunkel in beiden Themes, fängt Mausereignisse auf der ganzen Fläche
        if (_layers.Count == 0 || ActualWidth <= Left) return;
        double max = Math.Max(_layers.Max(l => l.Time), _limit) * 1.05;
        double h = ActualHeight - Bottom - Top, w = (ActualWidth - Left) / _layers.Count;
        double Y(double t) => Top + h - Math.Sqrt(t / max) * h;

        for (int i = 0; i < _layers.Count; i++)
        {
            double t = _layers[i].Time;
            var brush = i == _current ? CurrentBar : t < _limit ? Short : Bar;
            dc.DrawRectangle(brush, null, new Rect(Left + i * w, Y(t), Math.Max(1, w - (w > 3 ? 1 : 0)), Top + h - Y(t)));
        }
        dc.DrawLine(Limit, new Point(Left, Y(_limit)), new Point(ActualWidth, Y(_limit)));
        Label(dc, (max / 1.05).ToString("N0", De) + " s", 2, Top - 2);
        Label(dc, _limit.ToString("N0", De) + " s", 2, Y(_limit) - 8);

        int show = _hover >= 0 ? _hover : _current;
        if (show >= 0)
            Label(dc, $"Layer {show + 1}: {_layers[show].Time.ToString("N1", De)} s   Z {_layers[show].Z.ToString("N2", De)} mm", Left, 0);
    }

    void Label(DrawingContext dc, string s, double x, double y) =>
        dc.DrawText(new FormattedText(s, De, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Text,
            VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
}
