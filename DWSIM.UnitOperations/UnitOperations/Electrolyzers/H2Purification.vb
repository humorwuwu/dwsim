Imports System.Globalization
Imports System.Linq
Imports DWSIM.Drawing.SkiaSharp.GraphicObjects
Imports DWSIM.DrawingTools.Point
Imports DWSIM.Interfaces
Imports DWSIM.Interfaces.Enums
Imports DWSIM.Interfaces.Enums.GraphicObjects
Imports DWSIM.Thermodynamics.Streams

Namespace UnitOperations

    ''' <summary>
    ''' Black-box hydrogen purification (TSA/PSA/DeOxo): removes O2 and water to a target
    ''' ppm, reports H2 loss, pressure drop and regeneration duty. Not a rigorous adsorber.
    ''' </summary>
    Public Class H2Purification

        Inherits BoPUnitOpBase

        Public Overrides Function GetDisplayName() As String
            Return "H2 Purification"
        End Function

        Public Overrides Function GetDisplayDescription() As String
            Return "H2 Purification"
        End Function

        Public Overrides ReadOnly Property Prefix As String = "H2PUR-"

        Public Property ProductPurityPpm As Double = 10.0

        Public Property H2LossFraction As Double = 0.005

        Public Property PressureDropBar As Double = 0.5

        Public Property H2Recovery As Double

        Public Property ProductPurity As Double

        Public Property O2Removal As Double

        Public Property H2ORemoval As Double

        Public Property RegenerationDuty As Double

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
                .Position = New Point(x + w, y + h / 3),
                .Type = ConType.ConOut,
                .Direction = ConDir.Right,
                .ConnectorName = "Product H2"
            }

            Dim offgas As New ConnectionPoint With {
                .Position = New Point(x + w, y + 2 * h / 3),
                .Type = ConType.ConOut,
                .Direction = ConDir.Right,
                .ConnectorName = "Off-gas"
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
                    .Add(offgas)
                Else
                    .Item(0).Position = product.Position
                    .Item(0).ConnectorName = product.ConnectorName
                    If .Count > 1 Then
                        .Item(1).Position = offgas.Position
                        .Item(1).ConnectorName = offgas.ConnectorName
                    Else
                        .Add(offgas)
                    End If
                End If
            End With

        End Sub

        Public Overrides Sub Calculate(Optional args As Object = Nothing)

            Dim feed = GetInletMaterialStream(0)
            Dim productStream = GetOutletMaterialStream(0)
            Dim offGasStream = GetOutletMaterialStream(1)

            If feed Is Nothing Then Throw New Exception("Connect the feed stream.")
            If productStream Is Nothing OrElse offGasStream Is Nothing Then
                Throw New Exception("Connect the product and off-gas streams.")
            End If

            Dim names = feed.Phases(0).Compounds.Keys.ToList()
            If Not names.Contains("Hydrogen") Then Throw New Exception("Feed must contain Hydrogen.")

            Dim molarFlows = feed.Phases(0).Compounds.Values.Select(Function(c) c.MolarFlow.GetValueOrDefault()).ToList()
            Dim h2Index = names.IndexOf("Hydrogen")
            Dim o2Index = names.IndexOf("Oxygen")
            Dim waterIndex = names.IndexOf("Water")

            Dim nH2 = molarFlows(h2Index)
            If nH2 <= 0.0 Then Throw New Exception("Feed hydrogen flow must be positive.")

            Dim targetImpurity = nH2 * ProductPurityPpm * 1.0E-6 / Math.Max(1.0 - ProductPurityPpm * 1.0E-6, 1.0E-12)
            Dim o2 = If(o2Index >= 0, molarFlows(o2Index), 0.0)
            Dim water = If(waterIndex >= 0, molarFlows(waterIndex), 0.0)
            Dim impurity = o2 + water

            Dim h2Loss = Math.Max(0.0, H2LossFraction) * nH2
            Dim removed = Math.Max(0.0, impurity - targetImpurity)

            Dim o2Removed = If(impurity > 0.0, removed * o2 / impurity, 0.0)
            Dim waterRemoved = If(impurity > 0.0, removed * water / impurity, 0.0)

            Dim productFlows As New List(Of Double)(molarFlows)
            Dim offGasFlows As New List(Of Double)(molarFlows)

            For i = 0 To names.Count - 1
                offGasFlows(i) = 0.0
                productFlows(i) = 0.0
            Next

            productFlows(h2Index) = nH2 - h2Loss
            If o2Index >= 0 Then productFlows(o2Index) = Math.Max(0.0, o2 - o2Removed)
            If waterIndex >= 0 Then productFlows(waterIndex) = Math.Max(0.0, water - waterRemoved)

            offGasFlows(h2Index) = h2Loss
            If o2Index >= 0 Then offGasFlows(o2Index) = o2Removed
            If waterIndex >= 0 Then offGasFlows(waterIndex) = waterRemoved

            Dim pressure = Math.Max(feed.GetPressure() - Math.Max(0.0, PressureDropBar) * 100000.0, 1000.0)
            Dim temperature = feed.GetTemperature()

            SetStream(productStream, names, productFlows, pressure, temperature)
            SetStream(offGasStream, names, offGasFlows, pressure, temperature)

            H2Recovery = (nH2 - h2Loss) / nH2
            ProductPurity = 1.0E6 * productFlows(h2Index) / Math.Max(productFlows.Sum(), 1.0E-12)
            O2Removal = o2Removed
            H2ORemoval = waterRemoved
            RegenerationDuty = removed * 50.0 / 1000.0

            Me.Calculated = True

        End Sub

        Private Sub SetStream(stream As MaterialStream, names As List(Of String), flows As List(Of Double), pressure As Double, temperature As Double)

            stream.Clear()
            stream.ClearAllProps()

            For i = 0 To names.Count - 1
                If flows(i) > 0.0 Then stream.SetOverallCompoundMolarFlow(names(i), flows(i))
            Next

            stream.SetPressure(pressure)
            stream.SetTemperature(temperature)
            stream.SetFlashSpec("PT")
            stream.Calculate()

        End Sub

        Public Overrides Function GetProperties(proptype As PropertyType) As String()
            Return New String() {"H2 Recovery", "Product Purity", "Pressure Drop", "Regeneration Duty", "O2 Removal", "H2O Removal"}
        End Function

        Public Overrides Function GetPropertyValue(prop As String, Optional su As IUnitsOfMeasure = Nothing) As Object

            Select Case prop
                Case "H2 Recovery" : Return H2Recovery
                Case "Product Purity" : Return ProductPurity
                Case "Pressure Drop" : Return PressureDropBar
                Case "Regeneration Duty" : Return RegenerationDuty
                Case "O2 Removal" : Return O2Removal
                Case "H2O Removal" : Return H2ORemoval
                Case Else : Return 0.0
            End Select

        End Function

        Public Overrides Function GetPropertyUnit(prop As String, Optional su As IUnitsOfMeasure = Nothing) As String

            Select Case prop
                Case "Product Purity" : Return "ppm"
                Case "Pressure Drop" : Return "bar"
                Case "Regeneration Duty" : Return "kW"
                Case "O2 Removal", "H2O Removal" : Return "mol/s"
                Case Else : Return ""
            End Select

        End Function

        Public Overrides Function GetReport(su As IUnitsOfMeasure, ci As CultureInfo, nf As String) As String

            Dim sb As New Text.StringBuilder()
            sb.AppendLine(String.Format(ci, "H2 recovery: {0}", H2Recovery.ToString(nf, ci)))
            sb.AppendLine(String.Format(ci, "Product purity: {0} ppm", ProductPurity.ToString(nf, ci)))
            sb.AppendLine(String.Format(ci, "Pressure drop: {0} bar", PressureDropBar.ToString(nf, ci)))
            sb.AppendLine(String.Format(ci, "Regeneration duty: {0} kW", RegenerationDuty.ToString(nf, ci)))
            Return sb.ToString()

        End Function

    End Class

End Namespace