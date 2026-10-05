using MonitorAgent.Service.Config;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Service.Runtime;
using MonitorAgent.Service.SystemInfo;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Models.Reports;
using MonitorAgent.Shared.Monitoring;

namespace MonitorAgent.Service.Reports;

/// <summary>Builds every report from the history in <see cref="ReportStore"/> and the device's current state.</summary>
public sealed partial class ReportBuilder
{
    private readonly IServiceProvider _services;
    private readonly ReportStore _store;
    private readonly ILocalConfigCache _config;
    private readonly IAgentIdentity _identity;

    public ReportBuilder(IServiceProvider services, ReportStore store, ILocalConfigCache config, IAgentIdentity identity)
    {
        _services = services;
        _store = store;
        _config = config;
        _identity = identity;
    }

    /// <param name="from">Local time.</param>
    /// <param name="to">Local time.</param>
    /// <param name="subject">For the details report, one of <see cref="GetSubjectsAsync"/>.</param>
    public async Task<ReportDto> BuildAsync(string type, DateTime from, DateTime to, string? subject, CancellationToken cancellationToken)
    {
        var info = ReportTypes.Find(type) ?? throw new ArgumentException($"Unknown report \"{type}\".", nameof(type));
        var period = new Period(DateTime.SpecifyKind(from, DateTimeKind.Local), DateTime.SpecifyKind(to, DateTimeKind.Local));
        var report = new ReportDto
        {
            Type = info.Id,
            Title = info.Title,
            Description = info.Description,
            MachineName = _config.GetGeneral().DisplayName,
            GeneratedAt = DateTime.Now,
            From = period.From,
            To = period.To
        };

        if (info.Id == ReportTypes.Details)
        {
            subject ??= ReportTypes.CpuSubject;
            var subjects = await GetSubjectsAsync(cancellationToken);
            var found = subjects.FirstOrDefault(s => string.Equals(s.Id, subject, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown details subject \"{subject}\".", nameof(subject));
            report.Subject = found.Id;
            report.Title = $"Details: {found.Title}";
            if (!ReportTypes.IsAllSubject(found.Id))
            {
                report.Sections = await DetailsAsync(found, period, cancellationToken);
                return report;
            }

            var included = subjects.Where(s => !ReportTypes.IsAllSubject(s.Id) && found.Id switch
            {
                ReportTypes.AllDisksSubject => s.Id.StartsWith(ReportTypes.DiskSubjectPrefix, StringComparison.Ordinal),
                ReportTypes.AllPointsSubject => s.Id.StartsWith(ReportTypes.PointSubjectPrefix, StringComparison.Ordinal),
                _ => true
            });
            foreach (var part in included)
            {
                foreach (var section in await DetailsAsync(part, period, cancellationToken))
                {
                    section.Title = $"{part.Title} › {section.Title}";
                    report.Sections.Add(section);
                }
            }

            return report;
        }

        report.Sections = info.Id switch
        {
            ReportTypes.Summary => await SummaryAsync(period, cancellationToken),
            ReportTypes.Incidents => Incidents(period),
            ReportTypes.Availability => Availability(period),
            ReportTypes.Health => Health(period),
            ReportTypes.Compliance => await ComplianceAsync(period, cancellationToken),
            ReportTypes.Internet => Internet(period),
            ReportTypes.Settings => SettingsChanges(period),
            ReportTypes.Applications => ApplicationChangesReport(period),
            ReportTypes.Inventory => await InventoryAsync(cancellationToken),
            _ => []
        };
        return report;
    }

    /// <summary>What the details report can be about: CPU, RAM, every disk, the internet and every monitor point.</summary>
    public async Task<List<ReportSubject>> GetSubjectsAsync(CancellationToken cancellationToken)
    {
        var partitions = await _services.GetRequiredService<ISystemInfoService>().GetPartitionsAsync(cancellationToken);
        var drives = partitions.Select(p => p.DriveLetter.TrimEnd('\\'))
            .Concat(_store.GetDiskSamples(DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddMinutes(1)).Select(d => d.Drive))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order();
        var config = await _config.GetConfigAsync(cancellationToken);

        var diskSubjects = drives.Select(d => new ReportSubject($"{ReportTypes.DiskSubjectPrefix}{d}", $"Disk {d}", "Disks")).ToList();
        var subjects = new List<ReportSubject>
        {
            new(ReportTypes.AllSubject, "All", "All"),
            new(ReportTypes.CpuSubject, "Processor (CPU)", "Device"),
            new(ReportTypes.RamSubject, "Memory (RAM)", "Device")
        };
        if (diskSubjects.Count > 1)
        {
            subjects.Add(new ReportSubject(ReportTypes.AllDisksSubject, "All disks", "Disks"));
        }

        subjects.AddRange(diskSubjects);
        subjects.Add(new ReportSubject(ReportTypes.InternetSubject, "Internet & network", "Network"));
        if (config.MonitorPoints.Count > 1)
        {
            subjects.Add(new ReportSubject(ReportTypes.AllPointsSubject, "All monitor points", "Monitor points"));
        }

        subjects.AddRange(config.MonitorPoints.Select(p => new ReportSubject(
            $"{ReportTypes.PointSubjectPrefix}{p.MonitorPointId}",
            string.IsNullOrWhiteSpace(p.DisplayName) ? p.MonitorPointId : p.DisplayName,
            "Monitor points")));
        return subjects;
    }

    private async Task<List<ReportSection>> SummaryAsync(Period period, CancellationToken cancellationToken)
    {
        var sections = new List<ReportSection>();
        var incidents = Incidents(period);
        foreach (var (title, built, charts) in new (string, List<ReportSection>, int)[]
                 {
                     ("Problems & Warnings", incidents, 2),
                     ("Availability", Availability(period), 2),
                     ("Device Health", Health(period), 1),
                     ("Specifications Compliance", await ComplianceAsync(period, cancellationToken), 0),
                     ("Internet Quality", Internet(period), 1),
                     ("Settings Changes", SettingsChanges(period), 0),
                     ("Applications Changes", ApplicationChangesReport(period), 0)
                 })
        {
            if (built.FirstOrDefault() is { } overview)
            {
                sections.Add(new ReportSection
                {
                    Title = title,
                    Note = overview.Note,
                    Metrics = overview.Metrics,
                    Charts = built.SelectMany(s => s.Charts).Take(charts).ToList()
                });
            }
        }

        if (incidents.FirstOrDefault(section => section.Title == "Still open") is { Rows.Count: > 0 } open)
        {
            sections.Add(open);
        }

        if (incidents.FirstOrDefault(section => section.Title == "Most frequent") is { } frequent)
        {
            frequent.Rows = frequent.Rows.Take(5).ToList();
            frequent.RowLinks = frequent.RowLinks?.Take(5).ToList();
            sections.Add(frequent);
        }

        return sections;
    }

    private List<ReportSection> Incidents(Period period)
    {
        var incidents = LoadIncidents(period);
        var previous = LoadIncidents(period.Previous);
        var resolved = incidents.Where(i => i.Row.EndedBy == "resolved").ToList();
        var open = incidents.Where(i => i.Row.EndedUtc is null).ToList();
        var totalMinutes = incidents.Sum(i => i.Duration.TotalMinutes);

        var overview = new ReportSection
        {
            Title = "Overview",
            Metrics =
            [
                Fmt.Compared("Problems & warnings", incidents.Count, previous.Count, v => v.ToString("0"), incidents.Count == 0 ? "Good" : null, higherIsBetter: false),
                new("Critical", incidents.Count(i => i.Row.Severity == "Critical").ToString(), incidents.Any(i => i.Row.Severity == "Critical") ? "Bad" : "Good"),
                new("Warning", incidents.Count(i => i.Row.Severity == "Warning").ToString(), incidents.Any(i => i.Row.Severity == "Warning") ? "Warning" : "Good"),
                new("Still open", open.Count.ToString(), open.Count > 0 ? "Bad" : "Good"),
                new("Average time to resolve", resolved.Count == 0 ? "-" : Fmt.Duration(TimeSpan.FromSeconds(
                    resolved.Average(i => (i.Row.EndedUtc!.Value - i.Row.StartedUtc).TotalSeconds)))),
                Fmt.Compared("Total problem time", totalMinutes, previous.Sum(i => i.Duration.TotalMinutes),
                    v => Fmt.Duration(TimeSpan.FromMinutes(v)), null, higherIsBetter: false)
            ]
        };

        var charts = new ReportSection
        {
            Title = "At a glance",
            Charts = new[]
            {
                Charts.Pie("Problems by category", incidents.GroupBy(i => i.Category).Select(g => (g.Key, (double)g.Count()))),
                Charts.Pie("Critical and warning", incidents.GroupBy(i => i.Row.Severity).Select(g => (g.Key, (double)g.Count()))),
                Charts.ByDay("Problems per day", "problems", 0, period, false,
                    ("Critical", incidents.Where(i => i.Row.Severity == "Critical").Select(i => (i.Row.StartedUtc, 1d)), v => v.Sum()),
                    ("Warning", incidents.Where(i => i.Row.Severity != "Critical").Select(i => (i.Row.StartedUtc, 1d)), v => v.Sum())),
                Charts.ByHour("When problems start (hour of the day)", "problems", 0, true, null,
                    ("Problems", incidents.Select(i => (i.Row.StartedUtc, 1d)), v => v.Sum())),
                Charts.Bar("Problem time by category", "minutes",
                    incidents.GroupBy(i => i.Category).OrderByDescending(g => g.Sum(i => i.Duration.TotalMinutes)).Select(g => g.Key).ToList(),
                    0, true, null,
                    ("Minutes", incidents.GroupBy(i => i.Category).OrderByDescending(g => g.Sum(i => i.Duration.TotalMinutes))
                        .Select(g => g.Sum(i => i.Duration.TotalMinutes)).ToList()))
            }.OfType<ReportChart>().ToList()
        };

        var byCategory = new ReportSection
        {
            Title = "By category",
            Columns = ["Category", "Count", "Critical", "Warning", "Total time", "Longest"],
            Rows = incidents.GroupBy(i => i.Category)
                .OrderByDescending(group => group.Count())
                .Select(group => new List<string>
                {
                    group.Key,
                    group.Count().ToString(),
                    group.Count(i => i.Row.Severity == "Critical").ToString(),
                    group.Count(i => i.Row.Severity == "Warning").ToString(),
                    Fmt.Duration(TimeSpan.FromSeconds(group.Sum(i => i.Duration.TotalSeconds))),
                    Fmt.Duration(group.Max(i => i.Duration))
                })
                .ToList()
        };

        var frequentGroups = incidents.GroupBy(i => i.Row.Title)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Sum(i => i.Duration.TotalSeconds))
            .ToList();
        var frequent = new ReportSection
        {
            Title = "Most frequent",
            Columns = ["Problem", "Category", "Times", "Total time"],
            Rows = frequentGroups.Select(group => new List<string>
            {
                group.Key,
                group.First().Category,
                group.Count().ToString(),
                Fmt.Duration(TimeSpan.FromSeconds(group.Sum(i => i.Duration.TotalSeconds)))
            }).ToList(),
            RowLinks = frequentGroups.Select(group => SubjectOf(group.First().Row.IssueId)).ToList()
        };

        var stillOpen = new ReportSection
        {
            Title = "Still open",
            Columns = ["Started", "Open for", "Severity", "Problem", "Details"],
            Rows = open.Select(i => new List<string>
            {
                Fmt.Local(i.Row.StartedUtc),
                Fmt.Duration(DateTime.UtcNow - i.Row.StartedUtc),
                i.Row.Severity,
                i.Row.Title,
                i.Row.Message
            }).ToList(),
            RowLinks = open.Select(i => SubjectOf(i.Row.IssueId)).ToList()
        };

        var ordered = incidents.OrderByDescending(i => i.Row.StartedUtc).ToList();
        var all = new ReportSection
        {
            Title = "All problems & warnings",
            Columns = ["Started", "Ended", "Duration", "Severity", "Category", "Problem", "Details"],
            Rows = ordered.Select(i => IncidentRow(i.Row, i.Category)).ToList(),
            RowLinks = ordered.Select(i => SubjectOf(i.Row.IssueId)).ToList()
        };

        return [overview, charts, byCategory, frequent, stillOpen, all];
    }

