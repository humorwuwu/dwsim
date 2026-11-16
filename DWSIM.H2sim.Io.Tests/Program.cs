using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Opc.Ua;
using DWSIM.H2sim.Io;
using NModbus;

namespace DWSIM.H2sim.Io.Tests
{
    internal static class Program
    {
        private static int _failures;

        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--openplc-modbus-probe")
                return TestOpenPlcModbusMasterProbe(args[1], args.Length >= 3 ? args[2] : null);
            if (args.Length >= 2 && args[0] == "--openplc-opcua-probe")
                return TestOpenPlcOpcUaServerProbe(args[1], args.Length >= 3 ? args[2] : null);

            TestRoundTrip();
            TestDuplicateId();
            TestFromPlcConflict();
            TestAddressOverlap();
            TestCrossProtocol();
            TestScalerRoundTrip();
            TestModbusSelfLoop();

            Console.WriteLine(_failures == 0 ? "PASS" : "FAIL");
            return _failures == 0 ? 0 : 1;
        }

        private static int TestOpenPlcModbusMasterProbe(string openPlcRoot, string pythonExecutable)
        {
            var scriptCandidates = new[]
            {
                Path.Combine("openplc", "modbus_master_probe.py"),
                Path.Combine("tests", "openplc", "modbus_master_probe.py")
            };
            var script = Array.Find(scriptCandidates, File.Exists);
            if (script == null)
            {
                Console.Error.WriteLine("FAIL: OpenPLC Modbus probe script not found.");
                return 1;
            }

            var snapshot = new ModbusDataSnapshot();
            snapshot.WriteInputRegisters(0, new[] { (ushort)853 });

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var factory = new ModbusFactory();
            var network = factory.CreateSlaveNetwork(listener);
            network.AddSlave(factory.CreateSlave((byte)1, new ModbusSlaveDataStore(snapshot)));
            var cancellation = new CancellationTokenSource();
            var listenTask = network.ListenAsync(cancellation.Token);

            try
            {
                Thread.Sleep(150);
                var executable = pythonExecutable;
                if (string.IsNullOrWhiteSpace(executable))
                    executable = Environment.GetEnvironmentVariable("PYTHON_EXE") ??
                                 (Environment.OSVersion.Platform == PlatformID.Win32NT ? "python" : "python3");

                var arguments = Quote(script) + " --openplc-dir " + Quote(openPlcRoot) +
                                " --port " + port.ToString(CultureInfo.InvariantCulture);
                Console.WriteLine("OpenPLC v4 Modbus master probe: {0} {1}", executable, arguments);

                using (var process = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                }))
                {
                    if (process == null)
                    {
                        Console.Error.WriteLine("FAIL: could not start OpenPLC probe.");
                        return 1;
                    }

                    if (!process.WaitForExit(20000))
                    {
                        try { process.Kill(); } catch { }
                        Console.Error.WriteLine("FAIL: OpenPLC probe timed out.");
                        return 1;
                    }

                    if (process.ExitCode != 0)
                    {
                        Console.Error.WriteLine("FAIL: OpenPLC probe exited with " + process.ExitCode);
                        return 1;
                    }
                }

                var written = snapshot.ReadHoldingRegisters(0, 1);
                if (written.Length != 1 || written[0] != 1800)
                {
                    Console.Error.WriteLine("FAIL: OpenPLC master did not write holding register 0.");
                    return 1;
                }

