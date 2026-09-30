using System.Globalization;
using ClientAgent.Shared.Models.Reports;

namespace ClientAgent.Service.Reports;

/// <summary>The report period in local time, with its UTC bounds and the period of the same length just before it.</summary>
internal sealed record Period(DateTime From, DateTime To)
{
    public DateTime FromUtc { get; } = From.ToUniversalTime();

    public DateTime ToUtc { get; } = To.ToUniversalTime();

    public TimeSpan Length => To - From;

    public bool IsShort => Length.TotalDays <= 2;

    public Period Previous => new(From - Length, From);

    /// <summary>Every local date the period touches (up to today).</summary>
    public List<DateOnly> Days
    {
        get
        {
            var days = new List<DateOnly>();
            var last = DateOnly.FromDateTime((To > DateTime.Now ? DateTime.Now : To).AddTicks(-1));
            for (var day = DateOnly.FromDateTime(From); day <= last; day = day.AddDays(1))
            {
                days.Add(day);
            }

            return days;
        }
    }

    /// <summary>How much of [start, end] falls inside the period (and not in the future).</summary>
    public TimeSpan Clip(DateTime startUtc, DateTime endUtc)
    {
        var start = startUtc < FromUtc ? FromUtc : startUtc;
        var end = new[] { endUtc, ToUtc, DateTime.UtcNow }.Min();
        return end > start ? end - start : TimeSpan.Zero;
    }
}

internal static class Fmt
{
    public static readonly TimeSpan Interval = MetricsRecorder.SampleInterval;

