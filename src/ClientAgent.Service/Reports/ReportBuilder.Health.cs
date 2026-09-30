using ClientAgent.Shared.Models.Reports;

namespace ClientAgent.Service.Reports;

public sealed partial class ReportBuilder
{
    private const double PingWarningMs = 100;
    private const double PingProblemMs = 200;
    private const double LossWarningPercent = 2;
    private const double LossProblemPercent = 5;

    private List<ReportSection> Health(Period period)
    {
        var spec = _config.GetDeviceSpec();
        var samples = _store.GetSamples(period.FromUtc, period.ToUtc);
        var previous = _store.GetSamples(period.Previous.FromUtc, period.Previous.ToUtc);
        var temperatures = samples.Where(s => s.TemperatureC is not null).Select(s => s.TemperatureC!.Value).ToList();
        var cpuHigh = samples.Count(s => spec.CpuProblemPercent > 0 && s.Cpu >= spec.CpuProblemPercent);
        var ramHigh = samples.Count(s => spec.RamProblemPercent > 0 && s.Ram >= spec.RamProblemPercent);
        var disks = DiskRows(period);
        var cpu = samples.Select(s => (s.Utc, s.Cpu)).ToList();
        var ram = samples.Select(s => (s.Utc, s.Ram)).ToList();

        var overview = new ReportSection
        {
            Title = "Overview",
            Note = Fmt.Coverage(samples.Count, period),
            Metrics =
            [
                Fmt.Compared("CPU average", Avg(samples, s => s.Cpu), Avg(previous, s => s.Cpu), Fmt.Percent,
                    Fmt.Level(Avg(samples, s => s.Cpu), spec.CpuWarningPercent, spec.CpuProblemPercent), higherIsBetter: false),
                new("CPU peak", samples.Count == 0 ? "-" : Fmt.Percent(samples.Max(s => s.Cpu)),
                    Fmt.Level(samples.Count == 0 ? null : samples.Max(s => s.Cpu), spec.CpuWarningPercent, spec.CpuProblemPercent)),
                new($"CPU at or above {spec.CpuProblemPercent}%", Fmt.Minutes(cpuHigh), cpuHigh > 0 ? "Bad" : "Good"),
                Fmt.Compared("RAM average", Avg(samples, s => s.Ram), Avg(previous, s => s.Ram), Fmt.Percent,
                    Fmt.Level(Avg(samples, s => s.Ram), spec.RamWarningPercent, spec.RamProblemPercent), higherIsBetter: false),
                new("RAM peak", samples.Count == 0 ? "-" : Fmt.Percent(samples.Max(s => s.Ram)),
                    Fmt.Level(samples.Count == 0 ? null : samples.Max(s => s.Ram), spec.RamWarningPercent, spec.RamProblemPercent)),
                new($"RAM at or above {spec.RamProblemPercent}%", Fmt.Minutes(ramHigh), ramHigh > 0 ? "Bad" : "Good"),
                new("Highest temperature", temperatures.Count == 0 ? "Not available" : $"{temperatures.Max():0} °C",
                    temperatures.Count == 0 ? null : Fmt.Level(temperatures.Max(), 75, 85)),
                new("Disk busy (average)", Fmt.Percent(Fmt.Average(samples.Select(s => s.DiskActivePercent))),
                    Fmt.Level(Fmt.Average(samples.Select(s => s.DiskActivePercent)), 60, 90)),
                new("Disk that fills first", disks.FirstOrDefault(d => d.DaysLeft is not null) is { } first
                    ? $"{first.Drive} in ~{first.DaysLeft:0} days" : "None filling up",
                    disks.Any(d => d.DaysLeft < 30) ? "Bad" : disks.Any(d => d.DaysLeft < 90) ? "Warning" : "Good")
            ]
        };

        var sections = new List<ReportSection>
        {
            overview,
            new()
            {
                Title = "CPU and RAM",
                Note = "Average of each interval. Double-click the Processor or Memory rows in Details for more.",
                Charts = new[]
                {
                    Charts.Line("CPU and RAM usage", period, "%", 100, null, ("CPU", cpu), ("RAM", ram)),
                    Charts.ByHour("Average by hour of the day", "%", 100, true, null, ("CPU", cpu, v => v.Average()), ("RAM", ram, v => v.Average())),
                    Charts.Distribution("How busy the CPU was (share of the time)", cpu.Select(c => c.Cpu)),
                    Charts.DayHour("CPU busy hours (average %)", "%", 100, period, cpu),
                    Charts.ByDay("Daily average and peak CPU", "%", 100, period, false,
                        ("Average", cpu, v => v.Average()), ("Peak", cpu, v => v.Max()))
                }.OfType<ReportChart>().ToList()
            }
        };

        if (temperatures.Count > 0)
        {
            var temperature = samples.Where(s => s.TemperatureC is not null).Select(s => (s.Utc, s.TemperatureC!.Value)).ToList();
            sections.Add(new ReportSection
            {
                Title = "Temperature",
                Note = "The hottest of the CPU, GPU and motherboard sensors.",
                Charts = new[]
                {
                    Charts.Line("Temperature", period, "°C", 0, Charts.Levels(75, 85, " °C"), ("Temperature", temperature)),
                    Charts.ByHour("Average temperature by hour", "°C", 0, true, null, ("Temperature", temperature, v => v.Average()))
                }.OfType<ReportChart>().ToList()
            });
        }

        sections.Add(DiskActivitySection(samples, period));
        sections.Add(DiskSpaceSection(period, disks, null));
        sections.AddRange(BusyProgramSections(period, null));
        return sections;
    }

