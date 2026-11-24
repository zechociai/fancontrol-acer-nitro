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
| Temperature | CPU (Acer EC), GPU (Acer EC), System (Acer EC) |
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

This is the setup I run on the AN515-57. Each fan follows its own chip, and the CPU fan also watches the system NVMe, which sits under the CPU-side heatpipes.

| Curve | Type | Source | Points (°C → %) | Hysteresis (up / down, response time) |
|---|---|---|---|---|
| `CPU` | Graph | `CPU Package` | 40→25, 55→35, 65→50, 75→70, 85→90 | +2 / −4 °C, 2 s / 5 s |
| `GPU` | Graph | `GPU (Acer EC)` | 40→25, 55→35, 65→50, 72→70, 80→90, 87→100 | +2 / −4 °C, 2 s / 5 s |
| `SSD WD (sistema)` | Graph | NVMe 0 temperature | 60→20, 66→30, 72→55, 76→80, 80→100 | +2 / −3 °C, 2 s / 10 s |
| `Ventoinhas` | Mix, **Max** | `CPU` + `SSD WD (sistema)` | — | — |

| Control | Curve | Step |
|---|---|---|
| CPU fan (Acer) | `Ventoinhas` | 8% up / 4% down |
| GPU fan (Acer) | `GPU` | 8% up / 4% down |

Notes:

- `GPU (Acer EC)` is preferred over NVIDIA's own sensors because the dGPU powers down under Optimus, and its sensors then disappear.
- The SSD curve stays at 20% while the drive is in its normal range, so `CPU` usually wins the Max. It only takes over as the drive approaches its 80 °C warning threshold. Use your own drive's *Warning Temperature* from LibreHardwareMonitor or HWiNFO as the 100% point.
- The NVMe sensors only show up once storage sensors are enabled in FanControl's settings; in `userConfig.json` that is `"DisableStorageSensors": false` and `"Storage": true` under the LHM settings. Lowering the storage update interval from 60 s to 10 s makes the SSD reading close to live.
- **Display-only cards.** A Graph curve that no control uses doesn't move any fan, so it works as a temperature card on the main screen. I keep cards for the second NVMe, `CPU Core Max` (it rises before the package does) and `System (Acer EC)`. I haven't identified what the EC's "system" sensor actually measures; it reads around 90 °C at idle on this unit, so don't drive fans from it.

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
