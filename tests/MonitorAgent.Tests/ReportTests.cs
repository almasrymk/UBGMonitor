using MonitorAgent.Service.Reports;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Models.Reports;
using MonitorAgent.Shared.Reports;
using Microsoft.Extensions.Logging.Abstractions;

namespace MonitorAgent.Tests;

public sealed class ReportTests
{
    private static ReportDto SampleReport() => new()
    {
        Type = ReportTypes.Health,
        Title = "Device Health",
        MachineName = "PC-1",
        GeneratedAt = new DateTime(2026, 9, 30, 10, 0, 0),
        From = new DateTime(2026, 9, 29, 10, 0, 0),
        To = new DateTime(2026, 9, 30, 10, 0, 0),
        Sections =
        [
            new ReportSection
            {
                Title = "Overview",
                Metrics = [new ReportMetric("CPU average", "40%", "Good")],
                Charts =
                [
                    new ReportChart
                    {
                        Title = "CPU over time",
                        Unit = "%",
                        Maximum = 100,
                        Labels = ["09-29 10:00", "09-29 11:00", "09-29 12:00"],
                        Series = [new ReportSeries("CPU", [10, 50, 30])],
                        Thresholds = [new ReportThreshold("Warning 80%", 80, "Warning")]
                    },
                    new ReportChart
                    {
                        Kind = ReportChartKinds.Bar, Title = "By hour", Half = true, Unit = "%",
                        Labels = ["10", "11"], Series = [new ReportSeries("CPU", [20, 40])]
                    },
                    new ReportChart
                    {
                        Kind = ReportChartKinds.Pie, Title = "Share", Half = true,
                        Labels = ["Working", "Down"], Series = [new ReportSeries("Share", [90, 10])]
                    },
                    new ReportChart
                    {
                        Kind = ReportChartKinds.Timeline, Title = "Status",
                        Labels = ["10:00", "11:00", "12:00"], Series = [new ReportSeries("Web", [2, 0, -1])]
                    },
                    new ReportChart
                    {
                        Kind = ReportChartKinds.Heatmap, Title = "Heat", Unit = "%", Maximum = 100,
                        Labels = ["10", "11"], Series = [new ReportSeries("Mon", [30, -1])]
                    }
                ]
            },
            new ReportSection
            {
                Title = "Disk space",
                Columns = ["Drive", "Note"],
                Rows = [["C:", "Has, a comma and \"quotes\""], ["D:", "<b>not html</b>"]]
            }
        ]
    };

    [Fact]
    public void Html_contains_metrics_chart_and_escaped_table()
    {
        var html = ReportExporter.ToHtml(SampleReport());

        Assert.Contains("CPU average", html);
        Assert.Equal(5, html.Split("<svg").Length - 1);
        Assert.Contains("<polyline", html);
        Assert.Contains("stroke-dasharray", html);
        Assert.Contains("counter(pages)", html);
        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("Generated 2026-09-30 10:00", html);
        Assert.Contains("&lt;b&gt;not html&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>not html</b>", html);
    }

    [Fact]
    public void Csv_quotes_cells_with_commas_and_quotes()
    {
        var csv = ReportExporter.ToCsv(SampleReport());

        Assert.Contains("\"Has, a comma and \"\"quotes\"\"\"", csv);
        Assert.Contains("Drive,Note", csv);
    }

    [Fact]
    public void Incident_is_opened_once_keeps_worst_severity_and_closes()
    {
        var store = new ReportStore(NullLogger<ReportStore>.Instance);
        var id = $"test:{Guid.NewGuid():N}";
        var start = DateTime.UtcNow.AddMinutes(-10);

        store.OpenIncident(new AgentIssueDto { Id = id, Severity = "Critical", Title = "Down" }, "Test", start);
        store.OpenIncident(new AgentIssueDto { Id = id, Severity = "Warning", Title = "Slow" }, "Test", start.AddMinutes(2));
        store.CloseIncident(id, start.AddMinutes(5));

        var incident = Assert.Single(store.GetIncidents(start.AddMinutes(-1), DateTime.UtcNow), i => i.IssueId == id);
        Assert.Equal("Critical", incident.Severity);
        Assert.Equal("Slow", incident.Title);
        Assert.Equal("resolved", incident.EndedBy);
        Assert.Equal(5, (incident.EndedUtc!.Value - incident.StartedUtc).TotalMinutes, precision: 0);
    }
}
