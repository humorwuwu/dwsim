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

        Public Sub New()
            MyBase.New()
            Parameters = New Dictionary(Of String, ElectrolyzerModelParameter)
            SetupParameters()
        End Sub

        ''' <summary>Electrolyzers consume material; they are not sources.</summary>
        Public Overrides ReadOnly Property IsSource As Boolean = False

        Public Overrides Property ObjectClass As SimulationObjectClass = SimulationObjectClass.Electrolyzers

        Public Overrides ReadOnly Property MobileCompatible As Boolean = False

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

        ''' <summary>Adds the default parameter set. Derived classes override the defaults.</summary>
        Public Overridable Sub SetupParameters()

            AddParameter("A_cell", "Active cell area", 1000.0, "cm2")
            AddParameter("N_cells", "Number of cells", 1.0, "")
            AddParameter("P_cat", "Cathode pressure", 1.0, "bar")
            AddParameter("P_an", "Anode pressure", 1.0, "bar")
            AddParameter("T_op", "Operating temperature", 353.15, "K")
            AddParameter("etaF_ref", "Reference Faraday efficiency", 0.99, "")
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
                "Efficiency HHV", "Efficiency LHV", "Cooling Duty", "Stack Temperature"}

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

        Public Overrides Sub Calculate(Optional args As Object = Nothing)

            ' T-1.2 replaces this placeholder with the electrochemical model.
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