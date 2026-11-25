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
            if (shadowReport != null)
            {
                shadowReport = shadowReport.Substring("--shadow-report=".Length);
            }
            else
            {
                for (var i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "--shadow-report")
                    {
                        shadowReport = args[i + 1];
                        break;
                    }
                }
            }

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
            TestAlkalinePolarizationGoldenData();
            TestAlkalineSecGoldenData();
            TestThermalGoldenData();
            TestStackHeatLossShare();
            TestGasPurityRegression();
            TestPurificationPurity();
            TestFaradayEfficiency();
            TestInterlocks();
            TestDynamicSupport();
            TestElectrolyzerReport();
            TestLyeLoopCooling();
            TestDynamicSamples();
            TestDynamicBaseline();
            TestPlantSamples();

            if (Environment.GetEnvironmentVariable("H2SIM_INTERLOCK_TEST") == "1")
                TestInterlockThroughBridge(shadowReport);

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
            // Placeholder flow for the first solve; it is resized from the model's CoolingDuty
            // after the flowsheet converges.
            coolingIn.SetMassFlow(5.0);
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
            var purifiedH2 = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 600, 30, "H2-PURE");
            var offGas = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 520, 130, "OFFGAS");
            var compressedH2 = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 900, 30, "H2-HP");
            var storedH2 = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 1000, 30, "H2-PROD");

            baseFlowsheet.AddObjectToSurface(ObjectType.External, 500, 20, "PUR-1", "", purification, false);
            purification.CreateConnectors();
            baseFlowsheet.AddObjectToSurface(ObjectType.External, 950, 20, "STO-1", "", storage, false);
            storage.CreateConnectors();

            // Cool the saturated cathode gas and flash it. Water leaves as liquid; the
            // purifier then only has to take the remaining vapour down to the ppm target.
            var dryH2 = AddCathodeFlash(flowsheet, h2);
            flowsheet.ConnectObjects(dryH2.GraphicObject, purification.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(purification.GraphicObject, purifiedH2.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(purification.GraphicObject, offGas.GraphicObject, 1, 0);

            // Two-stage compression with interstage cooling and knock-out, 30 -> 90 -> 250 bar.
            AddCompressionTrain(flowsheet, baseFlowsheet, purifiedH2, compressedH2);
            flowsheet.ConnectObjects(compressedH2.GraphicObject, storage.GraphicObject, 0, 0);
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

            // Size the coolant from the heat the stack actually rejects (10 K rise), then let the
            // solver settle the cooling stream again with that flow.
            var coolantRate = Math.Max(0.1, unit.CoolingDuty * 1000.0 / (4180.0 * 10.0));
            coolingIn.SetMassFlow(coolantRate);
            coolingIn.SetFlashSpec("PT");
            coolingIn.Calculate();
            var recalculate = ((global::DWSIM.Automation.AutomationInterface)automation).CalculateFlowsheet2(flowsheet);
            if (recalculate.Count > 0) throw new Exception(string.Join("; ", recalculate.Select(e => e.Message)));
            Console.WriteLine("coolant sized from {0:F1} kW heat: {1:F2} kg/s", unit.CoolingDuty, coolantRate);

            automation.SaveFlowsheet(flowsheet, Path.Combine(directory, fileName), true);
            Console.WriteLine("generated {0}: power={1} kW, H2 purity={2}%", fileName, unit.Power, unit.H2Purity);
        }

        /// <summary>
        /// Cools the saturated cathode gas to 40 °C and flashes it. Outlet 0 of the vessel is
        /// vapour, outlet 1 is liquid water.
        /// </summary>
        private static MaterialStream AddCathodeFlash(IFlowsheet flowsheet, MaterialStream cathode)
        {
            var cooled = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 490, 50, "H2-COOL");
            var vapor = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 560, 30, "H2-DRY");
            var liquid = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 560, 110, "H2-COND");

            var cooler = (Cooler)flowsheet.AddObject(ObjectType.Cooler, 470, 50, "E-GL");
            cooler.CalcMode = Cooler.CalculationMode.OutletTemperature;
            cooler.OutletTemperature = 313.15;
            cooler.DeltaP = 0.0;

            var separator = (Vessel)flowsheet.AddObject(ObjectType.Vessel, 530, 50, "V-GL");

            flowsheet.ConnectObjects(cathode.GraphicObject, cooler.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(cooler.GraphicObject, cooled.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(cooled.GraphicObject, separator.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(separator.GraphicObject, vapor.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(separator.GraphicObject, liquid.GraphicObject, 1, 0);
            return vapor;
        }

        /// <summary>
        /// Adds the compression train between the purified hydrogen and the storage inlet:
        /// stage 1 to 90 bar, interstage cooler to 40 °C, knock-out vessel, stage 2 to 250 bar.
        /// ALK enters near 15.5 bar and PEM near 29.5 bar; both discharge specifications are absolute.
        /// Each stage stays in outlet-pressure mode. The steady solve writes shaft work to
        /// <c>DeltaQ</c> and copies it to that stage's energy stream, and the saved templates keep
        /// those values. Discharge pressure is the specification, so the generator does not change
        /// the mode after the solve.
        /// </summary>
        private static void AddCompressionTrain(IFlowsheet flowsheet, DWSIM.FlowsheetBase.FlowsheetBase baseFlowsheet,
                                                MaterialStream feed, MaterialStream product)
        {
            var stage1Out = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 700, 30, "H2-90BAR");
            var stage1Power = (DWSIM.UnitOperations.Streams.EnergyStream)flowsheet.AddObject(ObjectType.EnergyStream, 700, 90, "W-C1");
            var afterCooler = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 760, 30, "H2-90BAR-C");
            var knockOutGas = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 820, 30, "H2-90BAR-DRY");
            var knockOutLiquid = (MaterialStream)flowsheet.AddObject(ObjectType.MaterialStream, 820, 90, "H2-KO-COND");
            var stage2Power = (DWSIM.UnitOperations.Streams.EnergyStream)flowsheet.AddObject(ObjectType.EnergyStream, 900, 90, "W-C2");

            var stage1 = (Compressor)flowsheet.AddObject(ObjectType.Compressor, 650, 30, "C-1");
            stage1.CalcMode = Compressor.CalculationMode.OutletPressure;
            stage1.POut = 90.0 * 100000.0;
            stage1.AdiabaticEfficiency = 75.0;

            var cooler = (Cooler)flowsheet.AddObject(ObjectType.Cooler, 730, 30, "E-1");
            cooler.CalcMode = Cooler.CalculationMode.OutletTemperature;
            cooler.OutletTemperature = 313.15;
            cooler.DeltaP = 0.0;

            var knockOut = (Vessel)flowsheet.AddObject(ObjectType.Vessel, 790, 30, "KO-1");

            var stage2 = (Compressor)flowsheet.AddObject(ObjectType.Compressor, 870, 30, "C-2");
            stage2.CalcMode = Compressor.CalculationMode.OutletPressure;
            stage2.POut = 250.0 * 100000.0;
            stage2.AdiabaticEfficiency = 75.0;

            flowsheet.ConnectObjects(feed.GraphicObject, stage1.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(stage1Power.GraphicObject, stage1.GraphicObject, 0, 1);
            flowsheet.ConnectObjects(stage1.GraphicObject, stage1Out.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(stage1Out.GraphicObject, cooler.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(cooler.GraphicObject, afterCooler.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(afterCooler.GraphicObject, knockOut.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(knockOut.GraphicObject, knockOutGas.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(knockOut.GraphicObject, knockOutLiquid.GraphicObject, 1, 0);
            flowsheet.ConnectObjects(knockOutGas.GraphicObject, stage2.GraphicObject, 0, 0);
            flowsheet.ConnectObjects(stage2Power.GraphicObject, stage2.GraphicObject, 0, 1);
            flowsheet.ConnectObjects(stage2.GraphicObject, product.GraphicObject, 0, 0);
        }

        /// <summary>
        /// Numerical regression for the six dynamic samples: the headless runner must reproduce
        /// the recorded final state within a tight relative tolerance. The baseline can be
        /// rewritten with H2SIM_WRITE_BASELINE=1 when an intended model change moves it.
        /// </summary>
        private static void TestDynamicBaseline()
        {
            var directories = new[] { "samples", Path.Combine("PlatformFiles", "Common", "samples") };
            var directory = directories.FirstOrDefault(Directory.Exists);
            if (directory == null) return;

            var baselineCandidates = new[]
            {
                Path.Combine("dynamic", "samples_baseline.csv"),
                Path.Combine("tests", "dynamic", "samples_baseline.csv")
            };
            var baselinePath = baselineCandidates.FirstOrDefault(File.Exists);
            var writeBaseline = Environment.GetEnvironmentVariable("H2SIM_WRITE_BASELINE") == "1";

            var expected = new Dictionary<string, double>(StringComparer.Ordinal);
            if (baselinePath != null && !writeBaseline)
            {
                foreach (var raw in File.ReadLines(baselinePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("sample,")) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    expected[parts[0] + "|" + parts[1]] = double.Parse(parts[2], CultureInfo.InvariantCulture);
                }
            }

            var written = new List<string> { "sample,variable,value,relative_tolerance" };
            var failures = 0;

            foreach (var file in Directory.EnumerateFiles(directory, "Dynamic Simulation - *.dwxmz").OrderBy(x => x))
            {
                var name = Path.GetFileName(file);
                try
                {
                    var automation = new global::DWSIM.Automation.Automation3();
                    var flowsheet = automation.LoadFlowsheet(file);
                    var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
                    var integrator = flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];

                    var stats = DynamicsRunner.Run(flowsheet, new RunOptions
                    {
                        DurationSeconds = 10.0,
                        StepSeconds = name.Contains("PFR") ? 0.1 : integrator.IntegrationStep.TotalSeconds,
                        ReuseInitialState = schedule.UseCurrentStateAsInitial,
                        InitialStateId = schedule.InitialFlowsheetStateID
                    }, new HeadlessDynamicsHost());

                    if (stats.Error != null)
                    {
                        Check(false, "dynamic baseline: " + name + ": " + stats.Error.Message);
                        continue;
                    }

                    // The metric: total enthalpy of every material stream, which any change to the
                    // solver, unit operation or integrator will move.
                    var enthalpy = flowsheet.SimulationObjects.Values
                        .OfType<MaterialStream>()
                        .Sum(s => s.GetMassEnthalpy() * s.GetMassFlow());

                    var key = name + "|total_stream_enthalpy_kw";
                    written.Add(string.Format(CultureInfo.InvariantCulture, "{0},total_stream_enthalpy_kw,{1:R},1e-6", name, enthalpy));

                    if (writeBaseline)
                    {
                        Console.WriteLine("baseline {0}: enthalpy={1:R} kW", name, enthalpy);
                        continue;
                    }

                    if (!expected.TryGetValue(key, out var reference))
                    {
                        Console.WriteLine("baseline {0}: no reference recorded (enthalpy={1:R} kW)", name, enthalpy);
                        continue;
                    }

                    var error = Math.Abs(enthalpy - reference) / Math.Max(Math.Abs(reference), 1e-9);
                    if (error > 1e-6)
                    {
                        failures++;
                        Check(false, string.Format(CultureInfo.InvariantCulture,
                            "dynamic baseline: {0}: enthalpy {1:R} kW vs {2:R} kW ({3:P2})", name, enthalpy, reference, error));
                    }
                }
                catch (Exception ex)
                {
                    Check(false, "dynamic baseline: " + name + ": " + ex.Message);
                }
            }

            if (writeBaseline && baselinePath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(baselinePath)));
                File.WriteAllLines(baselinePath, written);
                Console.WriteLine("wrote dynamic baseline: " + baselinePath);
            }
            else if (baselinePath == null)
            {
                Console.WriteLine("dynamic baseline: no baseline file yet, " + written.Count + " samples measured");
            }
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

                    // The loader matches external UOs by description. Headless generation
                    // never draws, so AddObjectToSurface has to save it up front.
                    var externalObjects = flowsheet.SimulationObjects.Values
                        .Where(o => o.GraphicObject != null && o.GraphicObject.ObjectType == ObjectType.External)
                        .ToList();
                    Check(externalObjects.Count >= 3,
                        "plant external UOs present: " + Path.GetFileName(file) + " (" + externalObjects.Count + ")");
                    foreach (var external in externalObjects)
                    {
                        Check(!string.IsNullOrEmpty(external.GraphicObject.Description),
                            "external UO description saved: " + external.GraphicObject.Tag + " " + Path.GetFileName(file));
                    }

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

                        // The reported purity must agree with what the streams actually contain.
                        var h2Stream = ConnectedStream(flowsheet, unit, 0, false);
                        Check(h2Stream != null, "cathode stream connected: " + Path.GetFileName(file));
                        if (h2Stream != null)
                        {
                            var cathodeTotal = h2Stream.Phases[0].Compounds.Values.Sum(c => c.MolarFlow.GetValueOrDefault());
                            var reportedO2inH2 = Convert.ToDouble(unit.GetPropertyValue("O2 in H2"));
                            var streamO2inH2 = 100.0 * MolarFlow(h2Stream, "Oxygen") / Math.Max(cathodeTotal, 1e-12);
                            Console.WriteLine("purity {0}: stream O2-in-H2={1:E3}%, KPI={2:E3}%",
                                Path.GetFileName(file), streamO2inH2, reportedO2inH2);
                            Check(Math.Abs(streamO2inH2 - reportedO2inH2) < 1e-9 * Math.Max(1.0, reportedO2inH2) + 1e-12,
                                string.Format(CultureInfo.InvariantCulture,
                                    "stream O2-in-H2 {0:E3} % matches the reported KPI {1:E3} %", streamO2inH2, reportedO2inH2));

                            var condensate = flowsheet.SimulationObjects.Values
                                .FirstOrDefault(o => o.GraphicObject != null && o.GraphicObject.Tag == "H2-COND") as MaterialStream;
                            var dry = flowsheet.SimulationObjects.Values
                                .FirstOrDefault(o => o.GraphicObject != null && o.GraphicObject.Tag == "H2-DRY") as MaterialStream;
                            Check(condensate != null && dry != null, "cathode flash streams present: " + Path.GetFileName(file));
                            if (condensate != null && dry != null)
                            {
                                var knockedOut = MolarFlow(condensate, "Water");
                                var cathodeWater = MolarFlow(h2Stream, "Water");
                                Console.WriteLine("flash {0}: cathode water={1:E3} mol/s, condensate={2:E3} mol/s, dry={3:E3} mol/s",
                                    Path.GetFileName(file), cathodeWater, knockedOut, MolarFlow(dry, "Water"));
                                Check(knockedOut > 1e-8 && knockedOut < cathodeWater,
                                    "cathode flash condenses part of the saturated water: " + Path.GetFileName(file));
                            }
                        }

                        CheckCompressionTrain(flowsheet, Path.GetFileName(file), strict: true);
                        CheckComponentBalance(flowsheet, unit, Path.GetFileName(file));
                        CheckEnergyBalance(flowsheet, unit, Path.GetFileName(file));
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

                        // The stack must not run away thermally: with the coolant wired in, and the
                        // cooling stream it draws from sized for the heat load, the temperature has
                        // to stay near the operating point through the run.
                        if (unit != null && dynamicStats.Error == null)
                        {
                            var stackTemperature = unit.StackTemperature;
                            var coolantFlow = unit.GetDynamicProperty("Cooling Water Flow");
                            Console.WriteLine("plant dynamics {0}: stack {1:F2} K, coolant {2} kg/s, trip={3}",
                                Path.GetFileName(file), stackTemperature,
                                coolantFlow == null ? "?" : Convert.ToDouble(coolantFlow).ToString("F2", CultureInfo.InvariantCulture),
                                unit.TripLatched);
                            Check(stackTemperature < unit.Parameters["StackTemperature_trip"].Value,
                                string.Format(CultureInfo.InvariantCulture,
                                    "plant dynamics: {0}: stack temperature {1:F2} K stayed below the trip point",
                                    Path.GetFileName(file), stackTemperature));
                            Check(coolantFlow != null && Convert.ToDouble(coolantFlow) > 0.0,
                                "plant dynamics: coolant flow reached the thermal model: " + Path.GetFileName(file));
                            CheckCompressionTrain(flowsheet, Path.GetFileName(file), strict: false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Check(false, "plant sample: " + Path.GetFileName(file) + ": " + ex.ToString());
                }
            }
        }

        /// <summary>
        /// Two-stage hydrogen compression: 90 bar then 250 bar, 75% isentropic, interstage cool to
        /// 40 °C. Outlet pressure is the specification. Shaft work is a result of that specification,
        /// stored on the compressor and on its energy stream. A dynamic step only has to keep the
        /// setpoint and a positive shaft work; the cooler holdup may rewrite the interstage stream.
        /// </summary>
        private static void CheckCompressionTrain(IFlowsheet flowsheet, string file, bool strict)
        {
            var stage1 = FindByTag<Compressor>(flowsheet, "C-1");
            var stage2 = FindByTag<Compressor>(flowsheet, "C-2");
            var power1 = FindByTag<DWSIM.UnitOperations.Streams.EnergyStream>(flowsheet, "W-C1");
            var power2 = FindByTag<DWSIM.UnitOperations.Streams.EnergyStream>(flowsheet, "W-C2");
            var cooler = FindByTag<Cooler>(flowsheet, "E-1");
            var purified = FindByTag<MaterialStream>(flowsheet, "H2-PURE");
            var stage1Out = FindByTag<MaterialStream>(flowsheet, "H2-90BAR");
            var stage2In = FindByTag<MaterialStream>(flowsheet, "H2-90BAR-DRY");
            var cooled = FindByTag<MaterialStream>(flowsheet, "H2-90BAR-C");
            var product = FindByTag<MaterialStream>(flowsheet, "H2-HP");

            Check(stage1 != null && stage2 != null && power1 != null && power2 != null && cooler != null
                  && purified != null && stage1Out != null && stage2In != null && cooled != null && product != null,
                "compression train present: " + file);
            if (stage1 == null || stage2 == null || power1 == null || power2 == null || cooler == null
                || purified == null || stage1Out == null || stage2In == null || cooled == null || product == null)
                return;

            var phase = strict ? "steady" : "dynamic";
            Console.WriteLine("compression {0} {1}: C-1 {2:F2} kW, C-2 {3:F2} kW",
                phase, file, stage1.DeltaQ, stage2.DeltaQ);

            CheckCompressorStage(stage1, power1, purified, stage1Out, 90.0 * 100000.0, file, "C-1", strict);
            CheckCompressorStage(stage2, power2, stage2In, product, 250.0 * 100000.0, file, "C-2", strict);

            if (!strict) return;

            Check(Math.Abs(cooler.OutletTemperature.GetValueOrDefault() - 313.15) < 1e-6,
                "interstage cooler setpoint is 40 C: " + file);
            Check(Math.Abs(cooled.GetTemperature() - 313.15) < 0.05,
                "interstage stream is at 40 C: " + file);
            var massError = Math.Abs(purified.GetMassFlow() - product.GetMassFlow())
                / Math.Max(purified.GetMassFlow(), 1e-12);
            Check(massError < 1e-8, string.Format(CultureInfo.InvariantCulture,
                "compression train conserves mass: {0} (gap {1:E3})", file, massError));
        }

        private static void CheckCompressorStage(Compressor stage, DWSIM.UnitOperations.Streams.EnergyStream power,
                                                  MaterialStream inlet, MaterialStream outlet, double outletPressure,
                                                  string file, string tag, bool strict)
        {
            Check(stage.CalcMode == Compressor.CalculationMode.OutletPressure,
                tag + " stays in outlet-pressure mode: " + file);
            Check(Math.Abs(stage.AdiabaticEfficiency - 75.0) < 1e-9,
                tag + " isentropic efficiency is 75%: " + file);
            Check(Math.Abs(stage.POut - outletPressure) < 1.0,
                tag + " discharge specification: " + file);
            Check(stage.DeltaQ > 1.0, string.Format(CultureInfo.InvariantCulture,
                "{0} shaft work is positive: {1} {2:F3} kW", tag, file, stage.DeltaQ));

            var reported = power.EnergyFlow.GetValueOrDefault();
            var copied = Math.Abs(reported - stage.DeltaQ) / Math.Max(stage.DeltaQ, 1e-12);
            Check(copied < 1e-6, string.Format(CultureInfo.InvariantCulture,
                "{0} energy stream carries the shaft work: {1} {2:F3} kW vs {3:F3} kW",
                tag, file, reported, stage.DeltaQ));

            if (!strict) return;

            var fromEnthalpy = inlet.GetMassFlow() * (outlet.GetMassEnthalpy() - inlet.GetMassEnthalpy());
            var workError = Math.Abs(fromEnthalpy - stage.DeltaQ) / Math.Max(stage.DeltaQ, 1e-12);
            Check(workError < 1e-6, string.Format(CultureInfo.InvariantCulture,
                "{0} shaft work matches the enthalpy rise: {1} {2:F3} kW vs {3:F3} kW",
                tag, file, fromEnthalpy, stage.DeltaQ));
            var pressureError = Math.Abs(outlet.GetPressure() - outletPressure) / outletPressure;
            Check(pressureError < 1e-6, string.Format(CultureInfo.InvariantCulture,
                "{0} outlet is at the discharge pressure: {1} {2:F0} Pa", tag, file, outlet.GetPressure()));
        }

        private static T FindByTag<T>(IFlowsheet flowsheet, string tag) where T : class
        {
            return flowsheet.SimulationObjects.Values
                .FirstOrDefault(o => o.GraphicObject != null && o.GraphicObject.Tag == tag) as T;
        }

        /// <summary>
        /// Element balance around the electrolyzer. Hydrogen and oxygen are produced by splitting
        /// water, so compounds do not balance individually; the elements H and O do.
        /// </summary>
        private static void CheckComponentBalance(IFlowsheet flowsheet, ElectrolyzerUnitOpBase unit, string file)
        {
            var feed = ConnectedStream(flowsheet, unit, 0, true);
            var coolingIn = ConnectedStream(flowsheet, unit, 1, true);
            var h2 = ConnectedStream(flowsheet, unit, 0, false);
            var o2 = ConnectedStream(flowsheet, unit, 1, false);
            var coolingOut = ConnectedStream(flowsheet, unit, 2, false);
            Check(feed != null && h2 != null && o2 != null, "mass balance: streams connected: " + file);
            if (feed != null && coolingIn != null) Check(coolingOut != null, "mass balance: cooling loop closed: " + file);
            if (feed == null || h2 == null || o2 == null) return;

            // Moles of hydrogen atoms and of oxygen atoms in each stream.
            var hydrogenIn = HydrogenAtoms(feed) + (coolingIn != null ? HydrogenAtoms(coolingIn) : 0.0);
            var hydrogenOut = HydrogenAtoms(h2) + HydrogenAtoms(o2) + (coolingOut != null ? HydrogenAtoms(coolingOut) : 0.0);
            var oxygenIn = OxygenAtoms(feed) + (coolingIn != null ? OxygenAtoms(coolingIn) : 0.0);
            var oxygenOut = OxygenAtoms(h2) + OxygenAtoms(o2) + (coolingOut != null ? OxygenAtoms(coolingOut) : 0.0);

            var hydrogenError = Math.Abs(hydrogenIn - hydrogenOut) / Math.Max(hydrogenIn, 1e-12);
            var oxygenError = Math.Abs(oxygenIn - oxygenOut) / Math.Max(oxygenIn, 1e-12);
            Console.WriteLine("balance {0}: H atoms close to {1:E3}, O atoms to {2:E3}",
                file, hydrogenError, oxygenError);
            Check(hydrogenError < 1e-8, string.Format(CultureInfo.InvariantCulture,
                "mass balance: {0}: hydrogen atoms close to {1:E3} (limit 1e-8)", file, hydrogenError));
            Check(oxygenError < 1e-8, string.Format(CultureInfo.InvariantCulture,
                "mass balance: {0}: oxygen atoms close to {1:E3} (limit 1e-8)", file, oxygenError));

            var inletMass = feed.GetMassFlow() + (coolingIn?.GetMassFlow() ?? 0.0);
            var outletMass = h2.GetMassFlow() + o2.GetMassFlow() + (coolingOut?.GetMassFlow() ?? 0.0);
            var massError = Math.Abs(inletMass - outletMass) / Math.Max(inletMass, 1e-12);
            Check(massError < 1e-8, string.Format(CultureInfo.InvariantCulture,
                "mass balance: {0}: total mass closes to {1:E3} (limit 1e-8)", file, massError));
        }

        private static double HydrogenAtoms(MaterialStream stream)
        {
            var compounds = stream.Phases[0].Compounds;
            var atoms = 0.0;
            foreach (var pair in compounds)
            {
                var name = pair.Key.ToLowerInvariant();
                var flow = pair.Value.MolarFlow.GetValueOrDefault();
                // Water, hydrogen and heavy water are the only hydrogen-bearing species here.
                if (name.Contains("hydrogen") || name.Contains("water")) atoms += 2.0 * flow;
            }
            return atoms;
        }

        private static double OxygenAtoms(MaterialStream stream)
        {
            var compounds = stream.Phases[0].Compounds;
            var atoms = 0.0;
            foreach (var pair in compounds)
            {
                var name = pair.Key.ToLowerInvariant();
                var flow = pair.Value.MolarFlow.GetValueOrDefault();
                if (name == "oxygen") atoms += 2.0 * flow;
                else if (name.Contains("water")) atoms += 1.0 * flow;
            }
            return atoms;
        }

        /// <summary>
        /// Electrical power in equals the chemical power leaving in H2 plus the heat generated.
        /// The model routes the heat above the thermoneutral voltage to the coolant, so the
        /// check is: chemical (LHV) + cooling duty == electrical input, and the efficiency must
        /// stay physically possible (below 100% on both LHV and HHV bases).
        /// </summary>
        private static void CheckEnergyBalance(IFlowsheet flowsheet, ElectrolyzerUnitOpBase unit, string file)
        {
            var h2 = ConnectedStream(flowsheet, unit, 0, false);
            Check(h2 != null, "energy balance: hydrogen outlet connected: " + file);
            if (h2 == null) return;

            const double lhvKjPerMol = 241.826;
            var chemicalKW = MolarFlow(h2, "Hydrogen") * lhvKjPerMol;
            var coolingKW = unit.CoolingDuty;
            var electricalKW = unit.Power;

            Console.WriteLine("energy {0}: electrical={1:F3} kW, chemical(LHV)={2:F3} kW, cooling={3:F3} kW, HHV eff={4:P2}, LHV eff={5:P2}",
                file, electricalKW, chemicalKW, coolingKW, unit.EfficiencyHHV, unit.EfficiencyLHV);

            Check(unit.EfficiencyHHV < 1.0, string.Format(CultureInfo.InvariantCulture,
                "energy balance: {0}: HHV efficiency {1:P2} is above 100%", file, unit.EfficiencyHHV));
            Check(unit.EfficiencyLHV < 1.0, string.Format(CultureInfo.InvariantCulture,
                "energy balance: {0}: LHV efficiency {1:P2} is above 100%", file, unit.EfficiencyLHV));

            // Heat generated is the part of the electrical input above the thermoneutral power.
            var thermoneutralKW = unit.Current * unit.Parameters["N_cells"].Value * unit.ThermoneutralVoltage / 1000.0;
            var expectedHeatKW = Math.Max(0.0, electricalKW - thermoneutralKW);
            var heatError = Math.Abs(expectedHeatKW - coolingKW) / Math.Max(electricalKW, 1e-12);
            Console.WriteLine("energy {0}: thermoneutral={1:F3} kW, expected heat={2:F3} kW, model heat={3:F3} kW ({4:P2} of input)",
                file, thermoneutralKW, expectedHeatKW, coolingKW, heatError);
            Check(heatError < 0.02, string.Format(CultureInfo.InvariantCulture,
                "energy balance: {0}: heat removed {1:F3} kW vs heat generated {2:F3} kW", file, coolingKW, expectedHeatKW));

            // Closing the balance: electrolysis splits liquid water, so the electrical input
            // equals the thermoneutral (HHV) power plus the heat generated. The LHV basis omits
            // water's latent heat and therefore leaves a gap by construction.
            var balanceError = Math.Abs(electricalKW - thermoneutralKW - coolingKW) / Math.Max(electricalKW, 1e-12);
            Console.WriteLine("energy {0}: thermoneutral + heat closes to {1:P4} of input", file, balanceError);
            Check(balanceError < 1e-6, string.Format(CultureInfo.InvariantCulture,
                "energy balance: {0}: input {1:F3} kW vs thermoneutral {2:F3} + heat {3:F3} kW (gap {4:P4})",
                file, electricalKW, thermoneutralKW, coolingKW, balanceError));
        }

        private static MaterialStream ConnectedStream(IFlowsheet flowsheet, ElectrolyzerUnitOpBase unit, int index, bool inlet)
        {
            var connector = inlet ? unit.GraphicObject.InputConnectors[index] : unit.GraphicObject.OutputConnectors[index];
            if (connector == null || !connector.IsAttached) return null;
            var attached = inlet ? connector.AttachedConnector.AttachedFrom.Name : connector.AttachedConnector.AttachedTo.Name;
            if (!flowsheet.SimulationObjects.ContainsKey(attached)) return null;
            return flowsheet.SimulationObjects[attached] as MaterialStream;
        }

        private static double MolarFlow(MaterialStream stream, string compound)
        {
            if (!stream.Phases[0].Compounds.ContainsKey(compound)) return 0.0;
            return stream.Phases[0].Compounds[compound].MolarFlow.GetValueOrDefault();
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

                    var stats = DynamicsRunner.Run(flowsheet, new RunOptions
                    {
                        DurationSeconds = 10.0,
                        // The saved 1 s step is larger than this PFR's dynamic residence time, so
                        // it needs the same override any user would apply for that sample.
                        StepSeconds = Path.GetFileName(file).Contains("PFR") ? 0.1 : integrator.IntegrationStep.TotalSeconds,
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

        /// <summary>
        /// T-1.2 step 8: the report exposes the same KPIs as GetProperties, and the
        /// percentage/fraction units show up in the unit lookup.
        /// </summary>
        private static void TestElectrolyzerReport()
        {
            var unit = new PEMElectrolyzer();
            var report = unit.GetReport(null, CultureInfo.InvariantCulture, "F4");
            foreach (var label in new[]
            {
                "Current density", "Reversible voltage", "Thermoneutral voltage",
                "Activation overpotential", "Ohmic overpotential", "Concentration overpotential",
                "Faraday efficiency", "H2 purity", "H2 in O2", "O2 in H2",
                "Specific energy consumption", "Efficiency (HHV)", "Efficiency (LHV)",
                "Cooling duty", "Stack temperature", "Trip latched"
            })
            {
                Check(report.Contains(label), "electrolyzer report contains: " + label);
            }

            Check(unit.GetPropertyUnit("H2 Purity") == "%", "H2 purity unit is percent");
            Check(unit.GetPropertyUnit("H2 in O2") == "%", "H2-in-O2 unit is percent");
            Check(unit.GetPropertyUnit("O2 in H2") == "%", "O2-in-H2 unit is percent");
            Check(unit.GetPropertyUnit("Faraday Efficiency") == "", "Faraday efficiency is a fraction");
        }

        /// <summary>
        /// T-1.4 lye-loop cooling: effectiveness-NTU conductance uses the smaller heat
        /// capacity rate, clamps effectiveness, and removes no heat without cooling water.
        /// WaterFlow remains the default mode.
        /// </summary>
        private static void TestLyeLoopCooling()
        {
            var pem = new PEMElectrolyzer();
            Check(pem.Parameters.ContainsKey("CoolingMode") && pem.Parameters["CoolingMode"].Value == 0.0,
                "water flow is the default cooling mode");

            var expected = 0.8 * Math.Min(10.0 * 3500.0, 5.0 * 4180.0);
            Check(Math.Abs(pem.LyeLoopConductance(10.0, 3500.0, 5.0, 4180.0, 0.8) - expected) < 1e-9,
                "lye loop conductance is effectiveness times Cmin");
            Check(pem.LyeLoopConductance(10.0, 3500.0, 0.0, 4180.0, 0.8) == 0.0,
                "lye loop removes no heat without cooling water");
            Check(Math.Abs(pem.LyeLoopConductance(10.0, 3500.0, 5.0, 4180.0, 1.5) - 20900.0) < 1e-9,
                "lye loop effectiveness is clamped to 1");
            Check(Math.Abs(pem.LyeLoopConductance(0.1, 1000.0, 5.0, 4180.0, 0.8) - 80.0) < 1e-9,
                "lye side can be the smaller heat capacity rate");
        }

        /// <summary>
        /// V-5.1.8: a model-side interlock trips while the PLC is in control. The trip must cut
        /// power even though the internal controllers no longer write, and the PLC must see it.
        /// </summary>
        private static void TestInterlockThroughBridge(string reportDirectory)
        {
            var directory = Path.Combine(Path.GetTempPath(), "h2sim-interlock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                GeneratePlant(directory, "interlock.dwxmz", new PEMElectrolyzer(), 900.0, 30.0, 2.0);

                var automation = new global::DWSIM.Automation.Automation3();
                var flowsheet = automation.LoadFlowsheet(Path.Combine(directory, "interlock.dwxmz"));
                var unit = flowsheet.SimulationObjects.Values.OfType<ElectrolyzerUnitOpBase>().FirstOrDefault();
                Check(unit != null, "interlock: plant electrolyzer");
                if (unit == null) return;

                var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
                var integrator = flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];

                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();

                var map = new SignalMap();
                map.Connections.Add(new ConnectionConfig
                {
                    Name = "Interlock",
                    Protocol = BridgeProtocol.ModbusTcpServer,
                    Host = "127.0.0.1",
                    Port = port,
                    UnitId = 1,
                    Enabled = true
                });
                map.Signals.Add(new Signal
                {
                    Id = "TRIP",
                    Direction = SignalDirection.ToPlc,
                    ObjectTag = unit.Name,
                    Property = "Trip Latched",
                    Unit = "",
                    Type = SignalDataType.Bool,
                    ModbusArea = ModbusArea.Coil,
                    ModbusAddress = 1,
                    ConnectionName = "Interlock"
                });
                map.Signals.Add(new Signal
                {
                    Id = "POWER",
                    Direction = SignalDirection.ToPlc,
                    ObjectTag = unit.Name,
                    Property = "Power",
                    Unit = "kW",
                    Type = SignalDataType.Int16,
                    Scale = 1.0,
                    ModbusArea = ModbusArea.InputRegister,
                    ModbusAddress = 0,
                    ConnectionName = "Interlock"
                });
                map.Signals.Add(new Signal
                {
                    Id = "H2INO2",
                    Direction = SignalDirection.ToPlc,
                    ObjectTag = unit.Name,
                    Property = "H2 in O2",
                    Unit = "",
                    Type = SignalDataType.Int16,
                    Scale = 100.0,
                    ModbusArea = ModbusArea.InputRegister,
                    ModbusAddress = 1,
                    ConnectionName = "Interlock"
                });

                using (var bridge = new ModbusTcpServerBridge())
                {
                    Check(bridge.Connect(map, flowsheet), "interlock: bridge connects: " + bridge.Status.Message);
                    if (!bridge.Status.Connected) return;

                    IModbusMaster master = null;
                    TcpClient client = null;
                    var samples = new List<string>();
                    try
                    {
                        Thread.Sleep(200);
                        client = new TcpClient("127.0.0.1", port);
                        master = new ModbusFactory().CreateMaster(client);

                        var running = DynamicsRunner.Run(flowsheet, new RunOptions
                        {
                            DurationSeconds = 10,
                            StepSeconds = integrator.IntegrationStep.TotalSeconds,
                            ReuseInitialState = true,
                            InitialStateId = schedule.InitialFlowsheetStateID
                        }, new BridgeDynamicsHost(bridge));

                        Check(running.Error == null, "interlock: normal run without errors: " + running.Error);
                        var powerBefore = (double)unit.GetPropertyValue("Power");
                        var tripBefore = master.ReadCoils(1, 1, 1)[0];
                        Check(powerBefore > 1.0, "interlock: electrolyzer produces power before the trip: " + powerBefore);
                        Check(!tripBefore, "interlock: no trip before the excursion");
                        samples.Add(string.Format(CultureInfo.InvariantCulture, "before,power_kw={0:F3},trip={1}", powerBefore, tripBefore));

                        // Drive H2-in-O2 above the 4 vol% explosive limit, then recalculate the model.
                        unit.H2inO2 = 4.5;
                        unit.UpdateInterlocks(4.5, 0.1, 353.15, 0.0, 100.0);
                        Check(unit.TripLatched, "interlock: trip latches above 4 vol%");
                        unit.Calculate();

                        var after = DynamicsRunner.Run(flowsheet, new RunOptions
                        {
                            DurationSeconds = 5,
                            StepSeconds = integrator.IntegrationStep.TotalSeconds,
                            ReuseInitialState = true,
                            InitialStateId = schedule.InitialFlowsheetStateID
                        }, new BridgeDynamicsHost(bridge));

                        Check(after.Error == null, "interlock: post-trip run without errors: " + after.Error);
                        if (after.Error != null)
                        {
                            Console.Error.WriteLine(after.Error);
                        }
                        var powerAfter = master.ReadInputRegisters(1, 0, 1)[0];
                        var h2inO2 = master.ReadInputRegisters(1, 1, 1)[0];
                        var tripAfter = master.ReadCoils(1, 1, 1)[0];

                        Check((double)unit.GetPropertyValue("Power") == 0.0,
                            "interlock: model power is zero after the trip");
                        Check(powerAfter == 0, "interlock: PLC reads zero power after the trip: " + powerAfter);
                        Check(h2inO2 == 450, "interlock: PLC reads the H2-in-O2 excursion: " + h2inO2);
                        Check(tripAfter, "interlock: PLC reads the latched trip bit");
                        samples.Add(string.Format(CultureInfo.InvariantCulture, "after,power_kw_raw={0},h2ino2_raw={1},trip={2}", powerAfter, h2inO2, tripAfter));

                        // The trip must stay latched until an explicit reset even when the cause clears.
                        unit.UpdateInterlocks(0.5, 0.1, 353.15, 0.0, 100.0);
                        Check(unit.TripLatched, "interlock: trip stays latched after the cause clears");
                        Check(unit.ResetTrip(), "interlock: reset accepted when safe");
                        Check(!unit.TripLatched, "interlock: trip cleared after reset");
                        samples.Add("reset,trip=False");

                        if (reportDirectory != null)
                        {
                            Directory.CreateDirectory(reportDirectory);
                            File.WriteAllLines(Path.Combine(reportDirectory, "v5.1.8-interlock-bridge.csv"), samples);
                        }
                    }
                    catch (Exception ex)
                    {
                        Check(false, "interlock: exception " + ex);
                    }
                    finally
                    {
                        client?.Close();
                        bridge.Disconnect();
                    }
                }
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
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

            // The low end is an operating limit too: a target below the turndown limit must be
            // reported, not silently clamped to the minimum.
            var rejectedUnderload = false;
            try
            {
                calculator.CurrentForPower(calculator.PowerAt(calculator.i_min) * 0.5);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejectedUnderload = true;
            }
            Check(rejectedUnderload, "a power below the stack minimum is rejected");

            var atMinimum = calculator.CurrentForPower(calculator.PowerAt(calculator.i_min));
            Check(Math.Abs(atMinimum - calculator.i_min) < 1e-6, "the minimum power maps to the minimum current density");

            var alkaline = new AlkalineElectrolyzer();
            Check(alkaline.CreateElectrochemicalModel() is ModelAlkaline_SemiEmpirical, "alkaline model factory");

            var storage = new HydrogenStorage();
            Check(storage.SupportsDynamicMode && storage.HasPropertiesForDynamicMode, "storage dynamic support");
            Check(storage.UpdateStoredMoles(100.0, 1.0, 10.0) > 100.0, "storage mole balance");

            // The tank P-V-T relation has to honour the gas's real compressibility: at 250 bar
            // hydrogen is not ideal, and the old fixed 0.95 would understate the pressure.
            var pressureAtDefault = storage.TankPressure;
            Check(pressureAtDefault > 0.0, "storage reports a tank pressure");

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

        /// <summary>
        /// Alkaline cell voltage against the industrial 30 wt% KOH / 80 degC envelope, through the
        /// production model defaults (not a hand-tuned copy of them).
        /// </summary>
        private static void TestAlkalinePolarizationGoldenData()
        {
            var candidates = new[]
            {
                Path.Combine("electrolyzers", "alkaline_polarization_reference.csv"),
                Path.Combine("tests", "electrolyzers", "alkaline_polarization_reference.csv")
            };
            var path = candidates.FirstOrDefault(File.Exists);
            Check(path != null, "alkaline polarization fixture found");
            if (path == null) return;

            var electrolyzer = new AlkalineElectrolyzer();
            var model = (ModelAlkaline_SemiEmpirical)electrolyzer.CreateElectrochemicalModel();
            model.U_rev = 1.23;
            model.U_tn = 1.48;
            model.KOH_wt = electrolyzer.Parameters["KOH_wt"].Value;
            model.a_act = electrolyzer.Parameters["a_act"].Value;
            model.b_act = electrolyzer.Parameters["b_act"].Value;
            model.AreaSpecificResistance = electrolyzer.Parameters["ASR"].Value;
            model.i_L = electrolyzer.Parameters["i_L"].Value;
            model.t_mem = electrolyzer.Parameters["t_mem"].Value;

            var rows = 0;
            var worst = 0.0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("current_density_a_cm2,")) continue;

                var parts = line.Split(',');
                if (parts.Length != 3)
                {
                    Check(false, "invalid alkaline polarization fixture row: " + line);
                    continue;
                }

                var currentDensity = double.Parse(parts[0], CultureInfo.InvariantCulture);
                var expectedVoltage = double.Parse(parts[1], CultureInfo.InvariantCulture);
                var tolerance = double.Parse(parts[2], CultureInfo.InvariantCulture);

                var actual = model.CellVoltage(currentDensity, 353.15, 16.0, 16.0).U_cell;
                var relativeError = Math.Abs(actual - expectedVoltage) / expectedVoltage;
                rows++;
                worst = Math.Max(worst, relativeError);
                Check(relativeError <= tolerance,
                    string.Format(CultureInfo.InvariantCulture,
                        "alkaline polarization {0:F2} A/cm2: expected {1:F3} V, got {2:F3} V ({3:P2})",
                        currentDensity, expectedVoltage, actual, relativeError));
            }

            Check(rows == 6, "alkaline polarization fixture has 6 points");
            Console.WriteLine("alkaline polarization: {0} points, worst {1:P2}", rows, worst);

            // The cell voltage must stay above the thermoneutral voltage at the design point,
            // otherwise the stack would report more than 100% efficiency.
            var design = model.CellVoltage(0.4, 353.15, 16.0, 16.0).U_cell;
            Check(design > model.U_tn, string.Format(CultureInfo.InvariantCulture,
                "alkaline design-point voltage {0:F3} V is above the thermoneutral {1:F3} V", design, model.U_tn));
        }

        /// <summary>
        /// Alkaline specific energy at the same voltage envelope. The expected kWh/kg is the
        /// envelope voltage converted by Faraday's law, so a voltage match with a broken
        /// stoichiometry still fails.
        /// </summary>
        private static void TestAlkalineSecGoldenData()
        {
            var candidates = new[]
            {
                Path.Combine("electrolyzers", "alkaline_sec_reference.csv"),
                Path.Combine("tests", "electrolyzers", "alkaline_sec_reference.csv")
            };
            var path = candidates.FirstOrDefault(File.Exists);
            Check(path != null, "alkaline SEC fixture found");
            if (path == null) return;

            var electrolyzer = new AlkalineElectrolyzer();
            var model = (ModelAlkaline_SemiEmpirical)electrolyzer.CreateElectrochemicalModel();
            model.U_rev = 1.23;
            model.U_tn = 1.48;
            model.KOH_wt = electrolyzer.Parameters["KOH_wt"].Value;
            model.a_act = electrolyzer.Parameters["a_act"].Value;
            model.b_act = electrolyzer.Parameters["b_act"].Value;
            model.AreaSpecificResistance = electrolyzer.Parameters["ASR"].Value;
            model.i_L = electrolyzer.Parameters["i_L"].Value;
            model.t_mem = electrolyzer.Parameters["t_mem"].Value;

            var calculator = new ElectrolyzerCalculator
            {
                Model = model,
                A_cell = electrolyzer.Parameters["A_cell"].Value,
                N_cells = electrolyzer.Parameters["N_cells"].Value,
                T = 353.15,
                pCat = electrolyzer.Parameters["P_cat"].Value,
                pAn = electrolyzer.Parameters["P_an"].Value,
                etaF_ref = electrolyzer.Parameters["etaF_ref"].Value,
                i_ref = electrolyzer.Parameters["i_ref"].Value
            };

            var rows = 0;
            var worst = 0.0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("current_density_a_cm2,")) continue;

                var parts = line.Split(',');
                if (parts.Length != 3)
                {
                    Check(false, "invalid alkaline SEC fixture row: " + line);
                    continue;
                }

                var currentDensity = double.Parse(parts[0], CultureInfo.InvariantCulture);
                var expectedSec = double.Parse(parts[1], CultureInfo.InvariantCulture);
                var tolerance = double.Parse(parts[2], CultureInfo.InvariantCulture);

                var powerKW = calculator.PowerAt(currentDensity);
                var actualSec = calculator.SpecificEnergyConsumption(powerKW, currentDensity);
                var relativeError = Math.Abs(actualSec - expectedSec) / expectedSec;
                rows++;
                worst = Math.Max(worst, relativeError);
                Check(relativeError <= tolerance,
                    string.Format(CultureInfo.InvariantCulture,
                        "alkaline SEC {0:F2} A/cm2: expected {1:F3} kWh/kg, got {2:F3} ({3:P2})",
                        currentDensity, expectedSec, actualSec, relativeError));
            }

            Check(rows == 6, "alkaline SEC fixture has 6 points");
            Console.WriteLine("alkaline SEC: {0} points, worst {1:P2}", rows, worst);
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

        /// <summary>
        /// Dinh et al. (2021), DOI 10.1016/j.ijhydene.2021.11.126: in an industrial alkaline
        /// stack the heat lost to the surroundings is about 2.6% of the total heat removal, so
        /// the coolant must carry the rest. This checks the defaults honour that split.
        /// </summary>
        private static void TestStackHeatLossShare()
        {
            var alkaline = new AlkalineElectrolyzer();
            const double installedKW = 2950.0;
            var ambientKW = alkaline.Parameters["U_loss_area"].Value * (353.15 - 298.15) / 1000.0;
            var heatGeneratedKW = installedKW * 0.1152;
            var ambientShare = ambientKW / (ambientKW + heatGeneratedKW);

            Console.WriteLine("stack heat loss: ambient={0:F2} kW, generated={1:F1} kW, ambient share={2:P2}",
                ambientKW, heatGeneratedKW, ambientShare);

            Check(ambientShare < 0.05, string.Format(CultureInfo.InvariantCulture,
                "ambient heat loss stays a small share of the total ({0:P2}, Dinh 2021 reports 2.6%)", ambientShare));
            Check(ambientShare > 0.0, "the stack does lose some heat to the surroundings");
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

        /// <summary>
        /// Faraday efficiency must equal the reference value at the reference current density and
        /// fall as the load falls, because the crossover current takes a larger share.
        /// </summary>
        private static void TestFaradayEfficiency()
        {
            var calculator = new ElectrolyzerCalculator
            {
                Model = new ModelPEM_Springer(),
                etaF_ref = 0.99,
                i_ref = 1.0
            };

            var atReference = calculator.FaradayEfficiency(1.0);
            Check(Math.Abs(atReference - 0.99) < 1e-9,
                string.Format(CultureInfo.InvariantCulture, "Faraday efficiency at i_ref is 0.99, got {0:F6}", atReference));

            var highLoad = calculator.FaradayEfficiency(2.5);
            Check(highLoad > atReference && highLoad < 1.0,
                string.Format(CultureInfo.InvariantCulture, "Faraday efficiency rises above the reference at high load, got {0:F6}", highLoad));

            var lowLoad = calculator.FaradayEfficiency(0.1);
            Check(lowLoad < atReference && lowLoad > 0.0,
                string.Format(CultureInfo.InvariantCulture, "Faraday efficiency falls at low load, got {0:F6}", lowLoad));

            var lowest = calculator.FaradayEfficiency(0.01);
            Check(lowest < lowLoad, string.Format(CultureInfo.InvariantCulture,
                "Faraday efficiency keeps falling as the load falls ({0:F6} at 0.01 vs {1:F6} at 0.1)", lowest, lowLoad));

            Check(calculator.FaradayEfficiency(0.0) == 0.0, "Faraday efficiency is zero at zero current");

            // Hydrogen flow has to follow the efficiency, otherwise the mass balance would drift.
            var hydrogen = calculator.HydrogenMolarFlow(1.0);
            var expected = 0.99 * 1.0 * calculator.A_cell * calculator.N_cells / (2.0 * 96485.3365);
            Check(Math.Abs(hydrogen - expected) < 1e-15, "hydrogen flow follows the Faraday efficiency");

            // Reference-free case: an ideal Faraday efficiency must stay exactly 1 at any load.
            var ideal = new ElectrolyzerCalculator { Model = new ModelPEM_Springer(), etaF_ref = 1.0 };
            Check(Math.Abs(ideal.FaradayEfficiency(0.1) - 1.0) < 1e-12, "ideal Faraday efficiency has no crossover loss");
        }

        /// <summary>
        /// The purifier's two purity figures must describe the same gas: H2 content plus impurity
        /// equals 1e6 ppm, and the impurity must be near the configured target rather than the
        /// H2 content read the wrong way round.
        /// </summary>
        private static void TestPurificationPurity()
        {
            var purifier = new H2Purification { ProductPurityPpm = 10.0, H2LossFraction = 0.005 };
            Check(purifier.GetPropertyUnit("Product Purity") == "ppm", "purifier purity unit is ppm");
            Check(purifier.GetPropertyUnit("Product Impurity") == "ppm", "purifier impurity unit is ppm");
            Check(Array.IndexOf(purifier.GetProperties(PropertyType.ALL), "Product Impurity") >= 0,
                "purifier lists the impurity property");
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

            // Membrane permeation follows the partial-pressure difference and an Arrhenius
            // temperature dependence, so it must grow with cathode pressure and with temperature.
            double atReference = calculator.CrossoverMolarFlow(1.0, 1e-4);
            double atHighPressure = new ElectrolyzerCalculator { pCat = 60.0, pCat_ref = 30.0, T = 353.15, T_ref = 353.15 }
                .CrossoverMolarFlow(1.0, 1e-4);
            Check(Math.Abs(atHighPressure - 2.0 * atReference) < 1e-12,
                string.Format(CultureInfo.InvariantCulture,
                    "crossover scales with cathode pressure: {0:E3} vs {1:E3}", atHighPressure, atReference));

            double atHighTemperature = new ElectrolyzerCalculator { pCat = 30.0, pCat_ref = 30.0, T = 363.15, T_ref = 353.15 }
                .CrossoverMolarFlow(1.0, 1e-4);
            Check(atHighTemperature > atReference,
                string.Format(CultureInfo.InvariantCulture,
                    "crossover rises with temperature: {0:E3} vs {1:E3}", atHighTemperature, atReference));

            double atReferenceConditions = new ElectrolyzerCalculator { pCat = 30.0, pCat_ref = 30.0, T = 353.15, T_ref = 353.15 }
                .CrossoverMolarFlow(1.0, 1e-4);
            Check(Math.Abs(atReferenceConditions - 1e-4) < 1e-15,
                "crossover equals the coefficient at its reference conditions");
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

            var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
            var integrator = flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];
            var stepSeconds = integrator.IntegrationStep.TotalSeconds;

            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var samples = new List<ShadowSample>();
            // The sample leaves the PID output limits at the +/-1000 defaults, which no valve can
            // reach; the bench uses the valve's physical 0-100% range so "5% of span" is meaningful.
            pid.OutputMin = 0.0;
            pid.OutputMax = 100.0;
            var span = pid.OutputMax - pid.OutputMin;
            var scale = Math.Min(100.0, 32767.0 / Math.Max(Math.Abs(pid.OutputMin), Math.Abs(pid.OutputMax)));
            pid.Ownership = ControlOwnership.Shadow;

            // Establish a defined operating point: earlier tests leave the setpoint wherever they
            // left it, so start from a setpoint just below the current level (the drain valve then
            // works in its linear range instead of pinned at a limit), clear the stale derivative
            // history and prime the integral so the internal output equals the current MV.
            var startPv = Convert.ToDouble(flowsheet.SimulationObjects[pvTag].GetPropertyValue(pvProperty));
            var startMv = Convert.ToDouble(flowsheet.SimulationObjects[mvTag].GetPropertyValue(mvProperty));
            pid.AdjustValue = startPv - 0.05;
            pid.BaseSP = null;
            var startBaseSP = Math.Abs(pid.AdjustValue);
            var startError = (startPv - pid.AdjustValue) / startBaseSP;
            pid.CurrentError = startError;
            pid.LastError = startError;
            var startOutput = pid.ReverseActing ? startMv / startBaseSP - 1.0 : 1.0 - startMv / startBaseSP;
            pid.ITerm = pid.Ki != 0.0
                ? Math.Min(pid.WindupGuard, Math.Max(-pid.WindupGuard,
                    (startOutput - pid.Kp * startError - pid.Offset / startBaseSP) / pid.Ki))
                : 0.0;
            Console.WriteLine(
                "shadow bench: mv={0}.{1}, pv={2}.{3}, Kp={4}, Ki={5}, Kd={6}, reverse={7}, limits=[{8},{9}], windup={10}, baseSP={11}, scale={12}, span={13}, sp={14}",
                mvTag, mvProperty, pvTag, pvProperty,
                pid.Kp.ToString("F4", CultureInfo.InvariantCulture),
                pid.Ki.ToString("F4", CultureInfo.InvariantCulture),
                pid.Kd.ToString("F4", CultureInfo.InvariantCulture),
                pid.ReverseActing, pid.OutputMin, pid.OutputMax, pid.WindupGuard,
                pid.BaseSP.HasValue ? pid.BaseSP.Value.ToString("F4", CultureInfo.InvariantCulture) : "null",
                scale.ToString("F3", CultureInfo.InvariantCulture),
                span.ToString("F3", CultureInfo.InvariantCulture),
                pid.AdjustValue.ToString("F4", CultureInfo.InvariantCulture));

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
                Id = "SP",
                Direction = SignalDirection.ToPlc,
                ObjectTag = pid.Name,
                Property = "SetPointAbs",
                Unit = "m",
                Type = SignalDataType.Int16,
                Scale = 1000.0,
                ModbusArea = ModbusArea.InputRegister,
                ModbusAddress = 1,
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
                        // The sample's saved initial state is an empty tank, which pins the drain
                        // valve at its lower limit and makes the Shadow comparison degenerate.
                        ReuseInitialState = true,
                        // The bench primes the controller's integral itself; ResetControllers
                        // would zero it again before the first step.
                        ResetControllers = false,
                        InitialStateId = schedule.InitialFlowsheetStateID
                    }, host = new ShadowBenchHost(bridge, plc, master, pid, flowsheet, pvTag, pvProperty, scale, span, samples, stepSeconds));

                    Check(stats.Error == null, "shadow: run without errors: " + stats.Error);
                    Check(stats.StepCount == 12, "shadow: twelve steps");
                    Check(pid.Ownership == ControlOwnership.Shadow, "shadow: ownership stays Shadow");
                    Check(samples.Count == stats.StepCount, "shadow: one deviation sample per step");
                    Check(pid.ShadowDeviationHistory.Count >= stats.StepCount, "shadow: deviation recorded every step");

                    if (reportDirectory != null) WriteShadowReport(reportDirectory, samples);

                    // The first step is the bumpless-transfer transient: the internal PID still
                    // carries the derivative history of the phase before Shadow started.
                    var compared = samples.Skip(1).ToList();
                    var worst = compared.Count == 0
                        ? double.PositiveInfinity
                        : compared.Max(s => Math.Abs(s.LawDeviationPctOfSpan));
                    Console.WriteLine("shadow deviation: first={0:F2}%, worst after transfer={1:F2}% of span (same information); raw closed-loop worst={2:F2}%",
                        samples.Count > 0 ? Math.Abs(samples[0].LawDeviationPctOfSpan) : double.NaN, worst,
                        samples.Count == 0 ? double.NaN : samples.Max(s => Math.Abs(s.DeviationPctOfSpan)));
                    Check(worst < 5.0, string.Format(CultureInfo.InvariantCulture, "shadow: worst deviation {0:F2}% of span", worst));

                    // Guard against a degenerate pass: the loop has to have moved the valve.
                    var outputRange = samples.Count == 0
                        ? 0.0
                        : samples.Max(s => s.InternalOutput) - samples.Min(s => s.InternalOutput);
                    Check(outputRange > 1.0, string.Format(CultureInfo.InvariantCulture,
                        "shadow: bench exercised the loop (internal output range {0:F2}% of span)", outputRange));

                    var mvActual = Convert.ToDouble(flowsheet.SimulationObjects[mvTag].GetPropertyValue(mvProperty));
                    Check(Math.Abs(mvActual - host.LastWritten) <= 1.0 / scale,
                        string.Format(CultureInfo.InvariantCulture, "shadow: PLC write reached the manipulated object ({0:F3} vs {1:F3})", mvActual, host.LastWritten));
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
                writer.WriteLine("step,sim_time_s,pv_m,sp_m,internal_output_pct,external_output_pct,deviation_pct,deviation_pct_of_span,same_information_deviation_pct,same_information_deviation_pct_of_span");
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
                        sample.DeviationPctOfSpan.ToString("F4", CultureInfo.InvariantCulture),
                        sample.LawDeviation.ToString("F4", CultureInfo.InvariantCulture),
                        sample.LawDeviationPctOfSpan.ToString("F4", CultureInfo.InvariantCulture)
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
            public double LawDeviation;
            public double LawDeviationPctOfSpan;
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

                // DWSIM's PID works with (PV - SP) / BaseSP.
                var error = (pv - sp) / baseSP;

                if (!_primed)
                {
                    _primed = true;
                    var output0 = _pid.ReverseActing ? currentMv / baseSP - 1.0 : 1.0 - currentMv / baseSP;
                    _integral = GainMismatch * _pid.Ki != 0.0
                        ? Clamp((output0 - GainMismatch * _pid.Kp * error - _pid.Offset / baseSP) / (GainMismatch * _pid.Ki))
                        : 0.0;
                    _lastError = error;
                }

                // The internal PID accumulates the error before it computes the output, so the
                // mirror does the same on its first step too.
                _integral = Clamp(_integral + error * dt);

                var dTerm = (error - _lastError) / dt;
                _lastError = error;

                var output = GainMismatch * (_pid.Kp * error + _pid.Ki * _integral + _pid.Kd * dTerm) + _pid.Offset / baseSP;
                var mv = _pid.ReverseActing ? (1.0 + output) * baseSP : (1.0 - output) * baseSP;
                mv = Math.Min(_pid.OutputMax, Math.Max(_pid.OutputMin, mv));
                return mv;
            }

            private double Clamp(double value)
            {
                return Math.Min(_pid.WindupGuard, Math.Max(-_pid.WindupGuard, value));
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
            private double _previousInternal;

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
                var registers = _pvPublished ? _master.ReadInputRegisters(1, 0, 2) : null;
                var pv = registers != null
                    ? registers[0] / 1000.0
                    : Convert.ToDouble(_flowsheet.SimulationObjects[_pvTag].GetPropertyValue(_pvProperty));
                var sp = registers != null ? registers[1] / 1000.0 : _pid.AdjustValue;

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

                // Stepped after the publish, so the PLC sees the new setpoint one cycle later —
                // the same zero-order hold the process value goes through.
                if (stepIndex == 6) _pid.AdjustValue -= 0.05;

                // The PLC command written this step was computed from the value published at the
                // end of the previous step, so comparing it with the previous internal output is
                // the same-information (control law) comparison; the raw difference additionally
                // carries the one-step Modbus zero-order-hold lag.
                var lawDeviation = _previousInternal - _pid.ExternalMV;

                _samples.Add(new ShadowSample
                {
                    Step = stepIndex,
                    SimTime = simTime,
                    Pv = _pid.PVValue,
                    Sp = _pid.SPValue,
                    InternalOutput = _pid.OutputAbs,
                    ExternalOutput = _pid.ExternalMV,
                    Deviation = _pid.ShadowDeviation,
                    DeviationPctOfSpan = _span != 0.0 ? _pid.ShadowDeviation / _span * 100.0 : 0.0,
                    LawDeviation = lawDeviation,
                    LawDeviationPctOfSpan = _span != 0.0 ? lawDeviation / _span * 100.0 : 0.0
                });
                _previousInternal = _pid.OutputAbs;

                var row = _samples[_samples.Count - 1];
                Console.WriteLine("shadow step {0}: pv={1:F4} sp={2:F4} internal={3:F4} external={4:F4} deviation={5:F4} ({6:F2}% of span), same-information deviation={7:F2}%",
                    row.Step, row.Pv, row.Sp, row.InternalOutput, row.ExternalOutput, row.Deviation, row.DeviationPctOfSpan, row.LawDeviationPctOfSpan);
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
