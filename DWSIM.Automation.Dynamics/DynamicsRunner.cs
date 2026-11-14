using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DWSIM.DynamicsManager;
using DWSIM.ExtensionMethods;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.SharedClasses.SystemsOfUnits;
using DWSIM.SharedClasses.UnitOperations;
using DWSIM.UnitOperations.SpecialOps;

namespace DWSIM.Automation.Dynamics
{
    /// <summary>Runs a DWSIM flowsheet's dynamic schedule without a UI dependency.</summary>
    public static class DynamicsRunner
    {
        /// <summary>
        /// Runs until the requested simulated duration ends, the host aborts/pauses, or an error occurs.
        /// </summary>
        /// <param name="flowsheet">Flowsheet with a valid current schedule and integrator.</param>
        /// <param name="options">Run options; null uses defaults.</param>
        /// <param name="host">UI callbacks; null uses <see cref="HeadlessDynamicsHost"/>.</param>
        public static RunStats Run(IFlowsheet flowsheet, RunOptions options = null, IDynamicsHost host = null)
        {
            if (flowsheet == null) throw new ArgumentNullException(nameof(flowsheet));

            options = options ?? new RunOptions();
            host = host ?? new HeadlessDynamicsHost();

            var manager = flowsheet.DynamicsManager;
            if (manager == null) throw new InvalidOperationException("The flowsheet has no DynamicsManager.");

            if (!manager.ScheduleList.ContainsKey(manager.CurrentSchedule))
                throw new InvalidOperationException("Please select a valid schedule.");

            var schedule = manager.ScheduleList[manager.CurrentSchedule];

            if (!manager.IntegratorList.ContainsKey(schedule.CurrentIntegrator))
                throw new InvalidOperationException("Please select a valid integrator for the selected schedule.");

            var integrator = manager.IntegratorList[schedule.CurrentIntegrator] as Integrator;
            if (integrator == null) throw new InvalidOperationException("Unsupported dynamics integrator implementation.");

            var stepSeconds = options.RealTime ? integrator.RealTimeStepMs / 1000.0 : options.StepSeconds;
            if (stepSeconds <= 0) throw new InvalidOperationException("The integration step must be positive.");

            var finalSeconds = options.RealTime
                ? double.MaxValue
                : options.DurationSeconds > 0 ? options.DurationSeconds : integrator.Duration.TotalSeconds;

            if (finalSeconds <= 0 && !options.RealTime)
                throw new InvalidOperationException("The integrator duration is zero; set RunOptions.DurationSeconds.");

            var pidControllers = flowsheet.SimulationObjects.Values.OfType<PIDController>().ToList();
            var pythonControllers = flowsheet.SimulationObjects.Values.OfType<PythonController>().ToList();

            WireControllerObjects(flowsheet, pidControllers, pythonControllers);

            integrator.RealTime = options.RealTime;

            var stats = new RunStats();
            var historian = new Dictionary<DateTime, string>();
            IFlowsheet clone = null;
            EventProcessor eventProcessor = null;
            var started = false;
            var previousDynamicMode = flowsheet.DynamicMode;

            try
            {
                host.OnRunStarting(flowsheet, options);

                if (!options.ReuseInitialState && !options.RealTime)
                {
                    if (string.IsNullOrEmpty(options.InitialStateId))
                        throw new InvalidOperationException("InitialStateId is required when ReuseInitialState is false.");
                    RestoreState(flowsheet, options.InitialStateId);
                }

                integrator.MonitoredVariableValues.Clear();

                foreach (var controller in pidControllers) controller.Reset();
                foreach (var controller in pythonControllers) controller.ResetRequested = true;

                if (schedule.ResetContentsOfAllObjects) ResetDynamicContents(flowsheet);

                integrator.CurrentTime = DateTime.Now;
                integrator.MonitoredVariableValues.Clear();

                flowsheet.SupressMessages = true;
                flowsheet.DynamicMode = true;
                flowsheet.ClearLog();

                clone = flowsheet.Clone();
                if (clone == null) throw new InvalidOperationException("Could not clone the flowsheet for event interpolation.");
                eventProcessor = new EventProcessor(flowsheet, clone, historian);

                flowsheet.ProcessScripts(Scripts.EventType.IntegratorStarted, Scripts.ObjectType.Integrator, "");
                started = true;

                double controllersCheck = 100000;
                double streamsCheck = 100000;
                double pressureFlowCheck = 100000;
                double simTime = 0;
                double currentStep = stepSeconds;
                int step = 0;
                var abortRun = false;

                while (simTime < finalSeconds)
                {
                    if (host.ShouldAbort || host.IsPaused) break;

                    var stopwatch = Stopwatch.StartNew();

                    host.OnPreStep(step, simTime);
                    flowsheet.ProcessScripts(Scripts.EventType.IntegratorPreStep, Scripts.ObjectType.Integrator, "");

                    controllersCheck += currentStep;
                    streamsCheck += currentStep;
                    pressureFlowCheck += currentStep;

                    integrator.ShouldCalculateControl = controllersCheck >= options.ControlDecimation * currentStep;
                    if (integrator.ShouldCalculateControl) controllersCheck = 0.0;

                    integrator.ShouldCalculateEquilibrium = streamsCheck >= options.EquilibriumDecimation * currentStep;
                    if (integrator.ShouldCalculateEquilibrium) streamsCheck = 0.0;

                    integrator.ShouldCalculatePressureFlow = pressureFlowCheck >= options.PressureFlowDecimation * currentStep;
                    if (integrator.ShouldCalculatePressureFlow) pressureFlowCheck = 0.0;

                    var exceptions = DWSIM.FlowsheetSolver.FlowsheetSolver.SolveFlowsheet(flowsheet, DWSIM.GlobalSettings.Settings.SolverMode);
                    while (DWSIM.GlobalSettings.Settings.CalculatorBusy) Task.Delay(200).Wait();

                    // shortcut: the solver is synchronous, so the timeout is checked after the solve
                    if (step == 0 && stopwatch.Elapsed > options.StartupTimeout)
                        throw new TimeoutException(string.Format(CultureInfo.InvariantCulture,
                            "Initial solve exceeded the startup timeout of {0}.", options.StartupTimeout));

                    if (exceptions != null && exceptions.Count > 0) throw exceptions[0];

                    if (manager.EnableHistorian && !historian.ContainsKey(integrator.CurrentTime))
                    {
                        var snapshot = flowsheet.GetSnapshot(SnapshotType.ObjectData);
                        historian.Add(integrator.CurrentTime, snapshot.ToString().Compress());
                        if (historian.Count > manager.MaxHistorianItems) historian.Remove(historian.Keys.First());
                    }

                    StoreVariableValues(flowsheet, integrator, step, integrator.CurrentTime);

                    flowsheet.ProcessScripts(Scripts.EventType.IntegratorStep, Scripts.ObjectType.Integrator, "");
                    host.OnPostStep(step, simTime, stats);

                    integrator.CurrentTime = integrator.CurrentTime.AddSeconds(currentStep);

                    if (integrator.ShouldCalculateControl)
                        ControllerExecutor.Execute(flowsheet, pidControllers, pythonControllers, host);

                    var wait = integrator.RealTimeStepMs - stopwatch.ElapsedMilliseconds;
                    if (options.RealTime && wait > 0) Task.Delay((int)wait).Wait();

                    stopwatch.Stop();

                    stats.StepDurations.Add(stopwatch.Elapsed.TotalSeconds);
                    stats.WallClockLag.Add(stopwatch.Elapsed.TotalSeconds - currentStep);

                    if (stopwatch.Elapsed.TotalSeconds > currentStep * options.OverrunFactor)
                    {
                        stats.OverrunCount++;
                        if (options.Overrun == OverrunPolicy.Abort)
                        {
                            abortRun = true;
                        }
                        else if (options.Overrun == OverrunPolicy.StretchGrid)
                        {
                            currentStep = Math.Max(currentStep, stopwatch.Elapsed.TotalSeconds);
                        }
                    }

                    if (abortRun || host.ShouldAbort || host.IsPaused) break;

                    if (!options.RealTime && options.SubscribeToSchedule)
                    {
                        if (schedule.UsesEventList)
                            eventProcessor.ProcessEvents(schedule.CurrentEventList, integrator.CurrentTime, TimeSpan.FromSeconds(currentStep));

                        if (schedule.UsesCauseAndEffectMatrix)
                            eventProcessor.ProcessCEMatrix(schedule.CurrentCauseAndEffectMatrix);
                    }

                    step++;
                    simTime += currentStep;
                    stats.StepCount = step;
                    stats.SimTime = simTime;

                    options.OnStep?.Invoke(step, simTime);
                }

                if (stats.OverrunCount > 0) stats.Quality = RunQuality.Degraded;
            }
            catch (Exception ex)
            {
                stats.Error = ex;
                stats.Quality = RunQuality.Failed;
                host.OnError(ex, stats.StepCount);
                throw;
            }
            finally
            {
                flowsheet.SupressMessages = false;
                flowsheet.DynamicMode = previousDynamicMode;

                if (started)
                {
                    var eventType = stats.Error == null
                        ? Scripts.EventType.IntegratorFinished
                        : Scripts.EventType.IntegratorError;
                    try
                    {
                        flowsheet.ProcessScripts(eventType, Scripts.ObjectType.Integrator, "");
                    }
                    catch
                    {
                        // script failures must not hide the original error
                    }
                }

                if (clone is IDisposable disposable) disposable.Dispose();
                host.OnRunFinished(stats);
            }

            return stats;
        }

