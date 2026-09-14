using Microsoft.Data.SqlClient;

namespace SincronizadorBD;

public class DatabaseDiagnosticsService(
    ConfiguracionConexiones configuracionConexiones,
    ILogger<DatabaseDiagnosticsService> logger,
    EstadoSincronizacionService estadoSincronizacionService)
{
    public async Task ProbarDestinoAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuracionConexiones.ObtenerDestino();

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

        const string sqlPermisoEjecutarProcedimiento = """
            SELECT ISNULL (
                HAS_PERMS_BY_NAME(
                    'dbo.GOBMX_GUARDAR_PAGOS',
                    'OBJECT',
                    'EXECUTE'
                ),
                0
            )
            """;

        await using var commandPermisoProcedimiento =
            new SqlCommand(sqlPermisoEjecutarProcedimiento, connection);

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

    public async Task ProbarOrigenAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuracionConexiones.ObtenerOrigen();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await ValidarLecturaSfpTramitesAsync(connection, cancellationToken);

        var ultimoFolio = await ObtenerUltimoFolioActualAsync(connection, cancellationToken);

        logger.LogInformation(
            """
                Conexión y lectura de SFP_TRAMITES verificadas en origen.
                Servidor SQL: {Version}.
                Folio actual: {UltimoFolio}.
            """,
            connection.ServerVersion, ultimoFolio);
    }

    public async Task ProbarEstadoLocalAsync(CancellationToken cancellationToken)
    {
        var estado = await estadoSincronizacionService.CargarAsync(cancellationToken);

        logger.LogInformation(
            """
                Estado local leído. Carga histórica terminada: {Terminada}.
            """, estado.CargaHistoricaTerminada);
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

    private static async Task<long> ObtenerUltimoFolioActualAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT MAX(Folio)
            FROM dbo.SFP_FOLIOS
        """;

        await using var command = new SqlCommand(sql, connection);

        var resultado = await command.ExecuteScalarAsync(cancellationToken);

        if (resultado is null || resultado == DBNull.Value)
            throw new InvalidOperationException("SFP_FOLIOS no devolvió un folio actual.");

        return Convert.ToInt64(resultado);
    }
}
