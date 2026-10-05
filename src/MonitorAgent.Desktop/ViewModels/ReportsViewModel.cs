using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorAgent.Shared.Models.Reports;
using MonitorAgent.Shared.Reports;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject
{
    private const string CustomPeriod = "Custom";

    private readonly AgentApiClient _client;
    private int _request;
    private bool _suspendRegenerate;

    [ObservableProperty] private ReportTypeInfo _selectedType = ReportTypes.All[0];
    [ObservableProperty] private ReportSubject? _selectedSubject;
    [ObservableProperty] private bool _isDetails;
    [ObservableProperty] private string _selectedPeriod = "Last 7 days";
    [ObservableProperty] private DateTime _customFrom = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime _customTo = DateTime.Today;
    [ObservableProperty] private bool _isCustomPeriod;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private string _statusMessage = "Choose a report and a period, then press Generate.";
    [ObservableProperty] private string _reportTitle = string.Empty;
    [ObservableProperty] private string _reportDescription = string.Empty;
    [ObservableProperty] private string _reportPeriod = string.Empty;

    private ReportDto? _report;

    public ReportsViewModel(AgentApiClient client)
    {
        _client = client;
    }

    /// <summary>Asks the main window to show the Reports tab (set by the main view model).</summary>
    public Action? ShowRequested { get; set; }

    public IReadOnlyList<ReportTypeInfo> Types => ReportTypes.All;

    public IReadOnlyList<string> Periods { get; } = ["Today", "Yesterday", "Last 7 days", "Last 30 days", "Last 90 days", CustomPeriod];

    public ObservableCollection<ReportSubject> Subjects { get; } = [];

    public ObservableCollection<ReportSectionViewModel> Sections { get; } = [];

    partial void OnSelectedTypeChanged(ReportTypeInfo value)
    {
        IsDetails = value.Id == ReportTypes.Details;
        // Monitor points and disks can be added or renamed while the app is open, so the list is asked for again.
        if (IsDetails && !_suspendRegenerate)
        {
            _ = LoadSubjectsAndGenerateAsync();
            return;
        }

        RegenerateIfShown();
    }

    partial void OnSelectedSubjectChanged(ReportSubject? value)
    {
        if (IsDetails)
        {
            RegenerateIfShown();
        }
    }

    partial void OnSelectedPeriodChanged(string value)
    {
        IsCustomPeriod = value == CustomPeriod;
        RegenerateIfShown();
    }

    partial void OnCustomFromChanged(DateTime value) => RegenerateIfShown();

    partial void OnCustomToChanged(DateTime value) => RegenerateIfShown();

    private void RegenerateIfShown()
    {
        if (!_suspendRegenerate && (HasReport || IsLoading))
        {
            _ = GenerateAsync();
        }
    }

    /// <summary>Opens the details report of a subject such as "cpu" or "point:web1" for the current period.</summary>
    public async Task OpenDetailsAsync(string subject)
    {
        ShowRequested?.Invoke();
        _suspendRegenerate = true;
        try
        {
            SelectedType = ReportTypes.All.First(type => type.Id == ReportTypes.Details);
            await LoadSubjectsAsync();

            var match = Subjects.FirstOrDefault(s => string.Equals(s.Id, subject, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                match = new ReportSubject(subject, subject, string.Empty);
                Subjects.Add(match);
            }

            SelectedSubject = match;
        }
        finally
        {
            _suspendRegenerate = false;
        }

        await GenerateAsync();
    }

    /// <summary>Empties the screen when the service stops: what it showed came from the service, and any report being built is dropped.</summary>
    public void Unload()
    {
        _request++;
        _suspendRegenerate = true;
        try
        {
            Subjects.Clear();
            SelectedSubject = null;
        }
        finally
        {
            _suspendRegenerate = false;
        }

        IsLoading = false;
        ClearReport();
        StatusMessage = "The Agent service is not running. Reports come back when it is.";
    }

    private void ClearReport()
    {
        _report = null;
        Sections.Clear();
        HasReport = false;
        ReportTitle = string.Empty;
        ReportDescription = string.Empty;
        ReportPeriod = string.Empty;
        ExportHtmlCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Called when the Reports tab is shown: the details list picks up monitor points added since it was loaded.</summary>
    public void RefreshSubjects()
    {
        if (IsDetails)
        {
            _ = LoadSubjectsAsync();
        }
    }

    private async Task LoadSubjectsAndGenerateAsync()
    {
        await LoadSubjectsAsync();
        if (HasReport || IsLoading)
        {
            await GenerateAsync();
        }
    }

    private async Task LoadSubjectsAsync()
    {
        var subjects = await _client.GetReportSubjectsAsync();
        if (subjects is null)
        {
            return;
        }

        var selected = SelectedSubject?.Id;
        _suspendRegenerate = true;
        try
        {
            Subjects.Clear();
            foreach (var subject in subjects)
            {
                Subjects.Add(subject);
            }

            SelectedSubject = Subjects.FirstOrDefault(s => s.Id == selected)
                ?? Subjects.FirstOrDefault(s => !ReportTypes.IsAllSubject(s.Id))
                ?? Subjects.FirstOrDefault();
        }
        finally
        {
            _suspendRegenerate = false;
        }
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        var (from, to) = GetPeriod();
        if (from >= to)
        {
            StatusMessage = "The start date must be before the end date.";
            return;
        }

        var request = ++_request;
        IsLoading = true;
        if (IsDetails && SelectedSubject is null)
        {
            await LoadSubjectsAsync();
            if (request != _request)
            {
                return;
            }
        }

        var subject = IsDetails ? SelectedSubject?.Id : null;
        StatusMessage = subject is null ? $"Building {SelectedType.Title}..." : $"Building {SelectedType.Title} of {SelectedSubject!.Title}...";
        var report = await _client.GetReportAsync(SelectedType.Id, from, to, subject);
        if (request != _request)
        {
            return;
        }

        IsLoading = false;
        if (report is null)
        {
            // The report on screen is for another choice or an older period, so it would be mistaken for this one.
            ClearReport();
            StatusMessage = "The report could not be built. Make sure the Agent service is running.";
            return;
        }

        Show(report);
        StatusMessage = $"Built at {report.GeneratedAt:yyyy-MM-dd HH:mm}.";
    }

    private void Show(ReportDto report)
    {
        _report = report;
        ReportTitle = report.Title;
        ReportDescription = report.Description;
        ReportPeriod = $"{report.MachineName}   |   {report.From:yyyy-MM-dd HH:mm}  to  {report.To:yyyy-MM-dd HH:mm}";
        Sections.Clear();
        foreach (var section in report.Sections)
        {
            Sections.Add(new ReportSectionViewModel(section, this));
        }

        HasReport = true;
        ExportHtmlCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
    }

    private (DateTime From, DateTime To) GetPeriod()
    {
        var now = DateTime.Now;
        var today = DateTime.Today;
        return SelectedPeriod switch
        {
            "Today" => (today, now),
            "Yesterday" => (today.AddDays(-1), today),
            "Last 30 days" => (now.AddDays(-30), now),
            "Last 90 days" => (now.AddDays(-90), now),
            CustomPeriod => (CustomFrom.Date, Min(CustomTo.Date.AddDays(1), now)),
            _ => (now.AddDays(-7), now)
        };
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private bool CanExport() => _report is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportHtml()
    {
        var report = _report!;
        var path = await AskPathAsync(report, "Web page", ".html");
        if (path is not null && Save(path, ReportExporter.ToHtml(report)))
        {
            ShellOpen.Open(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportCsv()
    {
        var report = _report!;
        var path = await AskPathAsync(report, "CSV for Excel", ".csv");
        if (path is not null)
        {
            Save(path, ReportExporter.ToCsv(report));
        }
    }

    private Task<string?> AskPathAsync(ReportDto report, string fileTypeName, string extension)
    {
        var subject = report.Subject is null ? string.Empty : $" - {SelectedSubject?.Title ?? report.Subject}";
        // Windows forbids the most characters; using its list keeps the name valid on every system.
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var name = string.Concat($"{report.Title}{subject} {report.From:yyyy-MM-dd} to {report.To:yyyy-MM-dd}"
            .Select(c => invalid.Contains(c) ? '-' : c));
        return UiPlatform.PickSaveFileAsync(fileTypeName, extension, name + extension);
    }

    private bool Save(string path, string content)
    {
        try
        {
            // The BOM lets Excel read the CSV as UTF-8.
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = $"Saved to {path}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not save the file: {ex.Message}";
            return false;
        }
    }
}

/// <summary>A table row: the cell texts, and the details subject it opens on double-click.</summary>
public sealed class ReportRow : List<string>
{
    public ReportRow(IEnumerable<string> cells, string? link) : base(cells)
    {
        Link = link;
    }

    public string? Link { get; }
}

public sealed class ReportSectionViewModel
{
    private readonly ReportsViewModel _owner;

    public ReportSectionViewModel(ReportSection section, ReportsViewModel owner)
    {
        _owner = owner;
        Title = section.Title;
        Note = section.Note;
        Metrics = section.Metrics;
        Columns = section.Columns;
        Rows = section.Rows
            .Select((row, i) => new ReportRow(row, section.RowLinks is { } links && i < links.Count ? links[i] : null))
            .ToList();
        HasLinks = Rows.Any(row => row.Link is not null);

        var charts = section.Charts.Where(ReportExporter.HasData).Select(chart => new ReportChartViewModel(chart)).ToList();
        for (var i = 0; i < charts.Count; i++)
        {
            if (charts[i].Half && i + 1 < charts.Count && charts[i + 1].Half)
            {
                ChartRows.Add([charts[i], charts[i + 1]]);
                i++;
            }
            else
            {
                ChartRows.Add([charts[i]]);
            }
        }
    }

    public string Title { get; }

    public string? Note { get; }

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public List<ReportMetric> Metrics { get; }

    public bool HasMetrics => Metrics.Count > 0;

    /// <summary>Charts one or two per row: two half-width charts in a row share it.</summary>
    public List<List<ReportChartViewModel>> ChartRows { get; } = [];

    public bool HasCharts => ChartRows.Count > 0;

    public List<string> Columns { get; }

    public List<ReportRow> Rows { get; }

    public bool HasTable => Columns.Count > 0 && Rows.Count > 0;

    public bool HasLinks { get; }

    public bool IsEmpty => !HasMetrics && !HasCharts && !HasTable && !HasNote;

    public void Open(ReportRow row)
    {
        if (row.Link is { } link)
        {
            _ = _owner.OpenDetailsAsync(link);
        }
    }
}

/// <param name="Brush">The UI framework's brush.</param>
public sealed record ChartLegendItem(string Name, object Brush, bool Dashed, bool Box);

public sealed class ReportChartViewModel
{
    public ReportChartViewModel(ReportChart chart)
    {
        Chart = chart;
        Title = chart.Title;
        Half = chart.Half;
        switch (chart.Kind)
        {
            case ReportChartKinds.Timeline:
                foreach (var state in new[] { 2d, 1d, 0d, -1d })
                {
                    Legend.Add(new ChartLegendItem(ReportPalette.StateName(state), BrushOf(ReportPalette.State(state)), false, true));
                }

                break;
            case ReportChartKinds.Heatmap:
                Legend.Add(new ChartLegendItem("Low", BrushOf(ReportPalette.Heat(0, 1)), false, true));
                Legend.Add(new ChartLegendItem("Middle", BrushOf(ReportPalette.Heat(0.5, 1)), false, true));
                Legend.Add(new ChartLegendItem(string.IsNullOrEmpty(chart.Unit) ? "High" : $"High ({chart.Unit})", BrushOf(ReportPalette.Heat(1, 1)), false, true));
                break;
            case ReportChartKinds.Pie:
                break;
            default:
                if (chart.Series.Count > 1 || chart.Thresholds.Count > 0)
                {
                    for (var s = 0; s < chart.Series.Count; s++)
                    {
                        Legend.Add(new ChartLegendItem(chart.Series[s].Name, BrushOf(ReportPalette.SeriesColor(s)), false, chart.Kind == ReportChartKinds.Bar));
                    }

                    foreach (var threshold in chart.Thresholds)
                    {
                        Legend.Add(new ChartLegendItem(threshold.Name, BrushOf(ReportPalette.Status(threshold.Status)), true, false));
                    }
                }

                break;
        }
    }

    public ReportChart Chart { get; }

    public string Title { get; }

    public bool Half { get; }

    public List<ChartLegendItem> Legend { get; } = [];

    private static object BrushOf(string hex) => UiTheme.FromHex(hex);
}
