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

On the Nitro the CPU and GPU share heatpipes, so it works well to drive **both** fans from the hotter of the two:

1. Create a **Graph** curve called `CPU` on `CPU Package`, for example 40→25%, 55→35%, 65→50%, 75→70%, 85→90%, 92→100%.
2. Create a **Graph** curve called `GPU` on `GPU (Acer EC)`, for example 40→25%, 55→35%, 65→50%, 72→70%, 80→90%, 87→100%.
3. Create a **Mix** curve with function **Max** over `CPU` and `GPU`, and select it on both controls.
4. Set hysteresis to about +2 °C / −4 °C and the step to 8% up / 4% down for smooth changes.

`GPU (Acer EC)` is preferred over NVIDIA's own sensors because the dGPU powers down under Optimus, and its sensors then disappear.

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
