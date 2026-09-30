using ClientAgent.Service.Monitoring;
using ClientAgent.Shared.Models;
using ClientAgent.Shared.Models.Reports;
using ClientAgent.Shared.Monitoring;

namespace ClientAgent.Service.Reports;

public sealed partial class ReportBuilder
{
    private async Task<List<ReportSection>> DetailsAsync(ReportSubject subject, Period period, CancellationToken cancellationToken)
    {
        if (subject.Id == ReportTypes.CpuSubject || subject.Id == ReportTypes.RamSubject)
        {
            return UsageDetails(subject.Id, period);
        }

        if (subject.Id == ReportTypes.InternetSubject)
        {
            return InternetDetails(period);
        }

        if (subject.Id.StartsWith(ReportTypes.DiskSubjectPrefix, StringComparison.Ordinal))
        {
            return DiskDetails(subject.Id[ReportTypes.DiskSubjectPrefix.Length..], period);
        }

        return await PointDetailsAsync(subject.Id[ReportTypes.PointSubjectPrefix.Length..], period, cancellationToken);
    }

    private List<ReportSection> UsageDetails(string kind, Period period)
    {
        var spec = _config.GetDeviceSpec();
        var isCpu = kind == ReportTypes.CpuSubject;
        var name = isCpu ? "CPU" : "RAM";
        double warning = isCpu ? spec.CpuWarningPercent : spec.RamWarningPercent;
        double problem = isCpu ? spec.CpuProblemPercent : spec.RamProblemPercent;
        var samples = _store.GetSamples(period.FromUtc, period.ToUtc);
        var previous = _store.GetSamples(period.Previous.FromUtc, period.Previous.ToUtc);
        Func<SampleRow, double> pick = isCpu ? s => s.Cpu : s => s.Ram;
        var values = samples.Select(s => (s.Utc, Value: pick(s))).ToList();
        var high = Fmt.Stretches(values, v => warning > 0 && v >= warning);
        var atWarning = values.Count(v => warning > 0 && v.Value >= warning);
        var atProblem = values.Count(v => problem > 0 && v.Value >= problem);
        var previousProblem = previous.Count(s => problem > 0 && pick(s) >= problem);

        var metrics = new List<ReportMetric>
        {
            Fmt.Compared("Average", Avg(samples, pick), Avg(previous, pick), Fmt.Percent, Fmt.Level(Avg(samples, pick), warning, problem), higherIsBetter: false),
            new("Peak", values.Count == 0 ? "-" : Fmt.Percent(values.Max(v => v.Value)), Fmt.Level(values.Count == 0 ? null : values.Max(v => v.Value), warning, problem)),
            new("Lowest", values.Count == 0 ? "-" : Fmt.Percent(values.Min(v => v.Value))),
            new($"Time at or above {warning:0}% (warning)", Fmt.Minutes(atWarning), atWarning > 0 ? "Warning" : "Good"),
            Fmt.Compared($"Time at or above {problem:0}% (problem)", atProblem, previousProblem, v => Fmt.Minutes((int)v), atProblem > 0 ? "Bad" : "Good", higherIsBetter: false),
            new("High usage periods", high.Count.ToString(), high.Count > 0 ? "Warning" : "Good"),
            new("Longest high period", high.Count == 0 ? "-" : Fmt.Duration(high.Max(h => h.End - h.Start)))
        };
        var temperature = samples.Where(s => s.TemperatureC is not null).Select(s => (s.Utc, s.TemperatureC!.Value)).ToList();
        if (isCpu && temperature.Count > 0)
        {
            metrics.Add(new("Average temperature", $"{temperature.Average(t => t.Item2):0} °C", Fmt.Level(temperature.Average(t => t.Item2), 75, 85)));
            metrics.Add(new("Highest temperature", $"{temperature.Max(t => t.Item2):0} °C", Fmt.Level(temperature.Max(t => t.Item2), 75, 85)));
        }

        var sections = new List<ReportSection>
        {
            new() { Title = "Overview", Note = Fmt.Coverage(samples.Count, period), Metrics = metrics },
            new()
            {
                Title = $"{name} usage",
                Charts = new[]
                {
                    Charts.Line($"{name} usage", period, "%", 100, Charts.Levels(warning, problem), (name, values)),
                    isCpu && temperature.Count > 1 ? Charts.Line("Temperature", period, "°C", 0, Charts.Levels(75, 85, " °C"), ("Temperature", temperature)) : null,
                    Charts.ByHour("Average and peak by hour of the day", "%", 100, true, Charts.Levels(warning, problem),
                        ("Average", values, v => v.Average()), ("Peak", values, v => v.Max())),
                    Charts.Distribution($"How busy the {name} was (share of the time)", values.Select(v => v.Value)),
                    Charts.DayHour("Busy hours (average %)", "%", 100, period, values),
                    Charts.ByDay("Daily average and peak", "%", 100, period, false, ("Average", values, v => v.Average()), ("Peak", values, v => v.Max()))
                }.OfType<ReportChart>().ToList()
            },
            new()
            {
                Title = "High usage periods",
                Note = warning > 0 ? $"Every stretch at or above the warning level ({warning:0}%)." : "The warning level is not set in Device Specifications.",
                Charts = new[]
                {
                    Charts.ByHour("When high usage starts (hour of the day)", "periods", 0, true, null, ("Periods", high.Select(h => (h.Start, 1d)), v => v.Sum())),
                    Charts.Pie("High usage time by level",
                    [
                        ("Warning", (double)(atWarning - atProblem)),
                        ("Problem", atProblem)
                    ], "minutes")
                }.OfType<ReportChart>().ToList(),
                Columns = ["From", "To", "Duration", "Peak", "Level"],
                Rows = high.OrderByDescending(h => h.Start).Select(h => new List<string>
                {
                    Fmt.Local(h.Start), Fmt.Local(h.End), Fmt.Duration(h.End - h.Start), Fmt.Percent(h.Peak),
                    problem > 0 && h.Peak >= problem ? "Problem" : "Warning"
                }).ToList()
            }
        };

        sections.AddRange(BusyProgramSections(period, isCpu ? "cpu" : "ram"));
        sections.Add(DailyTable(period, samples, ["Day", "Average", "Peak", $"At or above {warning:0}%", $"At or above {problem:0}%"], day =>
        [
            Fmt.Percent(day.Average(pick)),
            Fmt.Percent(day.Max(pick)),
            Fmt.Minutes(day.Count(s => warning > 0 && pick(s) >= warning)),
            Fmt.Minutes(day.Count(s => problem > 0 && pick(s) >= problem))
        ]));
        sections.Add(RelatedProblems(period, i => Category(i.IssueId) == name));
        return sections;
    }

