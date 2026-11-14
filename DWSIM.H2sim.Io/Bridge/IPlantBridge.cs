using System;
using DWSIM.Interfaces;

namespace DWSIM.H2sim.Io
{
    /// <summary>A PLC-facing bridge. Implementations must not touch the model from communication threads.</summary>
    public interface IPlantBridge : IDisposable
    {
        /// <summary>Bridge name, e.g. the connection name.</summary>
        string Name { get; }

        /// <summary>Current connection state and counters.</summary>
        BridgeStatus Status { get; }

        /// <summary>Validates and starts the endpoint. Returns false on validation or connection failure.</summary>
        bool Connect(SignalMap map, IFlowsheet flowsheet);

        /// <summary>Stops the endpoint.</summary>
        void Disconnect();

        /// <summary>Copies the latest FromPlc values into the model. Called by the model thread.</summary>
        void ApplyInputs();

        /// <summary>Publishes the current ToPlc values. Called by the model thread.</summary>
        void PublishOutputs();

        /// <summary>Marks every published value with the given quality.</summary>
        void SetQuality(QualityCode quality, string reason);

        /// <summary>Raised on signal-level failures.</summary>
        event EventHandler<SignalErrorEventArgs> SignalError;
    }
}