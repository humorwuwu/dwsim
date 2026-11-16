#!/usr/bin/env python3
"""Run the OpenPLC Runtime v4 OPC UA server against an in-memory REAL."""

import argparse
import asyncio
import ctypes
import os
import sys
import tempfile
from types import SimpleNamespace


class ProbeRuntimeArgs:
    STATUS_OK = 0x7E

    def __init__(self):
        self.value = 5.0

    @staticmethod
    def _int(value):
        return int(value.value if hasattr(value, "value") else value)

    def debug_size(self, arr, elem):
        return 4 if (self._int(arr) == 0 and self._int(elem) == 0) else 0

    def debug_array_count(self):
        return 1

    def debug_read(self, arr, elem, destination):
        if self._int(arr) != 0 or self._int(elem) != 0:
            return 0
        ctypes.cast(destination, ctypes.POINTER(ctypes.c_float)).contents.value = self.value
        return 4

    def debug_write(self, arr, elem, source, length):
        if self._int(arr) != 0 or self._int(elem) != 0 or self._int(length) != 4:
            return 0
        self.value = ctypes.cast(source, ctypes.POINTER(ctypes.c_float)).contents.value
        return self.STATUS_OK

    def debug_set(self, arr, elem, forcing, source, length):
        return 0


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
    plugin_dir = os.path.join(plugin_root, "opcua")
    for path in (plugin_dir, plugin_root):
        if path not in sys.path:
            sys.path.insert(0, path)

    from server import OpcuaServerManager
    from shared.plugin_config_decode.opcua_config_model import OpcuaConfig

    config = OpcuaConfig.from_dict(
        {
            "format_version": 2,
            "server": {
                "name": "OpenPLC OPC UA Probe",
                "application_uri": "urn:h2sim:openplc:probe",
                "product_uri": "urn:openplc:runtime:product",
                "endpoint_url": "opc.tcp://{}:{}/openplc/opcua".format(
                    args.host, args.port
                ),
                "security_profiles": [
                    {
                        "name": "insecure",
                        "enabled": True,
                        "security_policy": "None",
                        "security_mode": "None",
                        "auth_methods": ["Anonymous"],
                        "anonymous_role": "viewer",
                    }
                ],
            },
            "security": {
                "server_certificate_strategy": "auto_self_signed",
                "trusted_client_certificates": [],
            },
            "users": [],
            "cycle_time_ms": 100,
            "address_space": {
                "namespace_uri": "urn:openplc:opcua:namespace",
                "variables": [
                    {
                        "node_id": "PLC.H2.P_set",
                        "browse_name": "P_set",
                        "display_name": "Pressure setpoint",
                        "datatype": "REAL",
                        "description": "H2sim OPC UA probe variable",
                        "arr": 0,
                        "elem": 0,
                        "size": 4,
                        "permissions": {
                            "viewer": "rw",
                            "operator": "rw",
                            "engineer": "rw",
                        },
                    }
                ],
                "structures": [],
                "arrays": [],
            },
        }
    )

    runtime_args = ProbeRuntimeArgs()
    buffer_accessor = SimpleNamespace(runtime_args=runtime_args)
    with tempfile.TemporaryDirectory(prefix="h2sim-openplc-opcua-") as cert_dir:
        manager = OpcuaServerManager(config, buffer_accessor, cert_dir)
        try:
            asyncio.run(manager.run())
        except KeyboardInterrupt:
            pass
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
