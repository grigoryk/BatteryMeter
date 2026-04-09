using System.Diagnostics;
using System.Management;

namespace BatteryMeter;

public sealed class BatteryPoller : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly ManagementObjectSearcher _batterySearcher;
    private readonly PerformanceCounter? _powerCounter;
    private uint _fullChargedCapacityMWh;

    public event Action<BatteryReading>? ReadingUpdated;

    public BatteryPoller(TimeSpan interval)
    {
        _fullChargedCapacityMWh = QueryFullChargedCapacity();
        _batterySearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM BatteryStatus");
        _powerCounter = CreatePowerCounter();
        _timer = new System.Threading.Timer(_ => Poll(), null, TimeSpan.Zero, interval);
    }

    public uint FullChargedCapacityMWh => _fullChargedCapacityMWh;

    private void Poll()
    {
        try
        {
            double systemPowerWatts = QuerySystemPower();

            foreach (var obj in _batterySearcher.Get())
            {
                var reading = new BatteryReading(
                    ChargeRateWatts: Convert.ToUInt32(obj["ChargeRate"]) / 1000.0,
                    DischargeRateWatts: Convert.ToUInt32(obj["DischargeRate"]) / 1000.0,
                    IsCharging: Convert.ToBoolean(obj["Charging"]),
                    PowerOnline: Convert.ToBoolean(obj["PowerOnline"]),
                    RemainingCapacityMWh: Convert.ToUInt32(obj["RemainingCapacity"]),
                    VoltageV: Convert.ToUInt32(obj["Voltage"]) / 1000.0,
                    SystemPowerWatts: systemPowerWatts,
                    Timestamp: DateTime.Now);

                ReadingUpdated?.Invoke(reading);
                return;
            }
        }
        catch
        {
            // WMI query can fail transiently; skip this cycle
        }
    }

    private double QuerySystemPower()
    {
        try
        {
            if (_powerCounter != null)
                return _powerCounter.NextValue() / 1000.0;
        }
        catch { }
        return 0;
    }

    private static PerformanceCounter? CreatePowerCounter()
    {
        try
        {
            return new PerformanceCounter("Power Meter", "Power", "Power Meter (0)", readOnly: true);
        }
        catch
        {
            return null;
        }
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
        _powerCounter?.Dispose();
    }
}
