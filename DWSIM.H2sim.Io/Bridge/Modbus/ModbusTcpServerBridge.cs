using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DWSIM.Interfaces;
using DWSIM.SharedClasses.SystemsOfUnits;
using NModbus;

namespace DWSIM.H2sim.Io
{
    /// <summary>H2sim as a Modbus TCP server. The PLC is the master and polls this bridge.</summary>
    public sealed class ModbusTcpServerBridge : IPlantBridge
    {
        private readonly object _statusLock = new object();
        private readonly Dictionary<string, double> _lastPublished = new Dictionary<string, double>(StringComparer.Ordinal);

        private SignalMap _map;
        private IFlowsheet _flowsheet;
        private ControlArbiter _arbiter;
        private ModbusAddressMap _addressMap;
        private ModbusDataSnapshot _snapshot;
        private TcpListener _listener;
        private IModbusSlaveNetwork _network;
        private CancellationTokenSource _cancellation;
        private readonly BridgeStatus _status = new BridgeStatus();
        private bool _disposed;

        /// <inheritdoc />
        public string Name { get; private set; } = "";

        /// <inheritdoc />
        public BridgeStatus Status
        {
            get
            {
                lock (_statusLock)
                {
                    return new BridgeStatus
                    {
                        Connected = _status.Connected,
                        Quality = _status.Quality,
                        InputCount = _status.InputCount,
                        OutputCount = _status.OutputCount,
                        LastUpdateUtc = _status.LastUpdateUtc,
                        Message = _status.Message
                    };
                }
            }
        }

        /// <inheritdoc />
        public event EventHandler<SignalErrorEventArgs> SignalError;

