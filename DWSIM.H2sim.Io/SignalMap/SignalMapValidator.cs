using System;
using System.Collections.Generic;
using System.Linq;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;

namespace DWSIM.H2sim.Io
{
    /// <summary>Result of validating a signal map.</summary>
    public sealed class SignalMapValidationResult
    {
        /// <summary>Human-readable errors; empty when the map is valid.</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>True when <see cref="Errors"/> is empty.</summary>
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>Checks a signal map before a bridge is connected.</summary>
    public static class SignalMapValidator
    {
        /// <summary>
        /// Validates structural rules always, and object/property existence when a flowsheet is supplied.
        /// Disabled signals are ignored for mapping and address rules but still need unique ids.
        /// </summary>
        public static SignalMapValidationResult Validate(SignalMap map, IFlowsheet flowsheet = null)
        {
            var result = new SignalMapValidationResult();
            if (map == null)
            {
                result.Errors.Add("Signal map is null.");
                return result;
            }

            ValidateConnections(map, result);
            ValidateIds(map, result);

            var enabled = map.Signals.Where(s => s.Enabled).ToList();

            ValidateScale(enabled, result);
            ValidateObjectProperties(enabled, flowsheet, result);
            ValidateFromPlcOwnership(enabled, result);
            ValidateModbusAddresses(map, enabled, result);
            ValidateOpcNodeIds(map, enabled, result);
            ValidateCrossProtocol(map, enabled, result);

            return result;
        }

        private static void ValidateConnections(SignalMap map, SignalMapValidationResult result)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var connection in map.Connections)
            {
                if (string.IsNullOrWhiteSpace(connection.Name))
                {
                    result.Errors.Add("connection with an empty Name");
                }
                else if (!names.Add(connection.Name))
                {
                    result.Errors.Add(string.Format("duplicate connection name '{0}'", connection.Name));
                }

                if (connection.Protocol == BridgeProtocol.ModbusTcpServer && connection.Port <= 0)
                    result.Errors.Add(string.Format("connection '{0}' has an invalid Modbus port", connection.Name));

                if (connection.Protocol == BridgeProtocol.OpcUaClient && string.IsNullOrWhiteSpace(connection.EndpointUrl))
                    result.Errors.Add(string.Format("connection '{0}' has an empty OPC UA endpoint", connection.Name));

                if (!connection.Anonymous && string.IsNullOrEmpty(connection.Username))
                    result.Errors.Add(string.Format("connection '{0}' requires a username for OPC UA authentication", connection.Name));
            }
        }

