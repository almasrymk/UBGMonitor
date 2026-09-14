using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.ViewModels;

public sealed partial class DiskViewModel : ObservableObject
{
    public ObservableCollection<DiskPartition> Partitions { get; } = [];

    public void Update(IEnumerable<DiskPartition> partitions)
    {
        Partitions.Clear();
        foreach (var partition in partitions)
        {
            Partitions.Add(partition);
        }
    }
}
