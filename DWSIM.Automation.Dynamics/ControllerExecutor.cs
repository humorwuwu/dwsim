using System;
using System.Collections.Generic;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.UnitOperations.SpecialOps;

namespace DWSIM.Automation.Dynamics
{
    /// <summary>Solves the flowsheet's internal controllers in the order used by the dynamics loop.</summary>
    internal static class ControllerExecutor
    {
        /// <summary>Runs every active PID controller, then every active Python controller.</summary>
        public static void Execute(IFlowsheet flowsheet, IList<PIDController> pidControllers, IList<PythonController> pythonControllers, IDynamicsHost host)
        {
            foreach (var controller in pidControllers)
            {
                if (!controller.Active) continue;
                Solve(flowsheet, controller, controller.Name, host);
            }

            foreach (var controller in pythonControllers)
            {
                if (!controller.Active) continue;
                Solve(flowsheet, controller, controller.Name, host);
            }
        }

        private static void Solve(IFlowsheet flowsheet, ISimulationObject controller, string name, IDynamicsHost host)
        {
            flowsheet.ProcessScripts(Scripts.EventType.ObjectCalculationStarted, Scripts.ObjectType.FlowsheetObject, name);
            try
            {
                if (controller is PIDController pid) pid.Solve();
                else if (controller is PythonController python) python.Solve();
                flowsheet.ProcessScripts(Scripts.EventType.ObjectCalculationFinished, Scripts.ObjectType.FlowsheetObject, name);
            }
            catch (Exception)
            {
                flowsheet.ProcessScripts(Scripts.EventType.ObjectCalculationError, Scripts.ObjectType.FlowsheetObject, name);
                throw;
            }

            host.OnControllerIteration(controller);
        }
    }
}