Imports System.Linq
Imports DWSIM.Drawing.SkiaSharp.GraphicObjects
Imports DWSIM.DrawingTools.Point
Imports DWSIM.Interfaces.Enums
Imports DWSIM.Interfaces.Enums.GraphicObjects
Imports DWSIM.Interfaces

Namespace UnitOperations

    ''' <summary>Common base for the new electrolyzer unit operations.</summary>
    Public MustInherit Class ElectrolyzerUnitOpBase

        Inherits UnitOpBaseClass

        Implements IExternalUnitOperation

        Private m_LastResetInput As Double = 0.0

        Public Sub New()
            MyBase.New()
            Parameters = New Dictionary(Of String, ElectrolyzerModelParameter)
            SetupParameters()
        End Sub

        ''' <summary>Electrolyzers consume material; they are not sources.</summary>
        Public Overrides ReadOnly Property IsSource As Boolean = False

        Public Overrides Property ObjectClass As SimulationObjectClass = SimulationObjectClass.Electrolyzers

        Public Overrides ReadOnly Property MobileCompatible As Boolean = False

        Public Overrides ReadOnly Property SupportsDynamicMode As Boolean = True

        Public Overrides ReadOnly Property HasPropertiesForDynamicMode As Boolean = True

        Public Overrides Property ComponentName As String = GetDisplayName()

        Public Overrides Property ComponentDescription As String = GetDisplayDescription()

        Private ReadOnly Property IExternalUnitOperation_Name As String = GetDisplayName() Implements IExternalUnitOperation.Name

        Public ReadOnly Property Description As String = GetDisplayDescription() Implements IExternalUnitOperation.Description

        Public MustOverride ReadOnly Property Prefix As String Implements IExternalUnitOperation.Prefix

        <Xml.Serialization.XmlIgnore>
        Public Property Parameters As New Dictionary(Of String, ElectrolyzerModelParameter)

        Public Overrides ReadOnly Property EquipmentTypes As List(Of String)
            Get
                Return New List(Of String) From {"", "PEM", "Alkaline"}
            End Get
        End Property

        Public Property CellVoltage As Double

        Public Property CurrentDensity As Double

        Public Property StackVoltage As Double

        Public Property Current As Double

        Public Property Power As Double

        Public Property ReversibleVoltage As Double

        Public Property ThermoneutralVoltage As Double

        Public Property ActivationOverpotential As Double

        Public Property OhmicOverpotential As Double

        Public Property ConcentrationOverpotential As Double

        Public Property FaradayEfficiency As Double

        Public Property H2Purity As Double

        Public Property H2inO2 As Double

        Public Property O2inH2 As Double

        Public Property SpecificEnergyConsumption As Double

        Public Property EfficiencyHHV As Double

        Public Property EfficiencyLHV As Double

        Public Property CoolingDuty As Double

        Public Property StackTemperature As Double

        <Xml.Serialization.XmlIgnore> Public Property TripLatched As Boolean

        <Xml.Serialization.XmlIgnore> Public Property H2inO2HighAlarm As Boolean

        <Xml.Serialization.XmlIgnore> Public Property O2inH2HighAlarm As Boolean

        <Xml.Serialization.XmlIgnore> Public Property StackTemperatureHighAlarm As Boolean

        <Xml.Serialization.XmlIgnore> Public Property LastH2inO2 As Double

        <Xml.Serialization.XmlIgnore> Public Property LastO2inH2 As Double

        <Xml.Serialization.XmlIgnore> Public Property LastStackTemperature As Double

        <Xml.Serialization.XmlIgnore> Public Property LastPressureDifference As Double

        <Xml.Serialization.XmlIgnore> Public Property LastWaterLevel As Double = 100.0

        ''' <summary>Adds the default parameter set. Derived classes override the defaults.</summary>
        Public Overridable Sub SetupParameters()

            AddParameter("A_cell", "Active cell area", 1000.0, "cm2")
            AddParameter("N_cells", "Number of cells", 1.0, "")
            AddParameter("P_cat", "Cathode pressure", 1.0, "bar")
            AddParameter("P_an", "Anode pressure", 1.0, "bar")
            AddParameter("T_op", "Operating temperature", 353.15, "K")
            AddParameter("etaF_ref", "Reference Faraday efficiency", 0.99, "")
            AddParameter("SetpointMode", "0 = power input, 1 = current input", 0.0, "")
            AddParameter("P_stack", "Stack power input", 0.0, "kW")
            AddParameter("I_stack", "Stack current input", 0.0, "A")
            AddParameter("crossover_coeff", "Gas crossover coefficient", 1.0E-5, "mol/s")
            AddParameter("H2inO2_alarm", "H2-in-O2 high alarm", 2.0, "%")
            AddParameter("H2inO2_trip", "H2-in-O2 trip", 4.0, "%")
            AddParameter("O2inH2_alarm", "O2-in-H2 high alarm", 2.0, "%")
            AddParameter("StackTemperature_trip", "Stack temperature trip", 363.15, "K")
            AddParameter("dP_max", "Maximum cathode-anode pressure difference", 30.0, "bar")
            AddParameter("WaterLevel_trip", "Low-low water level trip", 5.0, "%")
            AddParameter("WaterLevel", "Water level input", 100.0, "%")
            AddParameter("Reset", "Trip reset input (1 = reset)", 0.0, "")
            AddParameter("C_stack", "Stack heat capacity", 1.0E6, "J/K")
            AddParameter("m_stack", "Stack mass", 500.0, "kg")
            AddParameter("Cp_stack", "Stack specific heat", 500.0, "J/kgK")
            AddParameter("U_loss_area", "Stack heat loss coefficient", 50.0, "W/K")

            StackTemperature = 353.15

        End Sub

        Protected Sub AddParameter(name As String, description As String, value As Double, units As String)

            Parameters(name) = New ElectrolyzerModelParameter With {
                .Name = name,
                .Description = description,
                .Value = value,
                .Units = units
            }

        End Sub

        Protected Function ParameterValue(name As String, fallback As Double) As Double

            Dim parameter As ElectrolyzerModelParameter = Nothing
            If Parameters.TryGetValue(name, parameter) Then Return parameter.Value
            Return fallback

        End Function

        Public Overrides Function GetProperties(proptype As PropertyType) As String()

            Return New String() {"Cell Voltage", "Current Density", "Stack Voltage", "Current", "Power",
                "Reversible Voltage", "Thermoneutral Voltage", "Activation Overpotential",
                "Ohmic Overpotential", "Concentration Overpotential", "Faraday Efficiency",
                "H2 Purity", "H2 in O2", "O2 in H2", "Specific Energy Consumption",
                "Efficiency HHV", "Efficiency LHV", "Cooling Duty", "Stack Temperature",
                "H2 in O2 High Alarm", "O2 in H2 High Alarm", "Stack Temperature High Alarm", "Trip Latched"}

        End Function

        Public Overrides Function GetPropertyValue(prop As String, Optional su As IUnitsOfMeasure = Nothing) As Object

            Select Case prop
                Case "Cell Voltage" : Return CellVoltage
                Case "Current Density" : Return CurrentDensity
                Case "Stack Voltage" : Return StackVoltage
                Case "Current" : Return Current
                Case "Power" : Return Power
                Case "Reversible Voltage" : Return ReversibleVoltage
                Case "Thermoneutral Voltage" : Return ThermoneutralVoltage
                Case "Activation Overpotential" : Return ActivationOverpotential
                Case "Ohmic Overpotential" : Return OhmicOverpotential
                Case "Concentration Overpotential" : Return ConcentrationOverpotential
                Case "Faraday Efficiency" : Return FaradayEfficiency
                Case "H2 Purity" : Return H2Purity
                Case "H2 in O2" : Return H2inO2
                Case "O2 in H2" : Return O2inH2
                Case "Specific Energy Consumption" : Return SpecificEnergyConsumption
                Case "Efficiency HHV" : Return EfficiencyHHV
                Case "Efficiency LHV" : Return EfficiencyLHV
                Case "Cooling Duty" : Return CoolingDuty
                Case "Stack Temperature" : Return StackTemperature
                Case "H2 in O2 High Alarm" : Return If(H2inO2HighAlarm, 1.0, 0.0)
                Case "O2 in H2 High Alarm" : Return If(O2inH2HighAlarm, 1.0, 0.0)
                Case "Stack Temperature High Alarm" : Return If(StackTemperatureHighAlarm, 1.0, 0.0)
                Case "Trip Latched" : Return If(TripLatched, 1.0, 0.0)
                Case Else : Return 0.0
            End Select

        End Function

        Public Overrides Function GetPropertyUnit(prop As String, Optional su As IUnitsOfMeasure = Nothing) As String

            Select Case prop
                Case "Cell Voltage", "Stack Voltage", "Reversible Voltage", "Thermoneutral Voltage",
                     "Activation Overpotential", "Ohmic Overpotential", "Concentration Overpotential"
                    Return "V"
                Case "Current"
                    Return "A"
                Case "Current Density"
                    Return "A/cm2"
                Case "Power", "Cooling Duty"
                    Return "kW"
                Case "Specific Energy Consumption"
                    Return "kWh/kg"
                Case "Stack Temperature"
                    Return "K"
                Case Else
                    Return ""
            End Select

        End Function

        ''' <summary>Creates the cell voltage model. Derived classes choose PEM or alkaline.</summary>
        Public Overridable Function CreateElectrochemicalModel() As IElectrochemicalModel
            Return New ModelPEM_Springer()
        End Function

        Public Overrides Sub CreateDynamicProperties()

            AddDynamicProperty("Stack Temperature", "Current stack temperature", 353.15, UnitOfMeasure.temperature, 1.0.GetType())
            AddDynamicProperty("Cooling Water Flow", "Coolant mass flow", 0.0, UnitOfMeasure.massflow, 1.0.GetType())
            AddDynamicProperty("Coolant Inlet Temperature", "Coolant supply temperature", 298.15, UnitOfMeasure.temperature, 1.0.GetType())
            AddDynamicProperty("Coolant Specific Heat", "Coolant specific heat capacity", 4180.0, UnitOfMeasure.heatCapacityCp, 1.0.GetType())
            AddDynamicProperty("Ambient Temperature", "Ambient temperature", 298.15, UnitOfMeasure.temperature, 1.0.GetType())
            AddDynamicProperty("H2 in O2", "Vol% H2 in oxygen stream", 0.0, UnitOfMeasure.none, 1.0.GetType())
            AddDynamicProperty("O2 in H2", "Vol% O2 in hydrogen stream", 0.0, UnitOfMeasure.none, 1.0.GetType())
            AddDynamicProperty("Enable Thermal Model", "Enable stack thermal inertia", 1.0, UnitOfMeasure.none, True.GetType())

        End Sub

        ''' <summary>
        ''' Integrates the lumped stack energy balance for one model step with internal
        ''' sub-stepping, so a large IntegrationStep stays stable.
        ''' </summary>
        Public Function IntegrateStackTemperature(Tstack As Double, timestep As Double, qGenW As Double,
                                                  mCoolant As Double, cpCoolant As Double,
                                                  TcoolIn As Double, TAmbient As Double) As Double

            Dim Cstack = Math.Max(ParameterValue("C_stack", 1.0E6), 1.0)
            Dim UA = Math.Max(ParameterValue("U_loss_area", 50.0), 0.0)
            Dim mcp = Math.Max(mCoolant, 0.0) * Math.Max(cpCoolant, 1.0)

            Dim tau = Cstack / Math.Max(UA + mcp, 1.0E-9)
            Dim dtSub = Math.Min(timestep, tau / 10.0)
            Dim nSub = Math.Max(1, CInt(Math.Ceiling(timestep / Math.Max(dtSub, 1.0E-9))))
            Dim dt = timestep / nSub

            Dim T = Tstack

            For k = 1 To nSub
                T = (Cstack * T + dt * (qGenW + mcp * TcoolIn + UA * TAmbient)) / (Cstack + dt * (UA + mcp))
            Next

            Return T

        End Function

        Public Overrides Sub RunDynamicModel()

            Dim schedule = FlowSheet.DynamicsManager.ScheduleList(FlowSheet.DynamicsManager.CurrentSchedule)
            Dim integrator = FlowSheet.DynamicsManager.IntegratorList(schedule.CurrentIntegrator)

            Dim timestep = integrator.IntegrationStep.TotalSeconds
            If integrator.RealTime Then timestep = Convert.ToDouble(integrator.RealTimeStepMs) / 1000.0
            If timestep <= 0.0 Then Return

            Dim thermalProperty = GetDynamicProperty("Enable Thermal Model")
            Dim enableThermal = If(thermalProperty IsNot Nothing, Convert.ToBoolean(thermalProperty), True)

            If enableThermal Then

                Dim Tstack = StackTemperature
                Dim propertyValue = GetDynamicProperty("Stack Temperature")
                If propertyValue IsNot Nothing Then Tstack = Convert.ToDouble(propertyValue)

                Dim mCoolant = 0.0
                propertyValue = GetDynamicProperty("Cooling Water Flow")
                If propertyValue IsNot Nothing Then mCoolant = Convert.ToDouble(propertyValue)

                Dim TcoolIn = 298.15
                propertyValue = GetDynamicProperty("Coolant Inlet Temperature")
                If propertyValue IsNot Nothing Then TcoolIn = Convert.ToDouble(propertyValue)

                Dim cpCoolant = 4180.0
                propertyValue = GetDynamicProperty("Coolant Specific Heat")
                If propertyValue IsNot Nothing Then cpCoolant = Convert.ToDouble(propertyValue)

                Dim TAmbient = 298.15
                propertyValue = GetDynamicProperty("Ambient Temperature")
                If propertyValue IsNot Nothing Then TAmbient = Convert.ToDouble(propertyValue)

                StackTemperature = IntegrateStackTemperature(Tstack, timestep, CoolingDuty * 1000.0,
                                                             mCoolant, cpCoolant, TcoolIn, TAmbient)

            End If

            SetDynamicProperty("Stack Temperature", StackTemperature)
            SetDynamicProperty("H2 in O2", H2inO2)
            SetDynamicProperty("O2 in H2", O2inH2)

        End Sub

        ''' <summary>
        ''' Updates alarms and latches a trip when a safety limit is exceeded. Called every
        ''' calculation and available to headless tests and the PLC bridge.
        ''' </summary>
        Public Sub UpdateInterlocks(h2inO2 As Double, o2inH2 As Double, stackTemperature As Double,
                                    cathodeMinusAnodePressure As Double, waterLevelPercent As Double)

            LastH2inO2 = h2inO2
            LastO2inH2 = o2inH2
            LastStackTemperature = stackTemperature
            LastPressureDifference = cathodeMinusAnodePressure
            LastWaterLevel = waterLevelPercent

            H2inO2HighAlarm = h2inO2 > ParameterValue("H2inO2_alarm", 2.0)
            O2inH2HighAlarm = o2inH2 > ParameterValue("O2inH2_alarm", 2.0)
            StackTemperatureHighAlarm = stackTemperature > ParameterValue("StackTemperature_trip", 363.15)

            If h2inO2 > ParameterValue("H2inO2_trip", 4.0) OrElse
               stackTemperature > ParameterValue("StackTemperature_trip", 363.15) OrElse
               Math.Abs(cathodeMinusAnodePressure) > ParameterValue("dP_max", 30.0) OrElse
               waterLevelPercent < ParameterValue("WaterLevel_trip", 5.0) Then
                TripLatched = True
            End If

        End Sub

        ''' <summary>Clears a latched trip when the last measurements are safe. Returns false if unsafe.</summary>
        Public Function ResetTrip() As Boolean

            Dim unsafeCondition = LastH2inO2 > ParameterValue("H2inO2_trip", 4.0) OrElse
                LastStackTemperature > ParameterValue("StackTemperature_trip", 363.15) OrElse
                Math.Abs(LastPressureDifference) > ParameterValue("dP_max", 30.0) OrElse
                LastWaterLevel < ParameterValue("WaterLevel_trip", 5.0)

            If unsafeCondition Then Return False

            TripLatched = False
            Return True

        End Function

        Public Overrides Sub Calculate(Optional args As Object = Nothing)

            Dim resetInput = ParameterValue("Reset", 0.0)
            If resetInput > 0.0 AndAlso m_LastResetInput <= 0.0 Then ResetTrip()
            m_LastResetInput = resetInput

            If TripLatched Then
                Power = 0.0
                Current = 0.0
                CurrentDensity = 0.0
                StackVoltage = 0.0
                CellVoltage = 0.0
                CoolingDuty = 0.0
                Me.Calculated = True
                Return
            End If

            Dim msin = GetInletMaterialStream(0)
            If msin Is Nothing Then Throw New Exception("Connect the water inlet stream.")

            Dim msout1 = GetOutletMaterialStream(0)
            Dim msout2 = GetOutletMaterialStream(1)
            If msout1 Is Nothing OrElse msout2 Is Nothing Then
                Throw New Exception("Connect both gas outlet streams.")
            End If

            Dim names = msin.Phases(0).Compounds.Keys.ToList()

            Dim wid, hid As String

            If names.Contains("HeavyWater") Then
                If Not names.Contains("Deuterium") Then Throw New Exception("Needs Deuterium compound.")
                wid = "HeavyWater"
                hid = "Deuterium"
            Else
                If Not names.Contains("Water") Then Throw New Exception("Needs Water compound.")
                If Not names.Contains("Hydrogen") Then Throw New Exception("Needs Hydrogen compound.")
                wid = "Water"
                hid = "Hydrogen"
            End If

            If Not names.Contains("Oxygen") Then Throw New Exception("Needs Oxygen compound.")

            Dim pp = DirectCast(PropertyPackage, DWSIM.Thermodynamics.PropertyPackages.PropertyPackage)
            If pp Is Nothing Then Throw New Exception("A property package is required.")
            pp.CurrentMaterialStream = msin

            Dim T = msin.GetTemperature()

            Dim thermalProperty = GetDynamicProperty("Enable Thermal Model")
            If thermalProperty IsNot Nothing AndAlso Convert.ToBoolean(thermalProperty) Then T = StackTemperature

            Dim DGf = pp.AUX_DELGig_RT(298.15, T, New String() {wid, hid, "Oxygen"}, New Double() {-1.0, 1.0, 0.5}, 0) * 8.314 * T / 1000.0
            Dim DHf = pp.AUX_DELHig_RT(298.15, T, New String() {wid, hid, "Oxygen"}, New Double() {-1.0, 1.0, 0.5}, 0) * 8.314 * T / 1000.0

            Dim mw = msin.Phases(0).Compounds(wid).ConstantProperties.Molar_Weight

            DHf += pp.AUX_HVAPi(wid, T) * mw / 1000.0

            Dim S_water = -203.606 * Math.Log(T / 1000) + 1523.29 * T / 1000 - 3196.413 * (T / 1000) ^ 2 / 2 + 2474.455 * (T / 1000) ^ 3 / 3 - 3.855326 / (2 * (T / 1000) ^ 2) - 488.7163
            Dim S_hydrogen = 33.066178 * Math.Log(T / 1000) - 11.363417 * T / 1000 + 11.432816 * (T / 1000) ^ 2 / 2 - 2.772874 * (T / 1000) ^ 3 / 3 + 0.158558 / (2 * (T / 1000) ^ 2) + 172.707974
            Dim S_oxygen = 31.32234 * Math.Log(T / 1000) - 20.23531 * T / 1000 + 57.86644 * (T / 1000) ^ 2 / 2 - 36.50624 * (T / 1000) ^ 3 / 3 + 0.007374 / (2 * (T / 1000) ^ 2) + 246.7945

            DGf = DHf + T * (S_water - (0.5 * S_oxygen + S_hydrogen)) / 1000.0

            Dim U_rev = DGf * 1000.0 / (2.0 * 96485.3365)
            Dim U_tn = DHf * 1000.0 / (2.0 * 96485.3365)

            Dim model = CreateElectrochemicalModel()
            model.U_rev = U_rev
            model.U_tn = U_tn

            Dim A_cell = ParameterValue("A_cell", 1000.0)
            Dim N_cells = Math.Max(1.0, ParameterValue("N_cells", 1.0))
            Dim iMax = Math.Max(1.0E-6, ParameterValue("i_max", 2.5))
            Dim iMin = iMax * Math.Max(0.0, ParameterValue("i_min_frac", 0.1))
            Dim pCat = Math.Max(1.0E-6, ParameterValue("P_cat", 1.0))
            Dim pAn = Math.Max(1.0E-6, ParameterValue("P_an", 1.0))

            If TypeOf model Is ModelPEM_Springer Then
                Dim m = DirectCast(model, ModelPEM_Springer)
                m.i0_ref = ParameterValue("i0_an_ref", m.i0_ref)
                m.E_act = ParameterValue("E_act_an", m.E_act)
                m.alpha = ParameterValue("alpha_an", m.alpha)
                m.i_L = Math.Max(1.0E-6, ParameterValue("i_L", m.i_L))
                m.t_mem = ParameterValue("t_mem", m.t_mem)
                m.lambda_mem = ParameterValue("lambda_mem", m.lambda_mem)
                m.rho_electrode = ParameterValue("rho_electrode", m.rho_electrode)
                m.t_electrode = ParameterValue("t_electrode", m.t_electrode)
            ElseIf TypeOf model Is ModelAlkaline_SemiEmpirical Then
                Dim m = DirectCast(model, ModelAlkaline_SemiEmpirical)
                m.i_L = Math.Max(1.0E-6, ParameterValue("i_L", m.i_L))
            End If

            Dim calculator As New ElectrolyzerCalculator With {
                .Model = model,
                .A_cell = A_cell,
                .N_cells = N_cells,
                .i_min = iMin,
                .i_max = iMax,
                .T = T,
                .pCat = pCat,
                .pAn = pAn,
                .etaF_ref = ParameterValue("etaF_ref", 0.99)
            }

            Dim mode = CInt(ParameterValue("SetpointMode", 0.0))
            Dim currentDensityValue As Double
            Dim powerKW As Double

            If mode = 1 Then

                Dim currentA = Math.Max(0.0, ParameterValue("I_stack", 0.0))
                currentDensityValue = currentA / A_cell
                Dim bdCurrent = model.CellVoltage(currentDensityValue, T, pCat, pAn)
                powerKW = bdCurrent.U_cell * currentDensityValue * A_cell * N_cells / 1000.0

            Else

                Dim es = GetEnergyStream()
                powerKW = ParameterValue("P_stack", 0.0)
                If es IsNot Nothing AndAlso es.EnergyFlow.HasValue Then powerKW = es.EnergyFlow.Value
                If powerKW <= 0.0 Then Throw New Exception("Connect an energy stream or set the P_stack parameter.")
                currentDensityValue = calculator.CurrentForPower(powerKW)

            End If

            Dim bd = model.CellVoltage(currentDensityValue, T, pCat, pAn)

            CellVoltage = bd.U_cell
            StackVoltage = bd.U_cell * N_cells
            CurrentDensity = currentDensityValue
            Current = currentDensityValue * A_cell * N_cells
            Power = bd.U_cell * currentDensityValue * A_cell * N_cells / 1000.0
            ReversibleVoltage = bd.U_rev
            ThermoneutralVoltage = bd.U_tn
            ActivationOverpotential = bd.U_act
            OhmicOverpotential = bd.U_ohm
            ConcentrationOverpotential = bd.U_conc
            FaradayEfficiency = calculator.FaradayEfficiency(currentDensityValue)
            SpecificEnergyConsumption = calculator.SpecificEnergyConsumption(Power, currentDensityValue)
            EfficiencyHHV = If(bd.U_cell > 0.0, bd.U_tn / bd.U_cell, 0.0)
            EfficiencyLHV = If(bd.U_cell > 0.0, 1.253 / bd.U_cell, 0.0)
            CoolingDuty = N_cells * currentDensityValue * A_cell * (bd.U_cell - bd.U_tn) / 1000.0
            StackTemperature = T

            Dim nH2 = calculator.HydrogenMolarFlow(currentDensityValue)
            Dim o2Produced = nH2 / 2.0
            Dim crossover = ParameterValue("crossover_coeff", 0.0) / Math.Max(currentDensityValue, 1.0E-6)

            H2inO2 = 100.0 * crossover / Math.Max(o2Produced + crossover, 1.0E-12)
            O2inH2 = 100.0 * crossover / Math.Max(nH2 + crossover, 1.0E-12)
            H2Purity = 100.0 - O2inH2

            UpdateInterlocks(H2inO2, O2inH2, T, pCat - pAn, ParameterValue("WaterLevel", 100.0))

            If TripLatched Then
                Power = 0.0
                Current = 0.0
                CurrentDensity = 0.0
                StackVoltage = 0.0
                CellVoltage = 0.0
                CoolingDuty = 0.0
                Me.Calculated = True
                Return
            End If

            Dim N0 = msin.Phases(0).Compounds.Values.Select(Function(c) c.MolarFlow.GetValueOrDefault()).ToList()
            Dim Nf As New List(Of Double)(N0)

            Dim widx = names.IndexOf(wid)
            Dim hidx = names.IndexOf(hid)
            Dim oidx = names.IndexOf("Oxygen")

            Nf(widx) = N0(widx) - nH2
            If Nf(widx) < 0.0 Then Throw New Exception(String.Format("Negative {0} molar flow. Increase water rate or reduce power.", wid))
            Nf(hidx) = N0(hidx) + nH2
            Nf(oidx) = N0(oidx) + o2Produced

            Dim pCatPa = pCat * 100000.0
            Dim pAnPa = pAn * 100000.0

            Dim xH2Osat = pp.AUX_PVAPi(wid, T) / pCatPa
            Dim xH2 = 1.0 - xH2Osat
            Dim NtotH2 = Nf(hidx) / Math.Max(xH2, 1.0E-12)
            Dim NH2Osat = NtotH2 - Nf(hidx)

            msout1.Clear()
            msout1.ClearAllProps()
            msout1.SetOverallCompoundMolarFlow(hid, Nf(hidx))
            msout1.SetOverallCompoundMolarFlow(wid, NH2Osat)
            msout1.SetPressure(pCatPa)
            msout1.SetTemperature(T)
            msout1.SetFlashSpec("PT")
            msout1.Calculate()

            Nf(hidx) = 0.0
            Nf(widx) -= NH2Osat
            If Nf(widx) < 0.0 Then Throw New Exception("Negative water molar flow. Increase water rate.")

            msout2.Clear()
            msout2.ClearAllProps()
            msout2.SetOverallComposition(Nf.ToArray().MultiplyConstY(1.0 / Nf.Sum))
            msout2.SetMolarFlow(Nf.Sum)
            msout2.SetPressure(pAnPa)
            msout2.SetTemperature(T)
            msout2.SetFlashSpec("PT")
            msout2.Calculate()

            Dim wasteHeatKW = CoolingDuty
            Dim coolingIn = GetInletMaterialStream(1)
            Dim coolingOut = GetOutletMaterialStream(2)

            If coolingIn IsNot Nothing AndAlso coolingOut IsNot Nothing Then

                coolingOut.Clear()
                coolingOut.ClearAllProps()

                For Each cname In coolingIn.Phases(0).Compounds.Keys
                    coolingOut.SetOverallCompoundMolarFlow(cname, coolingIn.Phases(0).Compounds(cname).MolarFlow.GetValueOrDefault())
                Next

                coolingOut.SetPressure(coolingIn.GetPressure())
                coolingOut.SetTemperature(coolingIn.GetTemperature())
                coolingOut.SetFlashSpec("PT")
                coolingOut.Calculate()

                If coolingOut.GetMassFlow() > 1.0E-9 Then
                    coolingOut.SetMassEnthalpy(coolingOut.GetMassEnthalpy() + wasteHeatKW * 1000.0 / coolingOut.GetMassFlow())
                    coolingOut.SetFlashSpec("PH")
                    coolingOut.AtEquilibrium = False
                End If

            Else

                Dim wh1 = msout1.GetMassFlow() / Math.Max(msin.GetMassFlow(), 1.0E-9)
                msout1.SetMassEnthalpy(msout1.GetMassEnthalpy() + wasteHeatKW * 1000.0 * wh1)
                msout1.SetFlashSpec("PH")
                msout1.AtEquilibrium = False

                Dim wh2 = msout2.GetMassFlow() / Math.Max(msin.GetMassFlow(), 1.0E-9)
                msout2.SetMassEnthalpy(msout2.GetMassEnthalpy() + wasteHeatKW * 1000.0 * wh2)
                msout2.SetFlashSpec("PH")
                msout2.AtEquilibrium = False

            End If

            Me.Calculated = True

        End Sub

        Public Overridable Sub CreateConnectors() Implements IExternalUnitOperation.CreateConnectors

            Dim w = GraphicObject.Width
            Dim h = GraphicObject.Height
            Dim x = GraphicObject.X
            Dim y = GraphicObject.Y

            Dim waterIn As New ConnectionPoint With {
                .Position = New Point(x, y + h / 3),
                .Type = ConType.ConIn,
                .Direction = ConDir.Right,
                .ConnectorName = "Water Inlet"
            }

            Dim coolingIn As New ConnectionPoint With {
                .Position = New Point(x, y + 2 * h / 3),
                .Type = ConType.ConIn,
                .Direction = ConDir.Right,
                .ConnectorName = "Cooling Water Inlet"
            }

            Dim h2Out As New ConnectionPoint With {
                .Position = New Point(x + w, y + h / 4),
                .Type = ConType.ConOut,
                .Direction = ConDir.Right,
                .ConnectorName = "Hydrogen-Rich Outlet"
            }

            Dim o2Out As New ConnectionPoint With {
                .Position = New Point(x + w, y + h / 2),
                .Type = ConType.ConOut,
                .Direction = ConDir.Right,
                .ConnectorName = "Oxygen-Rich Outlet"
            }

            Dim coolingOut As New ConnectionPoint With {
                .Position = New Point(x + w, y + 3 * h / 4),
                .Type = ConType.ConOut,
                .Direction = ConDir.Right,
                .ConnectorName = "Cooling Water Outlet"
            }

            With GraphicObject.InputConnectors
                If .Count = 0 Then
                    .Add(waterIn)
                    .Add(coolingIn)
                Else
                    .Item(0).Position = waterIn.Position
                    .Item(0).ConnectorName = waterIn.ConnectorName
                    If .Count > 1 Then
                        .Item(1).Position = coolingIn.Position
                        .Item(1).ConnectorName = coolingIn.ConnectorName
                    Else
                        .Add(coolingIn)
                    End If
                End If
            End With

            With GraphicObject.OutputConnectors
                If .Count = 0 Then
                    .Add(h2Out)
                    .Add(o2Out)
                    .Add(coolingOut)
                Else
                    .Item(0).Position = h2Out.Position
                    .Item(0).ConnectorName = h2Out.ConnectorName
                    If .Count > 1 Then
                        .Item(1).Position = o2Out.Position
                        .Item(1).ConnectorName = o2Out.ConnectorName
                    Else
                        .Add(o2Out)
                    End If
                    If .Count > 2 Then
                        .Item(2).Position = coolingOut.Position
                        .Item(2).ConnectorName = coolingOut.ConnectorName
                    Else
                        .Add(coolingOut)
                    End If
                End If
            End With

        End Sub

        Public Overridable Sub Draw(g As Object) Implements IExternalUnitOperation.Draw

            Dim canvas = TryCast(g, SkiaSharp.SKCanvas)
            If canvas Is Nothing Then Return

            Using paint As New SkiaSharp.SKPaint With {
                .IsAntialias = True,
                .Style = SkiaSharp.SKPaintStyle.Stroke,
                .StrokeWidth = 2.0F,
                .Color = New SkiaSharp.SKColor(30, 120, 200)
            }
                canvas.DrawRect(GraphicObject.X, GraphicObject.Y, GraphicObject.Width, GraphicObject.Height, paint)
            End Using

        End Sub

        Public Overridable Sub PopulateEditorPanel(container As Object) Implements IExternalUnitOperation.PopulateEditorPanel

            ' T-1.2 fills the parameter editor.

        End Sub

        Public Overridable Function ReturnInstance(typename As String) As Object Implements IExternalUnitOperation.ReturnInstance

            Return Activator.CreateInstance(Me.GetType())

        End Function

        Public Overrides Function GetReport(su As IUnitsOfMeasure, ci As Globalization.CultureInfo, nf As String) As String

            Dim sb As New Text.StringBuilder()

            sb.AppendLine(String.Format(ci, "Active cell area: {0} cm2", ParameterValue("A_cell", 0.0).ToString(nf, ci)))
            sb.AppendLine(String.Format(ci, "Number of cells: {0}", ParameterValue("N_cells", 0.0).ToString(nf, ci)))
            sb.AppendLine()
            sb.AppendLine(String.Format(ci, "Cell voltage: {0} V", CellVoltage.ToString(nf, ci)))
            sb.AppendLine(String.Format(ci, "Stack voltage: {0} V", StackVoltage.ToString(nf, ci)))
            sb.AppendLine(String.Format(ci, "Current: {0} A", Current.ToString(nf, ci)))
            sb.AppendLine(String.Format(ci, "Power: {0} kW", Power.ToString(nf, ci)))

            Return sb.ToString()

        End Function

        Public Overrides Function GetIconBitmap() As Object

            Return Nothing

        End Function

        Public Overrides Sub DisplayEditForm()
        End Sub

        Public Overrides Sub UpdateEditForm()
        End Sub

        Public Overrides Sub CloseEditForm()
        End Sub

        Public Overrides Function CloneXML() As Object

            Dim clone = DirectCast(Activator.CreateInstance(Me.GetType()), DWSIM.Interfaces.ICustomXMLSerialization)
            clone.LoadData(Me.SaveData())
            Return clone

        End Function

        Public Overrides Function CloneJSON() As Object

            Return Newtonsoft.Json.JsonConvert.DeserializeObject(
                Newtonsoft.Json.JsonConvert.SerializeObject(Me), Me.GetType())

        End Function

        Public Overrides Function SaveData() As List(Of System.Xml.Linq.XElement)

            Dim elements = MyBase.SaveData()
            Dim parametersElement As New System.Xml.Linq.XElement("ElectrolyzerParameters")

            For Each parameter In Parameters.Values
                parametersElement.Add(New System.Xml.Linq.XElement("Parameter",
                    New System.Xml.Linq.XAttribute("Name", parameter.Name),
                    New System.Xml.Linq.XAttribute("Description", parameter.Description),
                    New System.Xml.Linq.XAttribute("Value", parameter.Value.ToString(Globalization.CultureInfo.InvariantCulture)),
                    New System.Xml.Linq.XAttribute("Units", parameter.Units)))
            Next

            elements.Add(parametersElement)
            Return elements

        End Function

        Public Overrides Function LoadData(data As List(Of System.Xml.Linq.XElement)) As Boolean

            MyBase.LoadData(data)

            If Parameters Is Nothing Then Parameters = New Dictionary(Of String, ElectrolyzerModelParameter)

            Dim parametersElement = data.FirstOrDefault(Function(x) x.Name = "ElectrolyzerParameters")
            If parametersElement IsNot Nothing Then
                For Each element In parametersElement.Elements("Parameter")
                    Dim name As String = element.@Name
                    If Parameters.ContainsKey(name) Then
                        Dim value As Double
                        If Double.TryParse(element.@Value, Globalization.NumberStyles.Float,
                                           Globalization.CultureInfo.InvariantCulture, value) Then
                            Parameters(name).Value = value
                        End If
                    End If
                Next
            End If

            Return True

        End Function

    End Class

End Namespace