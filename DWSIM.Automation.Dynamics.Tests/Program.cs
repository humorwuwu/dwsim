using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DWSIM.H2sim.Io;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.UnitOperations.SpecialOps;
using DWSIM.UnitOperations.UnitOperations;
using NModbus;

namespace DWSIM.Automation.Dynamics.Tests
{
    internal static class Program
    {
        private static int _failures;

        private static int Main(string[] args)
        {
            var path = args.Length > 0
                ? args[0]
                : Path.GetFullPath(Path.Combine("PlatformFiles", "Common", "samples", "Dynamic Simulation - Water Tank Level Control.dwxmz"));

            Console.WriteLine("Loading " + path);

            var automation = new global::DWSIM.Automation.Automation3();
            var flowsheet = automation.LoadFlowsheet(path);

            var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
            var integrator = flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];

            var stats = DynamicsRunner.Run(flowsheet, new RunOptions
            {
                DurationSeconds = 60,
                StepSeconds = integrator.IntegrationStep.TotalSeconds,
                ReuseInitialState = schedule.UseCurrentStateAsInitial,
                InitialStateId = schedule.InitialFlowsheetStateID
            }, new HeadlessDynamicsHost());

            Console.WriteLine("stepCount={0} simTime={1} quality={2}", stats.StepCount, stats.SimTime, stats.Quality);
            if (stats.Error != null) Console.Error.WriteLine(stats.Error);

            if (stats.Error != null) return 1;
            if (stats.StepCount != 12)
            {
                Console.Error.WriteLine("FAIL: expected 12 steps");
                return 1;
            }
            if (Math.Abs(stats.SimTime - 60) > 1e-9)
            {
                Console.Error.WriteLine("FAIL: expected 60 s of simulated time");
                return 1;
            }

            TestOwnership(flowsheet);
            TestBridgeHost(flowsheet);
            TestModbusClosedLoop(flowsheet);
            TestOpcUaClosedLoop(flowsheet);
            TestElectrolyzerDiscovery();
            TestElectrochemicalModel();
            TestInterlocks();
            TestDynamicSupport();

            Console.WriteLine(_failures == 0 ? "PASS" : "FAIL");
            return _failures == 0 ? 0 : 1;
        }

        private static void TestDynamicSupport()
        {
            var pem = new PEMElectrolyzer();
            Check(pem.SupportsDynamicMode && pem.HasPropertiesForDynamicMode, "dynamic support");
            pem.CreateDynamicProperties();
            Check(pem.GetDynamicProperty("Stack Temperature") != null, "dynamic stack temperature property");

            var heated = pem.IntegrateStackTemperature(353.15, 60.0, 100000.0, 0.0, 4180.0, 298.15, 298.15);
            Check(heated > 353.15 && heated < 400.0, "stack heats up");

            var cooled = pem.IntegrateStackTemperature(353.15, 60.0, 0.0, 1.0, 4180.0, 298.15, 298.15);
            Check(cooled < 353.15 && cooled > 298.15, "stack cools down");
        }

        private static void TestInterlocks()
        {
            var pem = new PEMElectrolyzer();

            pem.UpdateInterlocks(2.5, 0.1, 350.0, 5.0, 100.0);
            Check(pem.H2inO2HighAlarm, "H2-in-O2 high alarm");
            pem.UpdateInterlocks(1.0, 0.1, 350.0, 5.0, 100.0);
            Check(!pem.H2inO2HighAlarm, "H2-in-O2 alarm auto-resets");

            pem.UpdateInterlocks(4.5, 0.1, 350.0, 5.0, 100.0);
            Check(pem.TripLatched, "H2-in-O2 trip latches");
            Check(!pem.ResetTrip(), "reset refused while H2-in-O2 is high");

            pem.UpdateInterlocks(0.5, 0.1, 350.0, 5.0, 100.0);
            Check(pem.ResetTrip(), "reset accepted when safe");
            Check(!pem.TripLatched, "trip cleared");

            pem.UpdateInterlocks(0.1, 0.1, 365.0, 5.0, 100.0);
            Check(pem.TripLatched && pem.StackTemperatureHighAlarm, "stack temperature trip");

            pem.UpdateInterlocks(0.1, 0.1, 350.0, 5.0, 100.0);
            Check(pem.ResetTrip(), "second reset accepted");
        }