    private List<ReportSection> Availability(Period period)
    {
        var targets = AvailabilityTargets(period);
        var previousTargets = AvailabilityTargets(period.Previous).ToDictionary(t => $"{t.Subject}|{t.Name}", t => t.Stats);
        var samples = _store.GetSamples(period.FromUtc, period.ToUtc);
        var points = _store.GetPointSamples(period.FromUtc, period.ToUtc);

        double? Previous(Target t) => previousTargets.TryGetValue($"{t.Subject}|{t.Name}", out var s) && s.Known > 0 ? s.Percent : null;
        double? Now(Target t) => t.Stats.Known > 0 ? t.Stats.Percent : null;

        var measuredPoints = targets.Skip(2).Where(t => t.Stats.Known > 0).ToList();
        var previousPoints = targets.Skip(2).Select(Previous).Where(v => v is not null).ToList();
        var worst = measuredPoints.OrderBy(t => t.Stats.Percent).FirstOrDefault();
        var overview = new ReportSection
        {
            Title = "Overview",
            Note = Fmt.Coverage(samples.Count, period),
            Metrics =
            [
                Fmt.Compared("Network uptime", Now(targets[0]), Previous(targets[0]), Fmt.Percent, Now(targets[0]) is { } n ? Fmt.UptimeStatus(n) : null, higherIsBetter: true),
                Fmt.Compared("Internet uptime", Now(targets[1]), Previous(targets[1]), Fmt.Percent, Now(targets[1]) is { } i ? Fmt.UptimeStatus(i) : null, higherIsBetter: true),
                Fmt.Compared("Monitor points uptime (average)",
                    measuredPoints.Count == 0 ? null : measuredPoints.Average(t => t.Stats.Percent),
                    previousPoints.Count == 0 ? null : previousPoints.Average(), Fmt.Percent,
                    measuredPoints.Count == 0 ? null : Fmt.UptimeStatus(measuredPoints.Average(t => t.Stats.Percent)), higherIsBetter: true),
                new("Least available", worst is null ? "-" : $"{worst.Name} ({Fmt.Percent(worst.Stats.Percent)})",
                    worst is null ? null : Fmt.UptimeStatus(worst.Stats.Percent)),
                new("Internet disconnections", targets[1].Stats.Outages.Count.ToString(), targets[1].Stats.Outages.Count == 0 ? "Good" : "Warning"),
                new("Outages (all targets)", targets.Sum(t => t.Stats.Outages.Count).ToString(), targets.Any(t => t.Stats.Outages.Count > 0) ? "Warning" : "Good")
            ]
        };

        var measured = targets.Where(t => t.Stats.Known > 0).ToList();
        var uptime = new ReportSection
        {
            Title = "Uptime",
            Charts = new[]
            {
                Charts.Bar("Uptime by target", "%", measured.Select(t => t.Name).ToList(), 100, false,
                    [new ReportThreshold("99%", 99, "Warning"), new ReportThreshold("95%", 95, "Bad")],
                    ("Uptime", measured.Select(t => t.Stats.Percent).ToList())),
                Charts.Pie("Downtime by target", targets.Select(t => (t.Name, t.Stats.Downtime.TotalMinutes)), "minutes"),
                Charts.ByHour("Outages by hour of the day", "outages", 0, true, null,
                    ("Outages", targets.SelectMany(t => t.Stats.Outages).Select(o => (o.Start, 1d)), v => v.Sum()))
            }.OfType<ReportChart>().ToList()
        };

        var timeline = new ReportSection
        {
            Title = "Status over time",
            Note = "Green: working. Yellow: warning. Red: down. Gray: no reading.",
            Charts = new[]
            {
                Charts.Timeline("Status of every target", period,
                    new[]
                    {
                        ("Network", samples.Select(s => (s.Utc, State(s.Network)))),
                        ("Internet", samples.Select(s => (s.Utc, State(s.Internet))))
                    }.Concat(points.GroupBy(p => p.PointId).Select(g => (g.Last().Name, g.Select(p => (p.Utc, State(p.Status))))))),
                Charts.ByDay("Daily uptime", "%", 100, period, false,
                    ("Internet", samples.Where(s => s.Internet is not null).Select(s => (s.Utc, s.Internet!.Value ? 100d : 0d)), v => v.Average()),
                    ("Network", samples.Where(s => s.Network is not null).Select(s => (s.Utc, s.Network!.Value ? 100d : 0d)), v => v.Average()))
            }.OfType<ReportChart>().ToList()
        };

        var table = new ReportSection
        {
            Title = "Availability by target",
            Note = "Up means Healthy or Warning; Down means Critical. Minutes with no reading (service stopped, not checked yet) are not counted. Double-click a row for its details.",
            Columns = ["Target", "Type", "Uptime", "Outages", "Downtime", "Longest outage", "Time between failures", "Average repair time", "Last outage"],
            Rows = targets.Select(t => new List<string>
            {
                t.Name,
                t.Type,
                t.Stats.Known == 0 ? "No data" : Fmt.Percent(t.Stats.Percent),
                t.Stats.Outages.Count.ToString(),
                Fmt.Duration(t.Stats.Downtime),
                t.Stats.Outages.Count == 0 ? "-" : Fmt.Duration(t.Stats.Outages.Max(o => o.End - o.Start)),
                t.Stats.BetweenFailures is { } mtbf ? Fmt.Duration(mtbf) : "-",
                t.Stats.ToRepair is { } mttr ? Fmt.Duration(mttr) : "-",
                t.Stats.Outages.Count == 0 ? "-" : Fmt.Local(t.Stats.Outages[^1].Start)
            }).ToList(),
            RowLinks = targets.Select(t => t.Subject).ToList()
        };

        var outageRows = targets.SelectMany(t => t.Stats.Outages.Select(o => (Target: t, Outage: o)))
            .OrderByDescending(x => x.Outage.Start)
            .ToList();
        var outages = new ReportSection
        {
            Title = "Outages",
            Columns = ["Target", "Down from", "Back at", "Duration"],
            Rows = outageRows.Select(x => new List<string> { x.Target.Name, Fmt.Local(x.Outage.Start), Fmt.Local(x.Outage.End), Fmt.Duration(x.Outage.End - x.Outage.Start) }).ToList(),
            RowLinks = outageRows.Select(x => x.Target.Subject).ToList()
        };

        return [overview, uptime, timeline, table, outages];
    }

