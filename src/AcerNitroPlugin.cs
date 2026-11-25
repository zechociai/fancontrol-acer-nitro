using System;
using System.Collections.Generic;
using FanControl.Plugins;

namespace FanControl.AcerNitro;

/// <summary>
/// FanControl plugin for Acer Nitro laptops (tested target: AN515-57) using the
/// AcerGamingFunction WMI interface. Exposes CPU/GPU/system temperatures, both fan RPMs,
/// one control per fan, and the ACPI thermal zones.
/// </summary>
public sealed class AcerNitroPlugin : IPlugin2
{
    // Safety limits.
    internal const int MinPercent = 20;         // never command a fan below this
    internal const int EmergencyTemp = 92;      // held at/above this for EmergencyHold, both controlled fans go to 100%
    internal const int EmergencyHardTemp = 97;  // at/above this, 100% immediately
    private static readonly TimeSpan EmergencyHold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ModeRefresh = TimeSpan.FromSeconds(30);

    private readonly IPluginLogger _logger;
    private AcerWmi? _wmi;

    private readonly List<ValueSensor> _sensors = new();
    private readonly List<ZoneSensor> _zones = new();
    private ValueSensor? _cpuTemp;
    private ValueSensor? _gpuTemp;
    private FanControlSensor? _cpuControl;
    private FanControlSensor? _gpuControl;

    private DateTime _lastModeApply = DateTime.MinValue;
    private bool _turboPaused;
    private bool _emergency;
    private DateTime? _hotSince;

    public AcerNitroPlugin(IPluginLogger logger)
    {
        _logger = logger;
    }

    public string Name => "Acer Nitro (WMI)";

    internal void Log(string message) => _logger.Log($"[AcerNitro] {message}");

    public void Initialize()
    {
        try
        {
            _wmi = AcerWmi.Open();
            Log("Connected to AcerGamingFunction.");
        }
        catch (Exception ex)
        {
            _wmi = null;
            Log($"AcerGamingFunction unavailable (is FanControl running as administrator?): {ex.Message}");
        }
    }

    public void Load(IPluginSensorsContainer container)
    {
        LoadThermalZones(container);

        if (_wmi is null) return;

        _cpuTemp = AddSensor(container.TempSensors, "acer/temp/cpu", "CPU (Acer EC)", AcerWmi.Sensor.CpuTemperature);
        _gpuTemp = AddSensor(container.TempSensors, "acer/temp/gpu", "GPU (Acer EC)", AcerWmi.Sensor.GpuTemperature);
        AddSensor(container.TempSensors, "acer/temp/system", "System (Acer EC)", AcerWmi.Sensor.SystemTemperature);
        var cpuRpm = AddSensor(container.FanSensors, "acer/fan/cpu", "CPU fan (Acer)", AcerWmi.Sensor.CpuFanRpm);
        var gpuRpm = AddSensor(container.FanSensors, "acer/fan/gpu", "GPU fan (Acer)", AcerWmi.Sensor.GpuFanRpm);

        // FanControl stores plugin sensors as "<plugin name>/<sensor id>", so pair by that full identifier.
        _cpuControl = new FanControlSensor(this, _wmi, "acer/control/cpu", "CPU fan (Acer)", AcerWmi.CpuFanId, AcerWmi.CpuBit, $"{Name}/{cpuRpm.Id}");
        _gpuControl = new FanControlSensor(this, _wmi, "acer/control/gpu", "GPU fan (Acer)", AcerWmi.GpuFanId, AcerWmi.GpuBit, $"{Name}/{gpuRpm.Id}");
        container.ControlSensors.Add(_cpuControl);
        container.ControlSensors.Add(_gpuControl);

        Update();
    }

    private ValueSensor AddSensor(List<IPluginSensor> list, string id, string name, AcerWmi.Sensor sensor)
    {
        var s = new ValueSensor(id, name, sensor);
        _sensors.Add(s);
        list.Add(s);
        return s;
    }

