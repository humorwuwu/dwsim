using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DWSIM.ExtensionMethods;
using DWSIM.Interfaces.Enums;
using DWSIM.UnitOperations.SpecialOps;
using DWSIM.CrossPlatform.UI.Controls.ReoGrid;
using System.Diagnostics;
using DWSIM.SharedClasses;
using System.Xml.Linq;
using DWSIM.Interfaces;
using DWSIM.Automation.Dynamics;
using DWSIM.Thermodynamics.Databases.ChemeoLink;

namespace DWSIM.UI.Desktop.Editors.Dynamics
{
    public class DynamicsIntegratorControl : TableLayout
    {

        public DropDown cbsc;

        string imgprefix = "DWSIM.UI.Desktop.Editors.Resources.Icons.";

        private Shared.Flowsheet Flowsheet;

        public Button btnPlay, btnRT, btnStop, btnViewResults;

        private ProgressBar pbProgress;

        private Label lbStatus;

        public bool Abort = false;

        public DynamicsIntegratorControl(DWSIM.UI.Desktop.Shared.Flowsheet fs)
            : base()
        {
            Flowsheet = fs;
        }

        public void Init()
        {

            var tl1 = new TableLayout { Padding = new Padding(5), Spacing = new Size(10, 10) };

            var tr1 = new TableRow();

            tr1.Cells.Add(new Label { Text = "Schedule", Font = new Font(SystemFont.Default, UI.Shared.Common.GetEditorFontSize()), VerticalAlignment = VerticalAlignment.Center });

            cbsc = new DropDown { Width = 300 };

            if (cbsc.Items.Count > 0) cbsc.SelectedIndex = 0;

            cbsc.SelectedIndexChanged += (s, e) =>
            {
                if (cbsc.SelectedIndex < 0) return;
                Flowsheet.DynamicsManager.CurrentSchedule = Flowsheet.DynamicsManager.ScheduleList.Values.ToList()[cbsc.SelectedIndex].ID;
            };

            tr1.Cells.Add(cbsc);

            tr1.Cells.Add(null);

            btnViewResults = new Button { Text = "View Results" };

            tr1.Cells.Add(btnViewResults);

            tl1.Rows.Add(tr1);

            Rows.Add(new TableRow(tl1));

            var tl2 = new TableLayout { Padding = new Padding(5), Spacing = new Size(10, 10) };

            var tr2 = new TableRow();

            btnPlay = new Button { ImagePosition = ButtonImagePosition.Overlay, Text = "", Width = 40, Height = 40, Image = new Bitmap(Eto.Drawing.Bitmap.FromResource(imgprefix + "icons8-play.png", this.GetType().Assembly)).WithSize(30, 30) };
            btnRT = new Button { ImagePosition = ButtonImagePosition.Overlay, Text = "", Width = 40, Height = 40, Image = new Bitmap(Eto.Drawing.Bitmap.FromResource(imgprefix + "icons8-realtime.png", this.GetType().Assembly)).WithSize(30, 30) };
            btnStop = new Button { ImagePosition = ButtonImagePosition.Overlay, Text = "", Width = 40, Height = 40, Image = new Bitmap(Eto.Drawing.Bitmap.FromResource(imgprefix + "icons8-stop.png", this.GetType().Assembly)).WithSize(30, 30) };

            pbProgress = new ProgressBar { MinValue = 0, MaxValue = 100 };

            lbStatus = new Label { Text = "00:00:00/00:30:00", Font = new Font(SystemFont.Default, UI.Shared.Common.GetEditorFontSize()), VerticalAlignment = VerticalAlignment.Center };

            tr2.Cells.Add(btnPlay);
            tr2.Cells.Add(btnRT);
            tr2.Cells.Add(btnStop);
            tr2.Cells.Add(lbStatus);
            tr2.Cells.Add(pbProgress);

            tl2.Rows.Add(tr2);

            Rows.Add(new TableRow(tl2));

            Rows.Add(null);

            this.MouseEnter += (s, e) =>
            {
                var prevsel = -1;
                if (cbsc.Items.Count > 0) prevsel = cbsc.SelectedIndex;
                cbsc.Items.Clear();
                foreach (var sch in Flowsheet.DynamicsManager.ScheduleList.Values)
                {
                    if (sch.CurrentIntegrator != "")
                    {
                        var integ = Flowsheet.DynamicsManager.IntegratorList[sch.CurrentIntegrator].Description;
                        cbsc.Items.Add(sch.Description + " (" + integ + ")");
                    }
                    else
                    {
                        cbsc.Items.Add(sch.Description);
                    }
                }
                if (cbsc.Items.Count > 0) cbsc.SelectedIndex = 0;
                try
                {
                    if (prevsel != -1) cbsc.SelectedIndex = prevsel;
                }
                catch { }
            };

            btnStop.Click += (s, e) =>
            {
                Abort = true;
            };

            btnPlay.Click += (s, e) =>
            {
                if (Flowsheet.DynamicMode) { RunIntegrator(false, false, Flowsheet, this); } else { Flowsheet.ShowMessage("Dynamic Mode is inactive.", Interfaces.IFlowsheet.MessageType.Warning); }
            };

            btnRT.Click += (s, e) =>
            {
                if (Flowsheet.DynamicMode) { RunIntegrator(true, false, Flowsheet, this); } else { Flowsheet.ShowMessage("Dynamic Mode is inactive.", Interfaces.IFlowsheet.MessageType.Warning); }
            };

            btnViewResults.Click += btnViewResults_Click;

        }

