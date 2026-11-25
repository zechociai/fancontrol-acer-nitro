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
- **Emergency override:** if the CPU or GPU (Acer EC readings) stays at or above **92 °C for 3 s**, or reaches **97 °C** at all, both controlled fans go to 100% until it drops below 92 °C. The 3 s hold is there because turbo bursts make the EC's CPU reading jump for a second or two; I measured 58 → 78 °C in 0.2 s, back down 1.3 s later. Without the hold, those spikes kept triggering 1–3 s blasts at 100%.
  - **Load tests (AN515-57, suggested setup below).** I read the EC every 0.2 s during each test. The first two rows are from before the GPU fan watched the CPU (GPU fan on the `GPU` curve only); the last two are with `GPU + CPU quente`.

    | Load | CPU (EC) | GPU (EC) | Notes |
    |---|---|---|---|
    | All 16 CPU threads at 100%, 3 min | 43 → 70 °C in 30 s, then 73–75 °C | ≤ 45 °C | CPU fan followed the curve from 3370 to 4054 RPM (about 50%), back to the floor afterwards |
    | WebGL shader in Chrome, RTX 3050 at 100% / 67–75 W, about 90 s | 85–91 °C average, 92 °C peak (11 readings, none held 3 s) | 58–64 °C (`nvidia-smi` 59–64 °C) | CPU fan at 4761–5263 RPM (90–100%) from the curve alone; back to 3370 RPM about 30 s after the load ended. Emergency never triggered |
    | Same WebGL test, with `GPU + CPU quente`, about 90 s of load | 88 °C average, 92 °C peak; 88–92 °C without a break for 36 s | 57 °C average, 63 °C peak | Both fans at full speed (CPU 5172, GPU 6250 RPM) within 10 s. The emergency triggered once 92 °C held for 3 s and cleared about 6 s later. CPU back to about 50 °C within 30 s of the load ending |
    | Same 16-thread CPU test, with `GPU + CPU quente`, 3 min | 72 °C average, 75 °C peak | ≤ 47 °C | CPU fan at 4054 RPM; the GPU fan stayed at its floor (4477 RPM), so the mix adds no noise under CPU-only load |

    The GPU test is the one that heats the CPU. Both chips share the heatpipes, and the browser also loads the CPU. The CPU reading sat at 88–92 °C in stretches of 2–10 s, so the threshold sits right at the top of what the GPU test reaches. With the GPU fan also at full speed, the CPU reading was no lower than before (88 °C average either way): both fans are at their maximum, so the shared heatpipes are the limit. The WebGL load lasted only 90–160 s per run because Chrome itself drops the page's WebGL context (`webglcontextlost`) when frames this heavy make it think the GPU hung. Windows logged no driver reset, the GPU was at 65 °C, and launching Chrome so it keeps drawing covered windows didn't change it. That is still long enough for temperatures to level off and for the emergency to trigger. The i7-11800H's throttle point (TjMax) is 100 °C, so 90 °C under combined load is within spec. In a game (Overwatch) the GPU tops out around 67 °C. The old 1–3 s blasts came about every 65 s, which looks more like a periodic bad EC reading than real heat; the hold filters those out too.
  - The log line for an emergency includes the reading that caused it, e.g. `Emergency: 93 C (>= 97 C, or >= 92 C for 3 s), forcing 100%.`
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

This is the setup I run on the AN515-57, tuned from the load tests above. Each fan follows its own chip, and also steps in when the other chip gets hot, because they share the heatpipes. The CPU fan also watches the SSD, which sits in the CPU-side slot. The CPU curve stays near the 20% floor up to 70 °C for quiet everyday use, then ramps steeply to 100% at 85 °C. The plugin's 92 °C emergency override still applies on top.

| Curve | Type | Source | Points (°C → %) | Hysteresis (up / down, response time) |
|---|---|---|---|---|
| `CPU` | Graph | `CPU Package` | 55→20, 65→25, 70→30, 75→55, 80→75, 85→100 | +2 / −4 °C, 2 s / 5 s |
| `GPU` | Graph | `GPU (Acer EC)` | 40→25, 55→35, 65→50, 72→70, 80→90, 87→100 | +2 / −4 °C, 2 s / 5 s |
| `SSD WD (sistema)` | Graph | `/nvme/0` | 60→20, 66→30, 72→55, 76→80, 80→100 | +2 / −3 °C, 2 s / 10 s |
| `CPU quente` | Graph | `CPU Package` | 80→20, 85→50, 90→100 | +2 / −4 °C, 2 s / 5 s |
| `GPU quente` | Graph | `GPU (Acer EC)` | 70→20, 78→60, 85→100 | +2 / −4 °C, 2 s / 5 s |
| `GPU + CPU quente` | Mix (**Max**) | `GPU` + `CPU quente` | — | — |
| `CPU + GPU quente + SSD` | Mix (**Max**) | `CPU` + `GPU quente` + `SSD WD (sistema)` | — | — |

