using System;

namespace DWSIM.H2sim.Io
{
    /// <summary>
    /// Thread-safe image of the Modbus register and coil areas shared by the NModbus
    /// callbacks and the model thread.
    /// </summary>
    public sealed class ModbusDataSnapshot
    {
        private const int Size = 65536;

        private readonly object _lock = new object();
        private readonly ushort[] _holding = new ushort[Size];
        private readonly ushort[] _input = new ushort[Size];
        private readonly bool[] _coils = new bool[Size];
        private readonly bool[] _discrete = new bool[Size];

        /// <summary>Quality of the published values.</summary>
        public QualityCode Quality { get; private set; } = QualityCode.NotConnected;

        /// <summary>Sets the quality code.</summary>
        public void SetQuality(QualityCode quality)
        {
            lock (_lock) Quality = quality;
        }

        /// <summary>Reads holding registers.</summary>
        public ushort[] ReadHoldingRegisters(ushort start, ushort count) => ReadRegisters(_holding, start, count);

        /// <summary>Reads input registers.</summary>
        public ushort[] ReadInputRegisters(ushort start, ushort count) => ReadRegisters(_input, start, count);

        /// <summary>Writes holding registers.</summary>
        public void WriteHoldingRegisters(ushort start, ushort[] values) => WriteRegisters(_holding, start, values);

        /// <summary>Writes input registers.</summary>
        public void WriteInputRegisters(ushort start, ushort[] values) => WriteRegisters(_input, start, values);

        /// <summary>Reads coils.</summary>
        public bool[] ReadCoils(ushort start, ushort count) => ReadBits(_coils, start, count);

        /// <summary>Reads discrete inputs.</summary>
        public bool[] ReadDiscreteInputs(ushort start, ushort count) => ReadBits(_discrete, start, count);

        /// <summary>Writes coils.</summary>
        public void WriteCoils(ushort start, bool[] values) => WriteBits(_coils, start, values);

        /// <summary>Writes discrete inputs.</summary>
        public void WriteDiscreteInputs(ushort start, bool[] values) => WriteBits(_discrete, start, values);

        private ushort[] ReadRegisters(ushort[] area, ushort start, ushort count)
        {
            var result = new ushort[count];
            lock (_lock)
            {
                for (var i = 0; i < count; i++)
                {
                    var address = (int)start + i;
                    if (address < Size) result[i] = area[address];
                }
            }
            return result;
        }

        private void WriteRegisters(ushort[] area, ushort start, ushort[] values)
        {
            lock (_lock)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    var address = (int)start + i;
                    if (address < Size) area[address] = values[i];
                }
            }
        }

        private bool[] ReadBits(bool[] area, ushort start, ushort count)
        {
            var result = new bool[count];
            lock (_lock)
            {
                for (var i = 0; i < count; i++)
                {
                    var address = (int)start + i;
                    if (address < Size) result[i] = area[address];
                }
            }
            return result;
        }

        private void WriteBits(bool[] area, ushort start, bool[] values)
        {
            lock (_lock)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    var address = (int)start + i;
                    if (address < Size) area[address] = values[i];
                }
            }
        }
    }
}