                Console.WriteLine("PASS: OpenPLC v4 modbus_master read FC4 and wrote FC16.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: OpenPLC probe exception: " + ex);
                return 1;
            }
            finally
            {
                cancellation.Cancel();
                try { listener.Stop(); } catch { }
                try { (network as IDisposable)?.Dispose(); } catch { }
                try { listenTask.Wait(2000); } catch { }
                cancellation.Dispose();
            }
        }

        private static int TestOpenPlcOpcUaServerProbe(string openPlcRoot, string pythonExecutable)
        {
            var scriptCandidates = new[]
            {
                Path.Combine("openplc", "opcua_server_probe.py"),
                Path.Combine("tests", "openplc", "opcua_server_probe.py")
            };
            var script = Array.Find(scriptCandidates, File.Exists);
            if (script == null)
            {
                Console.Error.WriteLine("FAIL: OpenPLC OPC UA probe script not found.");
                return 1;
            }

            var portProbe = new TcpListener(IPAddress.Loopback, 0);
            portProbe.Start();
            var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();

            var executable = pythonExecutable;
            if (string.IsNullOrWhiteSpace(executable))
                executable = Environment.GetEnvironmentVariable("PYTHON_EXE") ??
                             (Environment.OSVersion.Platform == PlatformID.Win32NT ? "python" : "python3");

            var arguments = Quote(script) + " --openplc-dir " + Quote(openPlcRoot) +
                            " --port " + port.ToString(CultureInfo.InvariantCulture);
            Console.WriteLine("OpenPLC v4 OPC UA server probe: {0} {1}", executable, arguments);

            Process process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                });
                if (process == null)
                {
                    Console.Error.WriteLine("FAIL: could not start OpenPLC OPC UA probe.");
                    return 1;
                }

                var connection = new ConnectionConfig
                {
                    Name = "OpenPLC-v4",
                    Protocol = BridgeProtocol.OpcUaClient,
                    EndpointUrl = "opc.tcp://127.0.0.1:" +
                                  port.ToString(CultureInfo.InvariantCulture) + "/openplc/opcua",
                    SecurityPolicy = "None",
                    Anonymous = true
                };
                const string namespaceUri = "urn:openplc:opcua:namespace";

                var factory = new OpcUaSessionFactory();
                var initial = double.NaN;
                var deadline = DateTime.UtcNow.AddSeconds(15);
                Exception lastError = null;
                while (DateTime.UtcNow < deadline && !process.HasExited)
                {
                    try
                    {
                        initial = factory.ReadDouble(connection, "PLC.H2.P_set", namespaceUri);
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Thread.Sleep(250);
                    }
                }

                if (process.HasExited)
                {
                    Console.Error.WriteLine("FAIL: OpenPLC OPC UA server exited with " + process.ExitCode);
                    return 1;
                }
                if (double.IsNaN(initial) || Math.Abs(initial - 5.0) > 1e-6)
                {
                    Console.Error.WriteLine("FAIL: initial OPC UA value was " + initial + ". " + lastError);
                    return 1;
                }

                var session = factory.CreateSession(connection, null);
                try
                {
                    var nodeId = OpcUaNodeResolver.Resolve("PLC.H2.P_set", namespaceUri, session.NamespaceUris);
                    var writes = new[]
                    {
                        new WriteValue
                        {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(new Variant(6.5f))
                        }
                    };
                    StatusCodeCollection results;
                    DiagnosticInfoCollection diagnostics;
                    session.Write(null, writes, out results, out diagnostics);
                    if (results.Count != 1 || StatusCode.IsBad(results[0]))
                        throw new InvalidOperationException("OPC UA write returned " +
                            (results.Count > 0 ? results[0].ToString() : "no status"));
                }
                finally
                {
                    try { session.Close(); } catch { }
                }

                Thread.Sleep(300);
                var written = factory.ReadDouble(connection, "PLC.H2.P_set", namespaceUri);
                if (Math.Abs(written - 6.5) > 1e-6)
                {
                    Console.Error.WriteLine("FAIL: OPC UA write was not reflected, got " + written);
                    return 1;
                }

                Console.WriteLine("PASS: OpenPLC v4 OPC UA server read/write through H2sim client.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: OpenPLC OPC UA probe exception: " + ex);
                return 1;
            }
            finally
            {
                if (process != null && !process.HasExited)
                {
                    try { process.Kill(); } catch { }
                    try { process.WaitForExit(3000); } catch { }
                }
                process?.Dispose();
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TestRoundTrip()
        {
            var map = new SignalMap();
            map.Connections.Add(new ConnectionConfig
            {
                Name = "OpenPLC",
                Protocol = BridgeProtocol.ModbusTcpServer,
                Host = "0.0.0.0",
                Port = 502,
                UnitId = 1,
                Enabled = true
            });
            map.Connections.Add(new ConnectionConfig
            {
                Name = "OpenPLC-UA",
                Protocol = BridgeProtocol.OpcUaClient,
                EndpointUrl = "opc.tcp://localhost:4840/openplc/opcua",
                SecurityPolicy = "Basic256Sha256",
                Anonymous = false,
                Username = "operator",
                Password = "secret"
            });

            for (var i = 0; i < 20; i++)
            {
                map.Signals.Add(new Signal
                {
                    Id = "S" + i.ToString(CultureInfo.InvariantCulture),
                    Direction = i % 2 == 0 ? SignalDirection.FromPlc : SignalDirection.ToPlc,
                    ObjectTag = "WE-" + i.ToString(CultureInfo.InvariantCulture),
                    Property = "P" + i.ToString(CultureInfo.InvariantCulture),
                    Unit = i % 2 == 0 ? "kW" : "degC",
                    Type = (SignalDataType)(i % 4),
                    Scale = 10.0 + i,
                    Offset = -1.5 * i,
                    Deadband = 0.25 * i,
                    WordOrder = (WordOrder)(i % 4),
                    ClampOnWrite = i % 2 == 0 ? ClampOnWrite.Reject : ClampOnWrite.Clamp,
                    ModbusArea = (ModbusArea)(i % 4),
                    ModbusAddress = i * 4,
                    OpcNodeId = i % 3 == 0 ? "ns=2;s=PLC.P" + i.ToString(CultureInfo.InvariantCulture) : "",
                    OpcNamespaceUri = "urn:openplc:opcua:namespace",
                    ConnectionName = i % 3 == 0 ? "OpenPLC-UA" : "OpenPLC",
                    Enabled = i != 7
                });
            }

            var xml = SignalMapSerializer.Save(map);
            var loaded = SignalMapSerializer.Load(xml);

            Check(loaded.Connections.Count == 2, "round-trip connection count");
            Check(loaded.Signals.Count == 20, "round-trip signal count");
            Check(xml.Contains("ModbusAddress=\"1\""), "address is written one-based");

            foreach (var expected in map.Signals)
            {
                var actual = loaded.Signals.Find(s => s.Id == expected.Id);
                if (actual == null)
                {
                    Check(false, "missing signal " + expected.Id);
                    continue;
                }

                Check(actual.Direction == expected.Direction, expected.Id + " Direction");
                Check(actual.ObjectTag == expected.ObjectTag, expected.Id + " ObjectTag");
                Check(actual.Property == expected.Property, expected.Id + " Property");
                Check(actual.Unit == expected.Unit, expected.Id + " Unit");
                Check(actual.Type == expected.Type, expected.Id + " Type");
                Check(Near(actual.Scale, expected.Scale), expected.Id + " Scale");
                Check(Near(actual.Offset, expected.Offset), expected.Id + " Offset");
                Check(Near(actual.Deadband, expected.Deadband), expected.Id + " Deadband");
                Check(actual.WordOrder == expected.WordOrder, expected.Id + " WordOrder");
                Check(actual.ClampOnWrite == expected.ClampOnWrite, expected.Id + " ClampOnWrite");
                Check(actual.ModbusArea == expected.ModbusArea, expected.Id + " ModbusArea");
                Check(actual.ModbusAddress == expected.ModbusAddress, expected.Id + " ModbusAddress");
                Check(actual.OpcNodeId == expected.OpcNodeId, expected.Id + " OpcNodeId");
                Check(actual.ConnectionName == expected.ConnectionName, expected.Id + " ConnectionName");
                Check(actual.Enabled == expected.Enabled, expected.Id + " Enabled");
            }

            var ua = loaded.Connections.Find(c => c.Name == "OpenPLC-UA");
            Check(ua != null && ua.Protocol == BridgeProtocol.OpcUaClient && ua.Username == "operator", "round-trip OPC UA connection");
        }

        private static void TestDuplicateId()
        {
            var map = new SignalMap();
            map.Signals.Add(MakeSignal("dup", "WE-1", "P", 0));
            map.Signals.Add(MakeSignal("dup", "WE-1", "Q", 10));

            var result = SignalMapValidator.Validate(map);
            Check(!result.IsValid, "duplicate id is rejected");
        }

        private static void TestFromPlcConflict()
        {
            var map = new SignalMap();
            map.Signals.Add(MakeSignal("a", "WE-1", "Power", 0));
            map.Signals.Add(MakeSignal("b", "WE-1", "Power", 10));

            var result = SignalMapValidator.Validate(map);
            Check(!result.IsValid && result.Errors.Exists(e => e.Contains("conflict")), "FromPlc conflict is reported");
        }

        private static void TestAddressOverlap()
        {
            var map = new SignalMap();
            map.Signals.Add(MakeSignal("a", "WE-1", "P", 100));
            map.Signals.Add(MakeSignal("b", "WE-1", "Q", 101));
            map.Signals[1].Type = SignalDataType.Int32;

            var result = SignalMapValidator.Validate(map);
            Check(!result.IsValid && result.Errors.Exists(e => e.Contains("address overlap")), "32-bit address overlap is reported");
        }

        private static void TestCrossProtocol()
        {
            var map = new SignalMap();
            map.Connections.Add(new ConnectionConfig { Name = "M", Protocol = BridgeProtocol.ModbusTcpServer });
            map.Connections.Add(new ConnectionConfig { Name = "O", Protocol = BridgeProtocol.OpcUaClient });

            map.Signals.Add(MakeSignal("m", "WE-1", "Power", 0));
            map.Signals.Add(MakeSignal("o", "WE-1", "Power", 10));
            map.Signals[1].ConnectionName = "O";
            map.Signals[1].OpcNodeId = "ns=2;s=PLC.Power";
            map.Signals[0].ConnectionName = "M";

            var result = SignalMapValidator.Validate(map);
            Check(!result.IsValid && result.Errors.Exists(e => e.Contains("cross-protocol conflict")), "cross-protocol conflict is reported");
        }

        private static void TestScalerRoundTrip()
        {
            foreach (var type in new[] { SignalDataType.Int16, SignalDataType.Int32, SignalDataType.Float32 })
            {
                foreach (WordOrder order in Enum.GetValues(typeof(WordOrder)))
                {
                    var signal = new Signal { Type = type, WordOrder = order, Scale = 1.0, Offset = 0.0 };
                    var values = type == SignalDataType.Float32
                        ? new[] { -2.25, 0.0, 1.5, 100.0 }
                        : new[] { -100.0, 0.0, 37.0 };

                    foreach (var value in values)
                    {
                        var words = ModbusScaler.EngineeringToRaw(signal, value);
                        var back = ModbusScaler.RawToEngineering(signal, words);
                        var tolerance = type == SignalDataType.Float32 ? 1e-5 : 1e-9;
                        Check(Math.Abs(back - value) <= tolerance, string.Format("{0} {1} {2}", type, order, value));
                    }
                }
            }

            var scaled = new Signal { Type = SignalDataType.Int32, WordOrder = WordOrder.CDAB, Scale = 10.0, Offset = -5.0 };
            Check(Math.Abs(ModbusScaler.RawToEngineering(scaled, ModbusScaler.EngineeringToRaw(scaled, 345.0)) - 345.0) < 1e-9, "scale/offset round-trip");

            var temperature = new Signal { Type = SignalDataType.Int16, Scale = 10.0 };
            Check(ModbusScaler.EngineeringToRaw(temperature, 85.3)[0] == 853, "raw = engineering * scale");
            Check(Math.Abs(ModbusScaler.RawToEngineering(temperature, new[] { (ushort)853 }) - 85.3) < 1e-9, "engineering = raw / scale");

            var boolean = new Signal { Type = SignalDataType.Bool };
            Check(ModbusScaler.RawToEngineering(boolean, ModbusScaler.EngineeringToRaw(boolean, 1.0)) == 1.0, "bool round-trip");
        }

        private static void TestModbusSelfLoop()
        {
            var snapshot = new ModbusDataSnapshot();
            snapshot.WriteInputRegisters(100, new[] { (ushort)853 });

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var factory = new ModbusFactory();
            var network = factory.CreateSlaveNetwork(listener);
            network.AddSlave(factory.CreateSlave((byte)1, new ModbusSlaveDataStore(snapshot)));

            var cancellation = new CancellationTokenSource();
            var listenTask = network.ListenAsync(cancellation.Token);
            TcpClient client = null;

            try
            {
                Thread.Sleep(150);
                client = new TcpClient("127.0.0.1", port);
                var master = factory.CreateMaster(client);

                var input = master.ReadInputRegisters((byte)1, 100, 1);
                Check(input.Length == 1 && input[0] == 853, "input register round-trip");

                master.WriteSingleRegister((byte)1, 400, 1234);
                Check(snapshot.ReadHoldingRegisters(400, 1)[0] == 1234, "holding register write");

                master.WriteSingleCoil((byte)1, 5, true);
                Check(snapshot.ReadCoils(5, 1)[0], "coil write");

                master.WriteMultipleRegisters((byte)1, 410, new[] { (ushort)11, (ushort)22 });
                var registers = snapshot.ReadHoldingRegisters(410, 2);
                Check(registers[0] == 11 && registers[1] == 22, "multiple register write");
            }
            catch (Exception ex)
            {
                Check(false, "Modbus self-loop exception: " + ex);
            }
            finally
            {
                client?.Close();
                cancellation.Cancel();
                try { listener.Stop(); } catch { }
                try { (network as IDisposable)?.Dispose(); } catch { }
                try { listenTask.Wait(2000); } catch { }
                cancellation.Dispose();
            }
        }

        private static Signal MakeSignal(string id, string tag, string property, int address)
        {
            return new Signal
            {
                Id = id,
                ObjectTag = tag,
                Property = property,
                ModbusAddress = address,
                Scale = 1.0
            };
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) < 1e-12;
        }

        private static void Check(bool condition, string message)
        {
            if (condition) return;
            _failures++;
            Console.Error.WriteLine("FAIL: " + message);
        }
    }
}