    private ReportSection DiskActivitySection(List<SampleRow> samples, Period period)
    {
        var withDisk = samples.Where(s => s.DiskActivePercent is not null).ToList();
        return new ReportSection
        {
            Title = "Disk activity",
            Note = withDisk.Count == 0
                ? "No disk activity readings in this period."
                : "All disks together. Busy time is how much of the time the disks were working; response time is how long each read or write waited.",
            Metrics = withDisk.Count == 0
                ? []
                :
                [
                    new("Busy time (average)", Fmt.Percent(withDisk.Average(s => s.DiskActivePercent!.Value)), Fmt.Level(withDisk.Average(s => s.DiskActivePercent!.Value), 60, 90)),
                    new("Busy time (peak)", Fmt.Percent(withDisk.Max(s => s.DiskActivePercent!.Value))),
                    new("Read (average)", $"{withDisk.Average(s => s.DiskReadMbps ?? 0):0.##} MB/s"),
                    new("Write (average)", $"{withDisk.Average(s => s.DiskWriteMbps ?? 0):0.##} MB/s"),
                    new("Response time (average)", Fmt.Ms(Fmt.Average(withDisk.Select(s => s.DiskResponseMs))), Fmt.Level(Fmt.Average(withDisk.Select(s => s.DiskResponseMs)), 20, 50))
                ],
            Charts = new[]
            {
                Charts.Line("Read and write speed", period, "MB/s", 0, null,
                    ("Read", withDisk.Select(s => (s.Utc, s.DiskReadMbps ?? 0))), ("Write", withDisk.Select(s => (s.Utc, s.DiskWriteMbps ?? 0)))),
                Charts.Line("Busy time", period, "%", 100, Charts.Levels(60, 90), ("Busy", withDisk.Select(s => (s.Utc, s.DiskActivePercent!.Value)))),
                Charts.Line("Response time", period, "ms", 0, Charts.Levels(20, 50, " ms"),
                    ("Response", withDisk.Where(s => s.DiskResponseMs is not null).Select(s => (s.Utc, s.DiskResponseMs!.Value))))
            }.OfType<ReportChart>().Select(c => { c.Half = c.Title != "Read and write speed"; return c; }).ToList()
        };
    }

