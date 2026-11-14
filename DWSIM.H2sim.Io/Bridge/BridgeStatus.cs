using System;

namespace DWSIM.H2sim.Io
{
    /// <summary>Connection state and counters for a plant bridge.</summary>
    public class BridgeStatus
    {
        /// <summary>Whether the endpoint is connected.</summary>
        public bool Connected { get; set; }

        /// <summary>Current value quality.</summary>
        public QualityCode Quality { get; set; } = QualityCode.NotConnected;

        /// <summary>Values received from the PLC.</summary>
        public long InputCount { get; set; }

        /// <summary>Values published to the PLC.</summary>
        public long OutputCount { get; set; }

        /// <summary>Last successful exchange, UTC.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.MinValue;

        /// <summary>Last status or error message.</summary>
        public string Message { get; set; } = "";
    }

    /// <summary>Raised when a signal cannot be read, written or validated.</summary>
    public class SignalErrorEventArgs : EventArgs
    {
        /// <summary>The signal involved, when known.</summary>
        public Signal Signal { get; set; }

        /// <summary>Human-readable message.</summary>
        public string Message { get; set; } = "";

        /// <summary>Underlying exception, when any.</summary>
        public Exception Exception { get; set; }
    }
}