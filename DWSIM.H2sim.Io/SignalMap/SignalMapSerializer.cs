using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using DWSIM.Interfaces;

namespace DWSIM.H2sim.Io
{
    /// <summary>Reads and writes the signal map as XML.</summary>
    public static class SignalMapSerializer
    {
        /// <summary>ExtraProperties key used to persist the map with the flowsheet.</summary>
        public const string ExtraPropertyKey = "H2sim.SignalMap";

        private const string AddressBaseAttribute = "ModbusAddressBase";

        /// <summary>Serializes a map. Modbus addresses are written one-based.</summary>
        public static string Save(SignalMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var root = new XElement("SignalMap",
                new XAttribute("Version", map.Version ?? "1"),
                new XAttribute(AddressBaseAttribute, "1"),
                new XElement("Connections", map.Connections.Select(SaveConnection)),
                new XElement("Signals", map.Signals.Select(SaveSignal)));

            return new XDocument(root).ToString();
        }

        /// <summary>Deserializes a map written by <see cref="Save"/>.</summary>
        public static SignalMap Load(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new ArgumentException("Signal map XML is empty.", nameof(xml));

            var document = XDocument.Parse(xml);
            var root = document.Root;
            if (root == null || root.Name.LocalName != "SignalMap")
                throw new FormatException("Expected a SignalMap root element.");

            var addressBase = IntAttribute(root, AddressBaseAttribute, 0);

            var map = new SignalMap
            {
                Version = Attribute(root, "Version", "1")
            };

            var connections = root.Element("Connections");
            if (connections != null)
                map.Connections.AddRange(connections.Elements("Connection").Select(LoadConnection));

            var signals = root.Element("Signals");
            if (signals != null)
                map.Signals.AddRange(signals.Elements("Signal").Select(e => LoadSignal(e, addressBase)));

            return map;
        }

        /// <summary>Stores the map in the flowsheet's ExtraProperties so it travels with the file.</summary>
        public static void SaveToFlowsheet(IFlowsheet flowsheet, SignalMap map)
        {
            if (flowsheet == null) throw new ArgumentNullException(nameof(flowsheet));
            if (map == null) throw new ArgumentNullException(nameof(map));

            var properties = (IDictionary<string, object>)flowsheet.ExtraProperties;
            properties[ExtraPropertyKey] = Save(map);
        }

        /// <summary>Reads the map from the flowsheet's ExtraProperties, or null when absent.</summary>
        public static SignalMap LoadFromFlowsheet(IFlowsheet flowsheet)
        {
            if (flowsheet == null) throw new ArgumentNullException(nameof(flowsheet));

            var properties = (IDictionary<string, object>)flowsheet.ExtraProperties;
            if (!properties.TryGetValue(ExtraPropertyKey, out var raw) || raw == null) return null;

            return Load(Convert.ToString(raw, CultureInfo.InvariantCulture));
        }

        private static XElement SaveConnection(ConnectionConfig connection)
        {
            return new XElement("Connection",
                new XAttribute("Name", connection.Name ?? ""),
                new XAttribute("Protocol", connection.Protocol),
                new XAttribute("Host", connection.Host ?? ""),
                new XAttribute("Port", connection.Port),
                new XAttribute("UnitId", connection.UnitId),
                new XAttribute("Enabled", connection.Enabled),
                new XAttribute("EndpointUrl", connection.EndpointUrl ?? ""),
                new XAttribute("SecurityPolicy", connection.SecurityPolicy ?? ""),
                new XAttribute("Anonymous", connection.Anonymous),
                new XAttribute("Username", connection.Username ?? ""),
                new XAttribute("Password", connection.Password ?? ""));
        }

