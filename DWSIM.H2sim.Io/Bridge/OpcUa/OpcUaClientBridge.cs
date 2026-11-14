using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DWSIM.Interfaces;
using DWSIM.SharedClasses.SystemsOfUnits;
using Opc.Ua;
using Opc.Ua.Client;

namespace DWSIM.H2sim.Io
{
    /// <summary>H2sim as an OPC UA client connected to the PLC (OpenPLC v4) server.</summary>
    public sealed class OpcUaClientBridge : IPlantBridge
    {
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<string, SignalValue> _latest = new ConcurrentDictionary<string, SignalValue>(StringComparer.Ordinal);
        private readonly BridgeStatus _status = new BridgeStatus();

        private SignalMap _map;
        private IFlowsheet _flowsheet;
        private ConnectionConfig _connection;
        private ControlArbiter _arbiter;
        private OpcUaSubscriptionManager _subscription;
        private Session _session;
        private CancellationTokenSource _cancellation;
        private bool _reconnecting;
        private bool _disposed;

        /// <inheritdoc />
        public string Name { get; private set; } = "";

        /// <inheritdoc />
        public BridgeStatus Status
        {
            get
            {
                lock (_lock)
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

        /// <summary>Session and write diagnostics.</summary>
        public OpcUaDiagnostics Diagnostics { get; } = new OpcUaDiagnostics();

        /// <inheritdoc />
        public event EventHandler<SignalErrorEventArgs> SignalError;

        /// <inheritdoc />
        public bool Connect(SignalMap map, IFlowsheet flowsheet)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(OpcUaClientBridge));

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

            _connection = map.Connections.FirstOrDefault(c => c.Enabled && c.Protocol == BridgeProtocol.OpcUaClient);
            if (_connection == null)
            {
                Fail("no enabled OPC UA connection");
                return false;
            }

            _map = map;
            _flowsheet = flowsheet;
            Name = _connection.Name;
            _arbiter = new ControlArbiter(flowsheet, map);
            _arbiter.SignalError += OnArbiterSignalError;
            _arbiter.ApplyOwnership();
            _cancellation = new CancellationTokenSource();

            try
            {
                return OpenSession();
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
                return false;
            }
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            try { _cancellation?.Cancel(); } catch { }
            _cancellation?.Dispose();
            _cancellation = null;

            if (_arbiter != null)
            {
                _arbiter.SignalError -= OnArbiterSignalError;
                _arbiter = null;
            }

            CloseSession();
            SetStatus(false, QualityCode.NotConnected, "disconnected");
        }

        /// <inheritdoc />
        public void ApplyInputs()
        {
            if (_arbiter == null || _map == null) return;

            _arbiter.BeginStep();

            foreach (var signal in _map.Signals.Where(s => s.Enabled && s.Direction == SignalDirection.FromPlc))
            {
                if (!_latest.TryGetValue(signal.Id, out var value)) continue;
                if (value.Quality == QualityCode.Bad) continue;

                var engineering = signal.Type == SignalDataType.Bool
                    ? (value.Value != 0.0 ? 1.0 : 0.0)
                    : (value.Value - signal.Offset) / signal.Scale;

                _arbiter.WriteExternal(signal, engineering);
            }

            lock (_lock)
            {
                _status.InputCount += _map.Signals.Count(s => s.Enabled && s.Direction == SignalDirection.FromPlc);
                _status.LastUpdateUtc = DateTime.UtcNow;
            }
        }

