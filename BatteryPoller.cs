using System.Management;

namespace BatteryMeter;

public sealed class BatteryPoller : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly ManagementObjectSearcher _batterySearcher;
    private uint _fullChargedCapacityMWh;

    public event Action<BatteryReading>? ReadingUpdated;

    private readonly TimeSpan _interval;

    public BatteryPoller(TimeSpan interval)
    {
        _interval = interval;
        _fullChargedCapacityMWh = QueryFullChargedCapacity();
        _batterySearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM BatteryStatus");
        _timer = new System.Threading.Timer(_ => Poll(), null, Timeout.InfiniteTimeSpan, interval);
    }

    public void Start() => _timer.Change(TimeSpan.Zero, _interval);

    public uint FullChargedCapacityMWh => _fullChargedCapacityMWh;

    private void Poll()
    {
        var reading = QueryBattery();
        if (reading != null)
            ReadingUpdated?.Invoke(reading);
    }

    public BatteryReading? QueryBattery()
    {
        try
        {
            foreach (var obj in _batterySearcher.Get())
            {
                return new BatteryReading(
                    ChargeRateWatts: Convert.ToUInt32(obj["ChargeRate"]) / 1000.0,
                    DischargeRateWatts: Convert.ToUInt32(obj["DischargeRate"]) / 1000.0,
                    IsCharging: Convert.ToBoolean(obj["Charging"]),
                    PowerOnline: Convert.ToBoolean(obj["PowerOnline"]),
                    RemainingCapacityMWh: Convert.ToUInt32(obj["RemainingCapacity"]),
                    VoltageV: Convert.ToUInt32(obj["Voltage"]) / 1000.0,
                    Timestamp: DateTime.Now);
            }
        }
        catch
        {
            // WMI query can fail transiently
        }
        return null;
    }

    public void Refresh() => Poll();

    private static uint QueryFullChargedCapacity()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM BatteryFullChargedCapacity");
            foreach (var obj in searcher.Get())
            {
                return Convert.ToUInt32(obj["FullChargedCapacity"]);
            }
        }
        catch
        {
            // Fallback if WMI query fails
        }
        return 0;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _batterySearcher.Dispose();
    }
}
