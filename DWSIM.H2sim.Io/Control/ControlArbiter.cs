using System;
using System.Collections.Generic;
using System.Linq;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.SharedClasses.SystemsOfUnits;
using DWSIM.UnitOperations.SpecialOps;

namespace DWSIM.H2sim.Io
{
    /// <summary>
    /// Decides which PID owns a manipulated variable and applies external writes.
    /// Call <see cref="BeginStep"/> once per model step before the bridge applies inputs.
    /// </summary>
    public sealed class ControlArbiter
    {
        private readonly IFlowsheet _flowsheet;
        private readonly SignalMap _map;
        private readonly Dictionary<string, string> _lastWriter = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Creates an arbiter for one flowsheet and its signal map.</summary>
        public ControlArbiter(IFlowsheet flowsheet, SignalMap map)
        {
            _flowsheet = flowsheet ?? throw new ArgumentNullException(nameof(flowsheet));
            _map = map ?? throw new ArgumentNullException(nameof(map));
        }

        /// <summary>Raised when an external write is rejected.</summary>
        public event EventHandler<SignalErrorEventArgs> SignalError;

        /// <summary>Marks the start of a model step; allows one writer per MV per step.</summary>
        public void BeginStep()
        {
            _lastWriter.Clear();
        }

        /// <summary>
        /// Sets every PID whose manipulated property is written by an enabled FromPlc signal to
        /// <see cref="ControlOwnership.External"/>, and releases it when the signal disappears.
        /// Manual and Shadow ownership is left untouched.
        /// </summary>
        public void ApplyOwnership()
        {
            var externalKeys = new HashSet<string>(
                _map.Signals
                    .Where(s => s.Enabled && s.Direction == SignalDirection.FromPlc)
                    .Select(s => Key(s.ObjectTag, s.Property)),
                StringComparer.Ordinal);

            foreach (var controller in _flowsheet.SimulationObjects.Values.OfType<PIDController>())
            {
                var key = Key(controller.ManipulatedObjectData.ID, controller.ManipulatedObjectData.PropertyName);
                var external = externalKeys.Contains(key);

                if (external && controller.Ownership == ControlOwnership.Local)
                    controller.Ownership = ControlOwnership.External;
                else if (!external && controller.Ownership == ControlOwnership.External)
                    controller.Ownership = ControlOwnership.Local;
            }
        }

        /// <summary>
        /// Applies one PLC value in engineering units. Rejects a second write to the same MV in
        /// the same step.
        /// </summary>
        public bool WriteExternal(Signal signal, double engineeringValue)
        {
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            if (!signal.Enabled || signal.Direction != SignalDirection.FromPlc) return false;

            var key = Key(signal.ObjectTag, signal.Property);

            if (_lastWriter.TryGetValue(key, out var writer) && writer != signal.Id)
            {
                Raise(signal, string.Format(
                    "conflict: '{0}.{1}' was already written by signal '{2}' in this step",
                    signal.ObjectTag, signal.Property, writer), null);
                return false;
            }

            _lastWriter[key] = signal.Id;

            if (!_flowsheet.SimulationObjects.ContainsKey(signal.ObjectTag))
            {
                Raise(signal, string.Format("object '{0}' was not found", signal.ObjectTag), null);
                return false;
            }

            var obj = _flowsheet.SimulationObjects[signal.ObjectTag];
            var siValue = Converter.ConvertToSI(signal.Unit, engineeringValue);

            var controller = FindController(signal.ObjectTag, signal.Property);
            if (controller != null)
            {
                controller.ExternalMV = engineeringValue;
                if (controller.Ownership != ControlOwnership.Manual && controller.Ownership != ControlOwnership.Shadow)
                    controller.Ownership = ControlOwnership.External;
            }

            try
            {
                obj.SetPropertyValue(signal.Property, siValue);
            }
            catch (Exception ex)
            {
                Raise(signal, ex.Message, ex);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Switches ownership of one MV. External to Local pre-loads the integral term and clears
        /// the error history so the output does not jump.
        /// </summary>
        public bool SwitchOwnership(string objectTag, string property, ControlOwnership ownership)
        {
            var controller = FindController(objectTag, property);
            if (controller == null) return false;

            var current = CurrentManipulatedValue(controller);

            if (ownership == ControlOwnership.Local)
            {
                var baseSP = controller.BaseSP.HasValue ? controller.BaseSP.Value : Math.Abs(controller.AdjustValue);
                if (baseSP == 0.0) baseSP = 1.0;

                var output = controller.ReverseActing
                    ? current / baseSP - 1.0
                    : 1.0 - current / baseSP;

                if (controller.Ki != 0.0)
                {
                    controller.ITerm = (output - controller.PTerm - controller.Kd * controller.DTerm - controller.Offset / baseSP) / controller.Ki;
                    if (controller.ITerm < -controller.WindupGuard) controller.ITerm = -controller.WindupGuard;
                    if (controller.ITerm > controller.WindupGuard) controller.ITerm = controller.WindupGuard;
                }

                controller.LastError = 0.0;
                controller.CurrentError = 0.0;
            }

            controller.ExternalMV = current;
            controller.Ownership = ownership;
            return true;
        }

        /// <summary>Returns one deviation history per PID controller in Shadow mode.</summary>
        public Dictionary<string, List<double>> CollectShadowDeviations()
        {
            var result = new Dictionary<string, List<double>>(StringComparer.Ordinal);

            foreach (var controller in _flowsheet.SimulationObjects.Values.OfType<PIDController>())
            {
                if (controller.Ownership != ControlOwnership.Shadow) continue;
                result[controller.Name] = new List<double>(controller.ShadowDeviationHistory);
            }

            return result;
        }

        private PIDController FindController(string objectTag, string property)
        {
            return _flowsheet.SimulationObjects.Values
                .OfType<PIDController>()
                .FirstOrDefault(c => c.ManipulatedObjectData.ID == objectTag && c.ManipulatedObjectData.PropertyName == property);
        }

        private double CurrentManipulatedValue(PIDController controller)
        {
            var id = controller.ManipulatedObjectData.ID;
            if (!_flowsheet.SimulationObjects.ContainsKey(id)) return controller.MVValue;

            var obj = _flowsheet.SimulationObjects[id];
            return Converter.ConvertFromSI(controller.ManipulatedObjectData.Units, Convert.ToDouble(obj.GetPropertyValue(controller.ManipulatedObjectData.PropertyName)));
        }

        private static string Key(string objectTag, string property)
        {
            return (objectTag ?? "") + "\u0000" + (property ?? "");
        }

        private void Raise(Signal signal, string message, Exception exception)
        {
            SignalError?.Invoke(this, new SignalErrorEventArgs
            {
                Signal = signal,
                Message = message,
                Exception = exception
            });
        }
    }
}