    private sealed record Target(string Name, string Type, string? Subject, UpTime Stats);

    private List<Target> AvailabilityTargets(Period period)
    {
        var samples = _store.GetSamples(period.FromUtc, period.ToUtc);
        var targets = new List<Target>
        {
            new("Network connection", "Network", ReportTypes.InternetSubject, UpTime.From(samples.Select(s => (s.Utc, s.Network)))),
            new("Internet", "Internet", ReportTypes.InternetSubject, UpTime.From(samples.Select(s => (s.Utc, s.Internet))))
        };
        targets.AddRange(_store.GetPointSamples(period.FromUtc, period.ToUtc).GroupBy(p => p.PointId).Select(group =>
            new Target(group.Last().Name, TypeLabel(group.Last().Type), $"{ReportTypes.PointSubjectPrefix}{group.Key}",
                UpTime.From(group.Select(p => (p.Utc, IsUp(p.Status)))))));
        return targets;
    }

    private List<ReportSection> SettingsChanges(Period period)
    {
        var changes = _store.GetSettingsChanges(period.FromUtc, period.ToUtc);
        return
        [
            new ReportSection
            {
                Title = "Overview",
                Metrics =
                [
                    new("Changes", changes.Count.ToString()),
                    new("Times saved", changes.Select(c => c.Utc).Distinct().Count().ToString()),
                    new("Last change", changes.Count == 0 ? "-" : Fmt.Local(changes[^1].Utc))
                ]
            },
            new ReportSection
            {
                Title = "At a glance",
                Charts = new[]
                {
                    Charts.Pie("Changes by area", changes.GroupBy(c => SettingsArea(c.Change)).Select(g => (g.Key, (double)g.Count()))),
                    Charts.ByHour("Changes by hour of the day", "changes", 0, true, null, ("Changes", changes.Select(c => (c.Utc, 1d)), v => v.Sum())),
                    Charts.ByDay("Changes per day", "changes", 0, period, false, ("Changes", changes.Select(c => (c.Utc, 1d)), v => v.Sum()))
                }.OfType<ReportChart>().ToList()
            },
            new ReportSection
            {
                Title = "All changes",
                Columns = ["Time", "Change"],
                Rows = changes.OrderByDescending(c => c.Utc).Select(c => new List<string> { Fmt.Local(c.Utc), c.Change }).ToList()
            }
        ];
    }

