using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using SincronizadorBD.Modelos;

namespace SincronizadorBD;

public class RepositorioTramitesDestino(
    ConfiguracionConexiones configuracionConexiones, ILogger<RepositorioTramitesDestino> logger)
{
    public async Task<string?> ObtenerFolioControlEstadoAsync(
        string folioSeguimiento, CancellationToken cancellationToken)
    {
        var connectionString = configuracionConexiones.ObtenerCadenaDestino();

        const string sql = """
            SELECT TOP (1) folioControlEstado
            FROM dbo.SFP_TRAMITES
            WHERE folioSeguimiento = @folioSeguimiento;
        """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        AgregarParametro(command, "@folioSeguimiento", SqlDbType.VarChar, folioSeguimiento, 50);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task GuardarTramiteAsync(TramiteGobMx tramite, CancellationToken cancellationToken)
    {
        var connectionString = configuracionConexiones.ObtenerCadenaDestino();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EjecutarProcedimientoPagosAsync(
                connection, transaction, tramite, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            logger.LogDebug(
                """
                Trámite confirmado en destino.
                Seguimiento: {FolioSeguimiento}.
                """,
                tramite.FolioSeguimiento);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task EjecutarProcedimientoPagosAsync(
        SqlConnection connection, SqlTransaction transaction, TramiteGobMx tramite,
        CancellationToken cancellationToken)
    {
        await using var command =
            new SqlCommand("dbo.GOBMX_GUARDAR_PAGOS", connection, transaction);

        command.CommandType = CommandType.StoredProcedure;

        AgregarParametro(
            command, "@folioSeguimiento", SqlDbType.VarChar, tramite.FolioSeguimiento, 50);
        AgregarParametro(
            command, "@folioControlEstado", SqlDbType.VarChar, tramite.FolioControlEstado, 50);
        AgregarParametro(
            command, "@idTramite", SqlDbType.VarChar, tramite.IdTramite, 50);
        AgregarParametro(
            command, "@fechaVencimiento", SqlDbType.DateTime, tramite.FechaVencimiento);
        AgregarParametro(
            command, "@Fecha", SqlDbType.DateTime, tramite.Fecha);
        AgregarParametro(
            command, "@Estatus", SqlDbType.VarChar, tramite.Estatus, 50);
        AgregarParametro(
            command, "@importePagado", SqlDbType.VarChar,
            tramite.ImportePagado?.ToString(CultureInfo.InvariantCulture), 50);
        AgregarParametro(
            command, "@lineaCaptura", SqlDbType.VarChar, tramite.LineaCaptura, 50);
        AgregarParametro(
            command, "@referenciapago", SqlDbType.VarChar, tramite.ReferenciaPago, 50);
        AgregarParametro(
            command, "@fechaPago", SqlDbType.DateTime, tramite.FechaPago);
        AgregarParametro(
            command, "@numeroautorizacion", SqlDbType.VarChar, tramite.NumeroAutorizacion, 50);
        AgregarParametro(
            command, "@codigoBarras", SqlDbType.VarChar, tramite.CodigoBarras, 50);
        AgregarParametro(
            command, "@nombre", SqlDbType.VarChar, tramite.Nombre, 50);
        AgregarParametro(
            command, "@origenJson", SqlDbType.VarChar, tramite.OrigenJson, -1);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AgregarParametro(
        SqlCommand command, string nombre, SqlDbType tipo, object? valor, int? longitud = null)
    {
        var parametro =
            longitud.HasValue ?
                command.Parameters.Add(nombre, tipo, longitud.Value) :
                command.Parameters.Add(nombre, tipo);
        parametro.Value = valor ?? DBNull.Value;
    }
}
