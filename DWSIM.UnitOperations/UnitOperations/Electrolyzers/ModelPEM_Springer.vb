Namespace UnitOperations

    ''' <summary>PEM cell voltage model (Springer membrane conductivity + Butler-Volmer + limiting current).</summary>
    Public Class ModelPEM_Springer

        Implements IElectrochemicalModel

        Public Property U_rev As Double = 1.23 Implements IElectrochemicalModel.U_rev

        Public Property U_tn As Double = 1.48 Implements IElectrochemicalModel.U_tn

        Public Property i0_ref As Double = 2.16E6

        Public Property E_act As Double = 76000.0

        Public Property alpha As Double = 0.5

        Public Property i_L As Double = 6.0

        Public Property t_mem As Double = 0.0178

        Public Property lambda_mem As Double = 22.0

        Public Property rho_electrode As Double = 7.5

        Public Property t_electrode As Double = 0.1

        Public Property T_ref As Double = 353.15

        Public Function CellVoltage(i As Double, T As Double, pCat As Double, pAn As Double) As VoltageBreakdown Implements IElectrochemicalModel.CellVoltage

            Const R As Double = 8.314
            Const F As Double = 96485.3365

            Dim bd As New VoltageBreakdown With {.U_rev = U_rev, .U_tn = U_tn}

            Dim pH2 = Math.Max(pCat, 1.0E-12)
            Dim pO2 = Math.Max(pAn, 1.0E-12)
            bd.U_ocv = U_rev + R * T / (2.0 * F) * Math.Log(pH2 * Math.Sqrt(pO2))

            ' i0_ref is the pre-exponential factor k_i0,an from Bzovsky 2024 Eq. 13.
            Dim i0 = i0_ref * Math.Exp(-E_act / (R * T))
            Dim x = 0.5 * Math.Max(i, 0.0) / Math.Max(i0, 1.0E-30)
            ' Bzovsky 2024 Eq. 12 has no additional alpha factor in asinh term.
            bd.U_act = R * T / F * Math.Log(x + Math.Sqrt(x * x + 1.0))

            ' Bzovsky 2024 Table 4 and Fig. 2 use the 0.005139/0.00326 coefficients.
            ' Anchor the temperature correction at 80 C so the published 1 MW/2.5 A/cm2
            ' operating point remains calibrated. The previous coefficients were 100x too large.
            Dim sigma = Math.Max((0.005139 * lambda_mem - 0.00326) *
                                 Math.Exp(1268.0 * (1.0 / T_ref - 1.0 / T)), 1.0E-6)
            Dim rMem = t_mem / sigma
            Dim rElec = (rho_electrode / 1000.0) * t_electrode
            bd.U_ohm = i * (rMem + rElec)

            Dim ratio = Math.Min(Math.Max(i, 0.0) / Math.Max(i_L, 1.0E-9), 0.999999)
            bd.U_conc = R * T / (4.0 * F) * Math.Log(1.0 / (1.0 - ratio))

            bd.U_cell = bd.U_ocv + bd.U_act + bd.U_ohm + bd.U_conc

            Return bd

        End Function

    End Class

End Namespace