    private List<ReportSection> ApplicationChangesReport(Period period)
    {
        var changes = _store.GetApplicationChanges(period.FromUtc, period.ToUtc);
        return
        [
            new ReportSection
            {
                Title = "Overview",
                Metrics =
                [
                    new("Changes", changes.Count.ToString()),
                    new("Programs", changes.Count(c => c.Area == "Programs").ToString()),
                    new("Users", changes.Count(c => c.Area == "Users").ToString()),
                    new("Services", changes.Count(c => c.Area == "Services").ToString()),
                    new("Problems", changes.Count(c => c.Severity is "Critical" or "Warning").ToString()),
                    new("Last change", changes.Count == 0 ? "-" : Fmt.Local(changes[^1].Utc))
                ]
            },
            new ReportSection
            {
                Title = "At a glance",
                Charts = new[]
                {
                    Charts.Pie("Changes by area", changes.GroupBy(c => c.Area).Select(g => (g.Key, (double)g.Count()))),
                    Charts.ByHour("Changes by hour of the day", "changes", 0, true, null, ("Changes", changes.Select(c => (c.Utc, 1d)), v => v.Sum())),
                    Charts.ByDay("Changes per day", "changes", 0, period, false, ("Changes", changes.Select(c => (c.Utc, 1d)), v => v.Sum()))
                }.OfType<ReportChart>().ToList()
            },
            new ReportSection
            {
                Title = "All changes",
                Columns = ["Time", "Area", "Severity", "Change"],
                Rows = changes.OrderByDescending(c => c.Utc)
                    .Select(c => new List<string> { Fmt.Local(c.Utc), c.Area, c.Severity, c.Change }).ToList()
            }
        ];
    }