        /// <inheritdoc />
        public void PublishOutputs()
        {
            var session = _session;
            if (session == null || _flowsheet == null || _map == null) return;

            var quality = _status.Quality;
            var writes = new WriteValueCollection();
            var signals = new List<Signal>();

            foreach (var signal in _map.Signals.Where(s => s.Enabled && s.Direction == SignalDirection.ToPlc))
            {
                try
                {
                    if (!_flowsheet.SimulationObjects.ContainsKey(signal.ObjectTag)) continue;

                    var obj = _flowsheet.SimulationObjects[signal.ObjectTag];
                    var si = Convert.ToDouble(obj.GetPropertyValue(signal.Property), CultureInfo.InvariantCulture);
                    var engineering = Converter.ConvertFromSI(signal.Unit, si);
                    var raw = engineering * signal.Scale + signal.Offset;

                    var dataValue = new DataValue(
                        new Variant(Encode(signal, raw)),
                        quality == QualityCode.Good ? StatusCodes.Good : StatusCodes.BadInternalError,
                        DateTime.UtcNow);

                    writes.Add(new WriteValue
                    {
                        NodeId = Resolve(signal, session),
                        AttributeId = Attributes.Value,
                        Value = dataValue
                    });
                    signals.Add(signal);
                }
                catch (Exception ex)
                {
                    Raise(signal, ex.Message, ex);
                }
            }

            if (writes.Count == 0) return;

            try
            {
                var stopwatch = Stopwatch.StartNew();
                // shortcut: synchronous write on the model thread; swap to WriteAsync if a step ever stalls
                session.Write(null, writes, out var results, out _);
                stopwatch.Stop();
                Diagnostics.LastWriteLatencyMs = stopwatch.Elapsed.TotalMilliseconds;
                Diagnostics.LastPublishUtc = DateTime.UtcNow;

                for (var i = 0; i < results.Count && i < signals.Count; i++)
                {
                    if (StatusCode.IsBad(results[i]))
                        Raise(signals[i], "OPC UA write failed: " + results[i], null);
                }

                lock (_lock)
                {
                    _status.OutputCount += writes.Count;
                    _status.LastUpdateUtc = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                SetQuality(QualityCode.Bad, ex.Message);
                Raise(null, ex.Message, ex);
            }
        }

        /// <inheritdoc />
        public void SetQuality(QualityCode quality, string reason)
        {
            SetStatus(_status.Connected, quality, reason);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            Disconnect();
            _disposed = true;
        }

        private bool OpenSession()
        {
            CloseSession();

            var factory = new OpcUaSessionFactory();
            var session = factory.CreateSession(_connection, OnKeepAlive);
            Diagnostics.SessionConnected = true;

            var first = _map.Signals.FirstOrDefault(s => s.Enabled && s.Direction == SignalDirection.FromPlc);
            if (first != null)
            {
                var nodeId = Resolve(first, session);
                var value = session.ReadValue(nodeId);
                if (StatusCode.IsBad(value.StatusCode))
                {
                    session.Close();
                    Fail("probe read failed: " + value.StatusCode);
                    return false;
                }

                var probe = new WriteValueCollection
                {
                    new WriteValue { NodeId = nodeId, AttributeId = Attributes.Value, Value = value }
                };
                session.Write(null, probe, out var results, out _);
                if (results.Count == 0 || StatusCode.IsBad(results[0]))
                {
                    session.Close();
                    Fail("permission: writing a FromPlc node failed; use an operator or engineer account");
                    return false;
                }
            }

            var fromPlc = _map.Signals.Where(s => s.Enabled && s.Direction == SignalDirection.FromPlc).ToList();
            var subscription = new OpcUaSubscriptionManager(session, fromPlc, OnSubscriptionValue);
            subscription.Start();

            lock (_lock)
            {
                _session = session;
                _subscription = subscription;
                _status.Connected = true;
                _status.Quality = QualityCode.Good;
                _status.Message = "connected";
            }

            return true;
        }

        private void CloseSession()
        {
            OpcUaSubscriptionManager subscription;
            Session session;
            lock (_lock)
            {
                subscription = _subscription;
                session = _session;
                _subscription = null;
                _session = null;
            }

            try { subscription?.Dispose(); } catch { }
            try { session?.Close(); } catch { }
            try { session?.Dispose(); } catch { }
            Diagnostics.SessionConnected = false;
            _latest.Clear();
        }

        private void OnSubscriptionValue(Signal signal, DataValue value)
        {
            var quality = MapQuality(value.StatusCode);

            double raw;
            try
            {
                raw = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
            }
            catch
            {
                _latest[signal.Id] = new SignalValue(0.0, QualityCode.Bad, DateTime.UtcNow);
                return;
            }

            _latest[signal.Id] = new SignalValue(raw, quality, DateTime.UtcNow);
        }

        private void OnKeepAlive(Session session, KeepAliveEventArgs e)
        {
            Diagnostics.LastKeepAliveUtc = DateTime.UtcNow;

            if (ServiceResult.IsBad(e.Status))
            {
                SetStatus(false, QualityCode.NotConnected, "keep-alive failed");
                StartReconnect();
            }
        }

        private void StartReconnect()
        {
            if (_reconnecting || _disposed) return;
            _reconnecting = true;

            Task.Run(async () =>
            {
                var delay = 1000;
                var cancellation = _cancellation;
                while (!_disposed && cancellation != null && !cancellation.IsCancellationRequested)
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    if (_disposed) break;

                    try
                    {
                        if (OpenSession()) break;
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.LastError = ex.Message;
                    }

                    Diagnostics.ReconnectCount++;
                    delay = Math.Min(delay * 2, 30000);
                }

                _reconnecting = false;
            });
        }

        private NodeId Resolve(Signal signal, Session session)
        {
            return OpcUaNodeResolver.Resolve(signal.OpcNodeId, signal.OpcNamespaceUri, session.NamespaceUris);
        }

        private static object Encode(Signal signal, double raw)
        {
            switch (signal.Type)
            {
                case SignalDataType.Int16: return unchecked((short)Math.Round(raw));
                case SignalDataType.Int32: return unchecked((int)Math.Round(raw));
                case SignalDataType.Float32: return (float)raw;
                case SignalDataType.Bool: return raw != 0.0;
                default: return raw;
            }
        }

        private static QualityCode MapQuality(StatusCode status)
        {
            if (StatusCode.IsGood(status)) return QualityCode.Good;
            if (StatusCode.IsUncertain(status)) return QualityCode.Uncertain;
            return QualityCode.Bad;
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
            Diagnostics.LastError = message;
            SetStatus(false, QualityCode.NotConnected, message);
            Raise(null, message, null);
        }

        private void SetStatus(bool connected, QualityCode quality, string message)
        {
            lock (_lock)
            {
                _status.Connected = connected;
                _status.Quality = quality;
                _status.Message = message ?? "";
            }
        }

        private sealed class SignalValue
        {
            public SignalValue(double value, QualityCode quality, DateTime timestamp)
            {
                Value = value;
                Quality = quality;
                Timestamp = timestamp;
            }

            public double Value { get; }
            public QualityCode Quality { get; }
            public DateTime Timestamp { get; }
        }
    }
}