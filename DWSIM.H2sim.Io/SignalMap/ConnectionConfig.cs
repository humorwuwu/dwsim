using System;

namespace DWSIM.H2sim.Io
{
    /// <summary>Protocols supported by the plant bridge.</summary>
    public enum BridgeProtocol
    {
        /// <summary>H2sim is the Modbus TCP server; the PLC polls it.</summary>
        ModbusTcpServer = 0,
        /// <summary>H2sim is the OPC UA client; the PLC is the server.</summary>
        OpcUaClient = 1
    }

    /// <summary>One communication endpoint used by a signal map.</summary>
    public class ConnectionConfig
    {
        /// <summary>Unique name referenced by <see cref="Signal.ConnectionName"/>.</summary>
        public string Name { get; set; } = "";

        /// <summary>Protocol used by this endpoint.</summary>
        public BridgeProtocol Protocol { get; set; } = BridgeProtocol.ModbusTcpServer;

        /// <summary>Listen address (Modbus server) or host name.</summary>
        public string Host { get; set; } = "0.0.0.0";

        /// <summary>TCP port; 502 for Modbus, 4840 for OPC UA.</summary>
        public int Port { get; set; } = 502;

        /// <summary>Modbus unit/slave id.</summary>
        public int UnitId { get; set; } = 1;

        /// <summary>Whether the bridge should open this endpoint.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>OPC UA endpoint URL.</summary>
        public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840/openplc/opcua";

        /// <summary>OPC UA security policy name, e.g. None or Basic256Sha256.</summary>
        public string SecurityPolicy { get; set; } = "None";

        /// <summary>Use anonymous OPC UA authentication.</summary>
        public bool Anonymous { get; set; } = true;

        /// <summary>OPC UA user name (stored in the flowsheet as plain text).</summary>
        public string Username { get; set; } = "";

        /// <summary>OPC UA password (stored in the flowsheet as plain text).</summary>
        public string Password { get; set; } = "";
    }
}