    private static string SettingsArea(string change)
    {
        if (change.StartsWith("Monitor point", StringComparison.OrdinalIgnoreCase))
        {
            return "Monitor points";
        }

        var separator = change.IndexOf(" > ", StringComparison.Ordinal);
        return separator > 0 ? change[..separator] : "General";
    }

    private async Task<List<ReportSection>> ComplianceAsync(Period period, CancellationToken cancellationToken)
    {
        var system = _services.GetRequiredService<ISystemInfoService>();
        var hardware = _services.GetRequiredService<IHardwareService>();
        var internet = _services.GetRequiredService<IInternetStatus>().GetState();
        var spec = _config.GetDeviceSpec();

        var cpuTask = system.GetCpuAsync(cancellationToken);
        var ramTask = system.GetRamAsync(cancellationToken);
        var partitionsTask = system.GetPartitionsAsync(cancellationToken);
        var osTask = hardware.GetOsAsync(cancellationToken);
        await Task.WhenAll(cpuTask, ramTask, partitionsTask, osTask);

        var lastTest = _store.GetSpeedTests(DateTime.UtcNow.AddDays(-ReportStore.KeepDays), DateTime.UtcNow.AddMinutes(1))
            .LastOrDefault(test => test.Error is null);
        var download = internet.DownloadMbps ?? lastTest?.DownloadMbps ?? 0;
        var upload = internet.UploadMbps ?? lastTest?.UploadMbps ?? 0;
        var partitions = partitionsTask.Result;
        var operatingSystem = $"{osTask.Result.Name} {osTask.Result.Version}".Trim();
        var result = DeviceSpecEvaluator.Evaluate(spec, new DeviceSpecReading(
            cpuTask.Result.LogicalCores,
            cpuTask.Result.UsagePercent,
            ramTask.Result.TotalGB,
            ramTask.Result.UsagePercent,
            partitions.Select(p => new DiskSlice(p.DriveLetter.TrimEnd('\\'), p.TotalGB, p.FreeGB, p.UsagePercent)).ToList(),
            internet.Connected ?? true,
            download,
            upload,
            operatingSystem));
        var totalDiskGb = partitions.Sum(p => p.TotalGB);

        var rows = new List<List<string>>
        {
            Requirement("CPU cores", spec.CpuMinCores > 0 ? $"At least {spec.CpuMinCores}" : null, cpuTask.Result.LogicalCores.ToString(), Pass(spec.CpuMinCores > 0 ? cpuTask.Result.LogicalCores >= spec.CpuMinCores : null)),
            Requirement("Memory (RAM)", spec.RamMinGb > 0 ? $"At least {spec.RamMinGb:0.#} GB" : null, $"{ramTask.Result.TotalGB:0.#} GB", Pass(spec.RamMinGb > 0 ? ramTask.Result.TotalGB + 0.05 >= spec.RamMinGb : null)),
            Requirement("Disk capacity", spec.DiskMinimum > 0 ? $"At least {spec.DiskMinimum:0.#} GB" : null, $"{totalDiskGb:0.#} GB", Pass(spec.DiskMinimum > 0 ? totalDiskGb + 0.05 >= spec.DiskMinimum : null)),
            Requirement("Operating system", string.IsNullOrWhiteSpace(spec.OperatingSystem) ? null : $"{spec.OperatingSystem.Trim()} or newer", operatingSystem, Pass(result.OsOk ? true : result.OsBad ? false : null)),
            Requirement("Download speed", spec.DownloadMinKbps > 0 ? $"At least {spec.DownloadMinKbps / 1000:0.##} Mbps" : null, download > 0 ? $"{download:0.##} Mbps" : "Not measured yet", Pass(download > 0 ? result.DownloadOk ? true : result.DownloadBad ? false : null : null)),
            Requirement("Upload speed", spec.UploadMinKbps > 0 ? $"At least {spec.UploadMinKbps / 1000:0.##} Mbps" : null, upload > 0 ? $"{upload:0.##} Mbps" : "Not measured yet", Pass(upload > 0 ? result.UploadOk ? true : result.UploadBad ? false : null : null)),
            Requirement("Internet speed", spec.EffectiveInternetMinKbps > 0 ? $"At least {spec.EffectiveInternetMinKbps / 1000:0.##} Mbps" : null, download > 0 ? $"{download:0.##} Mbps" : "Not measured yet", Pass(download > 0 ? result.InternetOk ? true : result.InternetBad ? false : null : null)),
            Requirement("CPU usage now", spec.CpuProblemPercent > 0 ? $"Below {spec.CpuProblemPercent}%" : null, Fmt.Percent(cpuTask.Result.UsagePercent), Pass(spec.CpuProblemPercent > 0 ? cpuTask.Result.UsagePercent < spec.CpuProblemPercent : null)),
            Requirement("RAM usage now", spec.RamProblemPercent > 0 ? $"Below {spec.RamProblemPercent}%" : null, Fmt.Percent(ramTask.Result.UsagePercent), Pass(spec.RamProblemPercent > 0 ? ramTask.Result.UsagePercent < spec.RamProblemPercent : null))
        };
        var links = new List<string?>
        {
            ReportTypes.CpuSubject, ReportTypes.RamSubject, null, null,
            ReportTypes.InternetSubject, ReportTypes.InternetSubject, ReportTypes.InternetSubject,
            ReportTypes.CpuSubject, ReportTypes.RamSubject
        };
        foreach (var partition in partitions)
        {
            var name = partition.DriveLetter.TrimEnd('\\');
            var full = result.Issues.FirstOrDefault(issue => issue.Id == $"spec:disk-part:Drive {name}");
            rows.Add(Requirement($"Free space on {name}", spec.DiskRemainingProblem > 0 ? $"More than {Threshold(spec.DiskRemainingProblem, spec.DiskRemainingProblemUnit)}" : null,
                $"{partition.FreeGB:0.#} GB ({100 - partition.UsagePercent:0}% free)",
                spec.DiskRemainingProblem > 0 ? full is null ? "Meets" : full.Severity == "Critical" ? "Does not meet" : "Warning" : "Not set"));
            links.Add($"{ReportTypes.DiskSubjectPrefix}{name}");
        }

        var checkedRows = rows.Where(row => row[3] is not "Not set" and not "Not measured").ToList();
        var failed = checkedRows.Count(row => row[3] == "Does not meet");
        var samples = _store.GetSamples(period.FromUtc, period.ToUtc);
        var overview = new ReportSection
        {
            Title = "Overview",
            Note = "Based on the device's current state; the last two figures cover the whole period.",
            Metrics =
            [
                new("Result", failed == 0 ? "Meets the specifications" : "Does not meet the specifications", failed == 0 ? "Good" : "Bad"),
                new("Requirements met", $"{checkedRows.Count - failed} of {checkedRows.Count}", failed == 0 ? "Good" : "Bad"),
                new($"Time CPU was at or above {spec.CpuProblemPercent}%", Fmt.Minutes(samples.Count(s => spec.CpuProblemPercent > 0 && s.Cpu >= spec.CpuProblemPercent))),
                new($"Time RAM was at or above {spec.RamProblemPercent}%", Fmt.Minutes(samples.Count(s => spec.RamProblemPercent > 0 && s.Ram >= spec.RamProblemPercent)))
            ]
        };

        return
        [
            overview,
            new ReportSection
            {
                Title = "At a glance",
                Charts = new[]
                {
                    Charts.Pie("Requirements", rows.GroupBy(r => r[3]).Select(g => (g.Key, (double)g.Count()))),
                    Charts.ByDay("Time above the problem level per day", "minutes", 0, period, true,
                        ("CPU", samples.Where(s => spec.CpuProblemPercent > 0 && s.Cpu >= spec.CpuProblemPercent).Select(s => (s.Utc, Fmt.Interval.TotalMinutes)), v => v.Sum()),
                        ("RAM", samples.Where(s => spec.RamProblemPercent > 0 && s.Ram >= spec.RamProblemPercent).Select(s => (s.Utc, Fmt.Interval.TotalMinutes)), v => v.Sum()))
                }.OfType<ReportChart>().ToList()
            },
            new ReportSection { Title = "Requirements", Columns = ["Requirement", "Required", "This device", "Result"], Rows = rows, RowLinks = links }
        ];
    }

