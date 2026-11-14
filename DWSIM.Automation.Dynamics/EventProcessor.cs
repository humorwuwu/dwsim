using System;
using System.Collections.Generic;
using System.Linq;
using DWSIM.ExtensionMethods;
using DWSIM.Interfaces;
using DWSIM.SharedClasses.SystemsOfUnits;

namespace DWSIM.Automation.Dynamics
{
    /// <summary>Applies a schedule's event list and cause-and-effect matrix to the model.</summary>
    internal sealed class EventProcessor
    {
        private readonly IFlowsheet _flowsheet;
        private readonly IFlowsheet _clone;
        private readonly Dictionary<DateTime, string> _historian;

        /// <summary>
        /// Creates a processor. <paramref name="clone"/> must be an independent flowsheet instance:
        /// GetPropertyValuesFromEvents restores snapshots on the instance it receives.
        /// </summary>
        public EventProcessor(IFlowsheet flowsheet, IFlowsheet clone, Dictionary<DateTime, string> historian)
        {
            _flowsheet = flowsheet;
            _clone = clone;
            _historian = historian;
        }

        /// <summary>Applies property changes whose timestamp falls in (currentPosition - interval, currentPosition].</summary>
        public void ProcessEvents(string eventSetId, DateTime currentPosition, TimeSpan interval)
        {
            var eventSet = _flowsheet.DynamicsManager.EventSetList[eventSetId];
            var initialTime = currentPosition - interval;
            var finalTime = currentPosition;

            var events = eventSet.Events.Values
                .Where(x => x.TimeStamp >= initialTime && x.TimeStamp < finalTime)
                .ToList();

            // the clone keeps the interpolation-rewind off the live model
            var props = _flowsheet.DynamicsManager.GetPropertyValuesFromEvents(_clone, currentPosition, _historian, eventSet);

            foreach (var p in props)
            {
                var obj = _flowsheet.SimulationObjects[p.Item1];
                obj.SetPropertyValue(p.Item2, p.Item3);
            }

            foreach (var ev in events)
            {
                if (!ev.Enabled) continue;

                switch (ev.EventType)
                {
                    case Interfaces.Enums.Dynamics.DynamicsEventType.ChangeProperty:
                        var obj = _flowsheet.SimulationObjects[ev.SimulationObjectID];
                        var value = Converter.ConvertToSI(ev.SimulationObjectPropertyUnits, ev.SimulationObjectPropertyValue.ToDoubleFromInvariant());
                        obj.SetPropertyValue(ev.SimulationObjectProperty, value);
                        break;
                    case Interfaces.Enums.Dynamics.DynamicsEventType.RunScript:
                        break;
                }
            }
        }

        /// <summary>Applies every enabled cause-and-effect matrix action whose alarm is active.</summary>
        public void ProcessCEMatrix(string matrixId)
        {
            var matrix = _flowsheet.DynamicsManager.CauseAndEffectMatrixList[matrixId];

            foreach (var item in matrix.Items.Values)
            {
                if (!item.Enabled) continue;

                var indicator = (IIndicator)_flowsheet.SimulationObjects[item.AssociatedIndicator];

                switch (item.AssociatedIndicatorAlarm)
                {
                    case Interfaces.Enums.Dynamics.DynamicsAlarmType.LL:
                        if (indicator.VeryLowAlarmActive) DoAlarmEffect(item);
                        break;
                    case Interfaces.Enums.Dynamics.DynamicsAlarmType.L:
                        if (indicator.LowAlarmActive) DoAlarmEffect(item);
                        break;
                    case Interfaces.Enums.Dynamics.DynamicsAlarmType.H:
                        if (indicator.HighAlarmActive) DoAlarmEffect(item);
                        break;
                    case Interfaces.Enums.Dynamics.DynamicsAlarmType.HH:
                        if (indicator.VeryHighAlarmActive) DoAlarmEffect(item);
                        break;
                }
            }
        }

        private void DoAlarmEffect(IDynamicsCauseAndEffectItem item)
        {
            var obj = _flowsheet.SimulationObjects[item.SimulationObjectID];
            var value = Converter.ConvertToSI(item.SimulationObjectPropertyUnits, item.SimulationObjectPropertyValue.ToDoubleFromInvariant());
            obj.SetPropertyValue(item.SimulationObjectProperty, value);
        }
    }
}