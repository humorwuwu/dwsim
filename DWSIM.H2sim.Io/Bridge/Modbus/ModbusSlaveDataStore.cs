using System;
using NModbus;

namespace DWSIM.H2sim.Io
{
    /// <summary>Adapts <see cref="ModbusDataSnapshot"/> to the NModbus slave data store.</summary>
    public sealed class ModbusSlaveDataStore : ISlaveDataStore
    {
        private readonly ModbusDataSnapshot _snapshot;

        /// <summary>Creates a data store backed by the shared snapshot.</summary>
        public ModbusSlaveDataStore(ModbusDataSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

            CoilDiscretes = new SnapshotPointSource<bool>(_snapshot.ReadCoils, _snapshot.WriteCoils);
            CoilInputs = new SnapshotPointSource<bool>(_snapshot.ReadDiscreteInputs, _snapshot.WriteDiscreteInputs);
            HoldingRegisters = new SnapshotPointSource<ushort>(_snapshot.ReadHoldingRegisters, _snapshot.WriteHoldingRegisters);
            InputRegisters = new SnapshotPointSource<ushort>(_snapshot.ReadInputRegisters, _snapshot.WriteInputRegisters);
        }

        /// <inheritdoc />
        public IPointSource<bool> CoilDiscretes { get; }

        /// <inheritdoc />
        public IPointSource<bool> CoilInputs { get; }

        /// <inheritdoc />
        public IPointSource<ushort> HoldingRegisters { get; }

        /// <inheritdoc />
        public IPointSource<ushort> InputRegisters { get; }

        private sealed class SnapshotPointSource<T> : IPointSource<T>
        {
            private readonly Func<ushort, ushort, T[]> _read;
            private readonly Action<ushort, T[]> _write;

            public SnapshotPointSource(Func<ushort, ushort, T[]> read, Action<ushort, T[]> write)
            {
                _read = read;
                _write = write;
            }

            public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
            {
                return _read(startAddress, numberOfPoints);
            }

            public void WritePoints(ushort startAddress, T[] points)
            {
                _write(startAddress, points);
            }
        }
    }
}