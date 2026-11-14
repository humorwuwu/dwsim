using System;

namespace DWSIM.Automation.Dynamics
{
    /// <summary>Policy applied when a wall-clock step overruns the simulation step.</summary>
    public enum OverrunPolicy
    {
        /// <summary>Keep the model time grid and count the overrun.</summary>
        Skip,
        /// <summary>Advance model time by the actual elapsed time for the overrunning step.</summary>
        StretchGrid,
        /// <summary>Stop the run on the first overrun.</summary>
        Abort
    }

    /// <summary>Overall quality of a dynamics run.</summary>
    public enum RunQuality
    {
        /// <summary>All steps completed without overruns or errors.</summary>
        Good,
        /// <summary>At least one step overran its budget.</summary>
        Degraded,
        /// <summary>The run failed.</summary>
        Failed
    }

    /// <summary>Options for <see cref="DynamicsRunner"/>.</summary>
    public class RunOptions
    {
        /// <summary>Use wall-clock pacing instead of running as fast as possible.</summary>
        public bool RealTime { get; set; } = false;

        /// <summary>Simulation step in seconds for non-realtime runs.</summary>
        public double StepSeconds { get; set; } = 5.0;

        /// <summary>Run control calculations every N steps.</summary>
        public int ControlDecimation { get; set; } = 1;

        /// <summary>Run equilibrium calculations every N steps.</summary>
        public int EquilibriumDecimation { get; set; } = 1;

        /// <summary>Run pressure/flow calculations every N steps.</summary>
        public int PressureFlowDecimation { get; set; } = 1;

        /// <summary>Simulated seconds to run; 0 uses the integrator's duration.</summary>
        public double DurationSeconds { get; set; } = 0;

        /// <summary>Maximum wall-clock time allowed for the first solve.</summary>
        public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>What to do when a step overruns <see cref="OverrunFactor"/> times its budget.</summary>
        public OverrunPolicy Overrun { get; set; } = OverrunPolicy.Skip;

        /// <summary>Step budget multiplier that counts as an overrun.</summary>
        public double OverrunFactor { get; set; } = 1.5;

        /// <summary>Called after each completed step with (stepIndex, simTimeSeconds).</summary>
        public Action<int, double> OnStep { get; set; } = null;

        /// <summary>Start from the flowsheet's current state instead of <see cref="InitialStateId"/>.</summary>
        public bool ReuseInitialState { get; set; } = true;

        /// <summary>Stored solution ID used when <see cref="ReuseInitialState"/> is false.</summary>
        public string InitialStateId { get; set; } = "";

        /// <summary>Process the schedule's event list and cause-and-effect matrix.</summary>
        public bool SubscribeToSchedule { get; set; } = true;
    }
}