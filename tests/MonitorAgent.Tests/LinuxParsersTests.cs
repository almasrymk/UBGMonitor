using MonitorAgent.Service.Platform.Linux;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Tests;

public sealed class LinuxParsersTests
{
    [Fact]
    public void ProcStat_UsageBetweenTwoReadings()
    {
        const string before = "cpu  100 0 100 800 0 0 0 0 0 0\ncpu0 50 0 50 400 0 0 0 0 0 0\ncpu1 50 0 50 400 0 0 0 0 0 0\nintr 123\nbtime 1700000000\n";
        const string after = "cpu  200 0 200 1000 0 0 0 0 0 0\ncpu0 150 0 50 400 0 0 0 0 0 0\ncpu1 50 0 150 600 0 0 0 0 0 0\n";

        var a = LinuxParsers.ParseProcStat(before);
        var b = LinuxParsers.ParseProcStat(after);

        Assert.Equal(50, LinuxParsers.UsagePercent(a["cpu"], b["cpu"]), 1);
        Assert.Equal(100, LinuxParsers.UsagePercent(a["cpu0"], b["cpu0"]), 1);
        Assert.Equal(33.3, LinuxParsers.UsagePercent(a["cpu1"], b["cpu1"]), 1);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).LocalDateTime, LinuxParsers.ParseBootTime(before));
    }

    [Fact]
    public void CpuInfo_CountsCoresThreadsAndSpeed()
    {
        const string text = """
            processor	: 0
            model name	: Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz
            cpu MHz		: 1800.000
            physical id	: 0
            core id		: 0

            processor	: 1
            model name	: Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz
            cpu MHz		: 2000.000
            physical id	: 0
            core id		: 1

            processor	: 2
            model name	: Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz
            cpu MHz		: 1900.000
            physical id	: 0
            core id		: 0
            """;

        var info = LinuxParsers.ParseCpuInfo(text);

        Assert.Equal("Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz", info.Model);
        Assert.Equal(2, info.PhysicalCores);
        Assert.Equal(3, info.LogicalCores);
        Assert.Equal(1900, info.AverageMhz);
    }

    [Fact]
    public void MemInfo_ReturnsBytes()
    {
        var memory = LinuxParsers.ParseMemInfo("MemTotal:       16314164 kB\nMemAvailable:    8000000 kB\nHugePages_Total:       0\n");

        Assert.Equal(16314164L * 1024, memory["MemTotal"]);
        Assert.Equal(8000000L * 1024, memory["MemAvailable"]);
        Assert.Equal(0, memory["HugePages_Total"]);
    }

    [Theory]
    [InlineData("sda", true)]
    [InlineData("sda1", false)]
    [InlineData("nvme0n1", true)]
    [InlineData("nvme0n1p2", false)]
    [InlineData("mmcblk0", true)]
    [InlineData("mmcblk0p1", false)]
    [InlineData("mmcblk0boot0", false)]
    [InlineData("vda", true)]
    [InlineData("loop3", false)]
    [InlineData("dm-0", false)]
    [InlineData("zram0", false)]
    public void PhysicalDiskNames(string name, bool expected)
        => Assert.Equal(expected, LinuxParsers.IsPhysicalDiskName(name));

    [Fact]
    public void DiskStats_ActivityOverOneSecond()
    {
        const string before = "   8       0 sda 1000 0 2000 300 500 0 4000 200 0 400 500 0 0 0 0\n   8       1 sda1 900 0 1800 280 480 0 3900 190 0 380 470\n   7       0 loop0 10 0 20 0 0 0 0 0 0 0 0\n";
        const string after = "   8       0 sda 1100 0 4048 350 600 0 6096 250 2 900 600 0 0 0 0\n";

        var a = LinuxParsers.ParseDiskStats(before);
        var b = LinuxParsers.ParseDiskStats(after);
        var activity = LinuxDiskActivityReader.Compare(a, b, 1000);

        Assert.Single(a);
        Assert.Equal(50, activity.ActiveTimePercent, 1);
        Assert.Equal(2048 * 512, activity.ReadBytesPerSecond, 1);
        Assert.Equal(2096 * 512, activity.WriteBytesPerSecond, 1);
        Assert.Equal(0.5, activity.ResponseMs, 2);
        Assert.Equal(2, activity.QueueLength);
    }

    [Fact]
    public void OsRelease_RemovesQuotes()
    {
        var os = LinuxParsers.ParseKeyValueFile("NAME=\"Ubuntu\"\nVERSION_ID=\"24.04\"\nPRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\n# comment\nID=ubuntu\n");

        Assert.Equal("Ubuntu 24.04.1 LTS", os["PRETTY_NAME"]);
        Assert.Equal("24.04", os["VERSION_ID"]);
        Assert.Equal("ubuntu", os["ID"]);
    }

    [Fact]
    public void Dmidecode_ModulesAndSlots()
    {
        const string text = """
            # dmidecode 3.5
            Handle 0x0010, DMI type 16, 23 bytes
            Physical Memory Array
            	Location: System Board Or Motherboard
            	Number Of Devices: 4

            Handle 0x0011, DMI type 17, 92 bytes
            Memory Device
            	Size: 8 GB
            	Type: DDR4
            	Speed: 3200 MT/s
            	Manufacturer: Samsung
            	Serial Number: 1234ABCD
            	Part Number: M471A1K43DB1-CWE
            	Configured Memory Speed: 2667 MT/s

            Handle 0x0012, DMI type 17, 92 bytes
            Memory Device
            	Size: No Module Installed
            	Type: Unknown
            	Speed: Unknown

            Handle 0x0013, DMI type 17, 92 bytes
            Memory Device
            	Size: 8 GB
            	Type: DDR4
            	Speed: 3200 MT/s
            	Manufacturer: Samsung
            	Serial Number: 5678EFGH
            	Part Number: M471A1K43DB1-CWE
            	Configured Memory Speed: Unknown
            """;

        var memory = LinuxParsers.ParseDmidecodeMemory(text);

        Assert.Equal(4, memory.Slots);
        Assert.Equal(2, memory.Modules.Count);
        Assert.Equal("DDR4", memory.Modules[0].Type);
        Assert.Equal(2667, memory.Modules[0].SpeedMhz);
        Assert.Equal(3200, memory.Modules[1].SpeedMhz);
        Assert.Equal("Samsung", memory.Modules[0].Manufacturer);
        Assert.Equal("5678EFGH", memory.Modules[1].Serial);
    }

    [Fact]
    public void Nmcli_ConnectedNetworkWithEscapedColon()
    {
        const string text = "no:wlp2s0:Neighbor:40:6:2437 MHz:65 Mbit/s\nyes:wlp2s0:Office\\:5G:78:36:5180 MHz:270 Mbit/s\n";

        var wifi = Assert.Single(LinuxParsers.ParseNmcliWifi(text));

        Assert.Equal("wlp2s0", wifi.Device);
        Assert.Equal("Office:5G", wifi.Ssid);
        Assert.Equal(78, wifi.Signal);
        Assert.Equal("36", wifi.Channel);
        Assert.Equal("5180", wifi.FrequencyMhz);
        Assert.Equal(270, wifi.RateMbps);
    }

    [Fact]
    public void Lspci_FindsGpus()
    {
        const string text = """
            00:02.0 "VGA compatible controller" "Intel Corporation" "UHD Graphics 620" -r07 "Lenovo" "ThinkPad"
            00:1f.3 "Audio device" "Intel Corporation" "Sunrise Point-LP HD Audio" -r21 "Lenovo" "Device 225d"
            01:00.0 "3D controller" "NVIDIA Corporation" "GP108M [GeForce MX150]" -ra1 "Lenovo" "Device 225e"
            """;

        var gpus = LinuxParsers.ParseLspciGpus(text);

        Assert.Equal(new[] { "Intel UHD Graphics 620", "NVIDIA GP108M [GeForce MX150]" }, gpus);
    }

    [Fact]
    public void Mounts_DecodesSpaces()
    {
        var mounts = LinuxParsers.ParseMounts("/dev/nvme0n1p2 / ext4 rw,relatime 0 0\nproc /proc proc rw 0 0\n/dev/sdb1 /media/My\\040Disk exfat rw 0 0\n");

        Assert.Equal(3, mounts.Count);
        Assert.Equal(("/dev/sdb1", "/media/My Disk", "exfat"), mounts[2]);
    }

    [Fact]
    public void SecretProtector_RoundTripsOnThisSystem()
    {
        var protectedText = SecretProtector.Protect("p@ss word");

        Assert.True(SecretProtector.IsProtected(protectedText));
        Assert.Equal("p@ss word", SecretProtector.Unprotect(protectedText));
        Assert.Equal(protectedText, SecretProtector.Protect(protectedText));
        Assert.Equal(string.Empty, SecretProtector.Unprotect("plain"));
    }
}
