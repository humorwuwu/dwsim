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

            ' Both ends are operating limits the caller has to know about: silently clamping to
            ' the minimum hides a stack running below its turndown limit, which is a real event
            ' (the interlocks and the PLC both care about it).
            If powerKW < minimumPower AndAlso i_min > 0.0 Then
                Throw New ArgumentOutOfRangeException(
                    NameOf(powerKW),
                    String.Format(Globalization.CultureInfo.InvariantCulture,
                                  "Target power {0:F3} kW is below this stack's minimum of {1:F3} kW.",
                                  powerKW, minimumPower))
            End If
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
        ''' Faraday efficiency (fraction of the current that splits water). Gas crossover consumes
        ''' current, so the efficiency falls as the load falls: at the reference current density the
        ''' efficiency equals <see cref="etaF_ref"/>, and below it the crossover current becomes a
        ''' larger share of the total current.
        ''' </summary>
        Public Function FaradayEfficiency(i As Double) As Double

            If i <= 0.0 Then Return 0.0

            ' The crossover current that reproduces etaF_ref at i_ref, i_cross = (1/etaF_ref - 1) * i_ref.
            Dim crossoverCurrent As Double = Math.Max(0.0, 1.0 / etaF_ref - 1.0) * Math.Max(i_ref, 1.0E-9)

            Return Math.Max(0.0, Math.Min(1.0, i / (i + crossoverCurrent)))

        End Function

        ''' <summary>Current density at which <see cref="FaradayEfficiency"/> equals etaF_ref.</summary>
        Public Property i_ref As Double = 1.0

        ''' <summary>Hydrogen molar flow (mol/s).</summary>
        Public Function HydrogenMolarFlow(i As Double) As Double

            Const F As Double = 96485.3365
            Return FaradayEfficiency(i) * i * A_cell * N_cells / (2.0 * F)

        End Function

        ''' <summary>
        ''' Cross-membrane gas flow (mol/s). Membrane permeation is driven by the partial-pressure
        ''' difference and follows an Arrhenius temperature dependence, so the base coefficient is
        ''' scaled by the cathode/anode pressure ratio and the temperature relative to the reference.
        ''' The load dependence (1/i) reproduces the larger relative impact at low current density.
        ''' </summary>
        Public Function CrossoverMolarFlow(i As Double, crossoverCoeff As Double) As Double

            If i <= 0.0 Then Return 0.0
            Dim pressureFactor = If(pCat > 0.0, Math.Max(pCat, 0.0) / Math.Max(pCat_ref, 1.0E-9), 1.0)
            Dim temperatureFactor = Math.Exp(-E_perm / 8.314 * (1.0 / Math.Max(T, 1.0) - 1.0 / Math.Max(T_ref, 1.0)))
            Return Math.Max(crossoverCoeff, 0.0) * pressureFactor * temperatureFactor / i

        End Function

        ''' <summary>Reference pressure for the crossover coefficient, in bar.</summary>
        Public Property pCat_ref As Double = 30.0

        ''' <summary>Reference temperature for the crossover coefficient, in K.</summary>
        Public Property T_ref As Double = 353.15

        ''' <summary>Activation energy of hydrogen permeation through the membrane, J/mol.</summary>
        Public Property E_perm As Double = 20000.0

        ''' <summary>
        ''' Calculates both gas stream impurity concentrations in volume percent. The cathode also
        ''' carries saturated water vapour, so the denominator is the dry-gas-plus-impurity total
        ''' the stream actually contains; <paramref name="waterVapour"/> supplies that amount.
        ''' </summary>
        Public Sub GasPurity(i As Double, crossoverCoeff As Double,
                             ByRef h2inO2 As Double, ByRef o2inH2 As Double,
                             Optional waterVapour As Double = 0.0)

            Dim nH2 = HydrogenMolarFlow(i)
            Dim nO2 = nH2 / 2.0
            Dim crossover = CrossoverMolarFlow(i, crossoverCoeff)
            h2inO2 = 100.0 * crossover / Math.Max(nO2 + crossover, 1.0E-12)
            o2inH2 = 100.0 * crossover / Math.Max(nH2 + crossover + Math.Max(waterVapour, 0.0), 1.0E-12)

        End Sub

        ''' <summary>Specific energy consumption (kWh/kg H2).</summary>
        Public Function SpecificEnergyConsumption(powerKW As Double, i As Double) As Double

            Dim hydrogenMassFlow = HydrogenMolarFlow(i) * 0.002016
            If hydrogenMassFlow <= 0.0 Then Return 0.0
            Return powerKW / (hydrogenMassFlow * 3600.0)

        End Function

    End Class

End Namespace
