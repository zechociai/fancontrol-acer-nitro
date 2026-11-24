using System;
using System.Management;

namespace FanControl.AcerNitro;

/// <summary>
/// Thin wrapper over Acer's AcerGamingFunction WMI class (root\WMI).
/// Encodings match those used by NitroSense / FanCurve on the Nitro AN515-57.
/// </summary>
internal sealed class AcerWmi : IDisposable
{
    public const int CpuFanId = 1;
    public const int GpuFanId = 4;
    public const int CpuBit = 0x1;
    public const int GpuBit = 0x8;

    public const int ModeAuto = 1;
    public const int ModeTurbo = 2;
    public const int ModeCustom = 3;

    public enum Sensor : uint
    {
        CpuTemperature = 0x0101,
        CpuFanRpm = 0x0201,
        SystemTemperature = 0x0301,
        GpuFanRpm = 0x0601,
        GpuTemperature = 0x0A01,
    }

    private readonly object _lock = new();
    private ManagementObject? _instance;

    public static AcerWmi Open()
    {
        var wmi = new AcerWmi();
        wmi.Connect();
        return wmi;
    }

    private void Connect()
    {
        var scope = new ManagementScope(@"\\.\root\WMI");
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AcerGamingFunction"));
        foreach (ManagementObject obj in searcher.Get())
        {
            _instance = obj;
            return;
        }
        throw new InvalidOperationException("AcerGamingFunction WMI instance not found.");
    }

    private ulong Invoke(string method, object input)
    {
        lock (_lock)
        {
            if (_instance is null) throw new ObjectDisposedException(nameof(AcerWmi));
            using var inParams = _instance.GetMethodParameters(method);
            inParams["gmInput"] = input;
            using var outParams = _instance.InvokeMethod(method, inParams, null);
            var raw = outParams?["gmOutput"] ?? throw new InvalidOperationException($"{method} returned no output.");
            return Convert.ToUInt64(raw);
        }
    }

    /// <summary>Returns the sensor value, or null when the firmware reports an error.</summary>
    public int? ReadSensor(Sensor sensor)
    {
        var raw = Invoke("GetGamingSysInfo", (uint)sensor);
        if ((raw & 0xFF) != 0) return null;
        return (int)((raw >> 8) & 0xFFFF);
    }

    public (int Cpu, int Gpu)? ReadFanMode()
    {
        foreach (var bitmap in new uint[] { CpuBit | GpuBit, CpuBit, GpuBit })
        {
            try
            {
                var raw = Invoke("GetGamingFanBehavior", bitmap);
                if ((raw & 0xFF) == 0)
                    return ((int)((raw >> 8) & 0x3), (int)((raw >> 14) & 0x3));
            }
            catch
            {
                // try the next bitmap
            }
        }
        return null;
    }

    public void SetFanMode(int fanBitmap, int cpuMode, int gpuMode)
    {
        ulong value = (ulong)(fanBitmap & 0xFFFF);
        if ((fanBitmap & CpuBit) != 0) value |= (ulong)(cpuMode & 0x3) << 16;
        if ((fanBitmap & GpuBit) != 0) value |= (ulong)(gpuMode & 0x3) << 22;

        var status = Invoke("SetGamingFanBehavior", value) & 0xFF;
        if (status != 0)
            throw new InvalidOperationException($"SetGamingFanBehavior(0x{value:X}) failed with status {status}.");
    }

    public void SetFanSpeed(int fanId, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        ulong value = ((ulong)(byte)percent << 8) | (byte)fanId;
        var status = Invoke("SetGamingFanSpeed", value) & 0xFF;
        if (status != 0)
            throw new InvalidOperationException($"SetGamingFanSpeed(fan {fanId}, {percent}%) failed with status {status}.");
    }

    public int? ReadFanSpeed(int fanId)
    {
        var raw = Invoke("GetGamingFanSpeed", (uint)fanId);
        if ((raw & 0xFF) != 0) return null;
        return (int)((raw >> 8) & 0xFF);
    }

    public void RestoreAuto()
    {
        try
        {
            SetFanMode(CpuBit | GpuBit, ModeAuto, ModeAuto);
        }
        catch
        {
            SetFanMode(CpuBit, ModeAuto, ModeAuto);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _instance?.Dispose();
            _instance = null;
        }
    }
}
