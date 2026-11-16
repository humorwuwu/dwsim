#!/usr/bin/env python3
"""Run the PLC three-loop fixture through OpenPLC's OPC UA server."""

import argparse
import asyncio
import csv
import math
import time
from pathlib import Path

from asyncua import Client


DT = 0.1
TEMPERATURE_STEP_S = 60.0
PRESSURE_STEP_S = 120.0
LEVEL_STEP_S = 180.0


NODE_PATHS = {
    "temperature_sp": "PLC.H2sim.Temperature_SP",
    "temperature_pv": "PLC.H2sim.Temperature_PV",
    "cooling": "PLC.H2sim.Cooling_Output",
    "pressure_sp": "PLC.H2sim.Pressure_SP",
    "pressure_pv": "PLC.H2sim.Pressure_PV",
    "backpressure": "PLC.H2sim.Backpressure_Output",
    "level_sp": "PLC.H2sim.Level_SP",
    "level_pv": "PLC.H2sim.Level_PV",
    "level_command": "PLC.H2sim.Level_Command",
    "cycle_count": "PLC.H2sim.Cycle_Count",
}


async def connect_nodes(url, timeout_s):
    deadline = time.monotonic() + timeout_s
    last_error = None
    while time.monotonic() < deadline:
        client = Client(url)
        try:
            await client.connect()
            namespace = await client.get_namespace_index("urn:h2sim:opcua")
            nodes = {
                key: client.get_node(f"ns={namespace};s={value}")
                for key, value in NODE_PATHS.items()
            }
            for node in nodes.values():
                await node.read_value()
            return client, nodes
        except Exception as error:  # noqa: BLE001 - startup retry reports the final failure
            last_error = error
            try:
                await client.disconnect()
            except Exception:
                pass
            await asyncio.sleep(0.5)
    raise RuntimeError("OpenPLC OPC UA server did not become ready: {}".format(last_error))


def average(samples, field, start_s, end_s):
    values = [
        sample[field]
        for sample in samples
        if start_s <= sample["time_s"] <= end_s
    ]
    return sum(values) / max(len(values), 1)


def maximum(samples, field, start_s, end_s):
    values = [
        sample[field]
        for sample in samples
        if start_s <= sample["time_s"] <= end_s
    ]
    return max(values) if values else float("-inf")


def response_metrics(samples):
    temperature_peak = maximum(samples, "temperature_pv_c", TEMPERATURE_STEP_S, PRESSURE_STEP_S)
    return {
        "temperature_peak": temperature_peak,
        "temperature_final": average(samples, "temperature_pv_c", 110.0, PRESSURE_STEP_S),
        "pressure_final": average(samples, "pressure_pv_bar", 170.0, LEVEL_STEP_S),
        "level_final": average(samples, "level_pv_pct", 290.0, 300.0),
    }


def validate(metrics):
    temperature_overshoot = (metrics["temperature_peak"] - 85.0) / 5.0
    temperature_error = abs(metrics["temperature_final"] - 85.0)
    pressure_error = abs(metrics["pressure_final"] - 30.0)
    level_error = abs(metrics["level_final"] - 70.0)

    print(
        "temperature: peak={:.3f} C, overshoot={:.2%}, steady={:.3f} C, error={:.3f} K".format(
            metrics["temperature_peak"],
            temperature_overshoot,
            metrics["temperature_final"],
            temperature_error,
        )
    )
    print(
        "pressure: steady={:.3f} bar, error={:.3f} bar".format(
            metrics["pressure_final"], pressure_error
        )
    )
    print(
        "level: steady={:.3f} %, error={:.3f} %".format(
            metrics["level_final"], level_error
        )
    )

    failures = []
    if temperature_overshoot > 0.05:
        failures.append("temperature overshoot exceeds 5%")
    if temperature_error > 0.5:
        failures.append("temperature steady-state error exceeds 0.5 K")
    if pressure_error > 0.5:
        failures.append("pressure steady-state error exceeds 0.5 bar")
    if level_error > 1.0:
        failures.append("level steady-state error exceeds 1%")
    if failures:
        raise RuntimeError("; ".join(failures))


