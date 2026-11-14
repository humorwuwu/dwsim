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

            Dim minimumPower = PowerAt(i_min)
            Dim maximumPower = PowerAt(i_max)

            If powerKW <= minimumPower Then Return i_min
            If powerKW >= maximumPower Then Return i_max

            Dim target = powerKW
            Dim fx As Func(Of Double, Double) = Function(i) PowerAt(i) - target

            Return DWSIM.MathOps.MathEx.BrentOpt.Brent.BrentOpt3(i_min, i_max, 200, 1.0E-6, 100, fx)

        End Function

        ''' <summary>Stack power (kW) at a current density (A/cm2).</summary>
        Public Function PowerAt(i As Double) As Double

            Dim breakdown = Model.CellVoltage(i, T, pCat, pAn)
            Return breakdown.U_cell * i * A_cell * N_cells / 1000.0

        End Function

        ''' <summary>Faraday efficiency including the low-load penalty.</summary>
        Public Function FaradayEfficiency(i As Double) As Double

            If i <= 0.0 Then Return 0.0
            Dim load = Math.Min(1.0, i / Math.Max(i_min, 1.0E-9))
            Return etaF_ref * (0.5 + 0.5 * load)

        End Function

        ''' <summary>Hydrogen molar flow (mol/s).</summary>
        Public Function HydrogenMolarFlow(i As Double) As Double

            Const F As Double = 96485.3365
            Return FaradayEfficiency(i) * i * A_cell * N_cells / (2.0 * F)

        End Function

        ''' <summary>Specific energy consumption (kWh/kg H2).</summary>
        Public Function SpecificEnergyConsumption(powerKW As Double, i As Double) As Double

            Dim hydrogenMassFlow = HydrogenMolarFlow(i) * 0.002016
            If hydrogenMassFlow <= 0.0 Then Return 0.0
            Return powerKW / (hydrogenMassFlow * 3600.0)

        End Function

    End Class

End Namespace