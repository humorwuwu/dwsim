using System;
using DWSIM.Automation.Dynamics;
using DWSIM.Interfaces;

namespace DWSIM.H2sim.Io
{
    /// <summary>
    /// Drives an <see cref="IPlantBridge"/> from the dynamics loop: applies PLC inputs before
    /// each step and publishes outputs after it. Keeps the Dynamics assembly free of I/O types.
    /// </summary>
    public sealed class BridgeDynamicsHost : IDynamicsHost
    {
        private readonly IPlantBridge _bridge;

        /// <summary>Wraps a connected bridge.</summary>
        public BridgeDynamicsHost(IPlantBridge bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        /// <inheritdoc />
        public bool ShouldAbort => false;

        /// <inheritdoc />
        public bool IsPaused => false;

        /// <inheritdoc />
        public void OnRunStarting(IFlowsheet flowsheet, RunOptions options) { }

        /// <inheritdoc />
        public void OnPreStep(int stepIndex, double simTime)
        {
            _bridge.ApplyInputs();
        }

        /// <inheritdoc />
        public void OnPostStep(int stepIndex, double simTime, RunStats stats)
        {
            _bridge.PublishOutputs();
        }

        /// <inheritdoc />
        public void OnControllerIteration(ISimulationObject controller) { }

        /// <inheritdoc />
        public void OnError(Exception exception, int stepIndex)
        {
            _bridge.SetQuality(QualityCode.Bad, exception.Message);
        }

        /// <inheritdoc />
        public void OnRunFinished(RunStats stats) { }
    }
}