        private static void TestElectrochemicalModel()
        {
            var model = new ModelPEM_Springer { U_rev = 1.23, U_tn = 1.48 };
            var low = model.CellVoltage(0.1, 353.15, 30.0, 2.0);
            var high = model.CellVoltage(1.0, 353.15, 30.0, 2.0);

            Check(high.U_cell > low.U_cell, "polarization curve is monotonic");
            Check(Math.Abs(high.U_cell - (high.U_ocv + high.U_act + high.U_ohm + high.U_conc)) < 1e-9, "voltage breakdown adds up");

            var calculator = new ElectrolyzerCalculator
            {
                Model = model,
                A_cell = 1000.0,
                N_cells = 182.0,
                i_min = 0.25,
                i_max = 2.5,
                T = 353.15,
                pCat = 30.0,
                pAn = 2.0
            };

            const double targetPower = 500.0;
            var currentDensity = calculator.CurrentForPower(targetPower);
            Check(Math.Abs(calculator.PowerAt(currentDensity) - targetPower) / targetPower < 1e-4, "power/current closure");
            Check(calculator.HydrogenMolarFlow(currentDensity) > 0.0, "positive hydrogen flow");
            Check(calculator.SpecificEnergyConsumption(targetPower, currentDensity) > 0.0, "positive SEC");

            var alkaline = new AlkalineElectrolyzer();
            Check(alkaline.CreateElectrochemicalModel() is ModelAlkaline_SemiEmpirical, "alkaline model factory");

            var units = new DWSIM.SharedClasses.SystemsOfUnits.SI();
            Check(units.voltage == "V" && units.current == "A" && units.currentDensity == "A/cm2", "electrical unit properties");
            Check(units.GetUnitType("V") == UnitOfMeasure.voltage, "voltage unit type");
            Check(units.GetCurrentUnits(UnitOfMeasure.currentDensity) == "A/cm2", "current density units");
        }

        private static void TestBridgeHost(IFlowsheet flowsheet)
        {
            var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
            var integrator = flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];

            var bridge = new RecordingBridge();
            var stats = DynamicsRunner.Run(flowsheet, new RunOptions
            {
                DurationSeconds = 10,
                StepSeconds = integrator.IntegrationStep.TotalSeconds,
                ReuseInitialState = schedule.UseCurrentStateAsInitial,
                InitialStateId = schedule.InitialFlowsheetStateID
            }, new BridgeDynamicsHost(bridge));

