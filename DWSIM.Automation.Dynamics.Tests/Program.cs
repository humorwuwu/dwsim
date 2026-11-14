using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DWSIM.H2sim.Io;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.UnitOperations.SpecialOps;
using DWSIM.UnitOperations.UnitOperations;

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
            TestElectrolyzerDiscovery();

            Console.WriteLine(_failures == 0 ? "PASS" : "FAIL");
            return _failures == 0 ? 0 : 1;
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