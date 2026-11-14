using System;

namespace DWSIM.H2sim.Io
{
    /// <summary>Session diagnostics for the OPC UA client.</summary>
    public class OpcUaDiagnostics
    {
        /// <summary>Whether the session is currently connected.</summary>
        public bool SessionConnected { get; set; }

        /// <summary>Reconnect attempts since the bridge was created.</summary>
        public int ReconnectCount { get; set; }

        /// <summary>Last keep-alive received, UTC.</summary>
        public DateTime LastKeepAliveUtc { get; set; } = DateTime.MinValue;

        /// <summary>Last publish, UTC.</summary>
        public DateTime LastPublishUtc { get; set; } = DateTime.MinValue;

        /// <summary>Duration of the last write batch, milliseconds.</summary>
        public double LastWriteLatencyMs { get; set; }

        /// <summary>Last error message.</summary>
        public string LastError { get; set; } = "";
    }
}