    private async Task<List<ReportSection>> InventoryAsync(CancellationToken cancellationToken)
    {
        var hardware = _services.GetRequiredService<IHardwareService>();
        var network = _services.GetRequiredService<INetworkService>();
        var levelsTask = hardware.GetStaticLevelsAsync(cancellationToken);
        var networkTask = network.GetLevelAsync(cancellationToken);
        var configTask = _config.GetConfigAsync(cancellationToken);
        var pointsTask = MonitorPointStatusBuilder.BuildAsync(_services, cancellationToken);
        await Task.WhenAll(levelsTask, networkTask, configTask, pointsTask);
        var points = pointsTask.Result;

        var sections = new List<ReportSection>
        {
            new()
            {
                Title = "Overview",
                Metrics =
                [
                    new("Device name", _config.GetGeneral().DisplayName),
                    new("Windows name", Environment.MachineName),
                    new("Agent version", _identity.Version),
                    new("Agent ID", _identity.AgentId),
                    new("Monitor points", points.Count.ToString()),
                    new("Enabled monitor points", points.Count(p => p.Enabled).ToString())
                ]
            }
        };

        foreach (var level in levelsTask.Result.Levels.Append(networkTask.Result))
        {
            sections.Add(new ReportSection
            {
                Title = string.IsNullOrWhiteSpace(level.Title) ? $"Level {level.Level}" : level.Title,
                Columns = ["Item", "Value"],
                Rows = level.Items.Select(item => new List<string> { item.Name, item.Value }).ToList()
            });
        }

        sections.Add(new ReportSection
        {
            Title = "Monitor points",
            Note = "Double-click a row for its details.",
            Charts = new[]
            {
                Charts.Pie("Monitor points by type", points.GroupBy(p => MonitorPointText.Type(p.Type)).Select(g => (g.Key, (double)g.Count()))),
                Charts.Pie("Monitor points by status now", points.GroupBy(p => p.Enabled ? p.Status : "Disabled").Select(g => (g.Key, (double)g.Count())))
            }.OfType<ReportChart>().ToList(),
            Columns = ["Id", "Name", "Type", "Device kind", "Address / Target", "Location", "Model", "Enabled", "Shortcut", "Alert", "Check every", "Status", "Response", "Last check", "Result"],
            Rows = points.Select(PointRow).ToList(),
            RowLinks = points.Select(p => (string?)$"{ReportTypes.PointSubjectPrefix}{p.MonitorPointId}").ToList()
        });
        return sections;
    }

