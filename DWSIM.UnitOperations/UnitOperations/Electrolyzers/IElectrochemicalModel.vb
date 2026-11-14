Namespace UnitOperations

    ''' <summary>Cell voltage model used by the electrolyzer unit operations.</summary>
    Public Interface IElectrochemicalModel

        ''' <summary>Reversible cell voltage at the operating temperature, in volts.</summary>
        Property U_rev As Double

        ''' <summary>Thermoneutral cell voltage at the operating temperature, in volts.</summary>
        Property U_tn As Double

        Function CellVoltage(i As Double, T As Double, pCat As Double, pAn As Double) As VoltageBreakdown

    End Interface

End Namespace