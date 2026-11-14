Namespace UnitOperations

    ''' <summary>A named electrolyzer model parameter with an optional curve.</summary>
    Public Class ElectrolyzerModelParameter

        Public Property Name As String

        Public Property Description As String

        Public Property Value As Double

        Public Property Units As String

        Public Property ValuesX As New List(Of Double)

        Public Property ValuesY As New List(Of Double)

    End Class

End Namespace