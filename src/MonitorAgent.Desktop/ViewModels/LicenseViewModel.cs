using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

/// <summary>
/// The license screen shown over the results while there is no valid license, and the license card on the About screen.
/// The service does all the licensing; this only shows it and passes the key on.
/// </summary>
public sealed partial class LicenseViewModel : ObservableObject
{
    private readonly AgentApiClient _client;

    /// <summary>Raised when the service starts or stops needing a license, so the app can hide or reload its results.</summary>
    public event Action? RequiredChanged;

    [ObservableProperty] private string _stateText = "Checking the license...";
    [ObservableProperty] private string _health = "Unknown";
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _details = string.Empty;
    [ObservableProperty] private string _productKey = string.Empty;
    [ObservableProperty] private string _resultMessage = string.Empty;
    [ObservableProperty] private string _resultHealth = "Healthy";
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canEnterKey;
    [ObservableProperty] private bool _canRelease;

    /// <summary>True when the service answered and has no valid license; the results are hidden until it is activated.</summary>
    [ObservableProperty] private bool _isRequired;

    [ObservableProperty] private string _requiredTitle = "Activate MonitorAgent";

    public LicenseViewModel(AgentApiClient client)
    {
        _client = client;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var status = await _client.GetLicenseAsync();
        Apply(status, status is null ? "The Agent service is not answering, so the license could not be read." : null);
    }

    partial void OnIsRequiredChanged(bool value) => RequiredChanged?.Invoke();

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (!_client.CanAdminister) { ShowResult(false, "A MonitorAgent Administrator must activate the license."); return; }
        if (string.IsNullOrWhiteSpace(ProductKey))
        {
            ShowResult(false, "Enter the product key first.");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _client.ActivateLicenseAsync(ProductKey.Trim());
            if (result.Success)
            {
                ProductKey = string.Empty;
            }

            Apply(result.Status.DeviceId.Length > 0 ? result.Status : await _client.GetLicenseAsync(), null);
            ShowResult(result.Success, result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReleaseAsync()
    {
        if (!_client.CanAdminister) { ShowResult(false, "A MonitorAgent Administrator must release the license."); return; }
        IsBusy = true;
        try
        {
            var result = await _client.DeactivateLicenseAsync();
            Apply(result.Status.DeviceId.Length > 0 ? result.Status : await _client.GetLicenseAsync(), null);
            ShowResult(result.Success, result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (!_client.CanAdminister) { ShowResult(false, "A MonitorAgent Administrator must request a license refresh."); return; }
        IsBusy = true;
        try
        {
            var status = await _client.RefreshLicenseAsync();
            Apply(status, status is null ? "The Agent service is not answering." : null);
            HasResult = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(LicenseStatusDto? status, string? failure)
    {
        if (status is null)
        {
            StateText = "License: unknown";
            Health = "Unknown";
            Message = failure ?? string.Empty;
            Details = string.Empty;
            CanEnterKey = false;
            CanRelease = false;
            IsRequired = false;
            return;
        }

        StateText = status.State switch
        {
            LicenseState.NotActivated => "Not activated",
            LicenseState.Active => "Licensed",
            LicenseState.Offline => status.IsValid ? "Licensed (offline)" : "Offline too long",
            LicenseState.Expired => "License expired",
            LicenseState.Suspended => "License suspended",
            LicenseState.Revoked => "License revoked",
            _ => "License not valid"
        };
        Health = status.State switch
        {
            LicenseState.Active => "Healthy",
            LicenseState.Offline when status.IsValid => "Warning",
            _ => "Critical"
        };
        RequiredTitle = status.State switch
        {
            LicenseState.Expired => "The license has expired",
            LicenseState.Suspended => "The license is suspended",
            LicenseState.Revoked => "The license was revoked",
            LicenseState.Offline => "The license needs to be checked online",
            LicenseState.NotActivated => "Activate MonitorAgent",
            _ => "The license is not valid"
        };
        Message = status.Message;
        Details = string.Join("   ·   ", DetailParts(status));
        var hasActivation = status.KeyPrefix is not null && status.State is not (LicenseState.NotActivated or LicenseState.Revoked);
        CanRelease = _client.CanAdminister && hasActivation;
        CanEnterKey = _client.CanAdminister && !(status.IsValid && hasActivation);
        IsRequired = !status.IsValid;
    }

    private static IEnumerable<string> DetailParts(LicenseStatusDto status)
    {
        if (status.LicenseNumber is { } number)
        {
            yield return $"License {number}";
        }

        if (status.KeyPrefix is { } prefix)
        {
            yield return $"Key {prefix}";
        }

        if (status.ExpiresAtUtc is { } expires)
        {
            yield return $"Ends {expires.ToLocalTime():yyyy-MM-dd}";
        }
        else if (status.IsValid)
        {
            yield return "No end date";
        }

        if (status.LastOnlineUtc is { } online)
        {
            yield return $"Last checked {online.ToLocalTime():yyyy-MM-dd HH:mm}";
        }

        if (status.DeviceId.Length > 0)
        {
            yield return $"Device {status.DeviceId}";
        }
    }

    private void ShowResult(bool success, string message)
    {
        ResultHealth = success ? "Healthy" : "Critical";
        ResultMessage = message;
        HasResult = message.Length > 0;
    }
}
