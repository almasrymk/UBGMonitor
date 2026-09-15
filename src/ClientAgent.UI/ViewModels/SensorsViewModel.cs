using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Enums;
using ClientAgent.UI.Services;

namespace ClientAgent.UI.ViewModels;

public sealed partial class SensorsViewModel : ObservableObject, IDisposable
{
    private readonly AgentApiClient _client;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<InfoRowViewModel> Rows { get; } =
    [
        new("CPU Temp", SensorHealth.Critical),
        new("GPU Temp", SensorHealth.Critical),
        new("Motherboard Temp", SensorHealth.Critical),
        new("CPU Fan", SensorHealth.Critical),
        new("GPU Fan", SensorHealth.Critical),
        new("CPU Voltage", SensorHealth.Critical),
        new("+12V Rail", SensorHealth.Critical),
        new("+5V Rail", SensorHealth.Critical),
        new("+3.3V Rail", SensorHealth.Critical),
        new("Power Draw", SensorHealth.Critical),
        new("Battery Level", SensorHealth.Critical),
        new("Battery Health", SensorHealth.Critical)
    ];

    public SensorsViewModel(AgentApiClient client)
    {
        _client = client;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _ = RefreshAsync();
            _timer.Start();
            return;
        }

        _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        if (!IsExpanded)
        {
            return;
        }

        try
        {
            var sensors = await _client.GetSensorsAsync();
            Apply(sensors ?? new SensorsInfo());
        }
        catch
        {
            Apply(new SensorsInfo());
        }
    }

    private void Apply(SensorsInfo sensors)
    {
        Rows[0].Set(FormatTemp(sensors.CpuTempC), TempHealth(sensors.CpuTempC));
        Rows[1].Set(FormatTemp(sensors.GpuTempC), TempHealth(sensors.GpuTempC));
        Rows[2].Set(FormatTemp(sensors.MotherboardTempC), TempHealth(sensors.MotherboardTempC));
        Rows[3].Set(FormatFan(sensors.CpuFanRpm), FanHealth(sensors.CpuFanRpm));
        Rows[4].Set(FormatFan(sensors.GpuFanRpm), FanHealth(sensors.GpuFanRpm));
        Rows[5].Set(FormatVolt(sensors.CpuVoltage), VoltageHealth(sensors.CpuVoltage, 1.2));
        Rows[6].Set(FormatVolt(sensors.Rail12V), VoltageHealth(sensors.Rail12V, 12));
        Rows[7].Set(FormatVolt(sensors.Rail5V), VoltageHealth(sensors.Rail5V, 5));
        Rows[8].Set(FormatVolt(sensors.Rail33V), VoltageHealth(sensors.Rail33V, 3.3));
        Rows[9].Set(FormatPower(sensors.PowerDrawW), PowerHealth(sensors.PowerDrawW));
        Rows[10].Set(FormatPercent(sensors.BatteryLevelPercent), BatteryLevelHealth(sensors.BatteryLevelPercent));
        Rows[11].Set(FormatPercent(sensors.BatteryHealthPercent), BatteryHealthHealth(sensors.BatteryHealthPercent));
    }

    private static string FormatTemp(double? value) => value is null ? "-" : $"{value:0}°C";

    private static string FormatFan(double? value) => value is null ? "-" : $"{value:0} RPM";

    private static string FormatVolt(double? value) => value is null ? "-" : $"{value:0.00} V";

    private static string FormatPercent(double? value) => value is null ? "-" : $"{value:0}%";

    private static string FormatPower(double? value) => value is null ? "-" : $"{value:0} W";

    private static SensorHealth TempHealth(double? c)
    {
        if (c is null)
        {
            return SensorHealth.Critical;
        }

        if (c < 70)
        {
            return SensorHealth.Ok;
        }

        return c <= 85 ? SensorHealth.Warning : SensorHealth.Critical;
    }

    private static SensorHealth FanHealth(double? rpm)
    {
        if (rpm is null)
        {
            return SensorHealth.Critical;
        }

        return rpm > 0 ? SensorHealth.Ok : SensorHealth.Warning;
    }

    private static SensorHealth VoltageHealth(double? value, double nominal)
    {
        if (value is null || nominal <= 0)
        {
            return SensorHealth.Critical;
        }

        var delta = Math.Abs(value.Value - nominal) / nominal * 100;
        if (delta <= 5)
        {
            return SensorHealth.Ok;
        }

        return delta <= 10 ? SensorHealth.Warning : SensorHealth.Critical;
    }

    private static SensorHealth PowerHealth(double? watts)
    {
        if (watts is null)
        {
            return SensorHealth.Critical;
        }

        return watts > 0 ? SensorHealth.Ok : SensorHealth.Warning;
    }

    private static SensorHealth BatteryLevelHealth(double? percent)
    {
        if (percent is null)
        {
            return SensorHealth.Critical;
        }

        if (percent > 50)
        {
            return SensorHealth.Ok;
        }

        return percent >= 20 ? SensorHealth.Warning : SensorHealth.Critical;
    }

    private static SensorHealth BatteryHealthHealth(double? percent)
    {
        if (percent is null)
        {
            return SensorHealth.Critical;
        }

        if (percent > 80)
        {
            return SensorHealth.Ok;
        }

        return percent >= 50 ? SensorHealth.Warning : SensorHealth.Critical;
    }

    public void Dispose() => _timer.Stop();
}
