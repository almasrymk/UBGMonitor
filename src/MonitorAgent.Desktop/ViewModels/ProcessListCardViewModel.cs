using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorAgent.UI.Enums;
using MonitorAgent.UI.Models;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

public sealed partial class ProcessListCardViewModel : ObservableObject, IDisposable
{
    private readonly AgentApiClient _client;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    /// <summary>Hidden dashboard cards keep their last result without polling or rebuilding rows.</summary>
    public Func<bool>? IsActive { get; set; }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private ProcessSortBy _sortBy;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    /// <summary>The UI framework's brush.</summary>
    [ObservableProperty] private object? _accentColor;

    public ObservableCollection<ProcessItem> Items { get; } = [];

    [ObservableProperty] private ProcessItem? _selectedItem;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ProcessListCardViewModel(
        AgentApiClient client,
        string title,
        ProcessSortBy sortBy,
        object accentColor,
        bool autoStart = true)
    {
        _client = client;
        Title = title;
        SortBy = sortBy;
        AccentColor = accentColor;
        if (autoStart)
        {
            _ = RunAsync();
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (Items.Count == 0)
        {
            IsLoading = true;
        }

        try
        {
            var result = await _client.GetTopProcessesAsync(SortBy);
            if (_disposed)
            {
                return;
            }

            var selectedPid = SelectedItem?.Pid;
            if (result.Error is not null)
            {
                Items.Clear();
                SelectedItem = null;
                ErrorMessage = result.Error;
                OnPropertyChanged(nameof(HasError));
                return;
            }

            ErrorMessage = null;
            var incoming = result.Items.Take(5).ToArray();
            for (var i = Items.Count - 1; i >= 0; i--)
                if (!incoming.Any(item => item.Pid == Items[i].Pid)) Items.RemoveAt(i);
            for (var i = 0; i < incoming.Length; i++)
            {
                var item = incoming[i];
                var existing = Items.FirstOrDefault(row => row.Pid == item.Pid);
                if (existing is null) Items.Insert(i, item);
                else
                {
                    existing.UpdateFrom(item);
                    var at = Items.IndexOf(existing);
                    if (at != i) Items.Move(at, i);
                }
            }

            if (selectedPid is int pid)
            {
                SelectedItem = Items.FirstOrDefault(item => item.Pid == pid);
            }

            OnPropertyChanged(nameof(HasError));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Top5] {Title}{Environment.NewLine}{ex}");
            if (_disposed)
            {
                return;
            }

            Items.Clear();
            SelectedItem = null;
            ErrorMessage = $"Error: {ex.Message}";
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    [RelayCommand]
    private void OpenSelectedProcess()
    {
        if (!_client.IsLocalTransport) return;
        if (SelectedItem is null)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(SelectedItem.Pid);
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return;
            }

            ShellOpen.Reveal(path);
        }
        catch
        {
            // Some system processes cannot expose a path to open.
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (IsActive?.Invoke() != false)
                {
                    await RefreshAsync();
                }
                var delay = HasError ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(3);
                await Task.Delay(delay, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Timer stopped.
        }
    }
}