def load_reference(path):
    with Path(path).open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    for row in rows:
        for key, value in list(row.items()):
            row[key] = float(value)
    return rows


def compare_with_modbus(opcua_metrics, modbus_samples):
    modbus_metrics = response_metrics(modbus_samples)
    comparisons = {
        "temperature_peak": 5.0,
        "temperature_final": 5.0,
        "pressure_final": 10.0,
        "level_final": 20.0,
    }
    failures = []
    for key, span in comparisons.items():
        difference = abs(opcua_metrics[key] - modbus_metrics[key])
        relative = difference / span
        print(
            "parity {}: Modbus={:.4f}, OPC UA={:.4f}, difference={:.4f} ({:.3%} of span)".format(
                key, modbus_metrics[key], opcua_metrics[key], difference, relative
            )
        )
        if relative >= 0.02:
            failures.append("{} parity differs by at least 2%".format(key))
    if failures:
        raise RuntimeError("; ".join(failures))


async def run(args):
    client, nodes = await connect_nodes(args.url, args.startup_timeout)
    try:
        await nodes["cycle_count"].write_value(0)
        await asyncio.sleep(0.2)
        temperature = 80.0
        pressure = 20.0
        level = 50.0
        samples = []
        started = time.monotonic()
        next_tick = started

        while True:
            elapsed = time.monotonic() - started
            if elapsed >= args.duration:
                break

            values = {}
            for key, node in nodes.items():
                values[key] = float(await node.read_value())

            cooling_pct = max(0.0, min(100.0, values["cooling"]))
            vent_pct = max(0.0, min(100.0, values["backpressure"]))
            drain_pct = max(0.0, min(100.0, values["level_command"]))

            temperature += (
                0.2
                - 0.2 * cooling_pct / 100.0
                - 0.002 * (temperature - 25.0)
            ) * DT
            pressure += (0.5 - 0.02 - 0.8 * vent_pct / 100.0) * DT
            level += (0.3 - 0.4 * drain_pct / 100.0) * DT

            temperature = max(0.0, min(200.0, temperature))
            pressure = max(0.0, min(50.0, pressure))
            level = max(0.0, min(100.0, level))
            await nodes["temperature_pv"].write_value(temperature)
            await nodes["pressure_pv"].write_value(pressure)
            await nodes["level_pv"].write_value(level)

            if not samples or elapsed - samples[-1]["time_s"] >= 0.5 - 1e-9:
                samples.append(
                    {
                        "time_s": elapsed,
                        "temperature_sp_c": values["temperature_sp"],
                        "temperature_pv_c": temperature,
                        "pressure_sp_bar": values["pressure_sp"],
                        "pressure_pv_bar": pressure,
                        "level_sp_pct": values["level_sp"],
                        "level_pv_pct": level,
                        "cooling_command_pct": cooling_pct,
                        "vent_command_pct": vent_pct,
                        "drain_command_pct": drain_pct,
                    }
                )

            next_tick += DT
            await asyncio.sleep(max(0.0, next_tick - time.monotonic()))

        report = Path(args.report)
        report.parent.mkdir(parents=True, exist_ok=True)
        with report.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=list(samples[0]), lineterminator="\n")
            writer.writeheader()
            writer.writerows(samples)

        if not all(
            math.isfinite(sample[field])
            for sample in samples
            for field in ("temperature_pv_c", "pressure_pv_bar", "level_pv_pct")
        ):
            raise RuntimeError("simulation produced a non-finite value")

        metrics = response_metrics(samples)
        validate(metrics)
        compare_with_modbus(metrics, load_reference(args.reference_report))
        print("PASS: OpenPLC Runtime v4 OPC UA three-loop parity bench")
    finally:
        await client.disconnect()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="opc.tcp://127.0.0.1:4840/openplc/opcua")
    parser.add_argument("--duration", type=float, default=300.0)
    parser.add_argument("--startup-timeout", type=float, default=90.0)
    parser.add_argument("--report", required=True)
    parser.add_argument("--reference-report", required=True)
    args = parser.parse_args()
    asyncio.run(run(args))


if __name__ == "__main__":
    main()
