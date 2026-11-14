using System;

namespace DWSIM.H2sim.Io
{
    /// <summary>Direction of a mapped signal.</summary>
    public enum SignalDirection
    {
        /// <summary>The PLC writes this value into the model.</summary>
        FromPlc = 0,
        /// <summary>The model publishes this value to the PLC.</summary>
        ToPlc = 1
    }

    /// <summary>Raw value type used on the wire.</summary>
    public enum SignalDataType
    {
        Int16 = 0,
        Int32 = 1,
        Float32 = 2,
        Bool = 3
    }

    /// <summary>Word order for 32-bit Modbus values.</summary>
    public enum WordOrder
    {
        ABCD = 0,
        CDAB = 1,
        BADC = 2,
        DCBA = 3
    }

    /// <summary>What to do when an incoming value is outside the model's limits.</summary>
    public enum ClampOnWrite
    {
        /// <summary>Keep the old value and raise an alarm.</summary>
        Reject = 0,
        /// <summary>Clamp to the nearest allowed value.</summary>
        Clamp = 1
    }

    /// <summary>Modbus register area.</summary>
    public enum ModbusArea
    {
        HoldingRegister = 0,
        InputRegister = 1,
        Coil = 2,
        DiscreteInput = 3
    }

    /// <summary>One PLC point mapped to a flowsheet object property.</summary>
    public class Signal
    {
        /// <summary>Unique signal id, e.g. WE1.P_set.</summary>
        public string Id { get; set; } = "";

        /// <summary>Direction of the mapping.</summary>
        public SignalDirection Direction { get; set; } = SignalDirection.FromPlc;

        /// <summary>Flowsheet object tag, e.g. WE-1.</summary>
        public string ObjectTag { get; set; } = "";

        /// <summary>Property name on the object.</summary>
        public string Property { get; set; } = "";

        /// <summary>Engineering unit, e.g. kW or degC.</summary>
        public string Unit { get; set; } = "";

        /// <summary>Raw wire type.</summary>
        public SignalDataType Type { get; set; } = SignalDataType.Float32;

        /// <summary>Engineering value = raw value * Scale + Offset.</summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>Engineering value = raw value * Scale + Offset.</summary>
        public double Offset { get; set; } = 0.0;

        /// <summary>Only write when the value changes by more than this (engineering units).</summary>
        public double Deadband { get; set; } = 0.0;

        /// <summary>Word order for Int32/Float32 values.</summary>
        public WordOrder WordOrder { get; set; } = WordOrder.ABCD;

        /// <summary>Action on out-of-range writes.</summary>
        public ClampOnWrite ClampOnWrite { get; set; } = ClampOnWrite.Reject;

        /// <summary>Modbus register area.</summary>
        public ModbusArea ModbusArea { get; set; } = ModbusArea.HoldingRegister;

        /// <summary>Zero-based Modbus address. Serialized one-based in the flowsheet.</summary>
        public int ModbusAddress { get; set; } = 0;

        /// <summary>Full OPC UA NodeId, e.g. ns=2;s=PLC.H2.WE1.P_set.</summary>
        public string OpcNodeId { get; set; } = "";

        /// <summary>Namespace URI used to build NodeIds when only the string id is given.</summary>
        public string OpcNamespaceUri { get; set; } = "urn:openplc:opcua:namespace";

        /// <summary>Name of the <see cref="ConnectionConfig"/> this signal uses. Empty infers the protocol.</summary>
        public string ConnectionName { get; set; } = "";

        /// <summary>Whether the signal is active.</summary>
        public bool Enabled { get; set; } = true;
    }
}