using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClientAgent.Service.SystemInfo;

internal static class ProcessNetworkSampler
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int TcpConnectionEstatsData = 1;
    private const int MibTcpStateEstab = 5;
    private const uint ErrorInsufficientBuffer = 122;

    private static readonly ConcurrentDictionary<string, byte> EnabledConnections = new();
    private static readonly Stopwatch BudgetClock = new();

    public static Dictionary<int, ulong> ReadBytesByPid(TimeSpan? budget = null)
    {
        var totals = new Dictionary<int, ulong>();
        BudgetClock.Restart();
        var limit = budget ?? TimeSpan.FromMilliseconds(1200);
        AddIpv4(totals, limit);
        if (BudgetClock.Elapsed < limit)
        {
            AddIpv6(totals, limit);
        }

        return totals;
    }

    private static void AddIpv4(Dictionary<int, ulong> totals, TimeSpan limit)
    {
        if (!TryReadTable(AfInet, Marshal.SizeOf<MibTcpRowOwnerPid>(), out var buffer, out var count, out var rowSize))
        {
            return;
        }

        try
        {
            var rowPtr = buffer + 4;
            for (var i = 0; i < count && BudgetClock.Elapsed < limit; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr)!;
                rowPtr += rowSize;
                if (row.State != MibTcpStateEstab || row.OwningPid == 0)
                {
                    continue;
                }

                var tcpRow = new MibTcpRow
                {
                    State = row.State,
                    LocalAddr = row.LocalAddr,
                    LocalPort = row.LocalPort,
                    RemoteAddr = row.RemoteAddr,
                    RemotePort = row.RemotePort
                };
                var key = $"4:{row.LocalAddr}:{row.LocalPort}:{row.RemoteAddr}:{row.RemotePort}";
                AddPidBytes(totals, (int)row.OwningPid, ReadIpv4Bytes(tcpRow, key));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void AddIpv6(Dictionary<int, ulong> totals, TimeSpan limit)
    {
        if (!TryReadTable(AfInet6, Marshal.SizeOf<MibTcp6RowOwnerPid>(), out var buffer, out var count, out var rowSize))
        {
            return;
        }

        try
        {
            var rowPtr = buffer + 4;
            for (var i = 0; i < count && BudgetClock.Elapsed < limit; i++)
            {
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPtr)!;
                rowPtr += rowSize;
                if (row.State != MibTcpStateEstab || row.OwningPid == 0)
                {
                    continue;
                }

                var tcpRow = new MibTcp6Row
                {
                    State = row.State,
                    LocalAddr = row.LocalAddr,
                    LocalScopeId = row.LocalScopeId,
                    LocalPort = row.LocalPort,
                    RemoteAddr = row.RemoteAddr,
                    RemoteScopeId = row.RemoteScopeId,
                    RemotePort = row.RemotePort
                };
                var key = $"6:{row.LocalPort}:{row.RemotePort}:{row.OwningPid}";
                AddPidBytes(totals, (int)row.OwningPid, ReadIpv6Bytes(tcpRow, key));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryReadTable(int family, int rowSize, out IntPtr buffer, out int count, out int actualRowSize)
    {
        buffer = IntPtr.Zero;
        count = 0;
        actualRowSize = rowSize;
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, family, TcpTableOwnerPidAll, 0);
        if (result != 0 && result != ErrorInsufficientBuffer)
        {
            return false;
        }

        buffer = Marshal.AllocHGlobal(size);
        result = GetExtendedTcpTable(buffer, ref size, true, family, TcpTableOwnerPidAll, 0);
        if (result != 0)
        {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            return false;
        }

        count = Marshal.ReadInt32(buffer);
        return true;
    }

    private static ulong ReadIpv4Bytes(MibTcpRow row, string key)
    {
        if (EnabledConnections.TryAdd(key, 0))
        {
            EnableIpv4(row);
        }

        return ReadIpv4Stats(row);
    }

    private static ulong ReadIpv6Bytes(MibTcp6Row row, string key)
    {
        if (EnabledConnections.TryAdd(key, 0))
        {
            EnableIpv6(row);
        }

        return ReadIpv6Stats(row);
    }

    private static ulong ReadIpv4Stats(MibTcpRow row)
    {
        var size = Marshal.SizeOf<TcpEstatsDataRod>();
        var rod = Marshal.AllocHGlobal(size);
        try
        {
            if (GetPerTcpConnectionEStats(
                    ref row, TcpConnectionEstatsData,
                    IntPtr.Zero, 0, 0,
                    IntPtr.Zero, 0, 0,
                    rod, 0, (uint)size) != 0)
            {
                return 0;
            }

            var data = Marshal.PtrToStructure<TcpEstatsDataRod>(rod);
            return data.DataBytesOut + data.DataBytesIn;
        }
        finally
        {
            Marshal.FreeHGlobal(rod);
        }
    }

    private static ulong ReadIpv6Stats(MibTcp6Row row)
    {
        var size = Marshal.SizeOf<TcpEstatsDataRod>();
        var rod = Marshal.AllocHGlobal(size);
        try
        {
            if (GetPerTcp6ConnectionEStats(
                    ref row, TcpConnectionEstatsData,
                    IntPtr.Zero, 0, 0,
                    IntPtr.Zero, 0, 0,
                    rod, 0, (uint)size) != 0)
            {
                return 0;
            }

            var data = Marshal.PtrToStructure<TcpEstatsDataRod>(rod);
            return data.DataBytesOut + data.DataBytesIn;
        }
        finally
        {
            Marshal.FreeHGlobal(rod);
        }
    }

    private static void EnableIpv4(MibTcpRow row)
    {
        var rw = new TcpEstatsDataRw { EnableCollection = 1 };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<TcpEstatsDataRw>());
        try
        {
            Marshal.StructureToPtr(rw, ptr, false);
            _ = SetPerTcpConnectionEStats(ref row, TcpConnectionEstatsData, ptr, 0, (uint)Marshal.SizeOf<TcpEstatsDataRw>(), 0);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void EnableIpv6(MibTcp6Row row)
    {
        var rw = new TcpEstatsDataRw { EnableCollection = 1 };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<TcpEstatsDataRw>());
        try
        {
            Marshal.StructureToPtr(rw, ptr, false);
            _ = SetPerTcp6ConnectionEStats(ref row, TcpConnectionEstatsData, ptr, 0, (uint)Marshal.SizeOf<TcpEstatsDataRw>(), 0);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void AddPidBytes(Dictionary<int, ulong> totals, int pid, ulong bytes)
    {
        totals[pid] = totals.GetValueOrDefault(pid) + bytes;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int size, bool order, int ipVersion, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SetPerTcpConnectionEStats(ref MibTcpRow row, int estatsType, IntPtr rw, uint rwVersion, uint rwSize, uint offset);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetPerTcpConnectionEStats(
        ref MibTcpRow row, int estatsType,
        IntPtr rw, uint rwVersion, uint rwSize,
        IntPtr ros, uint rosVersion, uint rosSize,
        IntPtr rod, uint rodVersion, uint rodSize);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SetPerTcp6ConnectionEStats(ref MibTcp6Row row, int estatsType, IntPtr rw, uint rwVersion, uint rwSize, uint offset);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetPerTcp6ConnectionEStats(
        ref MibTcp6Row row, int estatsType,
        IntPtr rw, uint rwVersion, uint rwSize,
        IntPtr ros, uint rosVersion, uint rosSize,
        IntPtr rod, uint rodVersion, uint rodSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6Row
    {
        public uint State;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpEstatsDataRw
    {
        public byte EnableCollection;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpEstatsDataRod
    {
        public ulong DataBytesOut;
        public ulong DataBytesIn;
        public ulong DataSegsOut;
        public ulong DataSegsIn;
        public ulong SoftCongWs;
        public ulong SoftCongMs;
        public ulong SoftCongCount;
        public ulong SeqRcvWnd;
        public ulong MaxSsCwnd;
        public ulong CurRtoCount;
        public ulong LastRtoTime;
        public ulong MinRtt;
        public ulong MaxRtt;
        public ulong SumRtt;
        public ulong CountRtt;
        public ulong CurTimeoutCount;
        public ulong AbruptTimeouts;
        public ulong PktsRetrans;
        public ulong BytesRetrans;
        public ulong FastRetrans;
    }
}
