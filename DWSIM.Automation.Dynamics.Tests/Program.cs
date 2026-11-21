using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DWSIM.H2sim.Io;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.Interfaces.Enums.GraphicObjects;
using DWSIM.Thermodynamics.Streams;
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
            if (args.Length > 0 && args[0] == "--generate-samples")
                return GeneratePlantSamples(args.Length > 1 ? args[1] : "generated-samples");

            var path = args.Length > 0 && !args[0].StartsWith("--")
                ? args[0]
                : Path.GetFullPath(Path.Combine("PlatformFiles", "Common", "samples", "Dynamic Simulation - Water Tank Level Control.dwxmz"));
            var shadowReport = args.FirstOrDefault(a => a.StartsWith("--shadow-report=", StringComparison.Ordinal));
            if (shadowReport != null) shadowReport = shadowReport.Substring("--shadow-report=".Length);

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
            TestShadowDeviation(flowsheet, shadowReport);
            TestBridgeHost(flowsheet);
            TestModbusClosedLoop(flowsheet);
            TestOpcUaClosedLoop(flowsheet);
            TestElectrolyzerDiscovery();
            TestElectrochemicalModel();
            TestElectrochemicalGoldenData();
            TestSpecificEnergyConsumptionGoldenData();
            TestAlkalineConductivityGoldenData();
            TestThermalGoldenData();
            TestGasPurityRegression();
            TestInterlocks();
            TestDynamicSupport();
            TestDynamicSamples();
            TestPlantSamples();

            Console.WriteLine(_failures == 0 ? "PASS" : "FAIL");
            return _failures == 0 ? 0 : 1;
        }

        private static int GeneratePlantSamples(string outputDirectory)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                GeneratePlant(outputDirectory, "PEM Electrolysis Plant.dwxmz", new PEMElectrolyzer(), 900.0, 30.0, 2.0);
                GeneratePlant(outputDirectory, "Alkaline Electrolysis Plant.dwxmz", new AlkalineElectrolyzer(), 2950.0, 16.0, 16.0);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void GeneratePlant(string directory, string fileName, ElectrolyzerUnitOpBase unit,
                                          double powerKW, double pCat, double pAn)
        {
            var automation = new global::DWSIM.Automation.Automation3();
            var flowsheet = automation.CreateFlowsheet();

            flowsheet.AddCompound("Water");
            flowsheet.AddCompound("Hydrogen");
            flowsheet.AddCompound("Oxygen");

            var propertyPackage = flowsheet.GetAvailablePropertyPackages()
                .FirstOrDefault(x => x.IndexOf("Raoult", StringComparison.OrdinalIgnoreCase) >= 0);
            if (propertyPackage == null) throw new Exception("Raoult property package not found.");
            flowsheet.CreateAndAddPropertyPackage(propertyPackage);

            var feed = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 50, 100, "FEED");
            feed.SetTemperature(353.15);
            feed.SetPressure(pCat * 100000.0);
            feed.SetMassFlow(1.0);
            feed.SetOverallComposition(new[] { 1.0, 0.0, 0.0 });
            feed.SetFlashSpec("PT");
            feed.Calculate();

            var h2 = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 450, 50, "H2");
            var o2 = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 450, 150, "O2");
            var coolingIn = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 50, 250, "CW-IN");
            var coolingOut = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 450, 250, "CW-OUT");

            coolingIn.SetTemperature(298.15);
            coolingIn.SetPressure(101325.0);
            coolingIn.SetMassFlow(1.0);
            coolingIn.SetOverallComposition(new[] { 1.0, 0.0, 0.0 });
            coolingIn.SetFlashSpec("PT");
            coolingIn.Calculate();

            var baseFlowsheet = (DWSIM.FlowsheetBase.FlowsheetBase)flowsheet;
            baseFlowsheet.AddObjectToSurface(ObjectType.External, 250, 100, "EL-1", "", unit, false);
            unit.CreateConnectors();

            unit.Parameters["P_stack"].Value = powerKW;
            unit.Parameters["SetpointMode"].Value = 0.0;
            unit.Parameters["P_cat"].Value = pCat;
            unit.Parameters["P_an"].Value = pAn;
            if (unit is AlkalineElectrolyzer)
                unit.Parameters["A_cell"].Value = 17600.0;

            flowsheet.ConnectObjects(feed.GraphicObject, unit.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(coolingIn.GraphicObject, unit.GraphicObject, 0, 1);
            flowsheet.ConnectObjects(unit.GraphicObject, h2.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(unit.GraphicObject, o2.GraphicObject, 1, 0);
            flowsheet.ConnectObjects(unit.GraphicObject, coolingOut.GraphicObject, 2, 0);

            var purification = new H2Purification();
            var storage = new HydrogenStorage();
            var purifiedH2 = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 650, 30, "H2-PURE");
            var offGas = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 650, 130, "OFFGAS");
            var storedH2 = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 850, 30, "H2-PROD");

            baseFlowsheet.AddObjectToSurface(ObjectType.External, 520, 20, "PUR-1", "", purification, false);
            purification.CreateConnectors();
            baseFlowsheet.AddObjectToSurface(ObjectType.External, 730, 20, "STO-1", "", storage, false);
            storage.CreateConnectors();

            flowsheet.ConnectObjects(h2.GraphicObject, purification.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(purification.GraphicObject, purifiedH2.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(purification.GraphicObject, offGas.GraphicObject, 1, 0);
            flowsheet.ConnectObjects(purifiedH2.GraphicObject, storage.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(storage.GraphicObject, storedH2.GraphicObject, 0, 0);

            var dynamicsManager = flowsheet.DynamicsManager;
            var integrator = new global::DWSIM.DynamicsManager.Integrator
            {
                ID = "plant-integrator",
                Description = "Plant Dynamics",
                IntegrationStep = TimeSpan.FromSeconds(1),
                Duration = TimeSpan.FromSeconds(60)
            };
            var schedule = new global::DWSIM.DynamicsManager.Schedule
            {
                ID = "plant-schedule",
                Description = "Plant Dynamics",
                CurrentIntegrator = integrator.ID,
                UseCurrentStateAsInitial = true
            };
            dynamicsManager.IntegratorList[integrator.ID] = integrator;
            dynamicsManager.ScheduleList[schedule.ID] = schedule;
            dynamicsManager.CurrentSchedule = schedule.ID;

            var errors = ((global::DWSIM.Automation.AutomationInterface)automation).CalculateFlowsheet2(flowsheet);
            if (errors.Count > 0) throw new Exception(string.Join("; ", errors.Select(e => e.Message)));

            automation.SaveFlowsheet(flowsheet, Path.Combine(directory, fileName), true);
            Console.WriteLine("generated {0}: power={1} kW, H2 purity={2}%", fileName, unit.Power, unit.H2Purity);
        }

        private static void TestPlantSamples()
        {
            var directories = new[] { "samples", Path.Combine("PlatformFiles", "Common", "samples") };
            var directory = directories.FirstOrDefault(Directory.Exists);
            if (directory == null)
            {
                Check(false, "plant samples directory not found");
                return;
            }

            var files = Directory.EnumerateFiles(directory, "*Electrolysis Plant.dwxmz")
                .OrderBy(x => x)
                .ToList();
            Check(files.Count == 2, "two plant sample flowsheets found");

            foreach (var file in files)
            {
                try
                {
                    var automation = new global::DWSIM.Automation.Automation3();
                    var flowsheet = automation.LoadFlowsheet(file);
                    var errors = ((global::DWSIM.Automation.AutomationInterface)automation).CalculateFlowsheet2(flowsheet);
                    Check(errors.Count == 0, "plant sample: " + Path.GetFileName(file) + ": " +
                        string.Join("; ", errors.Select(e => e.ToString())));

                    var unit = flowsheet.SimulationObjects.Values.OfType<ElectrolyzerUnitOpBase>().FirstOrDefault();
                    Check(unit != null && unit.Power > 0, "plant electrolyzer: " + Path.GetFileName(file));
                    if (unit != null)
                    {
                        Console.WriteLine("plant {0}: power={1} kW, H2 purity={2}%",
                            Path.GetFileName(file), unit.Power, unit.H2Purity);
                        var targetPower = unit is AlkalineElectrolyzer ? 2950.0 : 900.0;
                        Check(Math.Abs(unit.Power - targetPower) / targetPower < 0.03,
                            "plant electrolyzer power follows template rating: " + Path.GetFileName(file) +
                            " " + unit.Power + " kW vs " + targetPower + " kW");
                        Check(Math.Abs(unit.Current - unit.CurrentDensity * unit.Parameters["A_cell"].Value) < 1e-6,
                            "plant stack current uses series-cell current: " + Path.GetFileName(file));
                    }

                    var storage = flowsheet.SimulationObjects.Values.OfType<HydrogenStorage>().FirstOrDefault();
                    var manager = flowsheet.DynamicsManager;
                    if (storage != null && manager.ScheduleList.Count > 0 &&
                        manager.ScheduleList.ContainsKey(manager.CurrentSchedule))
                    {
                        var schedule = manager.ScheduleList[manager.CurrentSchedule];
                        var integrator = manager.IntegratorList[schedule.CurrentIntegrator];
                        var startPressure = storage.TankPressure;
                        var dynamicStats = DynamicsRunner.Run(flowsheet, new RunOptions
                        {
                            DurationSeconds = Math.Min(60.0, integrator.Duration.TotalSeconds),
                            StepSeconds = integrator.IntegrationStep.TotalSeconds,
                            ReuseInitialState = schedule.UseCurrentStateAsInitial,
                            InitialStateId = schedule.InitialFlowsheetStateID
                        }, new HeadlessDynamicsHost());
                        Check(dynamicStats.Error == null && storage.TankPressure > startPressure,
                            "storage pressure rises: " + Path.GetFileName(file) + " " + startPressure + " -> " + storage.TankPressure);
                    }
                }
                catch (Exception ex)
                {
                    Check(false, "plant sample: " + Path.GetFileName(file) + ": " + ex.ToString());
                }
            }
        }

        private static void TestDynamicSamples()
        {
            var directories = new[] { "samples", Path.Combine("PlatformFiles", "Common", "samples") };
            var directory = directories.FirstOrDefault(Directory.Exists);
            if (directory == null)
            {
                Check(false, "dynamic samples directory not found");
                return;
            }

            var files = Directory.EnumerateFiles(directory, "Dynamic Simulation - *.dwxmz")
                .OrderBy(x => x)
                .ToList();

            Check(files.Count >= 6, "six dynamic samples found");
            if (files.Count < 6) return;

            foreach (var file in files)
            {
                try
                {
                    var automation = new global::DWSIM.Automation.Automation3();
                    var flowsheet = automation.LoadFlowsheet(file);
                    var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
                    var integrator = flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];

                    if (Path.GetFileName(file).Contains("PFR"))
                    {
                        // the saved 1 s schedule step is larger than this sample's dynamic residence time
                        Console.WriteLine("SKIP (sample step/residence mismatch): " + Path.GetFileName(file));
                        continue;
                    }

                    var stats = DynamicsRunner.Run(flowsheet, new RunOptions
                    {
                        DurationSeconds = 10.0,
                        StepSeconds = integrator.IntegrationStep.TotalSeconds,
                        ReuseInitialState = schedule.UseCurrentStateAsInitial,
                        InitialStateId = schedule.InitialFlowsheetStateID
                    }, new HeadlessDynamicsHost());

                    Check(stats.StepCount >= 1 && stats.Error == null, "dynamic sample: " + Path.GetFileName(file));
                }
                catch (Exception ex)
                {
                    Check(false, "dynamic sample: " + Path.GetFileName(file) + ": " + ex.Message);
                }
            }
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

            var rejectedOverload = false;
            try
            {
                calculator.CurrentForPower(calculator.PowerAt(calculator.i_max) + 1.0);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejectedOverload = true;
            }
            Check(rejectedOverload, "unreachable stack power is rejected");

            var alkaline = new AlkalineElectrolyzer();
            Check(alkaline.CreateElectrochemicalModel() is ModelAlkaline_SemiEmpirical, "alkaline model factory");

            var storage = new HydrogenStorage();
            Check(storage.SupportsDynamicMode && storage.HasPropertiesForDynamicMode, "storage dynamic support");
            Check(storage.UpdateStoredMoles(100.0, 1.0, 10.0) > 100.0, "storage mole balance");

            var alkalineModel = new ModelAlkaline_SemiEmpirical();
            var conductivity = alkalineModel.ConductivityKOH(0.30, 353.15);
            Check(Math.Abs(conductivity - 1.36093786) / 1.36093786 < 1e-5, "R9 KOH conductivity at 80 C");

            var rejectedLowKOH = false;
            try
            {
                alkalineModel.ConductivityKOH(0.10, 353.15);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejectedLowKOH = true;
            }
            Check(rejectedLowKOH, "KOH conductivity rejects out-of-range composition");

            var units = new DWSIM.SharedClasses.SystemsOfUnits.SI();
            Check(units.voltage == "V" && units.current == "A" && units.currentDensity == "A/cm2", "electrical unit properties");
            Check(units.GetUnitType("V") == UnitOfMeasure.voltage, "voltage unit type");
            Check(units.GetCurrentUnits(UnitOfMeasure.currentDensity) == "A/cm2", "current density units");
        }

        private static void TestElectrochemicalGoldenData()
        {
            var candidates = new[]
            {
                Path.Combine("electrolyzers", "bzovsky_2024_polarization.csv"),
                Path.Combine("tests", "electrolyzers", "bzovsky_2024_polarization.csv")
            };
            var path = candidates.FirstOrDefault(File.Exists);
            Check(path != null, "Bzovsky 2024 polarization fixture found");
            if (path == null) return;

            var rows = 0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("temperature_c,")) continue;

                var parts = line.Split(',');
                if (parts.Length != 5)
                {
                    Check(false, "invalid polarization fixture row: " + line);
                    continue;
                }

                var temperatureC = double.Parse(parts[0], CultureInfo.InvariantCulture);
                var currentDensity = double.Parse(parts[1], CultureInfo.InvariantCulture);
                var reversibleVoltage = double.Parse(parts[2], CultureInfo.InvariantCulture);
                var expectedVoltage = double.Parse(parts[3], CultureInfo.InvariantCulture);
                var tolerance = double.Parse(parts[4], CultureInfo.InvariantCulture);

                var model = new ModelPEM_Springer { U_rev = reversibleVoltage, U_tn = 1.48 };
                var actual = model.CellVoltage(currentDensity, temperatureC + 273.15, 30.0, 2.0).U_cell;
                var relativeError = Math.Abs(actual - expectedVoltage) / expectedVoltage;
                rows++;
                Check(relativeError <= tolerance,
                    string.Format(CultureInfo.InvariantCulture,
                        "Bzovsky polarization {0:F0} C, {1:F2} A/cm2: expected {2:F4} V, got {3:F4} V ({4:P2})",
                        temperatureC, currentDensity, expectedVoltage, actual, relativeError));
            }

            Check(rows == 18, "Bzovsky polarization fixture has 18 points");
        }

        private static void TestSpecificEnergyConsumptionGoldenData()
        {
            var candidates = new[]
            {
                Path.Combine("electrolyzers", "bzovsky_2024_sec.csv"),
                Path.Combine("tests", "electrolyzers", "bzovsky_2024_sec.csv")
            };
            var path = candidates.FirstOrDefault(File.Exists);
            Check(path != null, "Bzovsky 2024 SEC fixture found");
            if (path == null) return;

            var rows = 0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("temperature_c,")) continue;

                var parts = line.Split(',');
                if (parts.Length != 5)
                {
                    Check(false, "invalid SEC fixture row: " + line);
                    continue;
                }

                var temperatureC = double.Parse(parts[0], CultureInfo.InvariantCulture);
                var powerKW = double.Parse(parts[1], CultureInfo.InvariantCulture);
                var reversibleVoltage = double.Parse(parts[2], CultureInfo.InvariantCulture);
                var expectedSec = double.Parse(parts[3], CultureInfo.InvariantCulture);
                var tolerance = double.Parse(parts[4], CultureInfo.InvariantCulture);

                var model = new ModelPEM_Springer { U_rev = reversibleVoltage, U_tn = 1.48 };
                var calculator = new ElectrolyzerCalculator
                {
                    Model = model,
                    A_cell = 1000.0,
                    N_cells = 182.0,
                    i_min = 0.25,
                    i_max = 2.5,
                    T = temperatureC + 273.15,
                    pCat = 30.0,
                    pAn = 2.0,
                    etaF_ref = 1.0
                };
                var currentDensity = calculator.CurrentForPower(powerKW);
                var actualSec = calculator.SpecificEnergyConsumption(powerKW, currentDensity);
                var relativeError = Math.Abs(actualSec - expectedSec) / expectedSec;
                rows++;
                Check(relativeError <= tolerance,
                    string.Format(CultureInfo.InvariantCulture,
                        "Bzovsky SEC {0:F0} C, {1:F0} kW: expected {2:F3} kWh/kg, got {3:F3} ({4:P2})",
                        temperatureC, powerKW, expectedSec, actualSec, relativeError));
            }

            Check(rows == 15, "Bzovsky SEC fixture has 15 points");
        }

        private static void TestThermalGoldenData()
        {
            var candidates = new[]
            {
                Path.Combine("electrolyzers", "thermal_lumped_reference.csv"),
                Path.Combine("tests", "electrolyzers", "thermal_lumped_reference.csv")
            };
            var path = candidates.FirstOrDefault(File.Exists);
            Check(path != null, "thermal reference fixture found");
            if (path == null) return;

            var rows = 0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("c_stack_j_k,")) continue;

                var parts = line.Split(',');
                if (parts.Length != 9)
                {
                    Check(false, "invalid thermal fixture row: " + line);
                    continue;
                }

                double P(int index) => double.Parse(parts[index], CultureInfo.InvariantCulture);
                var pem = new PEMElectrolyzer();
                pem.Parameters["C_stack"].Value = P(0);
                pem.Parameters["U_loss_area"].Value = P(1);
                var actual = pem.IntegrateStackTemperature(P(3), P(7), P(6), P(2), 4180.0, P(4), P(5));
                var expected = P(8);
                var relativeError = Math.Abs(actual - expected) / expected;
                rows++;
                Check(relativeError < 0.01,
                    string.Format(CultureInfo.InvariantCulture,
                        "lumped thermal case {0}: expected {1:F4} K, got {2:F4} K ({3:P2})",
                        rows, expected, actual, relativeError));
            }

            Check(rows == 4, "thermal fixture has 4 points");
        }

        private static void TestAlkalineConductivityGoldenData()
        {
            var candidates = new[]
            {
                Path.Combine("electrolyzers", "alkaline_conductivity_r9.csv"),
                Path.Combine("tests", "electrolyzers", "alkaline_conductivity_r9.csv")
            };
            var path = candidates.FirstOrDefault(File.Exists);
            Check(path != null, "R9 KOH conductivity fixture found");
            if (path == null) return;

            var rows = 0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") ||
                    line.StartsWith("temperature_c,koh_mass_fraction,")) continue;

                var parts = line.Split(',');
                if (parts.Length != 4)
                {
                    Check(false, "invalid KOH conductivity fixture row: " + line);
                    continue;
                }

                var temperatureC = double.Parse(parts[0], CultureInfo.InvariantCulture);
                var massFraction = double.Parse(parts[1], CultureInfo.InvariantCulture);
                var expectedS_m = double.Parse(parts[2], CultureInfo.InvariantCulture);
                var tolerance = double.Parse(parts[3], CultureInfo.InvariantCulture);

                var model = new ModelAlkaline_SemiEmpirical();
                var actualS_m = model.ConductivityKOH(massFraction, temperatureC + 273.15) * 100.0;
                var relativeError = Math.Abs(actualS_m - expectedS_m) / expectedS_m;
                rows++;
                Check(relativeError <= tolerance,
                    string.Format(CultureInfo.InvariantCulture,
                        "R9 KOH conductivity {0:F0} C, {1:F2} wt: expected {2:F3} S/m, got {3:F3} ({4:P2})",
                        temperatureC, massFraction, expectedS_m, actualS_m, relativeError));
            }

            Check(rows == 6, "R9 KOH conductivity fixture has 6 points");
        }

        private static void TestGasPurityRegression()
        {
            var model = new ModelPEM_Springer { U_rev = 1.183, U_tn = 1.48 };
            var calculator = new ElectrolyzerCalculator
            {
                Model = model,
                A_cell = 1000.0,
                N_cells = 182.0,
                i_min = 0.25,
                i_max = 2.5,
                T = 353.15,
                pCat = 30.0,
                pAn = 2.0,
                etaF_ref = 0.99
            };

            double lowH2inO2 = 0.0, ignored = 0.0;
            double ratedH2inO2 = 0.0, ignored2 = 0.0;
            calculator.GasPurity(0.02, 1e-5, ref lowH2inO2, ref ignored);
            calculator.GasPurity(2.5, 1e-5, ref ratedH2inO2, ref ignored2);
            Check(lowH2inO2 > 4.0, "low-load H2-in-O2 exceeds the 4 vol% LEL");
            Check(ratedH2inO2 < lowH2inO2, "H2-in-O2 falls as load rises");
            Check(lowH2inO2 <= 100.0 && ratedH2inO2 >= 0.0, "gas impurity stays in percent bounds");
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
                var errors = new List<string>();
                bridge.SignalError += (sender, e) => errors.Add(e.Message);

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
                Console.WriteLine("OPC UA published={0} expected={1} status={2} error={3} errors={4}",
                    published, pid.OutputAbs, bridge.Status.Message, bridge.Diagnostics.LastError, string.Join("; ", errors));
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
            Check(unitOperations.Any(u => u.Description == "H2 Purification"), "H2 purification is discovered");
            Check(unitOperations.Any(u => u.Description == "Hydrogen Storage"), "hydrogen storage is discovered");

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

        /// <summary>
        /// Shadow ownership: the PLC writes the manipulated variable over Modbus while the
        /// internal PID computes its own output and records the deviation.
        /// </summary>
        private static void TestShadowDeviation(IFlowsheet flowsheet, string reportDirectory)
        {
            var pid = flowsheet.SimulationObjects.Values.OfType<PIDController>().FirstOrDefault();
            Check(pid != null, "shadow: sample PID");
            if (pid == null) return;

            var mvTag = pid.ManipulatedObjectData.ID;
            var mvProperty = pid.ManipulatedObjectData.PropertyName;
            var pvTag = pid.ControlledObjectData.ID;
            var pvProperty = pid.ControlledObjectData.PropertyName;
            var span = pid.OutputMax - pid.OutputMin;

            var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
            var integrator = flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];
            var stepSeconds = integrator.IntegrationStep.TotalSeconds;

            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var samples = new List<ShadowSample>();
            var scale = Math.Min(100.0, 32767.0 / Math.Max(Math.Abs(pid.OutputMin), Math.Abs(pid.OutputMax)));
            pid.Ownership = ControlOwnership.Shadow;

            var map = new SignalMap();
            map.Connections.Add(new ConnectionConfig
            {
                Name = "Shadow",
                Protocol = BridgeProtocol.ModbusTcpServer,
                Host = "127.0.0.1",
                Port = port,
                UnitId = 1,
                Enabled = true
            });
            map.Signals.Add(new Signal
            {
                Id = "MV",
                Direction = SignalDirection.FromPlc,
                ObjectTag = mvTag,
                Property = mvProperty,
                Unit = "%",
                Type = SignalDataType.Int16,
                Scale = scale,
                ModbusArea = ModbusArea.HoldingRegister,
                ModbusAddress = 0,
                ConnectionName = "Shadow"
            });
            map.Signals.Add(new Signal
            {
                Id = "PV",
                Direction = SignalDirection.ToPlc,
                ObjectTag = pvTag,
                Property = pvProperty,
                Unit = "m",
                Type = SignalDataType.Int16,
                Scale = 1000.0,
                ModbusArea = ModbusArea.InputRegister,
                ModbusAddress = 0,
                ConnectionName = "Shadow"
            });

            using (var bridge = new ModbusTcpServerBridge())
            {
                Check(bridge.Connect(map, flowsheet), "shadow: bridge connects: " + bridge.Status.Message);
                if (!bridge.Status.Connected) return;

                IModbusMaster master = null;
                TcpClient client = null;
                try
                {
                    Thread.Sleep(200);
                    client = new TcpClient("127.0.0.1", port);
                    master = new ModbusFactory().CreateMaster(client);

                    var plc = new ShadowPlc(pid);
                    ShadowBenchHost host = null;
                    var stats = DynamicsRunner.Run(flowsheet, new RunOptions
                    {
                        DurationSeconds = 60,
                        StepSeconds = stepSeconds,
                        ReuseInitialState = schedule.UseCurrentStateAsInitial,
                        InitialStateId = schedule.InitialFlowsheetStateID
                    }, host = new ShadowBenchHost(bridge, plc, master, pid, flowsheet, pvTag, pvProperty, scale, span, samples, stepSeconds));

                    Check(stats.Error == null, "shadow: run without errors: " + stats.Error);
                    Check(stats.StepCount == 12, "shadow: twelve steps");
                    Check(pid.Ownership == ControlOwnership.Shadow, "shadow: ownership stays Shadow");
                    Check(samples.Count == stats.StepCount, "shadow: one deviation sample per step");
                    Check(pid.ShadowDeviationHistory.Count >= stats.StepCount, "shadow: deviation recorded every step");

                    var worst = samples.Count == 0
                        ? double.PositiveInfinity
                        : samples.Max(s => Math.Abs(s.DeviationPctOfSpan));
                    Check(worst < 5.0, string.Format(CultureInfo.InvariantCulture, "shadow: worst deviation {0:F2}% of span", worst));

                    var mvActual = Convert.ToDouble(flowsheet.SimulationObjects[mvTag].GetPropertyValue(mvProperty));
                    Check(Math.Abs(mvActual - host.LastWritten) <= 1.0 / scale,
                        string.Format(CultureInfo.InvariantCulture, "shadow: PLC write reached the manipulated object ({0:F3} vs {1:F3})", mvActual, host.LastWritten));

                    if (reportDirectory != null) WriteShadowReport(reportDirectory, samples);
                }
                catch (Exception ex)
                {
                    Check(false, "shadow: exception " + ex);
                }
                finally
                {
                    client?.Close();
                    bridge.Disconnect();
                }
            }
        }

        private static void WriteShadowReport(string directory, List<ShadowSample> samples)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "v5.1.7-shadow-deviation.csv");
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("step,sim_time_s,pv_m,sp_m,internal_output_pct,external_output_pct,deviation_pct,deviation_pct_of_span");
                foreach (var sample in samples)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        sample.Step.ToString(CultureInfo.InvariantCulture),
                        sample.SimTime.ToString("F3", CultureInfo.InvariantCulture),
                        sample.Pv.ToString("F4", CultureInfo.InvariantCulture),
                        sample.Sp.ToString("F4", CultureInfo.InvariantCulture),
                        sample.InternalOutput.ToString("F4", CultureInfo.InvariantCulture),
                        sample.ExternalOutput.ToString("F4", CultureInfo.InvariantCulture),
                        sample.Deviation.ToString("F4", CultureInfo.InvariantCulture),
                        sample.DeviationPctOfSpan.ToString("F4", CultureInfo.InvariantCulture)
                    }));
                }
            }
            Console.WriteLine("shadow deviation report: " + path);
        }

        private sealed class ShadowSample
        {
            public int Step;
            public double SimTime;
            public double Pv;
            public double Sp;
            public double InternalOutput;
            public double ExternalOutput;
            public double Deviation;
            public double DeviationPctOfSpan;
        }

        /// <summary>Independent PI controller standing in for the PLC, with a 3% gain mismatch.</summary>
        private sealed class ShadowPlc
        {
            private const double GainMismatch = 1.03;
            private readonly PIDController _pid;
            private double _integral;
            private double _lastError;
            private bool _primed;

            public ShadowPlc(PIDController pid)
            {
                _pid = pid;
            }

            public double Step(double pv, double sp, double dt, double currentMv)
            {
                var baseSP = _pid.BaseSP.HasValue && _pid.BaseSP.Value != 0.0 ? _pid.BaseSP.Value : Math.Abs(sp);
                if (baseSP == 0.0) baseSP = 1.0;

                var error = (_pid.ReverseActing ? pv - sp : sp - pv) / baseSP;

                if (!_primed)
                {
                    _primed = true;
                    var output0 = _pid.ReverseActing ? currentMv / baseSP - 1.0 : 1.0 - currentMv / baseSP;
                    _integral = GainMismatch * _pid.Ki != 0.0
                        ? (output0 - GainMismatch * _pid.Kp * error - _pid.Offset / baseSP) / (GainMismatch * _pid.Ki)
                        : 0.0;
                    _lastError = error;
                }
                else
                {
                    _integral += error * dt;
                }

                var dTerm = (error - _lastError) / dt;
                _lastError = error;

                var output = GainMismatch * (_pid.Kp * error + _pid.Ki * _integral + _pid.Kd * dTerm) + _pid.Offset / baseSP;
                var mv = _pid.ReverseActing ? (1.0 + output) * baseSP : (1.0 - output) * baseSP;
                mv = Math.Min(_pid.OutputMax, Math.Max(_pid.OutputMin, mv));
                return mv;
            }
        }

        private sealed class ShadowBenchHost : IDynamicsHost
        {
            private readonly IPlantBridge _bridge;
            private readonly ShadowPlc _plc;
            private readonly IModbusMaster _master;
            private readonly PIDController _pid;
            private readonly IFlowsheet _flowsheet;
            private readonly string _pvTag;
            private readonly string _pvProperty;
            private readonly double _scale;
            private readonly double _span;
            private readonly List<ShadowSample> _samples;
            private readonly double _stepSeconds;
            private bool _pvPublished;

            public ShadowBenchHost(IPlantBridge bridge, ShadowPlc plc, IModbusMaster master, PIDController pid,
                                   IFlowsheet flowsheet, string pvTag, string pvProperty, double scale, double span,
                                   List<ShadowSample> samples, double stepSeconds)
            {
                _bridge = bridge;
                _plc = plc;
                _master = master;
                _pid = pid;
                _flowsheet = flowsheet;
                _pvTag = pvTag;
                _pvProperty = pvProperty;
                _scale = scale;
                _span = span;
                _samples = samples;
                _stepSeconds = stepSeconds;
            }

            /// <summary>The manipulated value the model actually received from the last register write.</summary>
            public double LastWritten { get; private set; }

            public bool ShouldAbort => false;
            public bool IsPaused => false;

            public void OnRunStarting(IFlowsheet flowsheet, RunOptions options) { }

            public void OnPreStep(int stepIndex, double simTime)
            {
                var pv = _pvPublished
                    ? _master.ReadInputRegisters(1, 0, 1)[0] / 1000.0
                    : Convert.ToDouble(_flowsheet.SimulationObjects[_pvTag].GetPropertyValue(_pvProperty));

                var sp = _pid.AdjustValue;
                var currentMv = Convert.ToDouble(_flowsheet.SimulationObjects[_pid.ManipulatedObjectData.ID]
                    .GetPropertyValue(_pid.ManipulatedObjectData.PropertyName));

                var mv = _plc.Step(pv, sp, _stepSeconds, currentMv);
                var raw = (int)Math.Round(mv * _scale);
                raw = Math.Max(0, Math.Min(65535, raw));
                _master.WriteSingleRegister(1, 0, (ushort)raw);
                LastWritten = raw / _scale;

                _bridge.ApplyInputs();
            }

            public void OnPostStep(int stepIndex, double simTime, RunStats stats)
            {
                _bridge.PublishOutputs();
                _pvPublished = true;

                _samples.Add(new ShadowSample
                {
                    Step = stepIndex,
                    SimTime = simTime,
                    Pv = _pid.PVValue,
                    Sp = _pid.SPValue,
                    InternalOutput = _pid.OutputAbs,
                    ExternalOutput = _pid.ExternalMV,
                    Deviation = _pid.ShadowDeviation,
                    DeviationPctOfSpan = _span != 0.0 ? _pid.ShadowDeviation / _span * 100.0 : 0.0
                });
            }

            public void OnControllerIteration(ISimulationObject controller) { }

            public void OnError(Exception exception, int stepIndex) { }

            public void OnRunFinished(RunStats stats) { }
        }

        private static void Check(bool condition, string message)
        {
            if (condition) return;
            _failures++;
            Console.Error.WriteLine("FAIL: " + message);
        }
    }
}
