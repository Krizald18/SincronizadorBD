using System.Data;
using Microsoft.Data.SqlClient;
using SincronizadorBD.Modelos;

namespace SincronizadorBD;

public class RepositorioTramitesOrigen(
    ConfiguracionConexiones configuracionConexiones, ILogger<RepositorioTramitesOrigen> logger)
{
    public async Task<List<TramiteGobMx>> ObtenerTramites(
        int diasAtrasConsulta, CancellationToken cancellationToken)
    {
        var connectionString = configuracionConexiones.ObtenerCadenaOrigen();

        const string sql = """
            DECLARE @Inicio datetime;
            DECLARE @Fin datetime;

            SET @Inicio = DATEADD(day, DATEDIFF(day, 0, GETDATE()) - @DiasAtrasConsulta, 0);
            SET @Fin = DATEADD(day, DATEDIFF(day, 0, GETDATE()) + 1, 0);

            SELECT
                folioSeguimiento, folioControlEstado, idTramite, fechaVencimiento, Fecha, Estatus,
                OrigenJSON, importePagado, lineaCaptura, referenciaPago, fechaPago,
                numeroautorizacion, codigoBarras, nombre
            FROM dbo.SFP_TRAMITES
            WHERE Fecha >= @Inicio
                AND Fecha < @Fin
            ORDER BY Fecha, folioSeguimiento, folioControlEstado;
        """;

        var tramites = new List<TramiteGobMx>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;
        command.Parameters.Add("@DiasAtrasConsulta", SqlDbType.Int).Value = diasAtrasConsulta;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            tramites.Add(CrearTramite(reader));

        logger.LogInformation(
            "Se leyeron {CantidadTramites} trámites de los últimos {DiasConsultados} días.",
            tramites.Count, diasAtrasConsulta + 1);

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
