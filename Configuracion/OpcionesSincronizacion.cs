namespace SincronizadorBD;

public sealed class OpcionesSincronizacion
{
    public const string Seccion = "Sincronizacion";

    public int IntervaloMinutos { get; set; }
    public bool ModoSimulacion { get; set; }
}
