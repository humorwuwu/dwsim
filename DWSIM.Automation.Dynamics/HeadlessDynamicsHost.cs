using DWSIM.Interfaces;

namespace DWSIM.Automation.Dynamics
{
    /// <summary>Host implementation that ignores every callback.</summary>
    public sealed class HeadlessDynamicsHost : IDynamicsHost
    {
        /// <inheritdoc />
        public bool ShouldAbort => false;

        /// <inheritdoc />
        public bool IsPaused => false;

        /// <inheritdoc />
        public void OnRunStarting(IFlowsheet flowsheet, RunOptions options) { }

        /// <inheritdoc />
        public void OnPreStep(int stepIndex, double simTime) { }

        /// <inheritdoc />
        public void OnPostStep(int stepIndex, double simTime, RunStats stats) { }

        /// <inheritdoc />
        public void OnControllerIteration(ISimulationObject controller) { }

        /// <inheritdoc />
        public void OnError(System.Exception exception, int stepIndex) { }

        /// <inheritdoc />
        public void OnRunFinished(RunStats stats) { }
    }
}