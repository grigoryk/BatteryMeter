namespace BatteryMeter;

public record BatteryReading(
    double ChargeRateWatts,
    double DischargeRateWatts,
    bool IsCharging,
    bool PowerOnline,
    uint RemainingCapacityMWh,
    double VoltageV,
    double SystemPowerWatts,
    DateTime Timestamp);
