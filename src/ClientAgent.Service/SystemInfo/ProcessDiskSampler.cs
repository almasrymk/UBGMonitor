using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClientAgent.Service.SystemInfo;

internal static class ProcessDiskSampler
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>Read plus write bytes per process since it started. Processes that deny access are skipped.</summary>
    public static Dictionary<int, ulong> ReadBytesByPid()
    {
        var result = new Dictionary<int, ulong>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)process.Id);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    if (GetProcessIoCounters(handle, out var counters))
                    {
                        result[process.Id] = counters.ReadTransferCount + counters.WriteTransferCount;
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch
            {
                // Process may have exited during sampling.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