    private void LoadThermalZones(IPluginSensorsContainer container)
    {
        try
        {
            foreach (var zone in AcpiThermalZones.ReadAll().Keys)
            {
                var s = new ZoneSensor($"acpi/tz/{zone.ToLowerInvariant()}", $"ACPI {zone}", zone);
                _zones.Add(s);
                container.TempSensors.Add(s);
            }
            Log($"ACPI thermal zones: {(_zones.Count == 0 ? "none" : string.Join(", ", _zones.ConvertAll(z => z.Zone)))}.");
        }
        catch (Exception ex)
        {
            Log($"ACPI thermal zones unavailable: {ex.Message}");
        }
    }

    private void UpdateThermalZones()
    {
        if (_zones.Count == 0) return;
        try
        {
            var temps = AcpiThermalZones.ReadAll();
            foreach (var z in _zones)
                z.Value = temps.TryGetValue(z.Zone, out var t) && t is > 0 and < 150 ? t : null;
        }
        catch (Exception ex)
        {
            foreach (var z in _zones) z.Value = null;
            Log($"Read ACPI thermal zones failed: {ex.Message}");
        }
    }

    public void Update()
    {
        UpdateThermalZones();

        if (_wmi is null) return;

        foreach (var s in _sensors)
        {
            try
            {
                var v = _wmi.ReadSensor(s.Sensor);
                s.Value = v is > 0 and < 10000 ? v : null;
            }
            catch (Exception ex)
            {
                s.Value = null;
                Log($"Read {s.Name} failed: {ex.Message}");
            }
        }

        _cpuControl?.RefreshFromFirmware();
        _gpuControl?.RefreshFromFirmware();

        var anyCustom = (_cpuControl?.IsCustom ?? false) || (_gpuControl?.IsCustom ?? false);
        if (!anyCustom) return;

        // Respect the Turbo key: if the firmware is in Turbo, stop writing until it leaves Turbo.
        try
        {
            var mode = _wmi.ReadFanMode();
            var turbo = mode?.Cpu == AcerWmi.ModeTurbo;
            if (turbo != _turboPaused)
            {
                _turboPaused = turbo;
                Log(turbo ? "Turbo mode detected; pausing fan writes." : "Turbo mode left; resuming.");
                if (!turbo) _lastModeApply = DateTime.MinValue;
            }
        }
        catch (Exception ex)
        {
            Log($"Read fan mode failed: {ex.Message}");
        }
        if (_turboPaused) return;

        // Re-assert custom mode periodically; firmware can revert it (sleep, power profile changes).
        if (DateTime.UtcNow - _lastModeApply >= ModeRefresh)
        {
            _cpuControl?.ReapplyMode();
            _gpuControl?.ReapplyMode();
            _lastModeApply = DateTime.UtcNow;
        }

        // Emergency override, independent of the user's curves. Turbo bursts make the EC's CPU reading spike
        // for a second or two, so 92 C must hold for EmergencyHold; 97 C triggers at once.
        var peak = Math.Max(_cpuTemp?.Value ?? 0, _gpuTemp?.Value ?? 0);
        var now = DateTime.UtcNow;
        if (peak >= EmergencyTemp) _hotSince ??= now;
        else _hotSince = null;
        var hot = peak >= EmergencyHardTemp || (_hotSince is DateTime since && now - since >= EmergencyHold);
        if (hot != _emergency)
        {
            _emergency = hot;
            Log(hot
                ? $"Emergency: {peak:0} C (>= {EmergencyHardTemp} C, or >= {EmergencyTemp} C for {EmergencyHold.TotalSeconds:0} s), forcing 100%."
                : "Emergency cleared.");
        }

        _cpuControl?.Apply(_emergency);
        _gpuControl?.Apply(_emergency);
    }

    internal bool TurboPaused => _turboPaused;

    public void Close()
    {
        _zones.Clear();
        if (_wmi is null) return;
        try
        {
            _wmi.RestoreAuto();
            Log("Fans restored to AUTO.");
        }
        catch (Exception ex)
        {
            Log($"Restore AUTO failed: {ex.Message}");
        }
        _wmi.Dispose();
        _wmi = null;
        _sensors.Clear();
    }
}

