Namespace UnitOperations

    ''' <summary>PEM water electrolyzer (assembly class; model in T-1.2).</summary>
    Public Class PEMElectrolyzer

        Inherits ElectrolyzerUnitOpBase

        Public Overrides Function GetDisplayName() As String
            Return "PEM Electrolyzer"
        End Function

        Public Overrides Function GetDisplayDescription() As String
            Return "PEM Electrolyzer"
        End Function

        Public Overrides ReadOnly Property Prefix As String = "PEMEL-"

        Public Overrides Sub SetupParameters()

            MyBase.SetupParameters()

            AddParameter("A_cell", "Active cell area", 1000.0, "cm2")
            AddParameter("N_cells", "Number of cells", 182.0, "")
            AddParameter("i_max", "Maximum current density", 2.5, "A/cm2")
            AddParameter("V_cell_max", "Maximum cell voltage", 2.2, "V")
            AddParameter("i_min_frac", "Minimum load fraction", 0.10, "")
            AddParameter("i0_an_ref", "Anode exchange current density", 2.16E6, "A/cm2")
            AddParameter("E_act_an", "Anode activation energy", 76000.0, "J/mol")
            AddParameter("alpha_an", "Anode transfer coefficient", 0.5, "")
            AddParameter("i_L", "Limiting current density", 6.0, "A/cm2")
            AddParameter("t_mem", "Membrane thickness", 0.0178, "cm")
            AddParameter("lambda_mem", "Membrane water content", 22.0, "")
            AddParameter("rho_electrode", "Electrode resistivity", 7.5, "mOhm*cm")
            AddParameter("t_electrode", "Electrode thickness", 0.1, "cm")
            AddParameter("P_cat", "Cathode pressure", 30.0, "bar")
            AddParameter("P_an", "Anode pressure", 2.0, "bar")
            AddParameter("T_op", "Operating temperature", 353.15, "K")
            AddParameter("etaF_ref", "Reference Faraday efficiency", 0.99, "")
            AddParameter("C_stack", "Stack heat capacity", 500000.0, "J/K")
            AddParameter("m_stack", "Stack mass", 500.0, "kg")
            AddParameter("Cp_stack", "Stack specific heat", 500.0, "J/kgK")
            AddParameter("U_loss_area", "Stack heat loss coefficient", 50.0, "W/K")

            StackTemperature = ParameterValue("T_op", 353.15)

        End Sub

    End Class

End Namespace