        /// <inheritdoc />
        public bool Connect(SignalMap map, IFlowsheet flowsheet)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ModbusTcpServerBridge));

            Disconnect();

            if (map == null || flowsheet == null)
            {
                Fail("signal map and flowsheet are required");
                return false;
            }

            var validation = SignalMapValidator.Validate(map, flowsheet);
            if (!validation.IsValid)
            {
                Fail("signal map validation failed: " + string.Join("; ", validation.Errors));
                return false;
            }

            var connection = map.Connections.FirstOrDefault(c => c.Enabled && c.Protocol == BridgeProtocol.ModbusTcpServer);
            if (connection == null)
            {
                Fail("no enabled Modbus TCP connection");
                return false;
            }

            IPAddress address;
            if (string.IsNullOrWhiteSpace(connection.Host) || connection.Host == "0.0.0.0")
                address = IPAddress.Any;
            else if (!IPAddress.TryParse(connection.Host, out address))
            {
                Fail("invalid listen address '" + connection.Host + "'");
                return false;
            }

            _map = map;
            _flowsheet = flowsheet;
            _addressMap = new ModbusAddressMap(map, connection.Name);
            _snapshot = new ModbusDataSnapshot();
            _arbiter = new ControlArbiter(flowsheet, map);
            _arbiter.SignalError += OnArbiterSignalError;
            _arbiter.ApplyOwnership();
            Name = connection.Name;

            try
            {
                _listener = new TcpListener(address, connection.Port);
                _listener.Start();

                var factory = new ModbusFactory();
                _network = factory.CreateSlaveNetwork(_listener);
                _network.AddSlave(factory.CreateSlave((byte)connection.UnitId, new ModbusSlaveDataStore(_snapshot)));

                _cancellation = new CancellationTokenSource();
                var cancellation = _cancellation;
                _ = _network.ListenAsync(cancellation.Token).ContinueWith(t =>
                {
                    if (t.IsFaulted && !cancellation.IsCancellationRequested)
                        Fail(t.Exception?.GetBaseException().Message ?? "Modbus listener stopped");
                }, TaskScheduler.Default);

                _snapshot.SetQuality(QualityCode.Good);
                SetStatus(true, QualityCode.Good, "listening on " + connection.Host + ":" + connection.Port);
                return true;
            }
            catch (Exception ex)
            {
                Disconnect();
                Fail(ex.Message);
                return false;
            }
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            try { _cancellation?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { (_network as IDisposable)?.Dispose(); } catch { }

            _cancellation?.Dispose();
            _cancellation = null;
            _network = null;
            _listener = null;

            if (_arbiter != null)
            {
                _arbiter.SignalError -= OnArbiterSignalError;
                _arbiter = null;
            }

            _snapshot?.SetQuality(QualityCode.NotConnected);
            SetStatus(false, QualityCode.NotConnected, "disconnected");
        }

        /// <inheritdoc />
        public void ApplyInputs()
        {
            if (_snapshot == null || _arbiter == null) return;

            _arbiter.BeginStep();

            foreach (var signal in _addressMap.FromPlc)
            {
                try
                {
                    _arbiter.WriteExternal(signal, ReadRaw(signal));
                }
                catch (Exception ex)
                {
                    Raise(signal, ex.Message, ex);
                }
            }

            lock (_statusLock)
            {
                _status.InputCount += _addressMap.FromPlc.Count();
                _status.LastUpdateUtc = DateTime.UtcNow;
            }
        }

        /// <inheritdoc />
        public void PublishOutputs()
        {
            if (_snapshot == null) return;

            var quality = _snapshot.Quality;

            foreach (var signal in _addressMap.ToPlc)
            {
                if (quality != QualityCode.Good)
                {
                    WriteFault(signal);
                    continue;
                }

                try
                {
                    if (!_flowsheet.SimulationObjects.ContainsKey(signal.ObjectTag)) continue;

                    var obj = _flowsheet.SimulationObjects[signal.ObjectTag];
                    var si = Convert.ToDouble(obj.GetPropertyValue(signal.Property));
                    var engineering = Converter.ConvertFromSI(signal.Unit, si);

                    if (signal.Deadband > 0.0 && _lastPublished.TryGetValue(signal.Id, out var last) &&
                        Math.Abs(engineering - last) < signal.Deadband)
                        continue;

                    _lastPublished[signal.Id] = engineering;
                    WriteOutput(signal, engineering);
                }
                catch (Exception ex)
                {
                    Raise(signal, ex.Message, ex);
                }
            }

            _snapshot.WriteCoils(ModbusAddressMap.ModelFaultCoil, new[] { quality != QualityCode.Good });

            lock (_statusLock)
            {
                _status.OutputCount += _addressMap.ToPlc.Count();
                _status.LastUpdateUtc = DateTime.UtcNow;
            }
        }

        /// <inheritdoc />
        public void SetQuality(QualityCode quality, string reason)
        {
            _snapshot?.SetQuality(quality);
            SetStatus(_status.Connected, quality, reason);

            if (quality != QualityCode.Good && _addressMap != null)
            {
                foreach (var signal in _addressMap.ToPlc) WriteFault(signal);
                _snapshot?.WriteCoils(ModbusAddressMap.ModelFaultCoil, new[] { true });
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            Disconnect();
            _disposed = true;
        }

        private double ReadRaw(Signal signal)
        {
            switch (signal.ModbusArea)
            {
                case ModbusArea.Coil:
                    return _snapshot.ReadCoils((ushort)signal.ModbusAddress, 1)[0] ? 1.0 : 0.0;
                case ModbusArea.DiscreteInput:
                    return _snapshot.ReadDiscreteInputs((ushort)signal.ModbusAddress, 1)[0] ? 1.0 : 0.0;
                case ModbusArea.InputRegister:
                    return ModbusScaler.RawToEngineering(signal, _snapshot.ReadInputRegisters((ushort)signal.ModbusAddress, (ushort)ModbusAddressMap.WordsFor(signal)));
                default:
                    return ModbusScaler.RawToEngineering(signal, _snapshot.ReadHoldingRegisters((ushort)signal.ModbusAddress, (ushort)ModbusAddressMap.WordsFor(signal)));
            }
        }

        private void WriteOutput(Signal signal, double engineering)
        {
            if (ModbusAddressMap.IsBooleanArea(signal.ModbusArea))
            {
                var bit = engineering != 0.0;
                if (signal.ModbusArea == ModbusArea.Coil)
                    _snapshot.WriteCoils((ushort)signal.ModbusAddress, new[] { bit });
                else
                    _snapshot.WriteDiscreteInputs((ushort)signal.ModbusAddress, new[] { bit });
                return;
            }

            var words = ModbusScaler.EngineeringToRaw(signal, engineering);

            if (signal.ModbusArea == ModbusArea.InputRegister)
                _snapshot.WriteInputRegisters((ushort)signal.ModbusAddress, words);
            else
                _snapshot.WriteHoldingRegisters((ushort)signal.ModbusAddress, words);
        }

        private void WriteFault(Signal signal)
        {
            if (ModbusAddressMap.IsBooleanArea(signal.ModbusArea))
            {
                var value = new[] { false };
                if (signal.ModbusArea == ModbusArea.Coil)
                    _snapshot.WriteCoils((ushort)signal.ModbusAddress, value);
                else
                    _snapshot.WriteDiscreteInputs((ushort)signal.ModbusAddress, value);
                return;
            }

            var words = Enumerable.Repeat((ushort)0x8000, ModbusAddressMap.WordsFor(signal)).ToArray();

            if (signal.ModbusArea == ModbusArea.InputRegister)
                _snapshot.WriteInputRegisters((ushort)signal.ModbusAddress, words);
            else
                _snapshot.WriteHoldingRegisters((ushort)signal.ModbusAddress, words);
        }

        private void OnArbiterSignalError(object sender, SignalErrorEventArgs e)
        {
            Raise(e.Signal, e.Message, e.Exception);
        }

        private void Raise(Signal signal, string message, Exception exception)
        {
            SignalError?.Invoke(this, new SignalErrorEventArgs
            {
                Signal = signal,
                Message = message,
                Exception = exception
            });
        }

        private void Fail(string message)
        {
            SetStatus(false, QualityCode.NotConnected, message);
            Raise(null, message, null);
        }

        private void SetStatus(bool connected, QualityCode quality, string message)
        {
            lock (_statusLock)
            {
                _status.Connected = connected;
                _status.Quality = quality;
                _status.Message = message ?? "";
            }
        }
    }
}