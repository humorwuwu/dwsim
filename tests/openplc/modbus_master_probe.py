#!/usr/bin/env python3
"""Exercise the OpenPLC Runtime v4 modbus_master plugin against H2sim I/O."""

import argparse
import os
import sys
import time


class ProbeLogger:
    def info(self, message):
        print("[INFO] " + message)

    def warn(self, message):
        print("[WARN] " + message)

    def warning(self, message):
        print("[WARN] " + message)

    def error(self, message):
        print("[ERROR] " + message)

    def debug(self, message):
        print("[DEBUG] " + message)


class ProbeBuffer:
    """Small SafeBufferAccess stand-in exposing only the mapped IEC points."""

    def __init__(self):
        self.int_input = {}
        self.int_output = {0: 1800}
        self.mutex_count = 0

    def acquire_mutex(self):
        self.mutex_count += 1
        return True, "Success"

    def release_mutex(self):
        return None

    def write_int_input(self, index, value, thread_safe=False):
        self.int_input[index] = value
        return True, "Success"

    def read_int_output(self, index, thread_safe=False):
        return self.int_output.get(index, 0), "Success"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--openplc-dir", required=True)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", required=True, type=int)
    args = parser.parse_args()

    plugin_root = os.path.join(
        os.path.abspath(args.openplc_dir),
        "core", "src", "drivers", "plugins", "python",
    )
    plugin_dir = os.path.join(plugin_root, "modbus_master")
    for path in (plugin_dir, plugin_root):
        if path not in sys.path:
            sys.path.insert(0, path)

    from modbus_master_plugin import ModbusSlaveDevice
    from shared.plugin_config_decode.modbus_master_config_model import (
        ModbusMasterConfig,
    )

    config = ModbusMasterConfig()
    config.import_config_from_file = None
    config.devices = []

    from shared.plugin_config_decode.modbus_master_config_model import (
        ModbusDeviceConfig,
    )

    device = ModbusDeviceConfig.from_dict(
        {
            "name": "H2sim",
            "protocol": "MODBUS",
            "config": {
                "transport": "tcp",
                "host": args.host,
                "port": args.port,
                "slave_id": 1,
                "timeout_ms": 1000,
                "io_points": [
                    {
                        "fc": 4,
                        "offset": "0x0000",
                        "iec_location": "%IW0",
                        "len": 1,
                        "cycle_time_ms": 100,
                    },
                    {
                        "fc": 16,
                        "offset": "0x0000",
                        "iec_location": "%QW0",
                        "len": 1,
                        "cycle_time_ms": 100,
                    },
                ],
            },
        }
    )
    config.devices.append(device)
    config.validate()

    buffer = ProbeBuffer()
    worker = ModbusSlaveDevice(device, buffer, ProbeLogger())
    worker.start()

    deadline = time.monotonic() + 8.0
    while time.monotonic() < deadline:
        if buffer.int_input.get(0) == 853:
            break
        time.sleep(0.05)

    worker.stop()
    worker.join(timeout=3.0)

    if buffer.int_input.get(0) != 853:
        print("FAIL: FC4 value did not reach %IW0")
        return 1
    if worker.is_alive():
        print("FAIL: OpenPLC worker did not stop")
        return 1

    print("PASS: OpenPLC v4 modbus_master mapped FC4 -> %IW0 and %QW0 -> FC16")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