internal sealed class ValueSensor : IPluginSensor
{
    public ValueSensor(string id, string name, AcerWmi.Sensor sensor)
    {
        Id = id;
        Name = name;
        Sensor = sensor;
    }

    public string Id { get; }
    public string Name { get; }
    public AcerWmi.Sensor Sensor { get; }
    public float? Value { get; set; }

    // Values are refreshed in bulk by AcerNitroPlugin.Update().
    public void Update() { }
}

internal sealed class ZoneSensor : IPluginSensor
{
    public ZoneSensor(string id, string name, string zone)
    {
        Id = id;
        Name = name;
        Zone = zone;
    }

    public string Id { get; }
    public string Name { get; }
    public string Zone { get; }
    public float? Value { get; set; }

    // Values are refreshed in bulk by AcerNitroPlugin.Update().
    public void Update() { }
}

internal sealed class FanControlSensor : IPluginControlSensor2
{
    private readonly AcerNitroPlugin _plugin;
    private readonly AcerWmi _wmi;
    private readonly int _fanId;
    private readonly int _bit;
    private int? _requested;
    private int _lastWritten = -1;
    private int _failures;

    public FanControlSensor(AcerNitroPlugin plugin, AcerWmi wmi, string id, string name, int fanId, int bit, string pairedFanId)
    {
        _plugin = plugin;
        _wmi = wmi;
        Id = id;
        Name = name;
        _fanId = fanId;
        _bit = bit;
        PairedFanSensorId = pairedFanId;
    }

    public string Id { get; }
    public string Name { get; }
    public string PairedFanSensorId { get; }
    public float? Value { get; private set; }
    public bool IsCustom => _requested.HasValue;

    public void Update() { }

    /// <summary>While the firmware owns the fan, report its current duty (%) so FanControl has a value to show.</summary>
    internal void RefreshFromFirmware()
    {
        if (IsCustom) return;
        try
        {
            var pct = _wmi.ReadFanSpeed(_fanId);
            Value = pct ?? Value ?? 0;
        }
        catch
        {
            Value ??= 0;
        }
    }

    public void Set(float val)
    {
        var percent = (int)Math.Round(Math.Clamp(val, 0f, 100f));
        percent = Math.Max(percent, AcerNitroPlugin.MinPercent);
        var wasCustom = IsCustom;
        _requested = percent;
        if (!wasCustom)
        {
            ReapplyMode();
        }
        if (!_plugin.TurboPaused)
            Write(percent);
    }

    public void Reset()
    {
        if (!IsCustom) return;
        _requested = null;
        _lastWritten = -1;
        try
        {
            _wmi.SetFanMode(_bit, AcerWmi.ModeAuto, AcerWmi.ModeAuto);
            _plugin.Log($"{Name}: back to AUTO.");
        }
        catch (Exception ex)
        {
            _plugin.Log($"{Name}: restore AUTO failed: {ex.Message}");
        }
    }

    internal void ReapplyMode()
    {
        if (!IsCustom) return;
        try
        {
            _wmi.SetFanMode(_bit, AcerWmi.ModeCustom, AcerWmi.ModeCustom);
            _lastWritten = -1; // force a speed rewrite after a mode change
        }
        catch (Exception ex)
        {
            _plugin.Log($"{Name}: enable custom mode failed: {ex.Message}");
        }
    }

    internal void Apply(bool emergency)
    {
        if (_requested is not int requested) return;
        var target = emergency ? 100 : requested;
        if (target != _lastWritten) Write(target);
    }

    private void Write(int percent)
    {
        try
        {
            _wmi.SetFanSpeed(_fanId, percent);
            _lastWritten = percent;
            Value = percent;
            _failures = 0;
        }
        catch (Exception ex)
        {
            _failures++;
            _plugin.Log($"{Name}: write {percent}% failed ({_failures}): {ex.Message}");
            if (_failures >= 3)
            {
                _plugin.Log($"{Name}: too many failures, returning fan to AUTO.");
                Reset();
                _failures = 0;
            }
        }
    }
}
