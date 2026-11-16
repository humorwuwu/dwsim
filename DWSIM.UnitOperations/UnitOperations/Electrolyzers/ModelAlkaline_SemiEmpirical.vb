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

        Public Property KOH_wt As Double = 0.30

        Public Property t_mem As Double = 0.05

        Public Property rho_electrode As Double = 10.0

        Public Property t_electrode As Double = 0.2

        Public Property UseKOHConductivity As Boolean = True

        ''' <summary>
        ''' Specific conductivity of a KOH solution in S/cm from Gambou et al. (2022),
        ''' Energies 15(9):3452, Equation 41 and Table 6. The correlation is valid for
        ''' 258.15-373.15 K and 0.15-0.45 KOH mass fraction.
        ''' </summary>
        Public Function ConductivityKOH(wt As Double, T As Double) As Double

            Const K1 As Double = 27.9844803
            Const K2 As Double = -0.924129482
            Const K3 As Double = -0.0149660371
            Const K4 As Double = -0.0905209551
            Const K5 As Double = 0.0114933252
            Const K6 As Double = 0.1765
            Const K7 As Double = 6.96648518
            Const K8 As Double = -2898.15658

            If wt < 0.15 OrElse wt > 0.45 Then
                Throw New ArgumentOutOfRangeException(
                    NameOf(wt), wt, "KOH mass fraction must be between 0.15 and 0.45.")
            End If
            If T < 258.15 OrElse T > 373.15 Then
                Throw New ArgumentOutOfRangeException(
                    NameOf(T), T, "KOH conductivity correlation is valid from 258.15 to 373.15 K.")
            End If

            Dim concentration = 100.0 * wt
            Dim conductivityS_m = K1 * concentration +
                                  K2 * T +
                                  K3 * T * T +
                                  K4 * T * concentration +
                                  K5 * T * T * Math.Pow(concentration, K6) +
                                  K7 * T / concentration +
                                  K8 * concentration / T

            Return conductivityS_m / 100.0

        End Function

        Public Function CellVoltage(i As Double, T As Double, pCat As Double, pAn As Double) As VoltageBreakdown Implements IElectrochemicalModel.CellVoltage

            Const R As Double = 8.314
            Const F As Double = 96485.3365

            Dim bd As New VoltageBreakdown With {.U_rev = U_rev, .U_tn = U_tn}

            Dim pH2 = Math.Max(pCat, 1.0E-12)
            Dim pO2 = Math.Max(pAn, 1.0E-12)
            bd.U_ocv = U_rev + R * T / (2.0 * F) * Math.Log(pH2 * Math.Sqrt(pO2))

            bd.U_act = a_act + b_act * Math.Log(Math.Max(i, 1.0E-9))

            If UseKOHConductivity Then
                Dim sigma = Math.Max(ConductivityKOH(KOH_wt, T), 1.0E-6)
                bd.U_ohm = i * (t_mem / sigma + (rho_electrode / 1000.0) * t_electrode)
            Else
                bd.U_ohm = i * R_ohm_ref * Math.Exp(E_act_ohm / R * (1.0 / T - 1.0 / T_ref))
            End If

            Dim ratio = Math.Min(Math.Max(i, 0.0) / Math.Max(i_L, 1.0E-9), 0.999999)
            bd.U_conc = R * T / (4.0 * F) * Math.Log(1.0 / (1.0 - ratio))

            bd.U_cell = bd.U_ocv + bd.U_act + bd.U_ohm + bd.U_conc

            Return bd

        End Function

    End Class

End Namespace