    private List<ReportSection> DiskDetails(string drive, Period period)
    {
        var spec = _config.GetDeviceSpec();
        var disks = DiskRows(period);
        var row = disks.FirstOrDefault(d => string.Equals(d.Drive, drive, StringComparison.OrdinalIgnoreCase));
        var samples = _store.GetDiskSamples(period.FromUtc, period.ToUtc)
            .Where(d => string.Equals(d.Drive, drive, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var previous = _store.GetDiskSamples(period.Previous.FromUtc, period.Previous.ToUtc)
            .Where(d => string.Equals(d.Drive, drive, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var inGb = spec.DiskRemainingProblemUnit is "GB" or "Gigabytes";
        var thresholds = new List<ReportThreshold>();
        if (spec.DiskRemainingWarning > 0 && spec.DiskRemainingWarningUnit is "GB" or "Gigabytes")
        {
            thresholds.Add(new ReportThreshold($"Warning {spec.DiskRemainingWarning:0.#} GB free", spec.DiskRemainingWarning, "Warning"));
        }

        if (spec.DiskRemainingProblem > 0 && inGb)
        {
            thresholds.Add(new ReportThreshold($"Problem {spec.DiskRemainingProblem:0.#} GB free", spec.DiskRemainingProblem, "Bad"));
        }

        double? usedNow = row is null ? null : row.FreeStartGb - row.FreeNowGb;
        double? usedBefore = previous.Count < 2 ? null : previous[0].FreeGb - previous[^1].FreeGb;
        var sections = new List<ReportSection>
        {
            new()
            {
                Title = "Overview",
                Note = samples.Count == 0 ? $"No readings of {drive} in this period." : null,
                Metrics = row is null
                    ? []
                    :
                    [
                        new("Size", $"{row.TotalGb:0.#} GB"),
                        new("Free now", $"{row.FreeNowGb:0.#} GB ({row.FreeNowGb / Math.Max(row.TotalGb, 0.001) * 100:0}%)",
                            row.FreeNowGb / Math.Max(row.TotalGb, 0.001) < 0.1 ? "Bad" : row.FreeNowGb / Math.Max(row.TotalGb, 0.001) < 0.2 ? "Warning" : "Good"),
                        new("Free at the start", $"{row.FreeStartGb:0.#} GB"),
                        new("Lowest free", $"{row.LowestFreeGb:0.#} GB"),
                        Fmt.Compared("Space used in the period", usedNow, usedBefore, v => $"{v:0.##} GB", null, higherIsBetter: false),
                        new("Used per day", row.UsedPerDayGb is { } rate ? $"{rate:0.##} GB" : "-"),
                        new("Full in", row.DaysLeft is { } days ? $"~{days:0} days" : "Not filling up",
                            row.DaysLeft < 30 ? "Bad" : row.DaysLeft < 90 ? "Warning" : "Good")
                    ]
            },
            new()
            {
                Title = "Space over time",
                Charts = new[]
                {
                    Charts.Line("Free space", period, "GB", 0, thresholds, ("Free", samples.Select(d => (d.Utc, d.FreeGb)))),
                    Charts.Line("Used space", period, "%", 100, Charts.Levels(80, 90),
                        ("Used", samples.Select(d => (d.Utc, (d.TotalGb - d.FreeGb) / Math.Max(d.TotalGb, 0.001) * 100)))),
                    Charts.Bar("Space used per day", "GB", period.Days.Select(Fmt.Day).ToList(), 0, true, null,
                        ("Used", period.Days.Select(day =>
                        {
                            var ofDay = samples.Where(d => DateOnly.FromDateTime(d.Utc.ToLocalTime()) == day).ToList();
                            return ofDay.Count < 2 ? 0 : Math.Max(0, ofDay[0].FreeGb - ofDay[^1].FreeGb);
                        }).ToList()))
                }.OfType<ReportChart>().Select(c => { c.Half = c.Title != "Free space"; return c; }).ToList()
            },
            DiskActivitySection(_store.GetSamples(period.FromUtc, period.ToUtc), period)
        };

        sections.Add(new ReportSection
        {
            Title = "Daily summary",
            Columns = ["Day", "Free at start", "Free at end", "Used", "Lowest free"],
            Rows = period.Days.Select(day => samples.Where(d => DateOnly.FromDateTime(d.Utc.ToLocalTime()) == day).ToList())
                .Where(ofDay => ofDay.Count > 0)
                .Select(ofDay => new List<string>
                {
                    Fmt.Day(DateOnly.FromDateTime(ofDay[0].Utc.ToLocalTime())),
                    $"{ofDay[0].FreeGb:0.##} GB",
                    $"{ofDay[^1].FreeGb:0.##} GB",
                    $"{ofDay[0].FreeGb - ofDay[^1].FreeGb:0.##} GB",
                    $"{ofDay.Min(d => d.FreeGb):0.##} GB"
                }).ToList()
        });
        sections.Add(RelatedProblems(period, i => Category(i.IssueId) == "Disk" && i.IssueId.Contains(drive, StringComparison.OrdinalIgnoreCase)));
        return sections;
    }

    private List<ReportSection> InternetDetails(Period period)
    {
        var sections = Internet(period);
        var samples = _store.GetSamples(period.FromUtc, period.ToUtc);
        var tests = _store.GetSpeedTests(period.FromUtc, period.ToUtc).Where(t => t.Error is null && t.DownloadMbps is not null).ToList();
        var index = sections.FindIndex(s => s.Title == "Disconnections");
        sections.Insert(index < 0 ? sections.Count : index, DailyTable(period, samples,
            ["Day", "Internet uptime", "Average ping", "Packet loss", "Average download", "Downloaded", "Uploaded"], day =>
            {
                var known = day.Where(s => s.Internet is not null).ToList();
                var date = DateOnly.FromDateTime(day[0].Utc.ToLocalTime());
                var dayTests = tests.Where(t => DateOnly.FromDateTime(t.Utc.ToLocalTime()) == date).ToList();
                return
                [
                    known.Count == 0 ? "-" : Fmt.Percent(known.Count(s => s.Internet == true) * 100d / known.Count),
                    Fmt.Ms(Fmt.Average(day.Select(s => s.PingMs))),
                    Fmt.Percent(Fmt.Average(day.Select(s => s.LossPercent))),
                    dayTests.Count == 0 ? "-" : Fmt.Mbps(dayTests.Average(t => t.DownloadMbps!.Value)),
                    Fmt.Size(day.Sum(s => s.ReceivedMb ?? 0)),
                    Fmt.Size(day.Sum(s => s.SentMb ?? 0))
                ];
            }));
        sections.Add(RelatedProblems(period, i => Category(i.IssueId) is "Network" or "Internet"));
        return sections;
    }

    private async Task<List<ReportSection>> PointDetailsAsync(string pointId, Period period, CancellationToken cancellationToken)
    {
        var config = await _config.GetConfigAsync(cancellationToken);
        var point = config.MonitorPoints.First(p => string.Equals(p.MonitorPointId, pointId, StringComparison.OrdinalIgnoreCase));
        var status = (await MonitorPointStatusBuilder.BuildAsync(_services, cancellationToken))
            .First(p => string.Equals(p.MonitorPointId, pointId, StringComparison.OrdinalIgnoreCase));
        var samples = _store.GetPointSamples(period.FromUtc, period.ToUtc).Where(p => string.Equals(p.PointId, pointId, StringComparison.OrdinalIgnoreCase)).ToList();
        var previous = _store.GetPointSamples(period.Previous.FromUtc, period.Previous.ToUtc).Where(p => string.Equals(p.PointId, pointId, StringComparison.OrdinalIgnoreCase)).ToList();
        var stats = UpTime.From(samples.Select(p => (p.Utc, IsUp(p.Status))));
        var previousStats = UpTime.From(previous.Select(p => (p.Utc, IsUp(p.Status))));
        var response = samples.Where(p => p.ResponseMs is not null).Select(p => (p.Utc, p.ResponseMs!.Value)).ToList();
        var previousResponse = previous.Where(p => p.ResponseMs is not null).Select(p => p.ResponseMs!.Value).ToList();
        var subject = $"{ReportTypes.PointSubjectPrefix}{point.MonitorPointId}";
        var hasResponse = point.Type is MonitorPointType.Website or MonitorPointType.Device or MonitorPointType.Database or MonitorPointType.Madkhal;

        var metrics = new List<ReportMetric>
        {
            new("Status now", point.Enabled ? status.Status : "Disabled", point.Enabled ? StatusColor(status.Status) : null),
            Fmt.Compared("Uptime", stats.Known == 0 ? null : stats.Percent, previousStats.Known == 0 ? null : previousStats.Percent, Fmt.Percent,
                stats.Known == 0 ? null : Fmt.UptimeStatus(stats.Percent), higherIsBetter: true),
            Fmt.Compared("Outages", stats.Outages.Count, previousStats.Known == 0 ? null : previousStats.Outages.Count, v => v.ToString("0"),
                stats.Outages.Count > 0 ? "Warning" : "Good", higherIsBetter: false),
            new("Downtime", Fmt.Duration(stats.Downtime), stats.Down > 0 ? "Bad" : "Good"),
            new("Longest outage", stats.Outages.Count == 0 ? "-" : Fmt.Duration(stats.Outages.Max(o => o.End - o.Start))),
            new("Time between failures", stats.BetweenFailures is { } mtbf ? Fmt.Duration(mtbf) : "-"),
            new("Average repair time", stats.ToRepair is { } mttr ? Fmt.Duration(mttr) : "-")
        };
        if (hasResponse)
        {
            metrics.Add(Fmt.Compared("Average response", response.Count == 0 ? null : response.Average(r => r.Item2),
                previousResponse.Count == 0 ? null : previousResponse.Average(), v => MonitorPointText.ResponseTime(v), null, higherIsBetter: false));
            metrics.Add(new("Slowest response", response.Count == 0 ? "-" : MonitorPointText.ResponseTime(response.Max(r => r.Item2))));
        }

        var sections = new List<ReportSection>
        {
            new() { Title = "Overview", Note = $"Based on {samples.Count:N0} readings, one per minute.", Metrics = metrics },
            new()
            {
                Title = "This monitor point",
                Columns = ["Item", "Value"],
                Rows =
                [
                    ["Id", point.MonitorPointId],
                    ["Name", point.DisplayName],
                    ["Type", MonitorPointText.Type(point.Type)],
                    ["Device kind", MonitorPointText.DeviceKind(point.DeviceKind)],
                    ["Address / Target", MonitorPointText.Target(point)],
                    ["Location", point.Location],
                    ["Model", point.Model],
                    ["Enabled", point.Enabled ? "Yes" : "No"],
                    ["Shown on the dashboard", point.ShowInShortcut ? "Yes" : "No"],
                    ["Alert level", MonitorPointText.Alert(point.Alert)],
                    ["Check every", $"{point.IntervalSeconds} s"],
                    ["Status now", point.Enabled ? status.Status : "Disabled"],
                    ["In this status for", status.StatusSinceUtc is { } since ? Fmt.Duration(DateTime.UtcNow - since) : "-"],
                    ["Last check", status.LastCheckedUtc is { } last ? Fmt.Local(last) : "-"],
                    ["Last response time", MonitorPointText.ResponseTime(status.ResponseMs)],
                    ["Last result", status.Message ?? "-"]
                ]
            },
            new()
            {
                Title = "Status and response",
                Note = "Green: working. Yellow: warning. Red: down. Gray: no reading.",
                Charts = new[]
                {
                    Charts.Timeline("Status over time", period, [(point.DisplayName, samples.Select(p => (p.Utc, State(p.Status))))]),
                    hasResponse ? Charts.Line("Response time", period, "ms", 0, null, ("Response", response)) : null,
                    hasResponse ? Charts.ByHour("Average response by hour of the day", "ms", 0, true, null, ("Response", response, v => v.Average())) : null,
                    Charts.Pie("Time by status", samples.GroupBy(p => State(p.Status) switch { 2 => "Working", 1 => "Warning", 0 => "Down", _ => "No reading" })
                        .Select(g => (g.Key, g.Count() * Fmt.Interval.TotalMinutes)), "minutes"),
                    Charts.ByDay("Daily uptime", "%", 100, period, false,
                        ("Uptime", samples.Where(p => IsUp(p.Status) is not null).Select(p => (p.Utc, IsUp(p.Status) == true ? 100d : 0d)), v => v.Average())),
                    hasResponse ? Charts.ByDay("Daily average response", "ms", 0, period, true, ("Response", response, v => v.Average())) : null,
                    Charts.ByHour("Outages by hour of the day", "outages", 0, true, null, ("Outages", stats.Outages.Select(o => (o.Start, 1d)), v => v.Sum()))
                }.OfType<ReportChart>().ToList()
            },
            new()
            {
                Title = "Outages",
                Columns = ["Down from", "Back at", "Duration"],
                Rows = stats.Outages.OrderByDescending(o => o.Start)
                    .Select(o => new List<string> { Fmt.Local(o.Start), Fmt.Local(o.End), Fmt.Duration(o.End - o.Start) }).ToList()
            }
        };

        sections.Add(DailyPointTable(period, samples, hasResponse));
        sections.Add(RelatedProblems(period, i => SubjectOf(i.IssueId) is { } s && string.Equals(s, subject, StringComparison.OrdinalIgnoreCase)));
        return sections;
    }

    private static ReportSection DailyPointTable(Period period, List<PointSampleRow> samples, bool hasResponse)
    {
        var columns = new List<string> { "Day", "Uptime", "Outages", "Downtime" };
        if (hasResponse)
        {
            columns.AddRange(["Average response", "Slowest response"]);
        }

        return new ReportSection
        {
            Title = "Daily summary",
            Columns = columns,
            Rows = period.Days.Select(day => samples.Where(p => DateOnly.FromDateTime(p.Utc.ToLocalTime()) == day).ToList())
                .Where(ofDay => ofDay.Count > 0)
                .Select(ofDay =>
                {
                    var stats = UpTime.From(ofDay.Select(p => (p.Utc, IsUp(p.Status))));
                    var response = ofDay.Where(p => p.ResponseMs is not null).Select(p => p.ResponseMs!.Value).ToList();
                    var row = new List<string>
                    {
                        Fmt.Day(DateOnly.FromDateTime(ofDay[0].Utc.ToLocalTime())),
                        stats.Known == 0 ? "No data" : Fmt.Percent(stats.Percent),
                        stats.Outages.Count.ToString(),
                        Fmt.Duration(stats.Downtime)
                    };
                    if (hasResponse)
                    {
                        row.Add(response.Count == 0 ? "-" : MonitorPointText.ResponseTime(response.Average()));
                        row.Add(response.Count == 0 ? "-" : MonitorPointText.ResponseTime(response.Max()));
                    }

                    return row;
                }).ToList()
        };
    }

    private static ReportSection DailyTable(Period period, List<SampleRow> samples, List<string> columns, Func<List<SampleRow>, List<string>> values) =>
        new()
        {
            Title = "Daily summary",
            Columns = columns,
            Rows = period.Days.Select(day => samples.Where(s => DateOnly.FromDateTime(s.Utc.ToLocalTime()) == day).ToList())
                .Where(ofDay => ofDay.Count > 0)
                .Select(ofDay => values(ofDay).Prepend(Fmt.Day(DateOnly.FromDateTime(ofDay[0].Utc.ToLocalTime()))).ToList())
                .ToList()
        };

    private ReportSection RelatedProblems(Period period, Func<IncidentRow, bool> match)
    {
        var incidents = _store.GetIncidents(period.FromUtc, period.ToUtc).Where(match).OrderByDescending(i => i.StartedUtc).ToList();
        return new ReportSection
        {
            Title = "Problems & warnings",
            Note = incidents.Count == 0 ? "No problems or warnings in this period." : null,
            Columns = ["Started", "Ended", "Duration", "Severity", "Category", "Problem", "Details"],
            Rows = incidents.Select(i => IncidentRow(i, Category(i.IssueId))).ToList()
        };
    }

    private static string? StatusColor(string status) => State(status) switch
    {
        2 => "Good",
        1 => "Warning",
        0 => "Bad",
        _ => null
    };
}
