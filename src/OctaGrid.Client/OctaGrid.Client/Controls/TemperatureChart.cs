using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using OctaGrid.Client.Models;

namespace OctaGrid.Client.Controls;

/// <summary>
/// Line chart of a <see cref="TemperatureHistory" />: hotend and bed as solid lines, their targets dashed. The
/// time axis ends at the latest reading and grows with the history up to <see cref="TemperatureHistory.Window" />,
/// after which the lines scroll left as new readings arrive. All colours come from properties so the theme decides them.
/// </summary>
public class TemperatureChart : Control
{
    public static readonly StyledProperty<IBrush?> BedBrushProperty =
        AvaloniaProperty.Register<TemperatureChart, IBrush?>(nameof(BedBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<TemperatureChart, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> HotendBrushProperty =
        AvaloniaProperty.Register<TemperatureChart, IBrush?>(nameof(HotendBrush));

    public static readonly StyledProperty<TemperatureHistory?> SourceProperty =
        AvaloniaProperty.Register<TemperatureChart, TemperatureHistory?>(nameof(Source));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<TemperatureChart, IBrush?>(nameof(TextBrush));

    private const double LeftMargin = 40;
    private const double BottomMargin = 20;
    private const double TopMargin = 8;
    private const double RightMargin = 8;
    private const double LabelSize = 11;
    private const double MinimumCeiling = 100;
    private static readonly TimeSpan MinimumSpan = TimeSpan.FromSeconds(30);

    // Moves the "now" edge of the time axis while no new readings arrive; runs only while the chart is on screen.
    private readonly DispatcherTimer _clock;
    private bool _isAttached;
    private bool _redrawQueued;

    public TemperatureChart()
    {
        _clock = new DispatcherTimer { Interval = TemperatureHistory.MinInterval };
        _clock.Tick += (_, _) => InvalidateVisual();
    }

    static TemperatureChart()
    {
        AffectsRender<TemperatureChart>(BedBrushProperty, GridBrushProperty, HotendBrushProperty, TextBrushProperty);
    }

    public IBrush? BedBrush
    {
        get => GetValue(BedBrushProperty);
        set => SetValue(BedBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? HotendBrush
    {
        get => GetValue(HotendBrushProperty);
        set => SetValue(HotendBrushProperty, value);
    }

    public TemperatureHistory? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>The top of the value axis: a multiple of the grid step above every reading, never below 100.</summary>
    internal static double CeilingFor(IReadOnlyList<TemperatureSample> samples)
    {
        var highest = 0.0;
        foreach (var s in samples)
        {
            highest = Math.Max(highest, Math.Max(Math.Max(s.Nozzle, s.NozzleTarget), Math.Max(s.Bed, s.BedTarget)));
        }

        var step = StepFor(highest);
        return Math.Max(MinimumCeiling, Math.Ceiling(highest * 1.05 / step) * step);
    }

    /// <summary>
    /// The readings with the latest one repeated at the current time. A printer may report only when something changes,
    /// so a reading is the temperature until the next one arrives, and an idle printer shows a flat line, not an empty chart.
    /// </summary>
    internal static IReadOnlyList<TemperatureSample> WithLatestHeldUntilNow(TemperatureHistory? source)
    {
        if (source is null)
        {
            return [];
        }

        var samples = source.Snapshot();
        if (samples.Count == 0 || source.Now <= samples[^1].Time)
        {
            return samples;
        }

        return [.. samples, samples[^1] with { Time = source.Now }];
    }

    /// <summary>
    /// How much time the horizontal axis covers: what the readings span, so a fresh history fills the chart instead of
    /// hugging its right edge, but never less than <see cref="MinimumSpan" /> and never more than the history keeps.
    /// </summary>
    internal static TimeSpan SpanFor(IReadOnlyList<TemperatureSample> samples)
    {
        var span = samples.Count > 0 ? samples[^1].Time - samples[0].Time : TimeSpan.Zero;
        return span < MinimumSpan ? MinimumSpan : span > TemperatureHistory.Window ? TemperatureHistory.Window : span;
    }

    /// <summary>The axis label for a span: whole seconds under two minutes, otherwise whole minutes.</summary>
    internal static string FormatSpan(TimeSpan span) =>
        span < TimeSpan.FromMinutes(2) ? $"{Math.Round(span.TotalSeconds):0} s" : $"{Math.Round(span.TotalMinutes):0} min";

    /// <summary>The distance between two grid lines.</summary>
    internal static double StepFor(double highest) => highest > 150 ? 50 : 25;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var plot = new Rect(LeftMargin, TopMargin, Bounds.Width - LeftMargin - RightMargin, Bounds.Height - TopMargin - BottomMargin);
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        var samples = WithLatestHeldUntilNow(Source);
        var ceiling = CeilingFor(samples);
        var gridPen = new Pen(GridBrush, 1);

        for (var value = 0.0; value <= ceiling; value += StepFor(ceiling))
        {
            var y = plot.Bottom - value / ceiling * plot.Height;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawLabel(context, $"{value:0}°", new Point(plot.Left - 6, y), rightAligned: true);
        }

        var window = SpanFor(samples);
        DrawLabel(context, $"-{FormatSpan(window)}", new Point(plot.Left, plot.Bottom + 4), rightAligned: false);
        DrawLabel(context, "now", new Point(plot.Right, plot.Bottom + 4), rightAligned: true);

        if (samples.Count < 2)
        {
            DrawCentered(context, "Collecting temperatures…", plot);
            return;
        }

        var end = samples[^1].Time;
        double X(DateTimeOffset time) => plot.Right - (end - time) / window * plot.Width;
        double Y(double value) => plot.Bottom - Math.Min(value, ceiling) / ceiling * plot.Height;

        using (context.PushClip(plot))
        {
            DrawSeries(context, samples, X, Y, s => s.NozzleTarget, HotendBrush, dashed: true);
            DrawSeries(context, samples, X, Y, s => s.BedTarget, BedBrush, dashed: true);
            DrawSeries(context, samples, X, Y, s => s.Nozzle, HotendBrush, dashed: false);
            DrawSeries(context, samples, X, Y, s => s.Bed, BedBrush, dashed: false);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Source?.Changed += OnSourceChanged;
        _clock.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // The history outlives the page, so the chart must not stay subscribed once it is gone.
        _isAttached = false;
        _clock.Stop();
        Source?.Changed -= OnSourceChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
        {
            if (_isAttached)
            {
                (change.OldValue as TemperatureHistory)?.Changed -= OnSourceChanged;
                (change.NewValue as TemperatureHistory)?.Changed += OnSourceChanged;
            }

            InvalidateVisual();
        }
    }

    private static void DrawSeries(
        DrawingContext context,
        IReadOnlyList<TemperatureSample> samples,
        Func<DateTimeOffset, double> x,
        Func<double, double> y,
        Func<TemperatureSample, double> value,
        IBrush? brush,
        bool dashed)
    {
        var pen = new Pen(brush, dashed ? 1 : 2, dashed ? new DashStyle([4, 4], 0) : null);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x(samples[0].Time), y(value(samples[0]))), false);
            for (var i = 1; i < samples.Count; i++)
            {
                ctx.LineTo(new Point(x(samples[i].Time), y(value(samples[i]))));
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawCentered(DrawingContext context, string text, Rect area)
    {
        var formatted = CreateText(text);
        context.DrawText(formatted, new Point(area.Center.X - formatted.Width / 2, area.Center.Y - formatted.Height / 2));
    }

    private void DrawLabel(DrawingContext context, string text, Point anchor, bool rightAligned)
    {
        var formatted = CreateText(text);
        var x = rightAligned ? anchor.X - formatted.Width : anchor.X;
        var isValueLabel = rightAligned && anchor.X < LeftMargin;
        context.DrawText(formatted, new Point(x, isValueLabel ? anchor.Y - formatted.Height / 2 : anchor.Y));
    }

    private FormattedText CreateText(string text) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this)),
        LabelSize,
        TextBrush);

    // Readings can arrive on any thread; redraw once per burst on the UI thread.
    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (_redrawQueued)
        {
            return;
        }

        _redrawQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _redrawQueued = false;
            InvalidateVisual();
        });
    }
}
