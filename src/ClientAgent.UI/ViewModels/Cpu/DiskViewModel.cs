using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Converters;

namespace ClientAgent.UI.ViewModels;

public sealed partial class DiskViewModel : ObservableObject
{
    public ObservableCollection<DiskRowViewModel> Partitions { get; } = [];
    public ObservableCollection<PhysicalDiskRowViewModel> PhysicalDisks { get; } = [];

    public void Update(IEnumerable<DiskPartition> partitions, IEnumerable<PhysicalDisk> disks)
    {
        Partitions.Clear();
        foreach (var p in partitions)
        {
            Partitions.Add(new DiskRowViewModel
            {
                Drive = p.DriveLetter.TrimEnd('\\'),
                Label = string.IsNullOrWhiteSpace(p.Label) ? "-" : p.Label,
                FileSystem = p.FileSystem,
                Total = FormatGb(p.TotalGB),
                Used = FormatGb(p.UsedGB),
                Free = FormatGb(p.FreeGB),
                UsagePercent = p.UsagePercent,
                Status = DiskStatusHelper.FromUsage(p.UsagePercent)
            });
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

    private static string FormatGb(double gb)
        => gb >= 1024 ? $"{gb / 1024:0.0} TB" : $"{gb:0} GB";
}
