using Google.Protobuf.WellKnownTypes;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorAgent.Cloud;

/// <summary>
/// Turns samples into one <see cref="MinuteAggregate"/> per completed minute (AG-6): min, max, average and P95 of CPU
/// and RAM, averages of the rates, the highest disk usage and temperature. A minute is complete when a sample of a
/// later minute arrives.
/// </summary>
public sealed class MinuteAggregator
{
    private readonly List<CloudSample> _samples = [];
    private DateTimeOffset? _minute;

    /// <summary>Adds a sample; returns the aggregate of the previous minute when this sample starts a new one.</summary>
    public MinuteAggregate? Add(CloudSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var minute = MinuteOf(sample.At);
        MinuteAggregate? completed = null;
        if (_minute is { } current && minute > current)
        {
            completed = Build(current, _samples);
            _samples.Clear();
        }

        if (_minute is null || minute > _minute)
            _minute = minute;
        if (minute == _minute)
            _samples.Add(sample);
        return completed;
    }

    public static DateTimeOffset MinuteOf(DateTimeOffset at) => new(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0, TimeSpan.Zero);

    public static MinuteAggregate Build(DateTimeOffset minute, IReadOnlyList<CloudSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
            throw new ArgumentException("A minute needs at least one sample.", nameof(samples));
        var cpu = samples.Select(s => Clamp(s.Cpu)).ToList();
        var ram = samples.Select(s => Clamp(s.Ram)).ToList();
        var aggregate = new MinuteAggregate
        {
            BucketStart = Timestamp.FromDateTimeOffset(minute),
            Samples = (uint)samples.Count,
            CpuAvg = Round(cpu.Average()),
            CpuMax = Round(cpu.Max()),
            CpuP95 = Round(P95(cpu)),
            RamAvg = Round(ram.Average()),
            RamMax = Round(ram.Max()),
            DiskPercentMax = Round(samples.Max(s => Clamp(s.DiskPercentMax))),
            UptimeSeconds = samples[^1].UptimeSeconds,
        };
        if (Average(samples.Select(s => s.DiskActive)) is { } active)
            aggregate.DiskActiveAvg = Round(Clamp(active));
        if (Average(samples.Select(s => (double?)s.ReadBps)) is { } read)
            aggregate.DiskReadBps = (ulong)Math.Max(0, read);
        if (Average(samples.Select(s => (double?)s.WriteBps)) is { } write)
            aggregate.DiskWriteBps = (ulong)Math.Max(0, write);
        if (Average(samples.Select(s => s.DiskResponseMs)) is { } response)
            aggregate.DiskResponseMs = Round(response);
        if (Average(samples.Select(s => (double?)s.RxBps)) is { } rx)
            aggregate.NetRxBps = (ulong)Math.Max(0, rx);
        if (Average(samples.Select(s => (double?)s.TxBps)) is { } tx)
            aggregate.NetTxBps = (ulong)Math.Max(0, tx);
        if (Average(samples.Select(s => s.PingMs)) is { } ping)
            aggregate.PingMs = Round(ping);
        if (Average(samples.Select(s => s.PacketLossPercent)) is { } loss)
            aggregate.PacketLossPercent = Round(Clamp(loss));
        if (samples.Where(s => s.TempC is not null).Select(s => s.TempC!.Value).DefaultIfEmpty(double.NaN).Max() is var temp && !double.IsNaN(temp))
            aggregate.TempMaxC = Round(temp);
        return aggregate;
    }

    /// <summary>Nearest-rank P95: the value at rank ceil(0.95 n).</summary>
    public static double P95(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Order().ToList();
        var rank = (int)Math.Ceiling(0.95 * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var present = values.Where(v => v is not null && double.IsFinite(v.Value)).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Average();
    }

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private static double Round(double value) => Math.Round(value, 2);
}
