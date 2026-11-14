using System.Collections.Generic;

namespace DWSIM.H2sim.Io
{
    /// <summary>Connections and point mappings persisted with a flowsheet.</summary>
    public class SignalMap
    {
        /// <summary>Schema version.</summary>
        public string Version { get; set; } = "1";

        /// <summary>Communication endpoints.</summary>
        public List<ConnectionConfig> Connections { get; set; } = new List<ConnectionConfig>();

        /// <summary>Point mappings.</summary>
        public List<Signal> Signals { get; set; } = new List<Signal>();
    }
}