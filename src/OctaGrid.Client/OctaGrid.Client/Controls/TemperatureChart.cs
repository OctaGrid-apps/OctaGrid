using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using OctaGrid.Client.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Controls;

/// <summary>
/// Line chart of a <see cref="TemperatureHistory" />: one solid line per reading key (hotend, bed, and whatever else the
/// printer reports), its target dashed in the same colour, minus the keys in <see cref="HiddenKeys" />. The time axis ends
/// at the latest reading and grows with the history up to <see cref="TemperatureHistory.Window" />, after which the lines
/// scroll left as new readings arrive. Pointing at the chart shows every visible value at that moment. Series colours come
/// from the theme (<see cref="ChartSeriesBrushes" />), the rest from properties.
/// </summary>
public partial class TemperatureChart : Control, ICustomHitTest
{
    private const double LeftMargin = 40;
    private const double BottomMargin = 20;
    private const double TopMargin = 8;
    private const double RightMargin = 8;
    private const double LabelSize = 11;
    private const double MinimumCeiling = 100;
    private const double ReadOutPadding = 6;
    private const double ReadOutOffset = 10;
    private const double MarkerRadius = 3;
    private static readonly TimeSpan MinimumSpan = TimeSpan.FromMinutes(5);

    // A tick lands this long after a whole second, so the rounded edge has already moved on when it redraws.
    private static readonly TimeSpan ClockLag = TimeSpan.FromMilliseconds(20);

    // Moves the "now" edge of the time axis while no new readings arrive; runs only while the chart is on screen.
    private readonly DispatcherTimer _clock;
    private bool _isAttached;
    private Point? _pointer;
    private int _redrawQueued;

    public TemperatureChart()
    {
        _clock = new DispatcherTimer();
        _clock.Tick += (_, _) =>
        {
            InvalidateVisual();
            _clock.Interval = UntilNextSecond(Now);
        };
    }

    // The history's clock, so tests that drive it also drive the chart.
    private DateTimeOffset Now => Source?.Now ?? DateTimeOffset.UtcNow;

    static TemperatureChart()
    {
        AffectsRender<TemperatureChart>(GridBrushProperty, HiddenKeysProperty, ReadOutBackgroundProperty, TextBrushProperty);
    }

    [GeneratedStyledProperty]
    public partial IBrush? GridBrush { get; set; }

    /// <summary>Reading keys that are not drawn. They still count for nothing else, so hiding one rescales the chart.</summary>
    [GeneratedStyledProperty]
    public partial IReadOnlyCollection<string>? HiddenKeys { get; set; }

    /// <summary>The background of the box that shows the values under the pointer.</summary>
    [GeneratedStyledProperty]
    public partial IBrush? ReadOutBackground { get; set; }

