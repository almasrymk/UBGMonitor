using MonitorAgent.Service.Platform.Mac;

namespace MonitorAgent.Tests;

public sealed class MacParsersTests
{
    [Fact]
    public void VmStat_PagesAndPageSize()
    {
        const string text = """
            Mach Virtual Memory Statistics: (page size of 16384 bytes)
            Pages free:                               12345.
            Pages active:                            400000.
            Pages inactive:                          300000.
            Pages speculative:                         5000.
            Pages purgeable:                           1000.
            File-backed pages:                       250000.
            """;

        var vm = MacParsers.ParseVmStat(text);

        Assert.Equal(16384, vm.PageSize);
        Assert.Equal(12345, vm.Free);
        Assert.Equal(300000, vm.Inactive);
        Assert.Equal(5000, vm.Speculative);
        Assert.Equal(1000, vm.Purgeable);
        Assert.Equal(250000, vm.FileBacked);
    }

    [Fact]
    public void SwapUsage_Bytes()
    {
        var (total, free) = MacParsers.ParseSwapUsage("total = 2048.00M  used = 1024.50M  free = 1023.50M  (encrypted)");

        Assert.Equal(2048L * 1024 * 1024, total);
        Assert.Equal((long)(1023.5 * 1024 * 1024), free);
    }

    [Fact]
    public void Ioreg_SumsDisks()
    {
        const string text = """
            +-o IOBlockStorageDriver  <class IOBlockStorageDriver>
                {
                  "Statistics" = {"Operations (Write)"=20,"Latency Time (Write)"=0,"Bytes (Read)"=1000,"Bytes (Write)"=2000,"Operations (Read)"=10,"Total Time (Read)"=3000000,"Total Time (Write)"=7000000}
                }
            +-o IOBlockStorageDriver  <class IOBlockStorageDriver>
                {
                  "Statistics" = {"Operations (Write)"=0,"Bytes (Read)"=500,"Bytes (Write)"=0,"Operations (Read)"=5,"Total Time (Read)"=1000000,"Total Time (Write)"=0}
                }
            """;

        var stats = MacParsers.ParseIoregStatistics(text);

        Assert.Equal(1500, stats.BytesRead);
        Assert.Equal(2000, stats.BytesWritten);
        Assert.Equal(35, stats.Operations);
        Assert.Equal(11_000_000, stats.TotalTimeNs);

        var activity = MacDiskActivityReader.Compare(new BlockStats(0, 0, 0, 0), stats, 1000);
        Assert.Equal(1.1, activity.ActiveTimePercent, 2);
        Assert.Equal(1500, activity.ReadBytesPerSecond);
    }

    [Fact]
    public void Nettop_BytesPerProcess()
    {
        const string text = "time,,bytes_in,bytes_out,\n12:00:00.000000,Google Chrome H.812,1000,500,\n12:00:00.000000,mDNSResponder.190,20,30,\n";

        var bytes = MacParsers.ParseNettop(text);

        Assert.Equal(1500UL, bytes[812]);
        Assert.Equal(50UL, bytes[190]);
    }

    [Fact]
    public void Pmset_Battery()
    {
        const string text = "Now drawing from 'AC Power'\n -InternalBattery-0 (id=4653155)\t85%; charging; 1:20 remaining present: true\n";

        var battery = MacParsers.ParsePmsetBattery(text);

        Assert.NotNull(battery);
        Assert.Equal(85, battery!.Percent);
        Assert.Equal("Charging", battery.Status);
        Assert.True(battery.OnAc);
        Assert.Null(MacParsers.ParsePmsetBattery("Now drawing from 'AC Power'\n"));
    }

    [Fact]
    public void HardwarePorts_FindsWifi()
    {
        const string text = "Hardware Port: Ethernet\nDevice: en1\nEthernet Address: aa\n\nHardware Port: Wi-Fi\nDevice: en0\nEthernet Address: bb\n";

        Assert.Equal(new[] { "en0" }, MacParsers.ParseWifiPorts(text));
    }

    [Fact]
    public void Storage_ReadsNvmeDisks()
    {
        const string json = """
            {"SPNVMeDataType":[{"_name":"Apple SSD Controller","_items":[{"_name":"APPLE SSD AP0512Q","bsd_name":"disk0","device_model":"APPLE SSD AP0512Q","device_revision":"387.100.","device_serial":"0ba0123","size_in_bytes":500277790720,"smart_status":"Verified"}]}]}
            """;

        var disk = Assert.Single(MacParsers.ParseStorage(json));

        Assert.Equal("APPLE SSD AP0512Q", disk.Model);
        Assert.Equal("NVMe", disk.Interface);
        Assert.Equal(500277790720, disk.SizeBytes);
        Assert.Equal("Verified", disk.Smart);
        Assert.True(disk.Ssd);
    }

    [Fact]
    public void Airport_ConnectedNetwork()
    {
        const string json = """
            {"SPAirPortDataType":[{"spairport_airport_interfaces":[{"_name":"en0","spairport_current_network_information":{"_name":"Office","spairport_network_channel":"149 (5GHz, 80MHz)","spairport_network_phymode":"802.11ac","spairport_network_rate":867,"spairport_signal_noise":"-55 dBm / -92 dBm"}},{"_name":"awdl0"}]}]}
            """;

        var wifi = Assert.Single(MacParsers.ParseAirport(json));

        Assert.Equal("en0", wifi.Interface);
        Assert.Equal("Office", wifi.Ssid);
        Assert.Equal(90, wifi.SignalPercent);
        Assert.Equal("149", wifi.Channel);
        Assert.Equal("5 GHz", wifi.Band);
        Assert.Equal(867, wifi.RateMbps);
    }
}
