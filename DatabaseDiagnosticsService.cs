using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;

namespace SincronizadorBD;

public class DatabaseDiagnosticsService(
    IOptions<OpcionesSincronizacion> opcionesSincronizacion,
    ILogger<DatabaseDiagnosticsService> logger,
    ConfiguracionConexiones configuracionConexiones,
    RepositorioTramitesOrigen repositorioTramitesOrigen)
{
    public async Task ProbarOrigenAsync(CancellationToken cancellationToken)
    {
        var diasAtrasConsulta = opcionesSincronizacion.Value.DiasAtrasConsulta;
        var tramites =
            await repositorioTramitesOrigen.ObtenerTramites(diasAtrasConsulta, cancellationToken);

        logger.LogInformation(
            """
            Conexión y consulta de los últimos {DiasConsultados} días verificadas en origen.
            Trámites encontrados: {CantidadTramites}.
            """,
            diasAtrasConsulta + 1, tramites.Count);
    }

    public async Task ProbarDestinoAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuracionConexiones.ObtenerCadenaDestino();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await ValidarLecturaSfpTramitesAsync(connection, cancellationToken);

        const string sqlPermisoInsert =
            "SELECT HAS_PERMS_BY_NAME('dbo.SFP_TRAMITES', 'OBJECT', 'INSERT')";

        await using var commandPermiso = new SqlCommand(sqlPermisoInsert, connection);

        var puedeInsertar =
            Convert.ToInt32(await commandPermiso.ExecuteScalarAsync(cancellationToken)) == 1;

        if (!puedeInsertar)
            throw new InvalidOperationException(
                "La cuenta configurada no tiene permiso INSERT en dbo.SFP_TRAMITES.");

        const string sqlPermisoProcedimiento = """
            SELECT ISNULL (HAS_PERMS_BY_NAME('dbo.GOBMX_GUARDAR_PAGOS', 'OBJECT', 'EXECUTE'), 0)
            """;

        await using var commandPermisoProcedimiento =
            new SqlCommand(sqlPermisoProcedimiento, connection);

        var puedeEjecutarProcedimiento =
            Convert.ToInt32(
                await commandPermisoProcedimiento.ExecuteScalarAsync(cancellationToken)) == 1;

        if (!puedeEjecutarProcedimiento)
            throw new InvalidOperationException(
                "La cuenta configurada no puede ejecutar dbo.GOBMX_GUARDAR_PAGOS.");

        logger.LogInformation(
            """
            Conexión, lectura, permiso INSERT Y permiso EXECUTE verificados en Destino.
            Servidor SQL: {Version}
            """,
            connection.ServerVersion);
    }

    private static async Task ValidarLecturaSfpTramitesAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.SFP_TRAMITES
        """;

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