        private static XElement SaveSignal(Signal signal)
        {
            return new XElement("Signal",
                new XAttribute("Id", signal.Id ?? ""),
                new XAttribute("Direction", signal.Direction),
                new XAttribute("ObjectTag", signal.ObjectTag ?? ""),
                new XAttribute("Property", signal.Property ?? ""),
                new XAttribute("Unit", signal.Unit ?? ""),
                new XAttribute("Type", signal.Type),
                new XAttribute("Scale", signal.Scale.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Offset", signal.Offset.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Deadband", signal.Deadband.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("WordOrder", signal.WordOrder),
                new XAttribute("ClampOnWrite", signal.ClampOnWrite),
                new XAttribute("ModbusArea", signal.ModbusArea),
                new XAttribute("ModbusAddress", signal.ModbusAddress + 1),
                new XAttribute("OpcNodeId", signal.OpcNodeId ?? ""),
                new XAttribute("OpcNamespaceUri", signal.OpcNamespaceUri ?? ""),
                new XAttribute("ConnectionName", signal.ConnectionName ?? ""),
                new XAttribute("Enabled", signal.Enabled));
        }

        private static ConnectionConfig LoadConnection(XElement element)
        {
            return new ConnectionConfig
            {
                Name = Attribute(element, "Name"),
                Protocol = EnumAttribute(element, "Protocol", BridgeProtocol.ModbusTcpServer),
                Host = Attribute(element, "Host", "0.0.0.0"),
                Port = IntAttribute(element, "Port", 502),
                UnitId = IntAttribute(element, "UnitId", 1),
                Enabled = BoolAttribute(element, "Enabled", true),
                EndpointUrl = Attribute(element, "EndpointUrl", "opc.tcp://localhost:4840/openplc/opcua"),
                SecurityPolicy = Attribute(element, "SecurityPolicy", "None"),
                Anonymous = BoolAttribute(element, "Anonymous", true),
                Username = Attribute(element, "Username"),
                Password = Attribute(element, "Password")
            };
        }

        private static Signal LoadSignal(XElement element, int addressBase)
        {
            return new Signal
            {
                Id = Attribute(element, "Id"),
                Direction = EnumAttribute(element, "Direction", SignalDirection.FromPlc),
                ObjectTag = Attribute(element, "ObjectTag"),
                Property = Attribute(element, "Property"),
                Unit = Attribute(element, "Unit"),
                Type = EnumAttribute(element, "Type", SignalDataType.Float32),
                Scale = DoubleAttribute(element, "Scale", 1.0),
                Offset = DoubleAttribute(element, "Offset", 0.0),
                Deadband = DoubleAttribute(element, "Deadband", 0.0),
                WordOrder = EnumAttribute(element, "WordOrder", WordOrder.ABCD),
                ClampOnWrite = EnumAttribute(element, "ClampOnWrite", ClampOnWrite.Reject),
                ModbusArea = EnumAttribute(element, "ModbusArea", ModbusArea.HoldingRegister),
                ModbusAddress = IntAttribute(element, "ModbusAddress", addressBase) - addressBase,
                OpcNodeId = Attribute(element, "OpcNodeId"),
                OpcNamespaceUri = Attribute(element, "OpcNamespaceUri", "urn:openplc:opcua:namespace"),
                ConnectionName = Attribute(element, "ConnectionName"),
                Enabled = BoolAttribute(element, "Enabled", true)
            };
        }

        private static string Attribute(XElement element, string name, string fallback = "")
        {
            return (string)element.Attribute(name) ?? fallback;
        }

        private static int IntAttribute(XElement element, string name, int fallback)
        {
            var value = (string)element.Attribute(name);
            if (string.IsNullOrEmpty(value)) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                throw new FormatException(string.Format("Invalid integer attribute '{0}' in {1}.", name, element.Name.LocalName));
            return parsed;
        }

        private static double DoubleAttribute(XElement element, string name, double fallback)
        {
            var value = (string)element.Attribute(name);
            if (string.IsNullOrEmpty(value)) return fallback;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                throw new FormatException(string.Format("Invalid number attribute '{0}' in {1}.", name, element.Name.LocalName));
            return parsed;
        }

        private static bool BoolAttribute(XElement element, string name, bool fallback)
        {
            var value = (string)element.Attribute(name);
            if (string.IsNullOrEmpty(value)) return fallback;
            if (!bool.TryParse(value, out var parsed))
                throw new FormatException(string.Format("Invalid boolean attribute '{0}' in {1}.", name, element.Name.LocalName));
            return parsed;
        }

        private static T EnumAttribute<T>(XElement element, string name, T fallback) where T : struct
        {
            var value = (string)element.Attribute(name);
            if (string.IsNullOrEmpty(value)) return fallback;
            if (!Enum.TryParse<T>(value, true, out var parsed))
                throw new FormatException(string.Format("Invalid value '{0}' for attribute '{1}' in {2}.", value, name, element.Name.LocalName));
            return parsed;
        }
    }
}