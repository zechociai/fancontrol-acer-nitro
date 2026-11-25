# FanControl.AcerNitro

A [FanControl](https://github.com/Rem0o/FanControl.Releases) plugin that reads and controls the fans of **Acer Nitro** laptops through Acer's `AcerGamingFunction` WMI interface, the same one NitroSense uses. You get real fan curves without NitroSense.

> **Unofficial.** Not affiliated with Acer or FanControl. It writes to your laptop's embedded controller, so use it at your own risk.

## Tested on

| Model | BIOS | CPU / GPU | Windows | FanControl |
|---|---|---|---|---|
| Nitro 5 AN515-57 | V1.22 | i7-11800H / RTX 3050 Laptop | 11 | V286 |

Other Nitro or Predator models that expose `AcerGamingFunction` with the same encodings may work. Please open an issue with your results.

## What it exposes

| Kind | Sensors |
|---|---|
| Temperature | CPU (Acer EC), GPU (Acer EC), System (Acer EC), plus one `ACPI <zone>` per ACPI thermal zone (`ACPI TZ00` on the AN515-57) |
| Fan speed (RPM) | CPU fan, GPU fan |
| Control (%) | CPU fan, GPU fan, each already paired with its RPM sensor so calibration works |

## Safety behavior

- **Minimum 20%:** the plugin never commands a fan below 20%, whatever the curve asks.
- **Emergency override:** if the CPU or GPU reaches **92 °C**, both controlled fans go to 100% until it drops.
- **Turbo key respected:** while the firmware is in Turbo, the plugin stops writing and resumes when Turbo ends.
- **Mode re-assert:** custom mode is re-applied every 30 s, since the firmware can drop it after sleep or a power-profile change.
- **Back to AUTO:** when a control is disabled, after 3 consecutive write failures, or when FanControl exits, the fans go back to the firmware's AUTO mode.

## Install

Requirements: FanControl (V286 or newer), the .NET 8 SDK, and FanControl running **as administrator**, which its default install does.

```powershell
git clone https://github.com/zechociai/fancontrol-acer-nitro
cd fancontrol-acer-nitro
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

`install.ps1` builds the plugin and asks for UAC once. It copies `FanControl.AcerNitro.dll` into `FanControl\Plugins` and restarts FanControl. If FanControl lives somewhere else, pass `-FanControlDir "D:\Apps\FanControl"`.

To install by hand, run `dotnet build src -c Release` and copy `src\bin\Release\net8.0-windows\FanControl.AcerNitro.dll` into `FanControl\Plugins`.

**Don't run it alongside NitroSense fan control or other fan tools** (FanCurve, NitroSense custom mode, etc.). They overwrite each other.

## Suggested setup

This is the setup I run on the AN515-57. Each fan follows its own chip. The CPU curve stays near the 20% floor up to 70 °C for quiet everyday use, then ramps steeply to 100% at 85 °C. The plugin's 92 °C emergency override still applies on top.

| Curve | Type | Source | Points (°C → %) | Hysteresis (up / down, response time) |
|---|---|---|---|---|
| `CPU` | Graph | `CPU Package` | 55→20, 65→25, 70→30, 75→55, 80→75, 85→100 | +2 / −4 °C, 2 s / 5 s |
| `GPU` | Graph | `GPU (Acer EC)` | 40→25, 55→35, 65→50, 72→70, 80→90, 87→100 | +2 / −4 °C, 2 s / 5 s |
| `SSD WD (sistema)` | Graph | `/nvme/0` | 60→20, 66→30, 72→55, 76→80, 80→100 | +2 / −3 °C, 2 s / 10 s |

| Control | Curve | Step |
|---|---|---|
| CPU fan (Acer) | `CPU` | 8% up / 4% down |
| GPU fan (Acer) | `GPU` | 8% up / 4% down |

**If the system NVMe is in the CPU-side slot.** The CPU-side M.2 slot sits under the heatpipes. A drive there can heat up while the CPU curve is still idling. When I ran the drive there, I added a Mix curve `Ventoinhas` (function **Max**, `CPU` + `SSD WD (sistema)`) and pointed the CPU fan at it, so the fan follows whichever of the two asks for more. In the cooler chipset slot the drive doesn't need it, so `SSD WD (sistema)` is a display-only card.

Notes:

- `GPU (Acer EC)` is preferred over NVIDIA's own sensors because the dGPU powers down under Optimus, and its sensors then disappear.
- In the Mix, the SSD curve stays at 20% while the drive is in its normal range, so `CPU` usually wins the Max. It only takes over as the drive approaches its 80 °C warning threshold. Use your own drive's *Warning Temperature* from LibreHardwareMonitor or HWiNFO as the 100% point.
- The NVMe sensors only show up once storage sensors are enabled in FanControl's settings; in `userConfig.json` that is `"DisableStorageSensors": false` and `"Storage": true` under the LHM settings. Lowering the storage update interval from 60 s to 10 s makes the SSD reading close to live.
- **Naming.** Sensors get nicknames for the physical slot they're in, and the drive-specific name goes only on the curve. The AN515-57 has two M.2 slots:

  | Slot | Wired to | PCIe root port | Slot type |
  |---|---|---|---|
  | M.2 slot 0 | CPU | PEG11 `9A05`, `PCIROOT(0)#PCI(0101)` | PCIe only |
  | M.2 slot 1 | Chipset (PCH) | Root Port #9 `43B0`, `PCIROOT(0)#PCI(1D00)` | PCIe or SATA |

  Both links run at PCIe 3.0 x4. The CPU slot skips the chipset-to-CPU link, but it sits under the CPU heatpipes; the chipset slot runs cooler. I now run a single drive, the WD SN530, in the chipset slot. It shows up as `/nvme/0/temperature/0` with the nickname `M.2 slot 1 (chipset)`, because the LHM index doesn't name the slot (see below). To check which port each drive is on, run this in PowerShell:

  ```powershell
  Get-PnpDevice -Class DiskDrive -PresentOnly | ForEach-Object {
    $ctrl = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_Parent).Data
    $port = (Get-PnpDeviceProperty -InstanceId $ctrl -KeyName DEVPKEY_Device_Parent).Data
    "{0} -> {1}" -f $_.FriendlyName, (Get-PnpDevice -InstanceId $port).FriendlyName
  }
  ```

  A port named `PEG` is on the CPU lanes; `PCI Express Root Port #N` is on the chipset.

  **Adding, removing or moving drives.** LHM's `/nvme/N` index is the Windows disk number (`Get-Disk`), and Windows numbers the disks that are present in PCI enumeration order at boot. With two drives, the CPU port (bus 2) comes before the chipset port, so `/nvme/0` is the CPU slot. With one drive, that drive is `/nvme/0` whichever slot it's in. So the index follows the order of the drives present, not the slot. After any hardware change:

  1. Check the new numbering with `Get-Disk | Select-Object Number, FriendlyName, Location` and the port snippet above.
  2. Fix the sensor nicknames so they name the right slot.
  3. Point each curve at the right `/nvme/N`, and adjust the points to that drive's warning threshold.
  4. Delete curves for drives that are gone. **FanControl refuses to start** ("Sensor validation failed: The following sensors are missing") while any curve still points at a missing sensor, and gives up after 6 retries. The error is in `FanControl\log.txt`.
- **Display-only cards.** A Graph curve that no control uses doesn't move any fan, so it works as a temperature card on the main screen. I keep cards for `CPU Core Max` (it rises before the package does), `System (Acer EC)` and `ACPI TZ00` (points 30→0, 80→100, so the bar fills over the board's normal range). I haven't identified what the EC's "system" sensor actually measures; it reads around 90 °C at idle on this unit, so don't drive fans from it.
- **What the EC exposes.** A scan of `GetGamingSysInfo` IDs 0x00–0x3F (command byte 0x01) on the AN515-57 returns data only for 0x01 CPU temperature, 0x02 CPU fan, 0x03 system temperature, 0x06 GPU fan and 0x0A GPU temperature, all already exposed. In a test with the CPU going from 51 to 78 °C under full load, 0x03 stayed at 90–92 °C. It doesn't follow load, so treat it as unreliable.
- **`ACPI TZ00`.** This is the board thermal zone Windows itself uses for passive cooling, around 48–51 °C on this unit. FanControl doesn't read ACPI zones natively, so the plugin adds them. It reads the *Thermal Zone Information* performance counter (`Win32_PerfRawData_Counters_ThermalZoneInformation`), which re-evaluates `_TMP` on each read, rather than `MSAcpi_ThermalZoneTemperature`, which often returns a cached value. The zone moves slowly; use it as a chassis-level reading, not a fan source.
- **Calibration (RPM × %).** Each control is paired with its RPM sensor, so FanControl's *Calibrate* works. My result on the AN515-57, in RPM:

  | Fan | 0–20% | 30% | 40% | 50% | 60% | 70% | 80% | 90% | 100% |
  |---|---|---|---|---|---|---|---|---|---|
  | CPU | 3370 | 3614 | 3834 | 4054 | 4411 | 4411 | 4598 | 4761 | 5172 |
  | GPU | 4477 | 4687 | 4918 | 5263 | 5263 | 5454 | 5660 | 5882 | 6250 |

  - **The percentage is duty, not a share of top speed.** At its 20% floor the CPU fan already spins at 3370 RPM, 65% of its 5172 RPM maximum. 30% is 3614 RPM, about 70%. Low percentages sound louder than the number suggests.
  - **0–20% read the same.** The plugin never commands less than 20%, so the fan never goes below that speed.
  - **Some neighbouring points match** (CPU 60/70%, GPU 50/60%). The EC runs the fans in a few fixed speed steps, so two nearby percentages can land on the same step.
  - **Spot check.** I read the EC directly while FanControl was running. CPU at 33–35% gave 3658–3750 RPM, within 1% of the table. GPU at 25% gave 4347–4411 RPM, about 4% below the table. The RPM reading moves in steps of about 50–60 RPM.
  - **The table doesn't change what a curve sends.** With percent-mode curves (the default), the fans get the percentage the curve asks for. The table only feeds FanControl's RPM estimates.
  - **Re-running it.** Use *Calibrate* with the laptop idle. Don't touch the window while it runs: closing FanControl or reloading the config mid-run aborts it ("was disposed during calibration" in `log.txt`), and the old table stays.

## WMI encodings used

All calls go to `root\WMI : AcerGamingFunction`.

| Method | Input | Meaning |
|---|---|---|
| `GetGamingSysInfo` | `0x0101` CPU temp, `0x0201` CPU RPM, `0x0301` system temp, `0x0601` GPU RPM, `0x0A01` GPU temp | status = `out & 0xFF`, value = `(out >> 8) & 0xFFFF` |
| `SetGamingFanBehavior` | `bitmap \| cpuMode << 16 \| gpuMode << 22` (bitmap CPU=`0x1`, GPU=`0x8`; mode 1=auto, 2=turbo, 3=custom) | switch fan mode |
| `SetGamingFanSpeed` | `percent << 8 \| fanId` (CPU=1, GPU=4) | set duty in custom mode |
| `GetGamingFanSpeed` | `fanId` | percent = `(out >> 8) & 0xFF` |
| `GetGamingFanBehavior` | bitmap | cpu = `(out >> 8) & 3`, gpu = `(out >> 14) & 3` |

## License

MIT
