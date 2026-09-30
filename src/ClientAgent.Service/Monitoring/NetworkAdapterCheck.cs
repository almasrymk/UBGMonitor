using System.Management;
using System.Net;
using System.Net.NetworkInformation;

namespace ClientAgent.Service.Monitoring;

/// <param name="Connected">At least one adapter is up with a gateway, so the device can reach its network.</param>
/// <param name="Problems">Why each adapter is not connected (cable unplugged, Wi-Fi off, disabled...).</param>
/// <param name="DriverProblems">Network adapters with a driver problem, or network devices Windows could not identify.</param>
public sealed record NetworkAdapterReport(bool Connected, IReadOnlyList<string> Problems, IReadOnlyList<string> DriverProblems);

/// <summary>Checks the device's own network adapters: cable, Wi-Fi, disabled adapters and drivers.</summary>
public static class NetworkAdapterCheck
{
    private static readonly string[] NotReal =
    [
        "Bluetooth", "Miniport", "VPN", "TAP-", "WireGuard", "Tunnel", "Loopback", "Virtual", "Hyper-V", "VMware", "VirtualBox", "Npcap"
    ];

    private static readonly string[] NetworkWords = ["Network", "Ethernet", "Wi-Fi", "WiFi", "Wireless", "WLAN", "802.11", "LAN"];

    public static NetworkAdapterReport Check()
    {
        var connected = HasGatewayConnection();
        var problems = new List<string>();
        var driverProblems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var adapters = new ManagementObjectSearcher(
                "SELECT Name, NetConnectionID, NetConnectionStatus, NetEnabled, ConfigManagerErrorCode FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE");
            foreach (var adapter in adapters.Get().Cast<ManagementObject>())
            {
                using (adapter)
                {
                    var name = adapter["Name"] as string ?? "Network adapter";
                    if (NotReal.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    seen.Add(name);
                    var connection = adapter["NetConnectionID"] as string;
                    var label = string.IsNullOrWhiteSpace(connection) ? name : $"{connection} ({name})";
                    var error = adapter["ConfigManagerErrorCode"] is null ? 0u : Convert.ToUInt32(adapter["ConfigManagerErrorCode"]);
                    ushort? status = adapter["NetConnectionStatus"] is null ? null : Convert.ToUInt16(adapter["NetConnectionStatus"]);
                    var enabled = adapter["NetEnabled"] as bool?;

                    if (error == 22)
                    {
                        problems.Add($"{label}: disabled");
                    }
                    else if (error != 0)
                    {
                        driverProblems.Add($"{label}: {DriverError(error)}");
                    }
                    else if (status is 2 or 9)
                    {
                        if (!connected)
                        {
                            problems.Add($"{label}: connected but has no gateway (check the router or the DHCP settings)");
                        }
                    }
                    else
                    {
                        problems.Add($"{label}: {StatusText(status, enabled, IsWireless(name, connection))}");
                    }
                }
            }

            using var devices = new ManagementObjectSearcher(
                "SELECT Name, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            foreach (var device in devices.Get().Cast<ManagementObject>())
            {
                using (device)
                {
                    var name = device["Name"] as string ?? "Unknown network device";
                    var pnpClass = device["PNPClass"] as string;
                    var looksLikeNetwork = string.Equals(pnpClass, "Net", StringComparison.OrdinalIgnoreCase)
                        || (string.IsNullOrEmpty(pnpClass) && NetworkWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
                    if (!looksLikeNetwork || seen.Contains(name) || NotReal.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var error = Convert.ToUInt32(device["ConfigManagerErrorCode"]);
                    if (error == 22)
                    {
                        problems.Add($"{name}: disabled");
                    }
                    else
                    {
                        driverProblems.Add($"{name}: {DriverError(error)}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Without WMI the gateway check alone decides; the reasons are just not available.
        }

        if (!connected && problems.Count == 0 && driverProblems.Count == 0)
        {
            problems.Add("No network adapter was found on this device");
        }

        return new NetworkAdapterReport(connected, problems, driverProblems);
    }

    private static bool HasGatewayConnection()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(nic =>
                nic.OperationalStatus == OperationalStatus.Up
                && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                && nic.GetIPProperties().GatewayAddresses.Any(gateway =>
                    !gateway.Address.Equals(IPAddress.Any) && !gateway.Address.Equals(IPAddress.IPv6Any)));
        }
        catch (NetworkInformationException)
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
    }

    private static bool IsWireless(string name, string? connection) =>
        new[] { "Wi-Fi", "WiFi", "Wireless", "WLAN", "802.11" }.Any(word =>
            name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || (connection?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false));

    private static string StatusText(ushort? status, bool? enabled, bool wireless) => status switch
    {
        _ when enabled == false => "disabled",
        1 => "still connecting",
        4 => "hardware not present",
        5 => "disabled",
        6 => "hardware malfunction",
        7 => wireless ? "not connected to a Wi-Fi network (Wi-Fi may be turned off)" : "network cable unplugged",
        8 => "still authenticating",
        10 => "authentication failed",
        11 => "no valid IP address (check the router or the DHCP settings)",
        12 => "network credentials required",
        _ => wireless ? "not connected to a Wi-Fi network" : "not connected"
    };

    private static string DriverError(uint code) => code switch
    {
        28 => "driver is not installed (the device is not recognized)",
        10 => "the device cannot start (driver problem)",
        31 => "the driver is not working",
        43 => "the device reported a problem and was stopped",
        _ => $"driver problem (Device Manager error code {code})"
    };
}
