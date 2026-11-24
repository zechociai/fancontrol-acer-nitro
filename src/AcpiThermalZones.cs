using System;
using System.Collections.Generic;
using System.Management;

namespace FanControl.AcerNitro;

/// <summary>
/// Reads the ACPI thermal zones (\_TZ.*) through the "Thermal Zone Information" performance counters,
/// which re-evaluate _TMP on each read. MSAcpi_ThermalZoneTemperature is avoided because it often returns
/// a value cached at the last ACPI notification.
/// </summary>
internal static class AcpiThermalZones
{
    private const string Query = "SELECT Name, HighPrecisionTemperature FROM Win32_PerfRawData_Counters_ThermalZoneInformation";

    /// <summary>Returns zone name (e.g. "TZ00") → temperature in °C.</summary>
    public static Dictionary<string, float> ReadAll()
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        using var searcher = new ManagementObjectSearcher(@"root\CIMV2", Query);
        foreach (ManagementObject obj in searcher.Get())
        {
            using (obj)
            {
                var name = obj["Name"] as string;
                if (string.IsNullOrEmpty(name)) continue;
                var tenthsKelvin = Convert.ToDouble(obj["HighPrecisionTemperature"]);
                if (tenthsKelvin <= 0) continue;
                result[ShortName(name)] = (float)Math.Round(tenthsKelvin / 10.0 - 273.15, 1);
            }
        }
        return result;
    }

    // "\_TZ.TZ00" → "TZ00"
    private static string ShortName(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name.TrimStart('\\');
    }
}
