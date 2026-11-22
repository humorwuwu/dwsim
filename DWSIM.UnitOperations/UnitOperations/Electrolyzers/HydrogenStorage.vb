Imports System.Globalization
Imports System.Linq
Imports DWSIM.Drawing.SkiaSharp.GraphicObjects
Imports DWSIM.DrawingTools.Point
Imports DWSIM.Interfaces.Enums
Imports DWSIM.Interfaces.Enums.GraphicObjects
Imports DWSIM.Thermodynamics.Streams

Namespace UnitOperations

    ''' <summary>
    ''' Hydrogen storage vessel/bundle: P-V-T with a fixed compressibility factor,
    ''' pass-through steady calculation and a dynamic mole balance.
    ''' </summary>
    Public Class HydrogenStorage

        Inherits BoPUnitOpBase

        Public Overrides Function GetDisplayName() As String
            Return "Hydrogen Storage"
        End Function

        Public Overrides Function GetDisplayDescription() As String
            Return "Hydrogen Storage"
        End Function

        Public Overrides ReadOnly Property Prefix As String = "H2ST-"

        Public Overrides ReadOnly Property SupportsDynamicMode As Boolean = True

        Public Overrides ReadOnly Property HasPropertiesForDynamicMode As Boolean = True

        Public Property TankVolume As Double = 50.0

        Public Property TankPressure As Double = 30.0

        Public Property DrawFlow As Double = 0.0

        Public Property MaxPressure As Double = 45.0

        Public Property CompressibilityFactor As Double = 0.95

        Public Property MolesStored As Double

        Public Property Overpressure As Boolean

        Public Overrides Sub CreateConnectors()

            Dim w = GraphicObject.Width
            Dim h = GraphicObject.Height
            Dim x = GraphicObject.X
            Dim y = GraphicObject.Y

            Dim feed As New ConnectionPoint With {
                .Position = New Point(x, y + h / 2),
                .Type = ConType.ConIn,
                .Direction = ConDir.Right,
                .ConnectorName = "Feed"
            }

            Dim product As New ConnectionPoint With {
                .Position = New Point(x + w, y + h / 2),
                .Type = ConType.ConOut,
                .Direction = ConDir.Right,
                .ConnectorName = "Outlet"
            }

            With GraphicObject.InputConnectors
                If .Count = 0 Then
                    .Add(feed)
                Else
                    .Item(0).Position = feed.Position
                    .Item(0).ConnectorName = feed.ConnectorName
                End If
            End With

            With GraphicObject.OutputConnectors
                If .Count = 0 Then
                    .Add(product)
                Else
                    .Item(0).Position = product.Position
                    .Item(0).ConnectorName = product.ConnectorName
                End If
            End With

        End Sub

        Public Overrides Sub CreateDynamicProperties()

            AddDynamicProperty("Tank Pressure", "Current tank pressure", TankPressure * 100000.0, UnitOfMeasure.pressure, 1.0.GetType())
            AddDynamicProperty("Moles Stored", "Hydrogen moles in the tank", MolesStored, UnitOfMeasure.mole, 1.0.GetType())

        End Sub

        Public Overrides Sub Calculate(Optional args As Object = Nothing)

            Dim feed = GetInletMaterialStream(0)
            Dim outlet = GetOutletMaterialStream(0)

            If feed Is Nothing Then Throw New Exception("Connect the feed stream.")
            If outlet Is Nothing Then Throw New Exception("Connect the outlet stream.")

            Dim names = feed.Phases(0).Compounds.Keys.ToList()

            outlet.Clear()
            outlet.ClearAllProps()

            For Each compoundName In names
                outlet.SetOverallCompoundMolarFlow(compoundName, feed.Phases(0).Compounds(compoundName).MolarFlow.GetValueOrDefault())
            Next

            Dim temperature = feed.GetTemperature()
            outlet.SetPressure(TankPressure * 100000.0)
            outlet.SetTemperature(temperature)
            outlet.SetFlashSpec("PT")
            outlet.Calculate()

            Dim z = Math.Max(CompressibilityFactor, 0.1)
            MolesStored = TankPressure * 100000.0 * TankVolume / (z * 8.314 * Math.Max(temperature, 1.0))
            Overpressure = TankPressure > MaxPressure

            Me.Calculated = True

        End Sub

        ''' <summary>Updates stored moles for a time step and returns the new mole count.</summary>
        Public Function UpdateStoredMoles(moles As Double, netFlow As Double, timestep As Double) As Double

            Return Math.Max(0.0, moles + netFlow * Math.Max(timestep, 0.0))

        End Function

        ''' <summary>
        ''' Compressibility factor used for the tank P-V-T relation. At 250 bar hydrogen is far
        ''' from ideal (Z is above 1, not the 0.95 the property value defaults to), so the flowsheet
        ''' property package is asked for the real value when it is available; the single value
        ''' remains as the fallback for a storage block with no property package attached.
        ''' </summary>
        Private Function TankCompressibilityFactor(feed As MaterialStream, moles As Double,
                                                   volume As Double, temperature As Double) As Double

            Dim fallback = Math.Max(CompressibilityFactor, 0.1)
            If feed Is Nothing OrElse PropertyPackage Is Nothing Then Return fallback
            If moles <= 0.0 OrElse volume <= 0.0 Then Return fallback

            Try
                Dim pressure = PressureFor(moles, fallback, volume, temperature)
                Dim composition = feed.Phases(0).Compounds.Values.Select(Function(c) c.MoleFraction.GetValueOrDefault()).ToArray()
                ' The tank holds the gas in the vapour phase; the package answers in its own
                ' pressure units (Pa).
                Dim z = PropertyPackage.AUX_Z(composition, temperature, pressure, PhaseName.Vapor)
                If Double.IsNaN(z) OrElse z < 0.1 OrElse z > 3.0 Then Return fallback
                Return z
            Catch
                Return fallback
            End Try

        End Function

        Private Shared Function PressureFor(moles As Double, z As Double, volume As Double, temperature As Double) As Double
            Return moles * z * 8.314 * Math.Max(temperature, 1.0) / Math.Max(volume, 1.0E-6)
        End Function

        Public Overrides Sub RunDynamicModel()

            Dim schedule = FlowSheet.DynamicsManager.ScheduleList(FlowSheet.DynamicsManager.CurrentSchedule)
            Dim integrator = FlowSheet.DynamicsManager.IntegratorList(schedule.CurrentIntegrator)

            Dim timestep = integrator.IntegrationStep.TotalSeconds
            If integrator.RealTime Then timestep = Convert.ToDouble(integrator.RealTimeStepMs) / 1000.0
            If timestep <= 0.0 Then Return

            Dim feed = GetInletMaterialStream(0)
            Dim outlet = GetOutletMaterialStream(0)

            Dim inletFlow = If(feed IsNot Nothing, feed.GetMolarFlow(), 0.0)
            Dim outletFlow = DrawFlow

            MolesStored = UpdateStoredMoles(MolesStored, inletFlow - outletFlow, timestep)

            Dim temperature = If(feed IsNot Nothing, feed.GetTemperature(), 298.15)
            Dim z = TankCompressibilityFactor(feed, MolesStored, TankVolume, temperature)
            Dim pressure = MolesStored * z * 8.314 * Math.Max(temperature, 1.0) / Math.Max(TankVolume, 1.0E-6)

            TankPressure = pressure / 100000.0
            Overpressure = TankPressure > MaxPressure

            SetDynamicProperty("Tank Pressure", pressure)
            SetDynamicProperty("Moles Stored", MolesStored)

        End Sub

        Public Overrides Function GetProperties(proptype As PropertyType) As String()
            Return New String() {"Tank Pressure", "Moles Stored", "Max Pressure", "Overpressure", "Draw Flow"}
        End Function

        Public Overrides Function GetPropertyValue(prop As String, Optional su As IUnitsOfMeasure = Nothing) As Object

            Select Case prop
                Case "Tank Pressure" : Return TankPressure
                Case "Moles Stored" : Return MolesStored
                Case "Max Pressure" : Return MaxPressure
                Case "Overpressure" : Return If(Overpressure, 1.0, 0.0)
                Case "Draw Flow" : Return DrawFlow
                Case Else : Return 0.0
            End Select

        End Function

        Public Overrides Function GetPropertyUnit(prop As String, Optional su As IUnitsOfMeasure = Nothing) As String

            Select Case prop
                Case "Tank Pressure", "Max Pressure" : Return "bar"
                Case "Moles Stored", "Draw Flow" : Return "mol/s"
                Case Else : Return ""
            End Select

        End Function

        Public Overrides Function GetReport(su As IUnitsOfMeasure, ci As CultureInfo, nf As String) As String

            Dim sb As New Text.StringBuilder()
            sb.AppendLine(String.Format(ci, "Tank pressure: {0} bar", TankPressure.ToString(nf, ci)))
            sb.AppendLine(String.Format(ci, "Moles stored: {0} mol", MolesStored.ToString(nf, ci)))
            sb.AppendLine(String.Format(ci, "Overpressure: {0}", Overpressure.ToString()))
            Return sb.ToString()

        End Function

    End Class

End Namespace