    private ReportSection DiskSpaceSection(Period period, List<DiskRow> disks, string? drive)
    {
        var samples = _store.GetDiskSamples(period.FromUtc, period.ToUtc)
            .Where(d => drive is null || string.Equals(d.Drive, drive, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var shown = disks.Where(d => drive is null || string.Equals(d.Drive, drive, StringComparison.OrdinalIgnoreCase)).ToList();
        return new ReportSection
        {
            Title = "Disk space",
            Note = "\"Used per day\" compares the first and last reading of the period; \"Full in\" assumes it keeps filling at that rate."
                   + (drive is null ? " Double-click a row for its details." : string.Empty),
            Charts = new[]
            {
                Charts.Line("Free space", period, "GB", 0, null,
                    samples.GroupBy(d => d.Drive).Select(g => ($"{g.Key} free", g.Select(d => (d.Utc, d.FreeGb)))).ToArray()),
                Charts.Bar("Used and free space now", "GB", shown.Select(d => d.Drive).ToList(), 0, true, null,
                    ("Used", shown.Select(d => d.TotalGb - d.FreeNowGb).ToList()), ("Free", shown.Select(d => d.FreeNowGb).ToList()))
            }.OfType<ReportChart>().ToList(),
            Columns = ["Drive", "Size", "Free now", "Free at start", "Lowest free", "Used per day", "Full in"],
            Rows = shown.Select(d => new List<string>
            {
                d.Drive,
                $"{d.TotalGb:0.#} GB",
                $"{d.FreeNowGb:0.#} GB ({d.FreeNowGb / Math.Max(d.TotalGb, 0.001) * 100:0}%)",
                $"{d.FreeStartGb:0.#} GB",
                $"{d.LowestFreeGb:0.#} GB",
                d.UsedPerDayGb is { } rate ? $"{rate:0.##} GB" : "-",
                d.DaysLeft is { } days ? $"~{days:0} days" : "Not filling up"
            }).ToList(),
            RowLinks = shown.Select(d => (string?)$"{ReportTypes.DiskSubjectPrefix}{d.Drive}").ToList()
        };
    }

    /// <summary>The programs saved while CPU or RAM was at or above the warning level.</summary>
    /// <param name="kind">"cpu", "ram" or null for both.</param>
    private List<ReportSection> BusyProgramSections(Period period, string? kind)
    {
        var rows = _store.GetProcessSamples(period.FromUtc, period.ToUtc);
        var sections = new List<ReportSection>();
        foreach (var (key, title, unit) in new[] { ("cpu", "Programs using the most CPU", "%"), ("ram", "Programs using the most RAM", "MB") })
        {
            if (kind is not null && kind != key)
            {
                continue;
            }

            var programs = rows.Where(r => r.Kind == key)
                .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Minutes: g.Select(r => r.Utc).Distinct().Count(), Average: g.Average(r => r.Value), Peak: g.Max(r => r.Value), Last: g.Max(r => r.Utc)))
                .OrderByDescending(p => p.Minutes)
                .ThenByDescending(p => p.Average)
                .ToList();
            var busyMinutes = rows.Where(r => r.Kind == key).Select(r => r.Utc).Distinct().Count();
            sections.Add(new ReportSection
            {
                Title = title,
                Note = programs.Count == 0
                    ? $"{(key == "cpu" ? "The CPU" : "RAM")} never reached the warning level in this period, so no programs were recorded."
                    : $"Recorded every minute {(key == "cpu" ? "the CPU" : "RAM")} was at or above the warning level ({Fmt.Minutes(busyMinutes)} in total), the five busiest programs each time.",
                Charts = new[]
                {
                    Charts.Pie($"Share of the busy time ({key.ToUpperInvariant()})", programs.Select(p => (p.Name, (double)p.Minutes)), "minutes"),
                    Charts.Bar($"Average use while busy ({unit})", unit, programs.Take(10).Select(p => p.Name).ToList(), 0, true, null,
                        ("Average", programs.Take(10).Select(p => p.Average).ToList()), ("Peak", programs.Take(10).Select(p => p.Peak).ToList()))
                }.OfType<ReportChart>().ToList(),
                Columns = ["Program", "Time among the busiest", "Average", "Peak", "Last seen"],
                Rows = programs.Select(p => new List<string>
                {
                    p.Name,
                    Fmt.Minutes(p.Minutes),
                    unit == "%" ? Fmt.Percent(p.Average) : Fmt.Size(p.Average),
                    unit == "%" ? Fmt.Percent(p.Peak) : Fmt.Size(p.Peak),
                    Fmt.Local(p.Last)
                }).ToList()
            });
        }

        return sections;
    }

