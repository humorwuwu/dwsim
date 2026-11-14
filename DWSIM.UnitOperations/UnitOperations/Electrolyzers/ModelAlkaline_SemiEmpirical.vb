Namespace UnitOperations

    ''' <summary>
    ''' Semi-empirical alkaline cell voltage model (Tafel activation, ohmic electrolyte,
    ''' limiting-current concentration). Coefficients are placeholders until the R9
    ''' conductivity correlation is fitted in T-2.1.
    ''' </summary>
    Public Class ModelAlkaline_SemiEmpirical

        Implements IElectrochemicalModel

        Public Property U_rev As Double = 1.23 Implements IElectrochemicalModel.U_rev

        Public Property U_tn As Double = 1.48 Implements IElectrochemicalModel.U_tn

        Public Property a_act As Double = 0.20

        Public Property b_act As Double = 0.05

        Public Property R_ohm_ref As Double = 0.25

        Public Property E_act_ohm As Double = 15000.0

        Public Property i_L As Double = 1.0

        Public Property T_ref As Double = 353.15

        Public Function CellVoltage(i As Double, T As Double, pCat As Double, pAn As Double) As VoltageBreakdown Implements IElectrochemicalModel.CellVoltage

            Const R As Double = 8.314
            Const F As Double = 96485.3365

            Dim bd As New VoltageBreakdown With {.U_rev = U_rev, .U_tn = U_tn}

            Dim pH2 = Math.Max(pCat, 1.0E-12)
            Dim pO2 = Math.Max(pAn, 1.0E-12)
            bd.U_ocv = U_rev + R * T / (2.0 * F) * Math.Log(pH2 * Math.Sqrt(pO2))

            bd.U_act = a_act + b_act * Math.Log(Math.Max(i, 1.0E-9))

            bd.U_ohm = i * R_ohm_ref * Math.Exp(E_act_ohm / R * (1.0 / T - 1.0 / T_ref))

            Dim ratio = Math.Min(Math.Max(i, 0.0) / Math.Max(i_L, 1.0E-9), 0.999999)
            bd.U_conc = R * T / (4.0 * F) * Math.Log(1.0 / (1.0 - ratio))

            bd.U_cell = bd.U_ocv + bd.U_act + bd.U_ohm + bd.U_conc

            Return bd

        End Function

    End Class

End Namespace