        public void Populate()
        {
            foreach (var sch in Flowsheet.DynamicsManager.ScheduleList.Values)
            {
                if (sch.CurrentIntegrator != "")
                {
                    var integ = Flowsheet.DynamicsManager.IntegratorList[sch.CurrentIntegrator].Description;
                    cbsc.Items.Add(sch.Description + " (" + integ + ")");
                }
                else
                {
                    cbsc.Items.Add(sch.Description);
                }
            }
        }

        private void btnRealtime_Click(object sender, EventArgs e)
        {
            if (Flowsheet.DynamicMode)
                RunIntegrator(true, false, Flowsheet, this);
        }

        public static void RestoreState(Interfaces.IFlowsheet Flowsheet, string stateID)
        {
            try
            {
                var initialstate = Flowsheet.StoredSolutions[stateID];
                Flowsheet.LoadProcessData(initialstate);
                Flowsheet.UpdateInterface();
            }
            catch (Exception ex)
            {
                MessageBox.Show(String.Format("Error Restoring State {0}: {1}", stateID, ex.Message), "Error", MessageBoxType.Error);
            }
        }

        public static Task RunIntegrator(bool realtime, bool waittofinish, Interfaces.IFlowsheet Flowsheet, DynamicsIntegratorControl control)
        {
            if (!Flowsheet.DynamicsManager.ScheduleList.ContainsKey(Flowsheet.DynamicsManager.CurrentSchedule))
            {
                Flowsheet.ShowMessage("Please select a schedule first.", IFlowsheet.MessageType.GeneralError);
                return null;
            }

            var schedule = Flowsheet.DynamicsManager.ScheduleList[Flowsheet.DynamicsManager.CurrentSchedule];

            if (!Flowsheet.DynamicsManager.IntegratorList.ContainsKey(schedule.CurrentIntegrator))
            {
                Flowsheet.ShowMessage("Please select an integrator first.", IFlowsheet.MessageType.GeneralError);
                return null;
            }

            var integrator = Flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];

            var options = new RunOptions
            {
                RealTime = realtime,
                StepSeconds = integrator.IntegrationStep.TotalSeconds,
                ControlDecimation = integrator.CalculationRateControl,
                EquilibriumDecimation = integrator.CalculationRateEquilibrium,
                PressureFlowDecimation = integrator.CalculationRatePressureFlow,
                ReuseInitialState = schedule.UseCurrentStateAsInitial,
                InitialStateId = schedule.InitialFlowsheetStateID
            };

            IDynamicsHost host = control != null ? (IDynamicsHost)new EtoDynamicsHost(control) : new HeadlessDynamicsHost();

            if (control != null)
            {
                control.Abort = false;
                control.btnPlay.Enabled = false;
                control.btnRT.Enabled = false;
                control.btnViewResults.Enabled = false;
            }

            if (waittofinish)
            {
                try
                {
                    DynamicsRunner.Run(Flowsheet, options, host);
                }
                catch (Exception ex)
                {
                    ShowError(Flowsheet, ex);
                }
                return Task.FromResult(0);
            }

            var task = DynamicsRunner.RunAsync(Flowsheet, options, host);
            task.ContinueWith(t => { var ignored = t.Exception; }, TaskScheduler.Default);
            return task;
        }

