Imports DWSIM.Interfaces
Imports DWSIM.Interfaces.Enums
Imports DWSIM.Interfaces.Enums.GraphicObjects

Namespace UnitOperations

    ''' <summary>Common plumbing for the balance-of-plant unit operations.</summary>
    Public MustInherit Class BoPUnitOpBase

        Inherits UnitOpBaseClass

        Implements IExternalUnitOperation

        Public Sub New()
            MyBase.New()
        End Sub

        Public Overrides Property ObjectClass As SimulationObjectClass = SimulationObjectClass.Other

        Public Overrides ReadOnly Property MobileCompatible As Boolean = False

        Public Overrides Property ComponentName As String = GetDisplayName()

        Public Overrides Property ComponentDescription As String = GetDisplayDescription()

        Private ReadOnly Property IExternalUnitOperation_Name As String = GetDisplayName() Implements IExternalUnitOperation.Name

        Public ReadOnly Property Description As String = GetDisplayDescription() Implements IExternalUnitOperation.Description

        Public MustOverride ReadOnly Property Prefix As String Implements IExternalUnitOperation.Prefix

        Public MustOverride Sub CreateConnectors() Implements IExternalUnitOperation.CreateConnectors

        Public Overridable Sub Draw(g As Object) Implements IExternalUnitOperation.Draw
        End Sub

        Public Overridable Sub PopulateEditorPanel(container As Object) Implements IExternalUnitOperation.PopulateEditorPanel
        End Sub

        Public Overridable Function ReturnInstance(typename As String) As Object Implements IExternalUnitOperation.ReturnInstance
            Return Activator.CreateInstance(Me.GetType())
        End Function

        Public Overrides Function LoadData(data As System.Collections.Generic.List(Of System.Xml.Linq.XElement)) As Boolean
            Dim result = MyBase.LoadData(data)
            CreateConnectors()
            Return result
        End Function

        Public Overrides Function GetIconBitmap() As Object
            Return Nothing
        End Function

        Public Overrides Sub DisplayEditForm()
        End Sub

        Public Overrides Sub UpdateEditForm()
        End Sub

        Public Overrides Sub CloseEditForm()
        End Sub

        Public Overrides Function CloneXML() As Object
            Dim clone = DirectCast(Activator.CreateInstance(Me.GetType()), DWSIM.Interfaces.ICustomXMLSerialization)
            clone.LoadData(Me.SaveData())
            Return clone
        End Function

        Public Overrides Function CloneJSON() As Object
            Return Newtonsoft.Json.JsonConvert.DeserializeObject(
                Newtonsoft.Json.JsonConvert.SerializeObject(Me), Me.GetType())
        End Function

    End Class

End Namespace