# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [0.3.0] - 2026-11-23

### Added

- Alkaline stack voltage is calibrated to a 30 wt% KOH / 80 °C industrial envelope
  (about 1.76 V at 0.4 A/cm²), with a 6-point regression. The same envelope,
  converted with Faraday's law at `etaF_ref = 0.99`, is the alkaline SEC reference.
- Faraday efficiency follows the load as `i / (i + i_cross)`. Each electrolyzer
  type carries its own `i_ref`.
- Gas crossover scales with cathode pressure and temperature and is written into
  the outlet streams. O2-in-H2, H2-in-O2 and hydrogen purity are read back from
  those streams.
- Plant templates cool the saturated cathode gas to 40 °C and flash it before
  purification, then compress the product in two stages to 90 bar and 250 bar
  (75% isentropic, interstage cool to 40 °C). Coolant flow is sized from the
  stack cooling duty, and each template has a dynamics schedule.
- The hydrogen tank uses the property-package compressibility factor. The
  purifier reports both product purity and product impurity.
- A numerical baseline for the six dynamic samples, and a check of the stack's
  ambient heat-loss share.
- Non-Windows builds of the dynamics tests, and an experimental macOS dmg
  packaging script. The macOS GUI is not runnable yet (see
  `docs/verification.md`, Gate 0 V-3).

### Fixed

- A power target below the stack minimum is rejected instead of being clamped
  to the lowest operating point.
- In dynamics, a cooler with an outlet-temperature, temperature-change or
  outlet-vapor-fraction setpoint holds that setpoint.
- The upstream PFR dynamic sample uses a catalyst void fraction of 0.4, so the
  residence time is no longer zero.
- `RunOptions.StepSeconds` is applied to the integrator.
- Generated flowsheets save the external unit operation description on its
  graphic, so loading matches the electrolyzer, purifier and tank instead of
  rebuilding generic connectors.

## [0.2.1] - 2026-11-16

### Added

- Bzovsky 2024 numerical regression data: 18 polarization points, 15 SEC points,
  four lumped thermal cases and a low-load gas-crossover/LEL trend test.
- OpenPLC Runtime v4 integration probes for the official Modbus `modbus_master`
  plugin and OPC UA server manager, pinned to upstream commit `9fb5e1a`.
- A three-loop OpenPLC Runtime v4 PLC fixture for temperature, pressure and
  level control, compiled in CI with the pinned OpenPLC Editor 4.3.5 toolchain
  and exercised against a 300-second Modbus virtual bench.
- An OPC UA version of the three-loop fixture and a Modbus parity run whose
  temperature, pressure and level results agree within 0.25% of their spans.

### Fixed

- PEM exchange-current, activation and membrane-conductivity equations now follow
  the published Bzovsky 2024 expressions instead of the previous transcription errors.
- Alkaline electrolyte conductivity now uses the R9/Gambou 2022 KOH correlation
  with validity-range checks and numerical regression against its published curve.
- Power targets above a stack's reachable maximum now fail explicitly instead of
  silently clamping to the maximum current density.
- Stack `Current` now reports series-cell current rather than multiplying by cell count.
- OPC UA client certificates are stored under the user's local application data
  directory instead of an unwritable system-wide path.
- Plant templates now use a 900 kW PEM operating point and a 2950 kW alkaline
  operating point with a 17600 cm2 cell area so their requested ratings remain
  reachable across property packages.

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