    [GeneratedStyledProperty]
    public partial TemperatureHistory? Source { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? TextBrush { get; set; }

    /// <summary>The whole control answers the pointer, not only where a line is drawn.</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    /// <summary>The keys in the samples: the known ones in their usual order, then the others as they first appear.</summary>
    internal static IReadOnlyList<string> SeriesKeys(IReadOnlyList<TelemetrySample> samples)
    {
        var seen = samples.SelectMany(s => s.Readings).Select(r => r.Key).Distinct().ToList();
        return ReadingLabels.KnownOrder.Where(seen.Contains).Concat(seen.Except(ReadingLabels.KnownOrder)).ToList();
    }

    /// <summary>The top of the value axis: a multiple of the grid step above every visible value and target, never below 100.</summary>
    internal static double CeilingFor(IReadOnlyList<TelemetrySample> samples, IReadOnlyCollection<string>? hidden)
    {
        var highest = 0.0;
        foreach (var reading in samples.SelectMany(s => s.Readings))
        {
            if (hidden?.Contains(reading.Key) != true)
            {
                highest = Math.Max(highest, Math.Max(reading.Value, reading.Target ?? 0));
            }
        }

        var step = StepFor(highest);
        return Math.Max(MinimumCeiling, Math.Ceiling(highest * 1.05 / step) * step);
    }

    /// <summary>
    /// The time at the right edge of the chart: the current time rounded up to a whole second, or the newest sample's
    /// rounded the same way when that is later (an agent whose clock runs ahead). Rounded, so the chart moves once per
    /// second however its redraws (the clock, each new sample) fall within that second; a sample arriving in between
    /// only adds its point.
    /// </summary>
    internal static DateTimeOffset AxisEnd(DateTimeOffset now, IReadOnlyList<TelemetrySample> samples)
    {
        var end = UpToSecond(now);
        return samples.Count > 0 && UpToSecond(samples[^1].Time) > end ? UpToSecond(samples[^1].Time) : end;
    }

    /// <summary>
    /// The readings with the latest one repeated at <paramref name="end"/>. A printer may report only when something
    /// changes, so a reading is the temperature until the next one arrives, and an idle printer shows a flat line to the
    /// right edge, not an empty chart.
    /// </summary>
    internal static IReadOnlyList<TelemetrySample> WithLatestHeldUntil(IReadOnlyList<TelemetrySample> samples, DateTimeOffset end)
    {
        if (samples.Count == 0 || end <= samples[^1].Time)
        {
            return samples;
        }

        return [.. samples, samples[^1] with { Time = end }];
    }

    /// <summary>How long until just after the next whole second, when the right edge moves.</summary>
    internal static TimeSpan UntilNextSecond(DateTimeOffset now) =>
        TimeSpan.FromTicks(TimeSpan.TicksPerSecond - now.UtcTicks % TimeSpan.TicksPerSecond) + ClockLag;

    private static DateTimeOffset UpToSecond(DateTimeOffset time)
    {
        var ticks = (time.UtcTicks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>
    /// How much time the horizontal axis covers: what the readings span, so a fresh history fills the chart instead of
    /// hugging its right edge, but never less than <see cref="MinimumSpan" /> and never more than the history keeps.
    /// </summary>
    internal static TimeSpan SpanFor(IReadOnlyList<TelemetrySample> samples)
    {
        var span = samples.Count > 0 ? samples[^1].Time - samples[0].Time : TimeSpan.Zero;
        return span < MinimumSpan ? MinimumSpan : span > TemperatureHistory.Window ? TemperatureHistory.Window : span;
    }

    /// <summary>The axis label for a span: whole seconds under two minutes, otherwise whole minutes.</summary>
    internal static string FormatSpan(TimeSpan span) =>
        span < TimeSpan.FromMinutes(2) ? $"{Math.Round(span.TotalSeconds):0} s" : $"{Math.Round(span.TotalMinutes):0} min";

    /// <summary>The distance between two grid lines.</summary>
    internal static double StepFor(double highest) => highest > 150 ? 50 : 25;

    /// <summary>The index of the sample closest in time; the samples are in time order and there is at least one.</summary>
    internal static int NearestIndex(IReadOnlyList<TelemetrySample> samples, DateTimeOffset time)
    {
        int low = 0, high = samples.Count - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (samples[middle].Time < time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low > 0 && time - samples[low - 1].Time < samples[low].Time - time ? low - 1 : low;
    }

    /// <summary>What the read-out says for a sample: its time, then each visible series with its target when one is set.</summary>
    internal static IReadOnlyList<string> ReadOutLines(
        TelemetrySample sample, IReadOnlyList<string> keys, IReadOnlyCollection<string>? hidden)
    {
        var lines = new List<string> { sample.Time.ToLocalTime().ToString("T", CultureInfo.CurrentCulture) };
        foreach (var key in keys.Where(k => hidden?.Contains(k) != true))
        {
            if (sample.Readings.FirstOrDefault(r => r.Key == key) is not { } reading)
            {
                continue;
            }

            var target = reading.Target is > 0 ? $" → {reading.Target:0}°" : string.Empty;
            lines.Add($"{ReadingLabels.For(key)}: {reading.Value:0.0}°{target}");
        }

        return lines;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var plot = new Rect(LeftMargin, TopMargin, Bounds.Width - LeftMargin - RightMargin, Bounds.Height - TopMargin - BottomMargin);
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        var recorded = Source?.Snapshot() ?? [];
        var end = AxisEnd(Now, recorded);
        var samples = WithLatestHeldUntil(recorded, end);
        var hidden = HiddenKeys;
        var keys = SeriesKeys(samples);
        var visible = keys.Where(k => hidden?.Contains(k) != true).ToList();
        var ceiling = CeilingFor(samples, hidden);
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

        double X(DateTimeOffset time) => plot.Right - (end - time) / window * plot.Width;
        double Y(double value) => plot.Bottom - Math.Min(value, ceiling) / ceiling * plot.Height;

        using (context.PushClip(plot))
        {
            // Targets first, so the measured lines are drawn over them.
            foreach (var key in visible)
            {
                DrawSeries(context, samples, X, Y, s => Reading(s, key)?.Target, ChartSeriesBrushes.For(key), dashed: true);
            }

            foreach (var key in visible)
            {
                DrawSeries(context, samples, X, Y, s => Reading(s, key)?.Value, ChartSeriesBrushes.For(key), dashed: false);
            }
        }

        if (_pointer is { } pointer && pointer.X >= plot.Left && pointer.X <= plot.Right && visible.Count > 0)
        {
            var time = end - (plot.Right - pointer.X) / plot.Width * window;
            var sample = samples[NearestIndex(samples, time)];
            DrawReadOut(context, plot, sample, X(sample.Time), Y, keys, hidden);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointer = null;
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Source?.Changed += OnSourceChanged;
        _clock.Interval = UntilNextSecond(Now);
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

    private static TelemetryReading? Reading(TelemetrySample sample, string key) =>
        sample.Readings.FirstOrDefault(r => r.Key == key);

    // A sample without the value leaves a gap: the line stops and starts again at the next sample that has it.
    private static void DrawSeries(
        DrawingContext context,
        IReadOnlyList<TelemetrySample> samples,
        Func<DateTimeOffset, double> x,
        Func<double, double> y,
        Func<TelemetrySample, double?> value,
        IBrush? brush,
        bool dashed)
    {
        var pen = new Pen(brush, dashed ? 1 : 2, dashed ? new DashStyle([4, 4], 0) : null);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var inFigure = false;
            foreach (var sample in samples)
            {
                if (value(sample) is not { } v)
                {
                    if (inFigure)
                    {
                        ctx.EndFigure(false);
                        inFigure = false;
                    }

                    continue;
                }

                var point = new Point(x(sample.Time), y(v));
                if (inFigure)
                {
                    ctx.LineTo(point);
                }
                else
                {
                    ctx.BeginFigure(point, false);
                    inFigure = true;
                }
            }

            if (inFigure)
            {
                ctx.EndFigure(false);
            }
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawReadOut(
        DrawingContext context,
        Rect plot,
        TelemetrySample sample,
        double x,
        Func<double, double> y,
        IReadOnlyList<string> keys,
        IReadOnlyCollection<string>? hidden)
    {
        context.DrawLine(new Pen(TextBrush, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
        foreach (var reading in sample.Readings.Where(r => hidden?.Contains(r.Key) != true))
        {
            context.DrawEllipse(ChartSeriesBrushes.For(reading.Key), null, new Point(x, y(reading.Value)), MarkerRadius, MarkerRadius);
        }

        var lines = ReadOutLines(sample, keys, hidden).Select(CreateText).ToList();
        var width = lines.Max(l => l.Width) + 2 * ReadOutPadding;
        var height = lines.Sum(l => l.Height) + 2 * ReadOutPadding;

        // Beside the line, on whichever side has room.
        var left = x + ReadOutOffset + width <= plot.Right ? x + ReadOutOffset : x - ReadOutOffset - width;
        var box = new Rect(Math.Max(plot.Left, left), plot.Top + ReadOutPadding, width, height);
        context.DrawRectangle(ReadOutBackground, new Pen(GridBrush, 1), box, ReadOutPadding / 2, ReadOutPadding / 2);

        var top = box.Top + ReadOutPadding;
        foreach (var line in lines)
        {
            context.DrawText(line, new Point(box.Left + ReadOutPadding, top));
            top += line.Height;
        }
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

    // Readings can arrive on any thread; redraw once per burst on the UI thread. The flag is shared between those threads
    // and the UI thread, so it is only ever changed atomically.
    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _redrawQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _redrawQueued, 0);
            InvalidateVisual();
        });
    }
}
