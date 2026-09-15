using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.UI.Enums;
using ClientAgent.UI.Models;
using ClientAgent.UI.Services;

namespace ClientAgent.UI.ViewModels;

public sealed partial class ProcessListCardViewModel : ObservableObject, IDisposable
{
    private readonly AgentApiClient _client;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private ProcessSortBy _sortBy;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private Brush _accentColor = Brushes.Gray;

    public ObservableCollection<ProcessItem> Items { get; } = [];

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ProcessListCardViewModel(
        AgentApiClient client,
        string title,
        ProcessSortBy sortBy,
        Brush accentColor,
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

            Items.Clear();
            if (result.Error is not null)
            {
                ErrorMessage = result.Error;
                OnPropertyChanged(nameof(HasError));
                return;
            }

            ErrorMessage = null;
            foreach (var item in result.Items.Take(5))
            {
                Items.Add(item);
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

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await RefreshAsync();
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