    public static string Local(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Label(DateTime utc, Period period) =>
        utc.ToLocalTime().ToString(period.Length.TotalDays <= 1 ? "HH:mm" : "MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Day(DateOnly day) => day.ToString("ddd MM-dd", CultureInfo.InvariantCulture);

    public static string Percent(double value) => $"{value:0.#}%";

    public static string Percent(double? value) => value is { } v ? Percent(v) : "-";

    public static string Ms(double? value) => value is { } v ? $"{v:0} ms" : "-";

    public static string Mbps(double? value) => value is { } v ? $"{v:0.##} Mbps" : "-";

    public static string Size(double megabytes) => megabytes >= 1024 ? $"{megabytes / 1024:0.##} GB" : $"{megabytes:0.#} MB";

    public static string Minutes(int count) => Duration(TimeSpan.FromTicks(Interval.Ticks * count));

    public static string Duration(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
        {
            return "0";
        }

        return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
            : $"{span.Seconds}s";
    }

    public static double? Average(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count == 0 ? null : list.Average();
    }

    public static double? Max(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count == 0 ? null : list.Max();
    }

    /// <summary>"Good", "Warning" or "Bad" for a value against a warning and a problem level (0 = not set).</summary>
    public static string? Level(double? value, double warning, double problem, bool higherIsWorse = true)
    {
        if (value is not { } v)
        {
            return null;
        }

        if (higherIsWorse)
        {
            return problem > 0 && v >= problem ? "Bad" : warning > 0 && v >= warning ? "Warning" : "Good";
        }

        return problem > 0 && v < problem ? "Bad" : warning > 0 && v < warning ? "Warning" : "Good";
    }

    public static string UptimeStatus(double percent) => percent >= 99 ? "Good" : percent >= 95 ? "Warning" : "Bad";

    /// <summary>A metric with its change from the previous period, e.g. "▲ 3.2% vs previous period".</summary>
    public static ReportMetric Compared(string label, double? current, double? previous, Func<double, string> format,
        string? status, bool higherIsBetter)
    {
        if (current is not { } now)
        {
            return new ReportMetric(label, "-", status);
        }

        if (previous is not { } before)
        {
            return new ReportMetric(label, format(now), status, "No data for the previous period");
        }

        var difference = now - before;
        if (Math.Abs(difference) < 0.05)
        {
            return new ReportMetric(label, format(now), status, "Same as the previous period");
        }

        var better = difference > 0 == higherIsBetter;
        return new ReportMetric(label, format(now), status,
            $"{(difference > 0 ? "▲" : "▼")} {format(Math.Abs(difference))} vs previous period",
            better ? "Good" : "Bad");
    }

    public static string Coverage(int samples, Period period)
    {
        var expected = Math.Max(1, (Math.Min(period.ToUtc.Ticks, DateTime.UtcNow.Ticks) - period.FromUtc.Ticks) / (double)Interval.Ticks);
        var percent = Math.Clamp(samples / expected * 100, 0, 100);
        return samples == 0
            ? "No readings were saved in this period (readings are saved every minute while the service runs)."
            : $"Based on {samples:N0} readings, one per minute, covering {percent:0}% of the period (the rest the service was not running).";
    }

    /// <summary>Continuous stretches where the condition held (a gap of more than three minutes ends a stretch).</summary>
    public static List<(DateTime Start, DateTime End, double Peak)> Stretches(IEnumerable<(DateTime Utc, double Value)> readings, Func<double, bool> condition)
    {
        var stretches = new List<(DateTime Start, DateTime End, double Peak)>();
        DateTime? start = null;
        var previous = DateTime.MinValue;
        var peak = 0d;
        foreach (var (utc, value) in readings)
        {
            if (start is not null && (!condition(value) || utc - previous > Interval * 3))
            {
                stretches.Add((start.Value, previous + Interval, peak));
                start = null;
            }

            if (condition(value))
            {
                if (start is null)
                {
                    start = utc;
                    peak = value;
                }

                peak = Math.Max(peak, value);
            }

            previous = utc;
        }

        if (start is not null)
        {
            stretches.Add((start.Value, previous + Interval, peak));
        }

        return stretches;
    }
}

/// <summary>Builds the charts of the reports.</summary>
internal static class Charts
{
    private const int MaxPoints = 150;
    private static readonly string[] Hours = Enumerable.Range(0, 24).Select(h => $"{h:00}").ToArray();

    public static List<ReportThreshold> Levels(double warning, double problem, string unit = "%")
    {
        var levels = new List<ReportThreshold>();
        if (warning > 0)
        {
            levels.Add(new ReportThreshold($"Warning {warning:0.#}{unit}", warning, "Warning"));
        }

        if (problem > 0)
        {
            levels.Add(new ReportThreshold($"Problem {problem:0.#}{unit}", problem, "Bad"));
        }

        return levels;
    }

    /// <summary>Lines over time, averaged into at most 150 intervals.</summary>
    public static ReportChart? Line(string title, Period period, string unit, double maximum, IEnumerable<ReportThreshold>? thresholds,
        params (string Name, IEnumerable<(DateTime Utc, double Value)> Values)[] series)
    {
        var (buckets, width) = Buckets(period);
        var grouped = series
            .Select(s => s.Values.GroupBy(v => Index(v.Utc, period, width, buckets)).ToDictionary(g => g.Key, g => g.Average(v => v.Value)))
            .ToList();
        var keys = grouped.SelectMany(g => g.Keys).Distinct().Order().ToList();
        if (keys.Count < 2)
        {
            return null;
        }

        return new ReportChart
        {
            Kind = ReportChartKinds.Line,
            Title = title,
            Unit = unit,
            Maximum = maximum,
            Labels = keys.Select(k => Fmt.Label(period.FromUtc + width * k, period)).ToList(),
            Series = series.Select((s, i) => (s.Name, Values: grouped[i]))
                .Where(s => s.Values.Count > 0)
                .Select(s =>
                {
                    var last = s.Values.MinBy(pair => pair.Key).Value;
                    return new ReportSeries(s.Name, keys.Select(k => last = s.Values.TryGetValue(k, out var value) ? Math.Round(value, 2) : last).ToList());
                })
                .ToList(),
            Thresholds = thresholds?.ToList() ?? []
        };
    }

    public static ReportChart? Bar(string title, string unit, IReadOnlyList<string> labels, double maximum, bool half,
        IEnumerable<ReportThreshold>? thresholds, params (string Name, IReadOnlyList<double> Values)[] series)
    {
        if (labels.Count == 0 || series.All(s => s.Values.All(v => v <= 0)))
        {
            return null;
        }

        return new ReportChart
        {
            Kind = ReportChartKinds.Bar,
            Title = title,
            Half = half,
            Unit = unit,
            Maximum = maximum,
            Labels = labels.ToList(),
            Series = series.Select(s => new ReportSeries(s.Name, s.Values.Select(v => Math.Round(v, 2)).ToList())).ToList(),
            Thresholds = thresholds?.ToList() ?? []
        };
    }

    /// <summary>Slices bigger than zero; beyond seven, the smallest are joined as "Other".</summary>
    public static ReportChart? Pie(string title, IEnumerable<(string Label, double Value)> slices, string unit = "", bool half = true)
    {
        var list = slices.Where(s => s.Value > 0).OrderByDescending(s => s.Value).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        if (list.Count > 8)
        {
            list = list.Take(7).Append(("Other", list.Skip(7).Sum(s => s.Value))).ToList();
        }

        return new ReportChart
        {
            Kind = ReportChartKinds.Pie,
            Title = title,
            Half = half,
            Unit = unit,
            Labels = list.Select(s => s.Label).ToList(),
            Series = [new ReportSeries(title, list.Select(s => Math.Round(s.Value, 2)).ToList())]
        };
    }

    /// <summary>Status bands: in each interval the worst reading wins (0 down, 1 warning, 2 working; -1 no reading).</summary>
    public static ReportChart? Timeline(string title, Period period, IEnumerable<(string Name, IEnumerable<(DateTime Utc, int State)> Readings)> rows)
    {
        var (buckets, width) = Buckets(period);
        var series = new List<ReportSeries>();
        foreach (var (name, readings) in rows)
        {
            var values = Enumerable.Repeat(-1d, buckets).ToArray();
            foreach (var (utc, state) in readings)
            {
                if (state < 0)
                {
                    continue;
                }

                var index = Index(utc, period, width, buckets);
                values[index] = values[index] < 0 ? state : Math.Min(values[index], state);
            }

            if (values.Any(v => v >= 0))
            {
                series.Add(new ReportSeries(name, values.ToList()));
            }
        }

        if (series.Count == 0)
        {
            return null;
        }

        return new ReportChart
        {
            Kind = ReportChartKinds.Timeline,
            Title = title,
            Labels = Enumerable.Range(0, buckets).Select(i => Fmt.Label(period.FromUtc + width * i, period)).ToList(),
            Series = series
        };
    }

    /// <summary>Average of each local hour of the day (00 to 23).</summary>
    public static ReportChart? ByHour(string title, string unit, double maximum, bool half, IEnumerable<ReportThreshold>? thresholds,
        params (string Name, IEnumerable<(DateTime Utc, double Value)> Values, Func<IEnumerable<double>, double> Combine)[] series)
    {
        var built = series.Select(s =>
        {
            var byHour = s.Values.GroupBy(v => v.Utc.ToLocalTime().Hour).ToDictionary(g => g.Key, g => s.Combine(g.Select(v => v.Value)));
            return (s.Name, (IReadOnlyList<double>)Enumerable.Range(0, 24).Select(h => byHour.TryGetValue(h, out var v) ? v : 0).ToList());
        }).ToArray();
        return Bar(title, unit, Hours, maximum, half, thresholds, built);
    }

    /// <summary>One bar per local day of the period; null for periods of one day or less.</summary>
    public static ReportChart? ByDay(string title, string unit, double maximum, Period period, bool half,
        params (string Name, IEnumerable<(DateTime Utc, double Value)> Values, Func<IEnumerable<double>, double> Combine)[] series)
    {
        var days = period.Days;
        if (days.Count < 2)
        {
            return null;
        }

        var built = series.Select(s =>
        {
            var byDay = s.Values.GroupBy(v => DateOnly.FromDateTime(v.Utc.ToLocalTime())).ToDictionary(g => g.Key, g => s.Combine(g.Select(v => v.Value)));
            return (s.Name, (IReadOnlyList<double>)days.Select(d => byDay.TryGetValue(d, out var v) ? v : 0).ToList());
        }).ToArray();
        return Bar(title, unit, days.Select(d => d.ToString("MM-dd", CultureInfo.InvariantCulture)).ToList(), maximum, half, null, built);
    }

    /// <summary>Average per day (rows) and hour (columns); for periods longer than two weeks the rows are the weekdays.</summary>
    public static ReportChart? DayHour(string title, string unit, double maximum, Period period, IEnumerable<(DateTime Utc, double Value)> values)
    {
        var list = values.Select(v => (Local: v.Utc.ToLocalTime(), v.Value)).ToList();
        if (list.Count < 2)
        {
            return null;
        }

        List<(string Row, Func<DateTime, bool> Match)> rows = period.Days.Count <= 14
            ? period.Days.Select(d => (Fmt.Day(d), (Func<DateTime, bool>)(t => DateOnly.FromDateTime(t) == d))).ToList()
            : Enum.GetValues<DayOfWeek>().Select(w => (w.ToString(), (Func<DateTime, bool>)(t => t.DayOfWeek == w))).ToList();

        return new ReportChart
        {
            Kind = ReportChartKinds.Heatmap,
            Title = title,
            Unit = unit,
            Maximum = maximum,
            Labels = Hours.ToList(),
            Series = rows.Select(row =>
            {
                var inRow = list.Where(v => row.Match(v.Local)).GroupBy(v => v.Local.Hour).ToDictionary(g => g.Key, g => g.Average(v => v.Value));
                return new ReportSeries(row.Row, Enumerable.Range(0, 24).Select(h => inRow.TryGetValue(h, out var v) ? Math.Round(v, 1) : -1).ToList());
            }).ToList()
        };
    }

    /// <summary>Share of the time spent in each 10% band.</summary>
    public static ReportChart? Distribution(string title, IEnumerable<double> percents, bool half = true)
    {
        var list = percents.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var bands = Enumerable.Range(0, 10).Select(i => list.Count(v => Math.Min(9, (int)(v / 10)) == i) * 100d / list.Count).ToList();
        return Bar(title, "% of time", Enumerable.Range(0, 10).Select(i => $"{i * 10}-{i * 10 + 10}").ToList(), 0, half, null, ("Time", bands));
    }

    private static (int Buckets, TimeSpan Width) Buckets(Period period)
    {
        var buckets = Math.Clamp((int)((period.ToUtc - period.FromUtc) / Fmt.Interval), 2, MaxPoints);
        return (buckets, (period.ToUtc - period.FromUtc) / buckets);
    }

    private static int Index(DateTime utc, Period period, TimeSpan width, int buckets) =>
        Math.Clamp((int)((utc - period.FromUtc) / width), 0, buckets - 1);
}

/// <summary>Up / down minutes of one target from its readings; a gap of more than three minutes is not counted.</summary>
internal sealed class UpTime
{
    public int Up { get; private set; }

    public int Down { get; private set; }

    public int Known => Up + Down;

    public double Percent => Known == 0 ? 0 : Up * 100d / Known;

    public TimeSpan Downtime => TimeSpan.FromTicks(Fmt.Interval.Ticks * Down);

    public List<(DateTime Start, DateTime End)> Outages { get; } = [];

    /// <summary>Mean time between failures: working time divided by the number of outages.</summary>
    public TimeSpan? BetweenFailures => Outages.Count == 0 ? null : TimeSpan.FromTicks(Fmt.Interval.Ticks * Up / Outages.Count);

    /// <summary>Mean time to repair: average outage length.</summary>
    public TimeSpan? ToRepair => Outages.Count == 0 ? null : TimeSpan.FromTicks((long)Outages.Average(o => (o.End - o.Start).Ticks));

    public static UpTime From(IEnumerable<(DateTime Utc, bool? Up)> readings)
    {
        var stats = new UpTime();
        DateTime? outageStart = null;
        var previous = DateTime.MinValue;
        var interval = Fmt.Interval;
        foreach (var (utc, up) in readings)
        {
            if (outageStart is not null && (up != false || utc - previous > interval * 3))
            {
                stats.Outages.Add((outageStart.Value, up == true && utc - previous <= interval * 3 ? utc : previous + interval));
                outageStart = null;
            }

            if (up == true)
            {
                stats.Up++;
            }
            else if (up == false)
            {
                stats.Down++;
                outageStart ??= utc;
            }

            previous = utc;
        }

        if (outageStart is not null)
        {
            stats.Outages.Add((outageStart.Value, previous + interval));
        }

        return stats;
    }
}
