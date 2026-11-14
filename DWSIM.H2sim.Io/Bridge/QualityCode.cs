namespace DWSIM.H2sim.Io
{
    /// <summary>Quality of a value exchanged with the PLC.</summary>
    public enum QualityCode
    {
        Good = 0,
        Bad = 1,
        Uncertain = 2,
        NotConnected = 3
    }
}