        /// <summary>Runs <see cref="Run"/> on a worker thread.</summary>
        public static Task<RunStats> RunAsync(IFlowsheet flowsheet, RunOptions options = null, IDynamicsHost host = null)
        {
            return Task.Run(() => Run(flowsheet, options, host));
        }

        // the UI binds controllers when a flowsheet is opened; headless runs need the same wiring
        private static void WireControllerObjects(IFlowsheet flowsheet, IEnumerable<PIDController> pidControllers, IEnumerable<PythonController> pythonControllers)
        {
            foreach (var controller in pidControllers)
            {
                if (controller.ManipulatedObject == null)
                    controller.ManipulatedObject = FindObject(flowsheet, controller.ManipulatedObjectData.ID);
                if (controller.ControlledObject == null)
                    controller.ControlledObject = FindObject(flowsheet, controller.ControlledObjectData.ID);
            }

            foreach (var controller in pythonControllers)
            {
                if (controller.ManipulatedObject == null)
                    controller.ManipulatedObject = FindObject(flowsheet, controller.ManipulatedObjectData.ID);
                if (controller.ControlledObject == null)
                    controller.ControlledObject = FindObject(flowsheet, controller.ControlledObjectData.ID);
            }
        }

        private static BaseClass FindObject(IFlowsheet flowsheet, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return flowsheet.SimulationObjects.Values.OfType<BaseClass>().FirstOrDefault(x => x.Name == id);
        }

