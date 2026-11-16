# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [0.2.1] - 2026-11-16

### Added

- Bzovsky 2024 numerical regression data: 18 polarization points, 15 SEC points,
  four lumped thermal cases and a low-load gas-crossover/LEL trend test.
- OpenPLC Runtime v4 integration probes for the official Modbus `modbus_master`
  plugin and OPC UA server manager, pinned to upstream commit `9fb5e1a`.

### Fixed

- PEM exchange-current, activation and membrane-conductivity equations now follow
  the published Bzovsky 2024 expressions instead of the previous transcription errors.
- Power targets above a stack's reachable maximum now fail explicitly instead of
  silently clamping to the maximum current density.
- Stack `Current` now reports series-cell current rather than multiplying by cell count.
- OPC UA client certificates are stored under the user's local application data
  directory instead of an unwritable system-wide path.
- Plant templates now use a 900 kW PEM operating point and a 17600 cm2 alkaline
  cell area so their requested ratings are reachable.

## [0.2.0] - 2026-11-15

### Added

- Headless dynamics runner (`DWSIM.Automation.Dynamics`) with host callbacks; the water-tank sample runs 12 steps / 60 s in CI.
- Signal map (`DWSIM.H2sim.Io`) with XML persistence, validation and bridge contracts.
- PID ownership, bumpless transfer, output rate limit, setpoint ramp and back-calculation, plus `ControlArbiter`.
- Modbus TCP server bridge with scaling, word orders, quality sentinel and self-loop tests.
- OPC UA client bridge with subscriptions, writes, permission probe and reconnect; mock-server test.
- PEM and alkaline electrolyzer unit operations: polarization models, power/current closure, Faraday/SEC/purity KPIs, latched interlocks and a dynamic stack thermal model.
- `voltage`, `current` and `currentDensity` unit types.
- Windows single-file packaging (`release/windows/H2sim-<version>-windows-x64.exe`).
- PEM and alkaline electrolysis plant sample flowsheets (`PlatformFiles/Common/samples/`) with cooling, H2 purification and storage, generated and calculation-checked in CI.
- The WinForms and Eto dynamics forms now drive the headless runner; the WinForms single-step path keeps the legacy loop.

### Fixed

- Modbus scaling direction (`raw = engineering * Scale + Offset`).
- Headless `Flowsheet2.Clone()` crash when the clone has no spreadsheet workbook.
- PID controllers are wired to their objects in headless runs.
- `OnPostStep` now runs after the controllers, so bridges publish the final state.

## [0.1.0] - 2026-11-14

### Added

- 基于 DWSIM 9.0.5（commit `1abf72d1b`）派生，开始电解水制氢改造。
- 项目基建：`VERSION` 单一版本来源、`scripts/build.*`、`scripts/clean.sh`、`scripts/version.sh`。
- Windows 构建 CI（`.github/workflows/build-windows.yml`）。
