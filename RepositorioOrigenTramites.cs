using System.Data;
using Microsoft.Data.SqlClient;
using SincronizadorBD.Modelos;

namespace SincronizadorBD;

public class RepositorioOrigenTramites(
    ConfiguracionConexiones configuracionConexiones, ILogger<RepositorioOrigenTramites> logger)
{
    public async Task<List<TramiteGobMx>> ObtenerLoteHistoricoAsync(
        string? ultimoFolioSeguimiento, string? ultimoFolioControlEstado, int tamanoLote,
        CancellationToken cancellationToken)
    {
        var connectionString = configuracionConexiones.ObtenerOrigen();

        const string sql = """
            SELECT TOP (@TamanoLote)
                folioSeguimiento, folioControlEstado, idTramite, fechaVencimiento, Fecha, Estatus,
                OrigenJSON, importePagado, lineaCaptura, referenciaPago, fechaPago,
                numeroautorizacion, codigoBarras, nombre
            FROM dbo.SFP_TRAMITES
            WHERE
            (
                @UltimoFolioSeguimiento IS NULL
                OR folioSeguimiento > @UltimoFolioSeguimiento
                OR (
                    folioSeguimiento = @UltimoFolioSeguimiento
                        AND folioControlEstado > @UltimoFolioControlEstado
                )
            )
            ORDER BY folioSeguimiento, folioControlEstado;
        """;

        var tramites = new List<TramiteGobMx>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;
        command.Parameters.Add("@TamanoLote", SqlDbType.Int).Value = tamanoLote;
        command.Parameters.Add("@UltimoFolioSeguimiento", SqlDbType.VarChar, 50).Value =
            (object?)ultimoFolioSeguimiento ?? DBNull.Value;
        command.Parameters.Add("@UltimoFolioControlEstado", SqlDbType.Char, 10).Value =
            (object?)ultimoFolioControlEstado ?? DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            tramites.Add(CrearTramite(reader));

        logger.LogInformation(
            "Se leyeron {CantidadTramites} trámites del lote histórico.", tramites.Count);

        return tramites;
    }

    public async Task<long> ObtenerFolioActualAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuracionConexiones.ObtenerOrigen();

        const string sql = """
            SELECT MAX(Folio)
            FROM dbo.SFP_FOLIOS;
        """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        var resultado = await command.ExecuteScalarAsync(cancellationToken);

        if (resultado is null || resultado == DBNull.Value)
            throw new InvalidOperationException("SFP_Folios no devolvió el folio actual.");

        return Convert.ToInt64(resultado);
    }

    public async Task<List<TramiteGobMx>> ObtenerLotePorFolioControlEstadoAsync(
        long ultimoFolioControlEstado, long folioControlEstadoActual, int tamanoLote,
        CancellationToken cancellationToken)
    {
        var connectionString = configuracionConexiones.ObtenerOrigen();

        const string sql = """
            SELECT TOP (@TamanoLote)
                folioSeguimiento, folioControlEstado, idTramite, fechaVencimiento, Fecha, Estatus,
                OrigenJSON, importePagado, lineaCaptura, referenciapago, fechaPago,
                numeroautorizacion, codigoBarras, nombre
            FROM dbo.SFP_TRAMITES
            WHERE folioControlEstado > @UltimoFolioControlEstado
                AND folioControlEstado <= @FolioControlEstadoActual
            ORDER BY folioControlEstado;
        """;

        var tramites = new List<TramiteGobMx>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;
        command.Parameters.Add("@TamanoLote", SqlDbType.Int).Value = tamanoLote;
        command.Parameters.Add("@UltimoFolioControlEstado", SqlDbType.Char, 10).Value =
            ultimoFolioControlEstado.ToString("D10");
        command.Parameters.Add("@FolioControlEstadoActual", SqlDbType.Char, 10).Value =
            folioControlEstadoActual.ToString("D10");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            tramites.Add(CrearTramite(reader));

        logger.LogInformation(
            """
                Se leyeron {CantidadTramites} trámites por folioControlEstado.
                Desde: {Desde}.
                Hasta: {Hasta}.
            """,
            tramites.Count, ultimoFolioControlEstado, folioControlEstadoActual);

        return tramites;
    }

    private static TramiteGobMx CrearTramite(SqlDataReader reader)
    {
        return new TramiteGobMx
        {
            FolioSeguimiento = reader.GetString(0),
            FolioControlEstado = reader.GetString(1),
            IdTramite = LeerTexto(reader, 2),
            FechaVencimiento = LeerFecha(reader, 3),
            Fecha = LeerFecha(reader, 4),
            Estatus = LeerTexto(reader, 5),
            OrigenJson = LeerTexto(reader, 6),
            ImportePagado = LeerImporte(reader, 7),
            LineaCaptura = LeerTexto(reader, 8),
            ReferenciaPago = LeerTexto(reader, 9),
            FechaPago = LeerFecha(reader, 10),
            NumeroAutorizacion = LeerTexto(reader, 11),
            CodigoBarras = LeerTexto(reader, 12),
            Nombre = LeerTexto(reader, 13)
        };
    }

    private static string? LeerTexto(SqlDataReader reader, int columna) =>
        reader.IsDBNull(columna) ? null : reader.GetString(columna);

    private static DateTime? LeerFecha(SqlDataReader reader, int columna) =>
        reader.IsDBNull(columna) ? null : reader.GetDateTime(columna);

    private static decimal? LeerImporte(SqlDataReader reader, int columna) =>
        reader.IsDBNull(columna) ? null : reader.GetDecimal(columna);

}