        private static void ShowError(IFlowsheet flowsheet, Exception exception)
        {
            var baseException = exception is AggregateException aggregate
                ? aggregate.GetBaseException()
                : exception.GetBaseException();
            flowsheet.RunCodeOnUIThread(() => flowsheet.ShowMessage(baseException.Message, IFlowsheet.MessageType.GeneralError));
        }

        private sealed class EtoDynamicsHost : IDynamicsHost
        {
            private readonly DynamicsIntegratorControl _control;

            public EtoDynamicsHost(DynamicsIntegratorControl control)
            {
                _control = control;
            }

            public bool ShouldAbort => _control.Abort;

            public bool IsPaused => false;

            public void OnRunStarting(IFlowsheet flowsheet, RunOptions options)
            {
                var integrator = GetIntegrator(flowsheet);
                flowsheet.RunCodeOnUIThread(() =>
                {
                    _control.pbProgress.Value = 0;
                    _control.pbProgress.MinValue = 0;
                    _control.pbProgress.MaxValue = options.RealTime ? int.MaxValue : (int)integrator.Duration.TotalSeconds;
                    _control.lbStatus.Text = "00:00:00/" + integrator.Duration.ToString("c");
                });
            }

            public void OnPreStep(int stepIndex, double simTime)
            {
                var integrator = GetIntegrator(_control.Flowsheet);
                _control.Flowsheet.RunCodeOnUIThread(() =>
                {
                    _control.pbProgress.Value = (int)Math.Min(simTime, int.MaxValue);
                    _control.lbStatus.Text = new TimeSpan(0, 0, (int)simTime).ToString("c") + "/" + integrator.Duration.ToString("c");
                });
            }

            public void OnPostStep(int stepIndex, double simTime, RunStats stats)
            {
                _control.Flowsheet.RunCodeOnUIThread(() => _control.Flowsheet.UpdateInterface());
            }

            public void OnControllerIteration(ISimulationObject controller) { }

            public void OnError(Exception exception, int stepIndex)
            {
                ShowError(_control.Flowsheet, exception);
            }

            public void OnRunFinished(RunStats stats)
            {
                _control.Flowsheet.RunCodeOnUIThread(() =>
                {
                    _control.btnPlay.Enabled = true;
                    _control.btnViewResults.Enabled = true;
                    _control.btnRT.Enabled = true;
                    _control.pbProgress.Value = 0;
                    _control.Flowsheet.UpdateOpenEditForms();
                });
            }

            private static IDynamicsIntegrator GetIntegrator(IFlowsheet flowsheet)
            {
                var schedule = flowsheet.DynamicsManager.ScheduleList[flowsheet.DynamicsManager.CurrentSchedule];
                return flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];
            }
        }

        private void btnViewResults_Click(object sender, EventArgs e)
        {

            var schedule = Flowsheet.DynamicsManager.ScheduleList[Flowsheet.DynamicsManager.CurrentSchedule];

            var integrator = Flowsheet.DynamicsManager.IntegratorList[schedule.CurrentIntegrator];

            var sheetc = ((dynamic)(Flowsheet.FlowsheetForm)).Spreadsheet.Sheet;

            var spreadsheet = (ReoGridControl)sheetc;

            var sheet = spreadsheet.NewWorksheet();

            sheet.RowCount = integrator.MonitoredVariableValues.Count + 1;

            sheet.Cells[0, 0].Data = "Time (ms)";

            int i, j;

            i = 1;
            foreach (var var in integrator.MonitoredVariables)
            {
                sheet.Cells[0, i].Data = var.Description + " " + (var.PropertyUnits != "" ? " (" + var.PropertyUnits + ")" : "");
                i += 1;
            }

            i = 1;
            foreach (var item in integrator.MonitoredVariableValues)
            {
                if (integrator.RealTime)
                {
                    sheet.Cells[i, 0].Data = item.Key * integrator.RealTimeStepMs;
                }
                else
                {
                    sheet.Cells[i, 0].Data = item.Key * integrator.IntegrationStep.TotalMilliseconds;
                }
                j = 1;
                foreach (var var in item.Value)
                {
                    sheet.Cells[i, j].Data = var.PropertyValue.ToDoubleFromInvariant();
                    j += 1;
                }
                i += 1;
            }

            spreadsheet.CurrentWorksheet = sheet;

        }
    }
}
