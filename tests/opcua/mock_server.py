"""Minimal OPC UA server used by the CI closed-loop test.

Run: python mock_server.py
Endpoint: opc.tcp://127.0.0.1:4840/h2sim/
Namespace: urn:openplc:opcua:namespace
Variables: P_set (Float, initial 1.8), Out (Float, initial 0)
"""

import asyncio

from asyncua import Server, ua


async def main():
    server = Server()
    await server.init()
    server.set_endpoint("opc.tcp://127.0.0.1:4840/h2sim/")
    server.set_server_name("H2sim mock PLC")
    server.set_security_policy([ua.SecurityPolicyType.NoSecurity])
    server.set_identity_tokens([ua.AnonymousIdentityToken])

    index = await server.register_namespace("urn:openplc:opcua:namespace")
    plc = await server.nodes.objects.add_object(index, "PLC")
    setpoint = await plc.add_variable(index, "P_set", 1.8, ua.VariantType.Float)
    output = await plc.add_variable(index, "Out", 0.0, ua.VariantType.Float)
    await setpoint.set_writable()
    await output.set_writable()

    async with server:
        print("mock OPC UA server ready", flush=True)
        while True:
            await asyncio.sleep(1)


if __name__ == "__main__":
    asyncio.run(main())