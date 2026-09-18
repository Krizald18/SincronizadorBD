namespace SincronizadorBD;

public sealed class ConfiguracionConexiones(IConfiguration configuration)
{
    public string ObtenerCadenaOrigen() => ObtenerCadenaConexion("Origen");
    public string ObtenerCadenaDestino() => ObtenerCadenaConexion("Destino");

    private string ObtenerCadenaConexion(string nombreConexion)
    {
        return configuration.GetConnectionString(nombreConexion) ??
            throw new InvalidOperationException(
                $"No se encontró la cadena de conexion {nombreConexion}");
    }
}
