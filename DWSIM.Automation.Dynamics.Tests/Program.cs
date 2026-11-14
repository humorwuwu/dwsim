using System;
using System.IO;

namespace DWSIM.Automation.Dynamics.Tests
{
    internal static class Program
    {
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

            Console.WriteLine("PASS");
            return 0;
        }
    }
}