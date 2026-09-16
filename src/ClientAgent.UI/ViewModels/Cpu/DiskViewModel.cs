using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Converters;

namespace ClientAgent.UI.ViewModels;

public sealed partial class DiskViewModel : ObservableObject
{
    public ObservableCollection<DiskRowViewModel> Partitions { get; } = [];
    public ObservableCollection<PhysicalDiskRowViewModel> PhysicalDisks { get; } = [];

    [ObservableProperty] private DiskRowViewModel? _selectedPartition;

    public void Update(IEnumerable<DiskPartition> partitions, IEnumerable<PhysicalDisk> disks)
    {
        var selectedDrive = SelectedPartition?.Drive;
        Partitions.Clear();
        foreach (var p in partitions)
        {
            Partitions.Add(new DiskRowViewModel
            {
                Drive = NormalizeDrive(p.DriveLetter),
                Label = string.IsNullOrWhiteSpace(p.Label) ? "-" : p.Label,
                FileSystem = p.FileSystem,
                Total = FormatGb(p.TotalGB),
                Used = FormatGb(p.UsedGB),
                Free = FormatGb(p.FreeGB),
                UsagePercent = p.UsagePercent,
                Status = DiskStatusHelper.FromUsage(p.UsagePercent)
            });
        }

        if (!string.IsNullOrWhiteSpace(selectedDrive))
        {
            SelectedPartition = Partitions.FirstOrDefault(row =>
                string.Equals(row.Drive, selectedDrive, StringComparison.OrdinalIgnoreCase));
        }

        PhysicalDisks.Clear();
        foreach (var d in disks)
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