| Control | Curve | Step |
|---|---|---|
| CPU fan (Acer) | `CPU + GPU quente + SSD` | 8% up / 4% down |
| GPU fan (Acer) | `GPU + CPU quente` | 8% up / 4% down |

**Why the GPU fan also watches the CPU.** The CPU and GPU share the heatpipes. In the GPU load test (see Safety behavior), the GPU's heat pushed the CPU to 88–92 °C while the GPU itself stayed at 64 °C. The CPU fan was already at full speed, but the GPU fan, following its own curve, was nowhere near it. `CPU quente` stays at 20% up to 80 °C, so it never wins the Max at idle or under CPU-only load (73–75 °C in the CPU test), and the GPU fan stays quiet there. Above 80 °C it takes over and brings the GPU fan to 100% at 90 °C, so both fans pull heat off the shared heatpipes.

**Why the CPU fan also watches the GPU.** The same thing, the other way round. `GPU quente` stays at 20% up to 70 °C, so in a game (Overwatch, GPU up to about 67 °C) it doesn't add noise. It only takes over if the GPU gets well above what games reached in testing, so the CPU fan helps then too. In the WebGL test the CPU fan was already at 100% from its own curve, so this curve is a safety margin, not a measured gain.

**Why the CPU fan also watches the SSD.** The system NVMe is in the CPU-side M.2 slot, which sits under the heatpipes. A drive there can heat up while the CPU curve is still idling, so `SSD WD (sistema)` is the third input of the CPU fan's Mix. If the drive is in the cooler chipset slot, drop it from the Mix and keep it as a display-only card.

**What more tuning can't fix.** With both fans at 100% the CPU still reads 88–92 °C under a heavy GPU load. That is the limit of the shared heatpipes, not of the curves. Ramping the fans earlier wouldn't lower that steady state, only make everyday use louder, so the curves stay quiet up to about 70 °C.

Notes:

- `GPU (Acer EC)` is preferred over NVIDIA's own sensors because the dGPU powers down under Optimus, and its sensors then disappear.
- In the Mix, the SSD curve stays at 20% while the drive is in its normal range, so `CPU` usually wins the Max. It only takes over as the drive approaches its 80 °C warning threshold. Use your own drive's *Warning Temperature* from LibreHardwareMonitor or HWiNFO as the 100% point.
- The NVMe sensors only show up once storage sensors are enabled in FanControl's settings; in `userConfig.json` that is `"DisableStorageSensors": false` and `"Storage": true` under the LHM settings. Lowering the storage update interval from 60 s to 10 s makes the SSD reading close to live.
- **Naming.** Sensors get nicknames for the physical slot they're in, and the drive-specific name goes only on the curve. The AN515-57 has two M.2 slots:

  | Slot | Wired to | PCIe root port | Slot type |
  |---|---|---|---|
  | M.2 slot 0 | CPU | PEG11 `9A05`, `PCIROOT(0)#PCI(0101)` | PCIe only |
  | M.2 slot 1 | Chipset (PCH) | Root Port #9 `43B0`, `PCIROOT(0)#PCI(1D00)` | PCIe or SATA |

  Both links run at PCIe 3.0 x4. The CPU slot skips the chipset-to-CPU link, but it sits under the CPU heatpipes; the chipset slot runs cooler. I now run a single drive, the WD SN530, in the CPU-side slot (under the heatpipes). It shows up as `/nvme/0/temperature/0` with the nickname `M.2 slot 0 (CPU)`, because the LHM index doesn't name the slot (see below). With the WD there, Windows reports its port as `PCI Express Root Port #9 - 43B0`, which doesn't match the table above. So the table's port-to-slot mapping is unconfirmed. Go by where the drive physically sits; that is what matters for heat. To check which port each drive is on, run this in PowerShell:

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