    private List<DiskRow> DiskRows(Period period)
    {
        return _store.GetDiskSamples(period.FromUtc, period.ToUtc)
            .GroupBy(d => d.Drive)
            .Select(group =>
            {
                var first = group.First();
                var last = group.Last();
                var days = (last.Utc - first.Utc).TotalDays;
                double? perDay = days >= 0.25 ? (first.FreeGb - last.FreeGb) / days : null;
                double? daysLeft = perDay is > 0.01 ? last.FreeGb / perDay : null;
                return new DiskRow(group.Key, last.TotalGb, last.FreeGb, first.FreeGb, group.Min(d => d.FreeGb), perDay, daysLeft);
            })
            .OrderBy(d => d.DaysLeft ?? double.MaxValue)
            .ThenBy(d => d.Drive)
            .ToList();
    }

    private sealed record DiskRow(string Drive, double TotalGb, double FreeNowGb, double FreeStartGb, double LowestFreeGb, double? UsedPerDayGb, double? DaysLeft);

    private List<ReportSection> Internet(Period period)
    {
        var spec = _config.GetDeviceSpec();
        var samples = _store.GetSamples(period.FromUtc, period.ToUtc);
        var previousSamples = _store.GetSamples(period.Previous.FromUtc, period.Previous.ToUtc);
        var tests = _store.GetSpeedTests(period.FromUtc, period.ToUtc);
        var passed = tests.Where(t => t.Error is null && t.DownloadMbps is not null).ToList();
        var previousPassed = _store.GetSpeedTests(period.Previous.FromUtc, period.Previous.ToUtc).Where(t => t.Error is null && t.DownloadMbps is not null).ToList();
        var minDownload = spec.DownloadMinKbps / 1000d;
        var minUpload = spec.UploadMinKbps / 1000d;
        var slow = passed.Count(t => (minDownload > 0 && t.DownloadMbps < minDownload) || (minUpload > 0 && t.UploadMbps < minUpload));
        var disconnections = Disconnections(period);
        var offline = TimeSpan.FromSeconds(disconnections.Sum(i => period.Clip(i.StartedUtc, i.EndedUtc ?? DateTime.UtcNow).TotalSeconds));
        var previousOffline = TimeSpan.FromSeconds(Disconnections(period.Previous).Sum(i => period.Previous.Clip(i.StartedUtc, i.EndedUtc ?? DateTime.UtcNow).TotalSeconds));
        var ping = samples.Where(s => s.PingMs is not null).Select(s => (s.Utc, s.PingMs!.Value)).ToList();
        var loss = samples.Where(s => s.LossPercent is not null).Select(s => (s.Utc, s.LossPercent!.Value)).ToList();
        var wifi = samples.Where(s => s.WifiSignal is not null).Select(s => (s.Utc, (double)s.WifiSignal!.Value)).ToList();
        var received = samples.Sum(s => s.ReceivedMb ?? 0);
        var sent = samples.Sum(s => s.SentMb ?? 0);

        var overview = new ReportSection
        {
            Title = "Overview",
            Metrics =
            [
                new("Speed tests", tests.Count.ToString()),
                new("Failed tests", tests.Count(t => t.Error is not null).ToString(), tests.Any(t => t.Error is not null) ? "Warning" : "Good"),
                Fmt.Compared("Average download", passed.Count == 0 ? null : passed.Average(t => t.DownloadMbps!.Value),
                    previousPassed.Count == 0 ? null : previousPassed.Average(t => t.DownloadMbps!.Value), v => $"{v:0.##} Mbps",
                    passed.Count == 0 ? null : Fmt.Level(passed.Average(t => t.DownloadMbps!.Value), 0, minDownload, higherIsWorse: false), higherIsBetter: true),
                new("Slowest download", passed.Count == 0 ? "-" : Fmt.Mbps(passed.Min(t => t.DownloadMbps!.Value))),
                new("Average upload", passed.Count == 0 ? "-" : Fmt.Mbps(passed.Average(t => t.UploadMbps ?? 0)),
                    passed.Count == 0 ? null : Fmt.Level(passed.Average(t => t.UploadMbps ?? 0), 0, minUpload, higherIsWorse: false)),
                new("Tests below the minimum", slow.ToString(), slow > 0 ? "Bad" : "Good"),
                Fmt.Compared("Average ping", Fmt.Average(ping.Select(p => (double?)p.Value)), Fmt.Average(previousSamples.Select(s => s.PingMs)), v => $"{v:0} ms",
                    Fmt.Level(Fmt.Average(ping.Select(p => (double?)p.Value)), PingWarningMs, PingProblemMs), higherIsBetter: false),
                new("Packet loss (average)", Fmt.Percent(Fmt.Average(loss.Select(l => (double?)l.Value))),
                    Fmt.Level(Fmt.Average(loss.Select(l => (double?)l.Value)), LossWarningPercent, LossProblemPercent)),
                new("Wi-Fi signal (average)", wifi.Count == 0 ? "Not on Wi-Fi" : Fmt.Percent(wifi.Average(w => w.Item2)),
                    wifi.Count == 0 ? null : Fmt.Level(wifi.Average(w => w.Item2), 50, 30, higherIsWorse: false)),
                new("Downloaded", Fmt.Size(received)),
                new("Uploaded", Fmt.Size(sent)),
                new("Disconnections", disconnections.Count.ToString(), disconnections.Count > 0 ? "Warning" : "Good"),
                Fmt.Compared("Time offline", offline.TotalMinutes, previousOffline.TotalMinutes, v => Fmt.Duration(TimeSpan.FromMinutes(v)),
                    offline > TimeSpan.Zero ? "Warning" : "Good", higherIsBetter: false)
            ]
        };

        var download = passed.Select(t => (t.Utc, t.DownloadMbps!.Value)).ToList();
        var upload = passed.Select(t => (t.Utc, t.UploadMbps ?? 0)).ToList();
        var sections = new List<ReportSection>
        {
            overview,
            new()
            {
                Title = "Speed",
                Note = passed.Count == 0 ? "No successful speed tests in this period." : null,
                Charts = new[]
                {
                    passed.Count < 2 ? null : new ReportChart
                    {
                        Title = "Speed tests",
                        Unit = "Mbps",
                        Labels = passed.Select(t => Fmt.Label(t.Utc, period)).ToList(),
                        Series = [new("Download", download.Select(d => d.Item2).ToList()), new("Upload", upload.Select(u => u.Item2).ToList())],
                        Thresholds = minDownload > 0 ? [new ReportThreshold($"Minimum download {minDownload:0.##} Mbps", minDownload, "Bad")] : []
                    },
                    Charts.ByHour("Average speed by hour of the day", "Mbps", 0, true, null, ("Download", download, v => v.Average()), ("Upload", upload, v => v.Average())),
                    Charts.ByDay("Average speed per day", "Mbps", 0, period, true, ("Download", download, v => v.Average()), ("Upload", upload, v => v.Average()))
                }.OfType<ReportChart>().ToList()
            },
            new()
            {
                Title = "Ping and packet loss",
                Note = ping.Count == 0 ? "No ping readings in this period." : "Ping to 8.8.8.8 every minute: how long the internet takes to answer, and how many of four pings were lost.",
                Charts = new[]
                {
                    Charts.Line("Ping", period, "ms", 0, Charts.Levels(PingWarningMs, PingProblemMs, " ms"), ("Ping", ping)),
                    Charts.Line("Packet loss", period, "%", 100, Charts.Levels(LossWarningPercent, LossProblemPercent), ("Loss", loss)),
                    Charts.ByHour("Average ping by hour of the day", "ms", 0, true, null, ("Ping", ping, v => v.Average()))
                }.OfType<ReportChart>().Select(c => { c.Half = c.Title != "Ping"; return c; }).ToList()
            },
            new()
            {
                Title = "Data usage",
                Metrics = [new("Downloaded", Fmt.Size(received)), new("Uploaded", Fmt.Size(sent)), new("Total", Fmt.Size(received + sent))],
                Charts = new[]
                {
                    period.IsShort
                        ? Charts.ByHour("Data used by hour of the day", "MB", 0, false, null,
                            ("Downloaded", samples.Select(s => (s.Utc, s.ReceivedMb ?? 0)), v => v.Sum()), ("Uploaded", samples.Select(s => (s.Utc, s.SentMb ?? 0)), v => v.Sum()))
                        : Charts.ByDay("Data used per day", "MB", 0, period, false,
                            ("Downloaded", samples.Select(s => (s.Utc, s.ReceivedMb ?? 0)), v => v.Sum()), ("Uploaded", samples.Select(s => (s.Utc, s.SentMb ?? 0)), v => v.Sum())),
                    Charts.Line("Network traffic", period, "MB/min", 0, null,
                        ("Downloaded", samples.Where(s => s.ReceivedMb is not null).Select(s => (s.Utc, s.ReceivedMb!.Value))),
                        ("Uploaded", samples.Where(s => s.SentMb is not null).Select(s => (s.Utc, s.SentMb!.Value))))
                }.OfType<ReportChart>().ToList()
            },
            new()
            {
                Title = "Connection status",
                Note = "Green: connected. Red: disconnected. Gray: no reading.",
                Charts = new[]
                {
                    Charts.Timeline("Network and internet", period,
                    [
                        ("Network", samples.Select(s => (s.Utc, State(s.Network)))),
                        ("Internet", samples.Select(s => (s.Utc, State(s.Internet))))
                    ]),
                    wifi.Count < 2 ? null : Charts.Line("Wi-Fi signal", period, "%", 100, Charts.Levels(50, 30), ("Signal", wifi))
                }.OfType<ReportChart>().ToList()
            }
        };

        sections.Add(new ReportSection
        {
            Title = "Disconnections",
            Columns = ["From", "To", "Duration", "Cause"],
            Rows = disconnections.OrderByDescending(i => i.StartedUtc).Select(i => new List<string>
            {
                Fmt.Local(i.StartedUtc),
                i.EndedUtc is { } ended ? Fmt.Local(ended) : "Still offline",
                Fmt.Duration((i.EndedUtc ?? DateTime.UtcNow) - i.StartedUtc),
                i.IssueId == "network:connection" ? "No network connection on this device" : "Internet not reachable"
            }).ToList()
        });

        sections.Add(new ReportSection
        {
            Title = "All speed tests",
            Columns = ["Time", "Download", "Upload", "Result"],
            Rows = tests.OrderByDescending(t => t.Utc).Select(t => new List<string>
            {
                Fmt.Local(t.Utc),
                Fmt.Mbps(t.DownloadMbps),
                Fmt.Mbps(t.UploadMbps),
                t.Error is not null ? $"Failed: {t.Error}"
                    : (minDownload > 0 && t.DownloadMbps < minDownload) || (minUpload > 0 && t.UploadMbps < minUpload) ? "Below the minimum" : "OK"
            }).ToList()
        });
        return sections;
    }

    private List<IncidentRow> Disconnections(Period period) =>
        _store.GetIncidents(period.FromUtc, period.ToUtc)
            .Where(i => i.IssueId is "internet:connection" or "network:connection")
            .ToList();

    private static double? Avg(List<SampleRow> samples, Func<SampleRow, double> value) =>
        samples.Count == 0 ? null : samples.Average(value);
}
