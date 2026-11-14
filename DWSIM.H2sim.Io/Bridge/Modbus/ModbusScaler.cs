using System;

namespace DWSIM.H2sim.Io
{
    /// <summary>Converts between engineering values and Modbus register words.</summary>
    public static class ModbusScaler
    {
        /// <summary>Decodes the register words of one signal into an engineering value.</summary>
        public static double RawToEngineering(Signal signal, ushort[] registers)
        {
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            if (registers == null || registers.Length == 0) throw new ArgumentException("No register words.", nameof(registers));

            switch (signal.Type)
            {
                case SignalDataType.Bool:
                    return registers[0] != 0 ? 1.0 : 0.0;
                case SignalDataType.Int16:
                    return Scale((short)registers[0], signal);
                case SignalDataType.Int32:
                    RequireWords(registers, 2);
                    return Scale(unchecked((int)Combine(registers[0], registers[1], signal.WordOrder)), signal);
                case SignalDataType.Float32:
                    RequireWords(registers, 2);
                    var bits = unchecked((int)Combine(registers[0], registers[1], signal.WordOrder));
                    return Scale(BitConverter.ToSingle(BitConverter.GetBytes(bits), 0), signal);
                default:
                    throw new NotSupportedException("Unsupported signal type " + signal.Type);
            }
        }

        /// <summary>Encodes an engineering value into the register words of one signal.</summary>
        public static ushort[] EngineeringToRaw(Signal signal, double engineeringValue)
        {
            if (signal == null) throw new ArgumentNullException(nameof(signal));

            switch (signal.Type)
            {
                case SignalDataType.Bool:
                    return new[] { engineeringValue != 0.0 ? (ushort)1 : (ushort)0 };
                case SignalDataType.Int16:
                    return new[] { unchecked((ushort)(short)Math.Round(Unscale(engineeringValue, signal))) };
                case SignalDataType.Int32:
                    return Split(unchecked((uint)(int)Math.Round(Unscale(engineeringValue, signal))), signal.WordOrder);
                case SignalDataType.Float32:
                    var bits = BitConverter.ToInt32(BitConverter.GetBytes((float)Unscale(engineeringValue, signal)), 0);
                    return Split(unchecked((uint)bits), signal.WordOrder);
                default:
                    throw new NotSupportedException("Unsupported signal type " + signal.Type);
            }
        }

        private static double Scale(double raw, Signal signal)
        {
            return raw * signal.Scale + signal.Offset;
        }

        private static double Unscale(double engineering, Signal signal)
        {
            if (signal.Scale == 0.0) throw new InvalidOperationException("Signal scale is zero.");
            return (engineering - signal.Offset) / signal.Scale;
        }

        private static void RequireWords(ushort[] registers, int count)
        {
            if (registers.Length < count)
                throw new ArgumentException("Expected " + count + " register words.");
        }

        private static uint Combine(ushort first, ushort second, WordOrder order)
        {
            var b0 = (byte)(first >> 8);
            var b1 = (byte)first;
            var b2 = (byte)(second >> 8);
            var b3 = (byte)second;

            switch (order)
            {
                case WordOrder.ABCD:
                    return (uint)(b0 << 24 | b1 << 16 | b2 << 8 | b3);
                case WordOrder.CDAB:
                    return (uint)(b2 << 24 | b3 << 16 | b0 << 8 | b1);
                case WordOrder.BADC:
                    return (uint)(b1 << 24 | b0 << 16 | b3 << 8 | b2);
                case WordOrder.DCBA:
                    return (uint)(b3 << 24 | b2 << 16 | b1 << 8 | b0);
                default:
                    throw new NotSupportedException("Unsupported word order " + order);
            }
        }

        private static ushort[] Split(uint value, WordOrder order)
        {
            var b0 = (byte)(value >> 24);
            var b1 = (byte)(value >> 16);
            var b2 = (byte)(value >> 8);
            var b3 = (byte)value;

            switch (order)
            {
                case WordOrder.ABCD:
                    return new[] { (ushort)(b0 << 8 | b1), (ushort)(b2 << 8 | b3) };
                case WordOrder.CDAB:
                    return new[] { (ushort)(b2 << 8 | b3), (ushort)(b0 << 8 | b1) };
                case WordOrder.BADC:
                    return new[] { (ushort)(b1 << 8 | b0), (ushort)(b3 << 8 | b2) };
                case WordOrder.DCBA:
                    return new[] { (ushort)(b3 << 8 | b2), (ushort)(b1 << 8 | b0) };
                default:
                    throw new NotSupportedException("Unsupported word order " + order);
            }
        }
    }
}