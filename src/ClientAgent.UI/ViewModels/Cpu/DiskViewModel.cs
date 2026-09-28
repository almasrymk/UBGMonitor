using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Converters;
using ClientAgent.UI.Services;

namespace ClientAgent.UI.ViewModels;

public sealed partial class DiskViewModel : ObservableObject
{
    public ObservableCollection<DiskRowViewModel> Partitions { get; } = [];
    public ObservableCollection<PhysicalDiskRowViewModel> PhysicalDisks { get; } = [];

    private const int GraphSeconds = 60;

    public ObservableCollection<double> History { get; } = [];
    public ObservableCollection<double> ActiveHistory { get; } = [];
    public ObservableCollection<double> ReadHistory { get; } = [];
    public ObservableCollection<double> WriteHistory { get; } = [];

    [ObservableProperty] private DiskRowViewModel? _selectedPartition;
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private double _totalGb;
    [ObservableProperty] private double _usedGb;
    [ObservableProperty] private double _freeGb;
    [ObservableProperty] private int _partitionCount;
    [ObservableProperty] private double _activeTimePercent;
    [ObservableProperty] private string _readSpeed = "-";
    [ObservableProperty] private string _writeSpeed = "-";
    [ObservableProperty] private string _responseTime = "-";
    [ObservableProperty] private string _queueLength = "-";
    [ObservableProperty] private bool _hasActivity;
    [ObservableProperty] private string _model = "-";
    [ObservableProperty] private string _diskType = "-";
    [ObservableProperty] private string _health = "-";
    [ObservableProperty] private string _temperature = "N/A";

    public void UpdateActivity(DiskActivity activity)
    {
        HasActivity = true;
        ActiveTimePercent = activity.ActiveTimePercent;
        ReadSpeed = FormatRate(activity.ReadBytesPerSecond);
        WriteSpeed = FormatRate(activity.WriteBytesPerSecond);
        ResponseTime = $"{activity.ResponseMs:0.0} ms";
        QueueLength = $"{activity.QueueLength:0}";
        PushGraph(ActiveHistory, activity.ActiveTimePercent);
        PushGraph(ReadHistory, activity.ReadBytesPerSecond / 1_048_576d);
        PushGraph(WriteHistory, activity.WriteBytesPerSecond / 1_048_576d);
    }

    private static void PushGraph(ObservableCollection<double> history, double value)
    {
        history.Add(value);
        while (history.Count > GraphSeconds)
        {
            history.RemoveAt(0);
        }
    }

    private static string FormatRate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_073_741_824d => $"{bytesPerSecond / 1_073_741_824d:0.0} GB/s",
        >= 1_048_576d => $"{bytesPerSecond / 1_048_576d:0.0} MB/s",
        _ => $"{bytesPerSecond / 1024d:0} KB/s"
    };

    public void Update(IEnumerable<DiskPartition> partitions, IEnumerable<PhysicalDisk> disks)
    {
        var selectedDrive = SelectedPartition?.Drive;
        var list = partitions.ToList();
        TotalGb = list.Sum(p => p.TotalGB);
        FreeGb = list.Sum(p => p.FreeGB);
        UsedGb = Math.Max(0, TotalGb - FreeGb);
        UsagePercent = TotalGb <= 0 ? 0 : UsedGb / TotalGb * 100d;
        PartitionCount = list.Count;
        CpuViewModel.Push(History, UsagePercent);
        Partitions.Clear();
        foreach (var p in list)
        {
            Partitions.Add(new DiskRowViewModel
            {
                Drive = NormalizeDrive(p.DriveLetter),
                Label = string.IsNullOrWhiteSpace(p.Label) ? "-" : p.Label,
                FileSystem = p.FileSystem,
                Total = FormatGb(p.TotalGB),
                Used = FormatGb(p.UsedGB),
                Free = FormatGb(p.FreeGB),
                TotalGb = p.TotalGB,
                FreeGb = p.FreeGB,
                UsagePercent = p.UsagePercent,
                Status = DiskStatusHelper.FromUsage(p.UsagePercent)
            });
        }

        if (!string.IsNullOrWhiteSpace(selectedDrive))
        {
            SelectedPartition = Partitions.FirstOrDefault(row =>
                string.Equals(row.Drive, selectedDrive, StringComparison.OrdinalIgnoreCase));
        }

        var physical = disks.ToList();
        if (physical.Count == 0)
        {
            return;
        }

        PhysicalDisks.Clear();
        foreach (var d in physical)
        {
            PhysicalDisks.Add(new PhysicalDiskRowViewModel
            {
                Model = d.Model,
                Type = $"{d.Type} / {d.Interface}",
                HealthPercent = d.HealthPercent ?? 100,
                Temperature = d.TemperatureC.HasValue ? $"{d.TemperatureC:0}°C" : "N/A",
                SmartStatus = d.SmartStatus
            });
        }

        var first = physical[0];
        Model = physical.Count > 1 ? $"{first.Model} (+{physical.Count - 1})" : first.Model;
        DiskType = $"{first.Type} / {first.Interface}";
        Health = first.HealthPercent.HasValue ? $"{first.HealthPercent:0}% ({first.SmartStatus})" : first.SmartStatus;
        Temperature = first.TemperatureC.HasValue ? $"{first.TemperatureC:0}°C" : "N/A";
    }

    [RelayCommand]
    private void OpenSelectedDrive() => OpenDrive(SelectedPartition);

    public void OpenDrive(DiskRowViewModel? row)
    {
        if (row is null || !TryGetDrivePath(row.Drive, out var path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening Explorer can fail if the drive was removed.
        }
    }

    private static string NormalizeDrive(string driveLetter)
        => string.IsNullOrWhiteSpace(driveLetter) ? string.Empty : driveLetter.Trim().TrimEnd('\\', '/');

    private static bool TryGetDrivePath(string drive, out string path)
    {
        path = string.Empty;
        var letter = NormalizeDrive(drive);
        if (letter.Length != 2 || letter[1] != ':' || !char.IsLetter(letter[0]))
        {
            return false;
        }

        path = char.ToUpperInvariant(letter[0]) + ":\\";
        return true;
    }

    private static string FormatGb(double gb)
        => gb >= 1024 ? $"{gb / 1024:0.0} TB" : $"{gb:0} GB";
}
