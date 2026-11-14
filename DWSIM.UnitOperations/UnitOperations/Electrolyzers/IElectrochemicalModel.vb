Namespace UnitOperations

    ''' <summary>Cell voltage model used by the electrolyzer unit operations.</summary>
    Public Interface IElectrochemicalModel

        Function CellVoltage(i As Double, T As Double, pCat As Double, pAn As Double) As VoltageBreakdown

    End Interface

End Namespace