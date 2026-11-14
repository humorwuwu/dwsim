Namespace UnitOperations

    ''' <summary>Alkaline water electrolyzer (assembly class; model in T-1.2/T-2.1).</summary>
    Public Class AlkalineElectrolyzer

        Inherits ElectrolyzerUnitOpBase

        Public Overrides Function GetDisplayName() As String
            Return "Alkaline Electrolyzer"
        End Function

        Public Overrides Function GetDisplayDescription() As String
            Return "Alkaline Electrolyzer"
        End Function

        Public Overrides ReadOnly Property Prefix As String = "ALKEL-"

        Public Overrides Sub SetupParameters()

            MyBase.SetupParameters()

            AddParameter("A_cell", "Active cell area", 1000.0, "cm2")
            AddParameter("N_cells", "Number of cells", 300.0, "")
            AddParameter("i_max", "Maximum current density", 0.4, "A/cm2")
            AddParameter("V_cell_max", "Maximum cell voltage", 2.0, "V")
            AddParameter("i_min_frac", "Minimum load fraction", 0.10, "")
            AddParameter("t_mem", "Diaphragm thickness", 0.05, "cm")
            AddParameter("rho_electrode", "Electrode resistivity", 10.0, "mOhm*cm")
            AddParameter("t_electrode", "Electrode thickness", 0.2, "cm")
            AddParameter("KOH_wt", "KOH mass fraction", 0.30, "")
            AddParameter("P_cat", "Cathode pressure", 16.0, "bar")
            AddParameter("P_an", "Anode pressure", 16.0, "bar")
            AddParameter("T_op", "Operating temperature", 353.15, "K")
            AddParameter("etaF_ref", "Reference Faraday efficiency", 0.86, "")
            AddParameter("C_stack", "Stack heat capacity", 1.0E6, "J/K")
            AddParameter("m_stack", "Stack mass", 1500.0, "kg")
            AddParameter("Cp_stack", "Stack specific heat", 500.0, "J/kgK")
            AddParameter("U_loss_area", "Stack heat loss coefficient", 100.0, "W/K")

            StackTemperature = ParameterValue("T_op", 353.15)

        End Sub

    End Class

End Namespace