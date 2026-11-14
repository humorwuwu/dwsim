using System;
using System.Collections.Generic;
using System.Linq;

namespace DWSIM.H2sim.Io
{
    /// <summary>Selects the Modbus signals of one connection and maps them to register counts.</summary>
    public sealed class ModbusAddressMap
    {
        /// <summary>Coil used to report a model-side fault. Documented in docs/io-map.md.</summary>
        public const ushort ModelFaultCoil = 0;

        private readonly List<Signal> _signals;

        /// <summary>Creates the map for one Modbus connection.</summary>
        public ModbusAddressMap(SignalMap map, string connectionName)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            _signals = map.Signals
                .Where(s => s.Enabled && IsModbusSignal(s, connectionName))
                .ToList();
        }

        /// <summary>Enabled Modbus signals.</summary>
        public IReadOnlyList<Signal> Signals => _signals;

        /// <summary>Signals written by the PLC.</summary>
        public IEnumerable<Signal> FromPlc => _signals.Where(s => s.Direction == SignalDirection.FromPlc);

        /// <summary>Signals published to the PLC.</summary>
        public IEnumerable<Signal> ToPlc => _signals.Where(s => s.Direction == SignalDirection.ToPlc);

        /// <summary>Register words occupied by the signal (32-bit values use two).</summary>
        public static int WordsFor(Signal signal)
        {
            if (signal.ModbusArea == ModbusArea.Coil || signal.ModbusArea == ModbusArea.DiscreteInput) return 1;
            return signal.Type == SignalDataType.Int32 || signal.Type == SignalDataType.Float32 ? 2 : 1;
        }

        /// <summary>True for the bit-oriented areas.</summary>
        public static bool IsBooleanArea(ModbusArea area)
        {
            return area == ModbusArea.Coil || area == ModbusArea.DiscreteInput;
        }

        private static bool IsModbusSignal(Signal signal, string connectionName)
        {
            if (!string.IsNullOrEmpty(signal.ConnectionName))
                return string.Equals(signal.ConnectionName, connectionName, StringComparison.Ordinal);

            return string.IsNullOrEmpty(signal.OpcNodeId);
        }
    }
}