    private static List<string> PointRow(MonitorPointStatusDto point) =>
    [
        point.MonitorPointId,
        point.DisplayName,
        MonitorPointText.Type(point.Type),
        MonitorPointText.DeviceKind(point.DeviceKind),
        point.Target,
        point.Location,
        point.Model,
        point.Enabled ? "Yes" : "No",
        point.ShowInShortcut ? "Yes" : "No",
        MonitorPointText.Alert(point.Alert),
        $"{point.IntervalSeconds} s",
        point.Enabled ? point.Status : "Disabled",
        MonitorPointText.ResponseTime(point.ResponseMs),
        point.LastCheckedUtc is { } checkedUtc ? Fmt.Local(checkedUtc) : "-",
        point.Message ?? string.Empty
    ];

    private sealed record LoadedIncident(IncidentRow Row, string Category, TimeSpan Duration);

    private List<LoadedIncident> LoadIncidents(Period period) =>
        _store.GetIncidents(period.FromUtc, period.ToUtc)
            .Select(incident => new LoadedIncident(incident, Category(incident.IssueId), period.Clip(incident.StartedUtc, incident.EndedUtc ?? DateTime.UtcNow)))
            .ToList();

    private static List<string> IncidentRow(IncidentRow row, string category) =>
    [
        Fmt.Local(row.StartedUtc),
        row.EndedUtc is { } ended ? $"{Fmt.Local(ended)}{(row.EndedBy == "resolved" ? string.Empty : $" ({row.EndedBy})")}" : "Still open",
        Fmt.Duration((row.EndedUtc ?? DateTime.UtcNow) - row.StartedUtc),
        row.Severity,
        category,
        row.Title,
        row.Message
    ];

