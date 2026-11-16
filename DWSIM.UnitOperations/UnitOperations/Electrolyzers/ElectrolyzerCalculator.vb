Namespace UnitOperations

    ''' <summary>Power/current closure, Faraday efficiency and KPI calculation.</summary>
    Public Class ElectrolyzerCalculator

        Public Property Model As IElectrochemicalModel

        Public Property A_cell As Double = 1000.0

        Public Property N_cells As Double = 1.0

        Public Property i_min As Double = 0.0

        Public Property i_max As Double = 2.5

        Public Property T As Double = 353.15

        Public Property pCat As Double = 1.0

        Public Property pAn As Double = 1.0

        Public Property etaF_ref As Double = 0.99

        ''' <summary>Current density (A/cm2) for a target stack power (kW).</summary>
        Public Function CurrentForPower(powerKW As Double) As Double

            If Model Is Nothing Then Throw New InvalidOperationException("Electrochemical model is not set.")
            If powerKW <= 0.0 Then Throw New ArgumentOutOfRangeException(NameOf(powerKW), "Target power must be positive.")

            Dim minimumPower = PowerAt(i_min)
            Dim maximumPower = PowerAt(i_max)

            If powerKW <= minimumPower Then Return i_min
            If powerKW > maximumPower Then
                Throw New ArgumentOutOfRangeException(
                    NameOf(powerKW),
                    String.Format(Globalization.CultureInfo.InvariantCulture,
                                  "Target power {0:F3} kW exceeds this stack's maximum of {1:F3} kW.",
                                  powerKW, maximumPower))
            End If

            Dim target = powerKW
            Dim fx As Func(Of Double, Double) = Function(i) PowerAt(i) - target

            Return DWSIM.MathOps.MathEx.BrentOpt.Brent.BrentOpt3(i_min, i_max, 200, 1.0E-6, 100, fx)

        End Function

        ''' <summary>Stack power (kW) at a current density (A/cm2).</summary>
        Public Function PowerAt(i As Double) As Double

            Dim breakdown = Model.CellVoltage(i, T, pCat, pAn)
            Return breakdown.U_cell * i * A_cell * N_cells / 1000.0

        End Function

        ''' <summary>
        ''' Faraday efficiency at the configured reference value. Low-load gas crossover
        ''' is accounted for separately by the electrolyzer KPI calculation.
        ''' </summary>
        Public Function FaradayEfficiency(i As Double) As Double

            If i <= 0.0 Then Return 0.0
            Return etaF_ref

        End Function

        ''' <summary>Hydrogen molar flow (mol/s).</summary>
        Public Function HydrogenMolarFlow(i As Double) As Double

            Const F As Double = 96485.3365
            Return FaradayEfficiency(i) * i * A_cell * N_cells / (2.0 * F)

        End Function

        ''' <summary>Cross-membrane gas flow (mol/s), increasing as load falls.</summary>
        Public Function CrossoverMolarFlow(i As Double, crossoverCoeff As Double) As Double

            If i <= 0.0 Then Return 0.0
            Return Math.Max(crossoverCoeff, 0.0) / i

        End Function

        ''' <summary>Calculates both gas stream impurity concentrations in volume percent.</summary>
        Public Sub GasPurity(i As Double, crossoverCoeff As Double,
                             ByRef h2inO2 As Double, ByRef o2inH2 As Double)

            Dim nH2 = HydrogenMolarFlow(i)
            Dim nO2 = nH2 / 2.0
            Dim crossover = CrossoverMolarFlow(i, crossoverCoeff)
            h2inO2 = 100.0 * crossover / Math.Max(nO2 + crossover, 1.0E-12)
            o2inH2 = 100.0 * crossover / Math.Max(nH2 + crossover, 1.0E-12)

        End Sub

        ''' <summary>Specific energy consumption (kWh/kg H2).</summary>
        Public Function SpecificEnergyConsumption(powerKW As Double, i As Double) As Double

            Dim hydrogenMassFlow = HydrogenMolarFlow(i) * 0.002016
            If hydrogenMassFlow <= 0.0 Then Return 0.0
            Return powerKW / (hydrogenMassFlow * 3600.0)

        End Function

    End Class

End Namespace
