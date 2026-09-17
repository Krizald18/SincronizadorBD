namespace SincronizadorBD;

public sealed class OpcionesSincronizacion
{
    public const string Seccion = "Sincronizacion";

    public int IntervaloMinutos { get; set; }
    public int DiasAtrasConsulta { get; set; }
    public bool ModoSimulacion { get; set; }
}