    private static List<string> Requirement(string name, string? required, string actual, string result) =>
        [name, required ?? "Not set", actual, required is null ? "Not set" : result];

    private static string Pass(bool? meets) => meets switch
    {
        true => "Meets",
        false => "Does not meet",
        null => "Not measured"
    };

    private static string Threshold(double value, string? unit) =>
        unit is "GB" or "Gigabytes" ? $"{value:0.#} GB" : $"{value:0}%";

    /// <summary>2 working, 1 warning, 0 down, -1 unknown.</summary>
    private static int State(string status)
    {
        if (status.Contains("Critical", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Offline", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Down", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return status.Contains("Warning", StringComparison.OrdinalIgnoreCase) ? 1
            : status.Contains("Healthy", StringComparison.OrdinalIgnoreCase) ? 2
            : -1;
    }

    private static int State(bool? up) => up switch { true => 2, false => 0, null => -1 };

    private static bool? IsUp(string status) => State(status) switch
    {
        0 => false,
        1 or 2 => true,
        _ => null
    };

    private static string TypeLabel(string type) =>
        Enum.TryParse<MonitorPointType>(type, out var parsed)
            ? parsed == MonitorPointType.Madkhal ? "Madkhal server" : MonitorPointText.Type(parsed)
            : type;

    /// <summary>The details subject a problem belongs to, or null.</summary>
    private static string? SubjectOf(string issueId)
    {
        var category = Category(issueId);
        var colon = issueId.IndexOf(':');
        return category switch
        {
            "CPU" => ReportTypes.CpuSubject,
            "RAM" => ReportTypes.RamSubject,
            "Network" or "Internet" => ReportTypes.InternetSubject,
            "Website" or "Device" or "Application" when colon > 0 => $"{ReportTypes.PointSubjectPrefix}{issueId[(colon + 1)..]}",
            "Database" when issueId.StartsWith("database:", StringComparison.Ordinal) => $"{ReportTypes.PointSubjectPrefix}{issueId[(colon + 1)..]}",
            _ => null
        };
    }

    private static string Category(string id) => id switch
    {
        _ when id.StartsWith("network:", StringComparison.Ordinal) => "Network",
        _ when id.StartsWith("internet:", StringComparison.Ordinal) || id is "spec:internet" or "spec:download" or "spec:upload" => "Internet",
        _ when id.StartsWith("spec:cpu", StringComparison.Ordinal) || id == "cpu" => "CPU",
        _ when id.StartsWith("spec:ram", StringComparison.Ordinal) || id == "ram" => "RAM",
        _ when id.StartsWith("spec:disk", StringComparison.Ordinal) || id.StartsWith("disk", StringComparison.Ordinal) => "Disk",
        _ when id.StartsWith("hw:", StringComparison.Ordinal) => "Hardware & OS",
        _ when id.StartsWith("sensor:", StringComparison.Ordinal) => "Temperature",
        _ when id.StartsWith("website:", StringComparison.Ordinal) => "Website",
        _ when id.StartsWith("database", StringComparison.Ordinal) => "Database",
        _ when id.StartsWith("device:", StringComparison.Ordinal) => "Device",
        _ when id.StartsWith("application:", StringComparison.Ordinal) => "Application",
        _ when id.StartsWith(ApplicationChanges.ServiceIssuePrefix, StringComparison.Ordinal) => "Service",
        "madkhal" => "Madkhal server",
        _ => "Other"
    };
}
