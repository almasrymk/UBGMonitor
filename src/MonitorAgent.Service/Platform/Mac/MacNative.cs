using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MonitorAgent.Service.Platform.Mac;

/// <summary>The macOS system calls the agent needs: sysctl values, per-core CPU ticks and per-process disk bytes.</summary>
[SupportedOSPlatform("macos")]
internal static class MacNative
{
    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const int ProcessorCpuLoadInfo = 2;
    private const int CpuStateMax = 4;
    private const int RusageInfoV2 = 2;

    public static string? SysctlString(string name)
    {
        nint size = 0;
        if (sysctlbyname(name, null, ref size, IntPtr.Zero, 0) != 0 || size <= 0)
        {
            return null;
        }

        var buffer = new byte[size];
        return sysctlbyname(name, buffer, ref size, IntPtr.Zero, 0) == 0
            ? Encoding.UTF8.GetString(buffer, 0, (int)size).TrimEnd('\0').Trim()
            : null;
    }

    /// <summary>A 32- or 64-bit number (hw.memsize, hw.physicalcpu...).</summary>
    public static long? SysctlLong(string name)
    {
        var buffer = new byte[8];
        nint size = buffer.Length;
        if (sysctlbyname(name, buffer, ref size, IntPtr.Zero, 0) != 0)
        {
            return null;
        }

        return size switch
        {
            4 => BitConverter.ToInt32(buffer, 0),
            8 => BitConverter.ToInt64(buffer, 0),
            _ => null
        };
    }

    /// <summary>kern.boottime: a timeval whose first field is the seconds since 1970.</summary>
    public static DateTime? BootTime()
    {
        var buffer = new byte[16];
        nint size = buffer.Length;
        return sysctlbyname("kern.boottime", buffer, ref size, IntPtr.Zero, 0) == 0 && size >= 8
            ? DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(buffer, 0)).LocalDateTime
            : null;
    }

    /// <summary>Busy and total ticks of each core since boot.</summary>
    public static List<(long Busy, long Total)> CoreTicks()
    {
        var result = new List<(long, long)>();
        if (host_processor_info(mach_host_self(), ProcessorCpuLoadInfo, out var count, out var info, out var infoCount) != 0)
        {
            return result;
        }

        try
        {
            var ticks = new int[infoCount];
            Marshal.Copy(info, ticks, 0, (int)infoCount);
            for (var cpu = 0; cpu < count; cpu++)
            {
                // user, system, idle, nice
                long user = (uint)ticks[cpu * CpuStateMax];
                long system = (uint)ticks[cpu * CpuStateMax + 1];
                long idle = (uint)ticks[cpu * CpuStateMax + 2];
                long nice = (uint)ticks[cpu * CpuStateMax + 3];
                result.Add((user + system + nice, user + system + nice + idle));
            }
        }
        finally
        {
            vm_deallocate(TaskSelf(), info, (nuint)(infoCount * sizeof(int)));
        }

        return result;
    }

    /// <summary>Bytes the process read and wrote on disk since it started (needs root for other users' processes).</summary>
    public static ulong? DiskBytes(int pid)
    {
        // rusage_info_v2: a 16-byte uuid, 16 counters, then ri_diskio_bytesread and ri_diskio_byteswritten.
        var buffer = new byte[512];
        if (proc_pid_rusage(pid, RusageInfoV2, buffer) != 0)
        {
            return null;
        }

        return BitConverter.ToUInt64(buffer, 144) + BitConverter.ToUInt64(buffer, 152);
    }

    private static uint TaskSelf()
    {
        var library = NativeLibrary.Load(LibSystem);
        return (uint)Marshal.ReadInt32(NativeLibrary.GetExport(library, "mach_task_self_"));
    }

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int sysctlbyname(string name, byte[]? oldp, ref nint oldlenp, IntPtr newp, nint newlen);

    [DllImport(LibSystem)]
    private static extern uint mach_host_self();

    [DllImport(LibSystem)]
    private static extern int host_processor_info(uint host, int flavor, out uint processorCount, out IntPtr processorInfo, out uint processorInfoCount);

    [DllImport(LibSystem)]
    private static extern int vm_deallocate(uint task, IntPtr address, nuint size);

    [DllImport(LibSystem)]
    private static extern int proc_pid_rusage(int pid, int flavor, byte[] buffer);
}
