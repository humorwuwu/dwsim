using DWSIM.Interfaces;

namespace DWSIM.Automation.Dynamics
{
    /// <summary>UI hooks for a dynamics run. Implementations may be called from the runner's thread.</summary>
    public interface IDynamicsHost
    {
        /// <summary>Called once before the first step.</summary>
        void OnRunStarting(IFlowsheet flowsheet, RunOptions options);

        /// <summary>Called before each step is solved.</summary>
        void OnPreStep(int stepIndex, double simTime);

        /// <summary>Called after each step is solved.</summary>
        void OnPostStep(int stepIndex, double simTime, RunStats stats);

        /// <summary>Called after a controller is solved.</summary>
        void OnControllerIteration(ISimulationObject controller);

        /// <summary>Called when the run throws.</summary>
        void OnError(System.Exception exception, int stepIndex);

        /// <summary>Called once when the run ends, whether successful or not.</summary>
        void OnRunFinished(RunStats stats);

        /// <summary>When true, the runner stops after the current step.</summary>
        bool ShouldAbort { get; }

        /// <summary>When true, the runner stops after the current step.</summary>
        bool IsPaused { get; }
    }
}