        private static void ValidateIds(SignalMap map, SignalMapValidationResult result)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var signal in map.Signals)
            {
                if (string.IsNullOrWhiteSpace(signal.Id))
                    result.Errors.Add("signal with an empty Id");
                else if (!ids.Add(signal.Id))
                    result.Errors.Add(string.Format("duplicate signal id '{0}'", signal.Id));
            }
        }

        private static void ValidateScale(IEnumerable<Signal> signals, SignalMapValidationResult result)
        {
            foreach (var signal in signals)
            {
                if (signal.Type != SignalDataType.Bool && signal.Scale == 0.0)
                    result.Errors.Add(string.Format("signal '{0}' has Scale 0", signal.Id));
            }
        }

        private static void ValidateObjectProperties(IEnumerable<Signal> signals, IFlowsheet flowsheet, SignalMapValidationResult result)
        {
            if (flowsheet == null) return;

            foreach (var signal in signals)
            {
                if (string.IsNullOrWhiteSpace(signal.ObjectTag))
                {
                    result.Errors.Add(string.Format("signal '{0}' has an empty ObjectTag", signal.Id));
                    continue;
                }

                if (!flowsheet.SimulationObjects.ContainsKey(signal.ObjectTag))
                {
                    result.Errors.Add(string.Format("signal '{0}' references unknown object '{1}'", signal.Id, signal.ObjectTag));
                    continue;
                }

                var obj = flowsheet.SimulationObjects[signal.ObjectTag];
                var properties = obj.GetProperties(PropertyType.ALL);

                if (Array.IndexOf(properties, signal.Property) < 0 && !obj.IsDynamicProperty(signal.Property))
                {
                    result.Errors.Add(string.Format("signal '{0}' references unknown property '{1}' on '{2}'",
                        signal.Id, signal.Property, signal.ObjectTag));
                }
            }
        }

        private static void ValidateFromPlcOwnership(IEnumerable<Signal> signals, SignalMapValidationResult result)
        {
            var groups = signals
                .Where(s => s.Direction == SignalDirection.FromPlc)
                .GroupBy(s => s.ObjectTag + "\u0000" + s.Property);

            foreach (var group in groups.Where(g => g.Count() > 1))
            {
                result.Errors.Add(string.Format(
                    "conflict: property '{0}' on '{1}' is written by more than one FromPlc signal ({2})",
                    group.First().Property, group.First().ObjectTag,
                    string.Join(", ", group.Select(s => s.Id))));
            }
        }

        private static void ValidateModbusAddresses(SignalMap map, IEnumerable<Signal> signals, SignalMapValidationResult result)
        {
            var occupied = new Dictionary<ModbusArea, Dictionary<int, string>>();

            foreach (var signal in signals.Where(s => ProtocolOf(s, map) == BridgeProtocol.ModbusTcpServer))
            {
                if (signal.ModbusAddress < 0)
                {
                    result.Errors.Add(string.Format("signal '{0}' has a negative Modbus address", signal.Id));
                    continue;
                }

                if (!occupied.TryGetValue(signal.ModbusArea, out var addresses))
                {
                    addresses = new Dictionary<int, string>();
                    occupied.Add(signal.ModbusArea, addresses);
                }

                var words = WordsFor(signal);
                for (var offset = 0; offset < words; offset++)
                {
                    var address = signal.ModbusAddress + offset;
                    if (addresses.TryGetValue(address, out var owner))
                    {
                        result.Errors.Add(string.Format(
                            "address overlap in {0} at {1}: signals '{2}' and '{3}'",
                            signal.ModbusArea, address, owner, signal.Id));
                    }
                    else
                    {
                        addresses.Add(address, signal.Id);
                    }
                }
            }
        }

        private static void ValidateOpcNodeIds(SignalMap map, IEnumerable<Signal> signals, SignalMapValidationResult result)
        {
            var nodes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var signal in signals.Where(s => ProtocolOf(s, map) == BridgeProtocol.OpcUaClient))
            {
                if (string.IsNullOrWhiteSpace(signal.OpcNodeId))
                {
                    result.Errors.Add(string.Format("OPC UA signal '{0}' has an empty NodeId", signal.Id));
                    continue;
                }

                if (nodes.TryGetValue(signal.OpcNodeId, out var owner))
                    result.Errors.Add(string.Format("duplicate OPC UA NodeId '{0}' on signals '{1}' and '{2}'", signal.OpcNodeId, owner, signal.Id));
                else
                    nodes.Add(signal.OpcNodeId, signal.Id);
            }
        }

        private static void ValidateCrossProtocol(SignalMap map, IEnumerable<Signal> signals, SignalMapValidationResult result)
        {
            var hasModbus = map.Connections.Any(c => c.Enabled && c.Protocol == BridgeProtocol.ModbusTcpServer);
            var hasOpcUa = map.Connections.Any(c => c.Enabled && c.Protocol == BridgeProtocol.OpcUaClient);
            if (!hasModbus || !hasOpcUa) return;

            var groups = signals
                .Where(s => s.Direction == SignalDirection.FromPlc)
                .GroupBy(s => s.ObjectTag + "\u0000" + s.Property);

            foreach (var group in groups)
            {
                var protocols = group.Select(s => ProtocolOf(s, map)).Distinct().ToList();
                if (protocols.Contains(BridgeProtocol.ModbusTcpServer) && protocols.Contains(BridgeProtocol.OpcUaClient))
                {
                    result.Errors.Add(string.Format(
                        "cross-protocol conflict: property '{0}' on '{1}' is written over both Modbus and OPC UA ({2})",
                        group.First().Property, group.First().ObjectTag,
                        string.Join(", ", group.Select(s => s.Id))));
                }
            }
        }

        private static BridgeProtocol? ProtocolOf(Signal signal, SignalMap map)
        {
            if (!string.IsNullOrEmpty(signal.ConnectionName))
            {
                var connection = map.Connections.FirstOrDefault(c => c.Name == signal.ConnectionName);
                return connection?.Protocol;
            }

            return string.IsNullOrEmpty(signal.OpcNodeId)
                ? BridgeProtocol.ModbusTcpServer
                : BridgeProtocol.OpcUaClient;
        }

        private static int WordsFor(Signal signal)
        {
            if (signal.ModbusArea == ModbusArea.Coil || signal.ModbusArea == ModbusArea.DiscreteInput) return 1;
            return signal.Type == SignalDataType.Int32 || signal.Type == SignalDataType.Float32 ? 2 : 1;
        }
    }
}