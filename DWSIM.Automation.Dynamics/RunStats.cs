using System;
using System.Collections.Generic;

namespace DWSIM.Automation.Dynamics
{
    /// <summary>Statistics collected during a dynamics run.</summary>
    public class RunStats
    {
        /// <summary>Number of completed simulation steps.</summary>
        public int StepCount { get; internal set; }

        /// <summary>Simulated seconds completed.</summary>
        public double SimTime { get; internal set; }

        /// <summary>Wall-clock time minus step budget for each step, in seconds.</summary>
        public List<double> WallClockLag { get; } = new List<double>();

        /// <summary>Wall-clock duration of each step, in seconds.</summary>
        public List<double> StepDurations { get; } = new List<double>();

        /// <summary>Number of steps that exceeded the overrun factor.</summary>
        public int OverrunCount { get; internal set; }

        /// <summary>Overall run quality.</summary>
        public RunQuality Quality { get; internal set; } = RunQuality.Good;

        /// <summary>The exception that ended the run, if any.</summary>
        public Exception Error { get; internal set; }
    }
}