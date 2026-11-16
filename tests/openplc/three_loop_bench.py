#!/usr/bin/env python3
"""Run the OpenPLC three-loop fixture against a deterministic Modbus plant."""

import argparse
import asyncio
import csv
import logging
import math
import time
from pathlib import Path

from pymodbus.datastore import (
    ModbusSequentialDataBlock,
    ModbusServerContext,
    ModbusSlaveContext,
)
from pymodbus.server import ServerAsyncStop, StartAsyncTcpServer


DT = 0.1
TEMPERATURE_STEP_S = 60.0
PRESSURE_STEP_S = 120.0
LEVEL_STEP_S = 180.0


async def wait_for_plc(hr_block, timeout_s):
    deadline = time.monotonic() + timeout_s
    while time.monotonic() < deadline:
        values = hr_block.getValues(1, 3)
        if any(value != 0 for value in values):
            return
        await asyncio.sleep(0.1)
    raise RuntimeError("OpenPLC did not write the three Modbus command registers")


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


def validate(samples):
    temperature_peak = maximum(samples, "temperature_pv_c", TEMPERATURE_STEP_S, PRESSURE_STEP_S)
    temperature_final = average(samples, "temperature_pv_c", 110.0, PRESSURE_STEP_S)
    pressure_final = average(samples, "pressure_pv_bar", 170.0, LEVEL_STEP_S)
    level_final = average(samples, "level_pv_pct", 230.0, 240.0)

    temperature_overshoot = (temperature_peak - 85.0) / 5.0
    temperature_error = abs(temperature_final - 85.0)
    pressure_error = abs(pressure_final - 30.0)
    level_error = abs(level_final - 70.0)

    print(
        "temperature: peak={:.3f} C, overshoot={:.2%}, steady={:.3f} C, error={:.3f} K".format(
            temperature_peak, temperature_overshoot, temperature_final, temperature_error
        )
    )
    print("pressure: steady={:.3f} bar, error={:.3f} bar".format(pressure_final, pressure_error))
    print("level: steady={:.3f} %, error={:.3f} %".format(level_final, level_error))

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


async def run(args):
    ir_block = ModbusSequentialDataBlock(1, [800, 2000, 500])
    hr_block = ModbusSequentialDataBlock(1, [0, 0, 0])
    store = ModbusSlaveContext(ir=ir_block, hr=hr_block)
    context = ModbusServerContext(slaves=store, single=True)

    server_task = asyncio.create_task(
        StartAsyncTcpServer(context=context, address=(args.host, args.port))
    )
    try:
        await asyncio.sleep(0.3)
        await wait_for_plc(hr_block, args.startup_timeout)

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

            commands = hr_block.getValues(1, 3)
            cooling_pct = max(0, min(100, commands[0] / 10.0))
            vent_pct = max(0, min(100, commands[1] / 10.0))
            drain_pct = max(0, min(100, commands[2] / 10.0))

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
            ir_block.setValues(
                1,
                [
                    int(round(temperature * 10.0)),
                    int(round(pressure * 100.0)),
                    int(round(level * 10.0)),
                ],
            )

            if not samples or elapsed - samples[-1]["time_s"] >= 0.5 - 1e-9:
                samples.append(
                    {
                        "time_s": elapsed,
                        "temperature_sp_c": 80.0 if elapsed < TEMPERATURE_STEP_S else 85.0,
                        "temperature_pv_c": temperature,
                        "pressure_sp_bar": 20.0 if elapsed < PRESSURE_STEP_S else 30.0,
                        "pressure_pv_bar": pressure,
                        "level_sp_pct": 50.0 if elapsed < LEVEL_STEP_S else 70.0,
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
            writer = csv.DictWriter(handle, fieldnames=list(samples[0]))
            writer.writeheader()
            writer.writerows(samples)

        validate(samples)
        if not math.isfinite(samples[-1]["level_pv_pct"]):
            raise RuntimeError("simulation produced a non-finite value")
        print("PASS: OpenPLC Runtime v4 three-loop virtual bench")
    finally:
        await ServerAsyncStop()
        await server_task


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=1502)
    parser.add_argument("--duration", type=float, default=240.0)
    parser.add_argument("--startup-timeout", type=float, default=90.0)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.WARNING)
    asyncio.run(run(args))


if __name__ == "__main__":
    main()