        private static void RestoreState(IFlowsheet flowsheet, string stateId)
        {
            if (!flowsheet.StoredSolutions.ContainsKey(stateId))
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Stored state '{0}' was not found.", stateId));

            flowsheet.LoadProcessData(flowsheet.StoredSolutions[stateId]);
            flowsheet.UpdateInterface();
        }

        private static void ResetDynamicContents(IFlowsheet flowsheet)
        {
            foreach (var obj in flowsheet.SimulationObjects.Values)
            {
                if (!obj.HasPropertiesForDynamicMode || !(obj is BaseClass bobj)) continue;

                if (bobj.GetDynamicProperty("Reset Content") != null) bobj.SetDynamicProperty("Reset Content", 1);
                if (bobj.GetDynamicProperty("Reset Contents") != null) bobj.SetDynamicProperty("Reset Contents", 1);
                if (bobj.GetDynamicProperty("Initialize using Inlet Stream") != null) bobj.SetDynamicProperty("Initialize using Inlet Stream", 1);
                if (bobj.GetDynamicProperty("Initialize using Inlet Streams") != null) bobj.SetDynamicProperty("Initialize using Inlet Streams", 1);
            }
        }

        private static void StoreVariableValues(IFlowsheet flowsheet, Integrator integrator, int step, DateTime timestamp)
        {
            if (integrator.MonitoredVariableValues.ContainsKey(timestamp.Ticks)) return;

            var values = new List<IDynamicsMonitoredVariable>();

            foreach (var monitored in integrator.MonitoredVariables)
            {
                var copy = (MonitoredVariable)((MonitoredVariable)monitored).Clone();

                if (!flowsheet.SimulationObjects.ContainsKey(copy.ObjectID))
                    throw new InvalidOperationException("At least one of the monitored variables is not configured correctly, please check.");

                var obj = flowsheet.SimulationObjects[copy.ObjectID];
                copy.PropertyValue = Converter.ConvertFromSI(copy.PropertyUnits, Convert.ToDouble(obj.GetPropertyValue(copy.PropertyID)))
                    .ToString(CultureInfo.InvariantCulture);
                copy.TimeStamp = timestamp;
                values.Add(copy);
            }

            integrator.MonitoredVariableValues.Add(timestamp.Ticks, values);
        }
    }
}