            Check(stats.StepCount == 2, "bridge-host run completes two steps");
            Check(bridge.Applied == 2 && bridge.Published == 2, "bridge callbacks run once per step");
        }

        private sealed class RecordingBridge : IPlantBridge
        {
            public int Applied { get; private set; }
            public int Published { get; private set; }

            public string Name => "recording";
            public BridgeStatus Status { get; } = new BridgeStatus();
            public event EventHandler<SignalErrorEventArgs> SignalError { add { } remove { } }

            public bool Connect(SignalMap map, IFlowsheet flowsheet) => true;
            public void Disconnect() { }
            public void ApplyInputs() => Applied++;
            public void PublishOutputs() => Published++;
            public void SetQuality(QualityCode quality, string reason) { }
            public void Dispose() { }
        }

        private static void TestModbusClosedLoop(IFlowsheet flowsheet)
        {
            var pid = flowsheet.SimulationObjects.Values.OfType<PIDController>().FirstOrDefault();
            Check(pid != null, "closed loop: sample PID");
            if (pid == null) return;

            var tankTag = pid.ControlledObjectData.ID;
            if (!flowsheet.SimulationObjects.ContainsKey(tankTag))
            {
                Check(false, "closed loop: tank object");
                return;
            }
            var tank = flowsheet.SimulationObjects[tankTag];

            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var map = new SignalMap();
            map.Connections.Add(new ConnectionConfig
            {
                Name = "Loop",
                Protocol = BridgeProtocol.ModbusTcpServer,
                Host = "127.0.0.1",
                Port = port,
                UnitId = 1,
                Enabled = true
            });
            map.Signals.Add(new Signal
            {
                Id = "SP",
                Direction = SignalDirection.FromPlc,
                ObjectTag = pid.Name,
                Property = "SetPointAbs",
                Unit = "m",
                Type = SignalDataType.Int16,
                Scale = 1000.0,
                ModbusArea = ModbusArea.HoldingRegister,
                ModbusAddress = 0,
                ConnectionName = "Loop"
            });
            map.Signals.Add(new Signal
            {
                Id = "LEVEL",
                Direction = SignalDirection.ToPlc,
                ObjectTag = tankTag,
                Property = "Liquid Level",
                Unit = "m",
                Type = SignalDataType.Int16,
                Scale = 1000.0,
                ModbusArea = ModbusArea.InputRegister,
                ModbusAddress = 0,
                ConnectionName = "Loop"
            });

            using (var bridge = new ModbusTcpServerBridge())
            {
                Check(bridge.Connect(map, flowsheet), "bridge connects: " + bridge.Status.Message);
                if (!bridge.Status.Connected) return;

                TcpClient client = null;
                try
                {
                    Thread.Sleep(200);
                    client = new TcpClient("127.0.0.1", port);
                    var master = new ModbusFactory().CreateMaster(client);
                    master.WriteSingleRegister(1, 0, 1800);

                    var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
                    var integrator = flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];
                    var stats = DynamicsRunner.Run(flowsheet, new RunOptions
                    {
                        DurationSeconds = 10,
                        StepSeconds = integrator.IntegrationStep.TotalSeconds,
                        ReuseInitialState = schedule.UseCurrentStateAsInitial,
                        InitialStateId = schedule.InitialFlowsheetStateID
                    }, new BridgeDynamicsHost(bridge));

                    Check(stats.StepCount == 2, "closed loop: two steps");
                    Check(Math.Abs(pid.AdjustValue - 1.8) < 1e-6, "closed loop: PLC setpoint reached the PID");

                    var raw = master.ReadInputRegisters(1, 0, 1)[0];
                    var level = Convert.ToDouble(tank.GetPropertyValue("Liquid Level"));
                    Check(!double.IsNaN(level) && Math.Abs(raw - level * 1000.0) <= 2.0, "closed loop: level published");
                }
                catch (Exception ex)
                {
                    Check(false, "closed loop exception: " + ex);
                }
                finally
                {
                    client?.Close();
                    bridge.Disconnect();
                }
            }
        }

        private static void TestOpcUaClosedLoop(IFlowsheet flowsheet)
        {
            if (Environment.GetEnvironmentVariable("H2SIM_OPCUA_TEST") != "1") return;

            var pid = flowsheet.SimulationObjects.Values.OfType<PIDController>().FirstOrDefault();
            Check(pid != null, "OPC UA closed loop: sample PID");
            if (pid == null) return;

            var connection = new ConnectionConfig
            {
                Name = "PLC",
                Protocol = BridgeProtocol.OpcUaClient,
                EndpointUrl = "opc.tcp://127.0.0.1:4840/h2sim/",
                SecurityPolicy = "None",
                Anonymous = true,
                Enabled = true
            };

            var map = new SignalMap();
            map.Connections.Add(connection);
            map.Signals.Add(new Signal
            {
                Id = "SP",
                Direction = SignalDirection.FromPlc,
                ObjectTag = pid.Name,
                Property = "SetPointAbs",
                Unit = "m",
                Type = SignalDataType.Float32,
                Scale = 1.0,
                OpcNodeId = "P_set",
                OpcNamespaceUri = "urn:openplc:opcua:namespace",
                ConnectionName = "PLC"
            });
            map.Signals.Add(new Signal
            {
                Id = "OUT",
                Direction = SignalDirection.ToPlc,
                ObjectTag = pid.Name,
                Property = "OutputAbs",
                Unit = "",
                Type = SignalDataType.Float32,
                Scale = 1.0,
                OpcNodeId = "Out",
                OpcNamespaceUri = "urn:openplc:opcua:namespace",
                ConnectionName = "PLC"
            });

            using (var bridge = new OpcUaClientBridge())
            {
                Check(bridge.Connect(map, flowsheet), "OPC UA connects: " + bridge.Status.Message);
                if (!bridge.Status.Connected) return;

                var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
                var integrator = flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];
                var stats = DynamicsRunner.Run(flowsheet, new RunOptions
                {
                    DurationSeconds = 10,
                    StepSeconds = integrator.IntegrationStep.TotalSeconds,
                    ReuseInitialState = schedule.UseCurrentStateAsInitial,
                    InitialStateId = schedule.InitialFlowsheetStateID
                }, new BridgeDynamicsHost(bridge));

                Check(stats.StepCount == 2, "OPC UA closed loop: two steps");
                Check(Math.Abs(pid.AdjustValue - 1.8) < 1e-3, "OPC UA setpoint reached the PID");

                var published = new OpcUaSessionFactory().ReadDouble(connection, "Out", "urn:openplc:opcua:namespace");
                Check(Math.Abs(published - pid.OutputAbs) < 0.1, "OPC UA output published");

                bridge.Disconnect();
            }
        }

        private static void TestElectrolyzerDiscovery()
        {
            var assembly = typeof(PEMElectrolyzer).Assembly;
            var unitOperations = DWSIM.SharedClasses.Utility.GetUnitOperations(assembly);

            Check(unitOperations.Any(u => u.Description == "PEM Electrolyzer"), "PEM electrolyzer is discovered");
            Check(unitOperations.Any(u => u.Description == "Alkaline Electrolyzer"), "alkaline electrolyzer is discovered");

            var pem = new PEMElectrolyzer();
            Check(pem.Prefix == "PEMEL-" && !pem.IsSource, "PEM electrolyzer metadata");
            Check(pem.GetPropertyUnit("Cell Voltage") == "V", "cell voltage unit");
            Check(pem.Parameters.ContainsKey("A_cell"), "default parameter set");

            pem.Parameters["A_cell"].Value = 1234.0;
            var clone = new PEMElectrolyzer();
            clone.LoadData(pem.SaveData());
            Check(Math.Abs(clone.Parameters["A_cell"].Value - 1234.0) < 1e-9, "parameter round-trip");
        }

        private static void TestOwnership(IFlowsheet flowsheet)
        {
            var controller = flowsheet.SimulationObjects.Values.OfType<PIDController>().FirstOrDefault();
            Check(controller != null, "sample has a PID controller");
            if (controller == null) return;

            var objectTag = controller.ManipulatedObjectData.ID;
            var property = controller.ManipulatedObjectData.PropertyName;

            var map = new SignalMap();
            map.Connections.Add(new ConnectionConfig { Name = "M", Protocol = BridgeProtocol.ModbusTcpServer });
            map.Signals.Add(new Signal { Id = "A", Direction = SignalDirection.FromPlc, ObjectTag = objectTag, Property = property, ConnectionName = "M", Scale = 1.0 });
            map.Signals.Add(new Signal { Id = "B", Direction = SignalDirection.FromPlc, ObjectTag = objectTag, Property = property, ConnectionName = "M", Scale = 1.0 });

            var arbiter = new ControlArbiter(flowsheet, map);
            var errors = new List<string>();
            arbiter.SignalError += (sender, e) => errors.Add(e.Message);

            var mv = controller.MVValue;
            arbiter.BeginStep();
            Check(arbiter.WriteExternal(map.Signals[0], mv), "first external write accepted");
            Check(!arbiter.WriteExternal(map.Signals[1], mv), "second writer rejected");
            Check(errors.Any(e => e.Contains("conflict")), "conflict is reported");
            Check(controller.Ownership == ControlOwnership.External, "ownership became External");

            Check(arbiter.SwitchOwnership(objectTag, property, ControlOwnership.Local), "switch to Local");
            Check(controller.Ownership == ControlOwnership.Local, "ownership became Local");
            Check(controller.CurrentError == 0.0 && controller.LastError == 0.0, "error history cleared on transfer");

            controller.OutputRateLimit = 1.0;
            flowsheet.DynamicMode = true;
            controller.Solve();
            var before = controller.MVValue;
            controller.AdjustValue += 50.0;
            controller.Solve();
            var after = controller.MVValue;
            Check(Math.Abs(after - before) <= 5.0 + 1e-6, "output rate limit bounds the move");
            flowsheet.DynamicMode = false;
        }

        private static void Check(bool condition, string message)
        {
            if (condition) return;
            _failures++;
            Console.Error.WriteLine("FAIL: " + message);
        }
    }
}