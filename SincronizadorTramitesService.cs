using Microsoft.Extensions.Options;
using SincronizadorBD.Modelos;

namespace SincronizadorBD;

public class SincronizadorTramitesService(
    RepositorioOrigenTramites repositorioOrigenTramites,
    RepositorioDestinoTramites repositorioDestinoTramites,
    IOptions<OpcionesSincronizacion> opcionesSincronizacion,
    ILogger<SincronizadorTramitesService> logger)
{
    private readonly OpcionesSincronizacion _opcionesSincronizacion = opcionesSincronizacion.Value;

    public async Task SincronizarAsync(CancellationToken cancellationToken)
    {
        var modoSimulacion = _opcionesSincronizacion.ModoSimulacion;

        if (modoSimulacion)
        {
            logger.LogWarning(
              "Modo simulación activo: no se insertarán datos.");
        }

        var diasAtrasConsulta = _opcionesSincronizacion.DiasAtrasConsulta;
        var tramites =
            await repositorioOrigenTramites.ObtenerTramites(diasAtrasConsulta, cancellationToken);
        var resultado = await MigrarTramitesAsync(tramites, modoSimulacion, cancellationToken);

        if (modoSimulacion)
        {
            logger.LogInformation(
                """
                Sincronización simulada de los últimos {DiasConsultados} días.
                Leídos: {Leidos}.
                Ya existentes: {Existentes}.
                Por insertar: {PorInsertar}.
                Conflictos omitidos: {Conflictos}.
                """,
                diasAtrasConsulta + 1, tramites.Count, resultado.Existentes, resultado.PorInsertar,
                resultado.Conflictos);

            return;
        }

        logger.LogInformation(
            """
            Sincronización de los últimos {DiasConsultados} días terminada.
            Leídos: {Leidos}.
            Ya existentes: {Existentes}.
            Insertados: {Insertados}.
            Conflictos omitidos: {Conflictos}.
            """,
            diasAtrasConsulta + 1, tramites.Count, resultado.Existentes, resultado.Insertados,
            resultado.Conflictos);
    }

    private async Task<ResultadoMigracion> MigrarTramitesAsync(
        List<TramiteGobMx> tramites, bool modoSimulacion, CancellationToken cancellationToken)
    {
        var existentes = 0;
        var insertados = 0;
        var porInsertar = 0;
        var conflictos = 0;

        foreach (var tramite in tramites)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folioControlEstadoDestino =
                await repositorioDestinoTramites.ObtenerFolioControlEstadoAsync(
                    tramite.FolioSeguimiento, cancellationToken);

            if (folioControlEstadoDestino is not null)
            {
                var esMismoFolio =
                    String.Equals(
                        tramite.FolioControlEstado,
                        folioControlEstadoDestino.Trim(),
                        StringComparison.Ordinal);
                if (!esMismoFolio)
                {
                    conflictos++;

                    logger.LogWarning(
                        """
                            Conflicto de folioSeguimiento omitido.

                            Seguimiento: {FolioSeguimiento}.
                            Control en origen: {FolioControlEstadoOrigen}.
                            Control en destino: {FolioControlEstadoDestino}.

                            El destino sólo permite un registro por folioSeguimiento.
                        """,
                        tramite.FolioSeguimiento,
                        tramite.FolioControlEstado,
                        folioControlEstadoDestino.Trim());

                    continue;
                }

                existentes++;
                continue;
            }

            if (modoSimulacion)
            {
                porInsertar++;
                continue;
            }

            try
            {
                await repositorioDestinoTramites.GuardarAsync(tramite, cancellationToken);
                insertados++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    """
                    No se pudo migrar un trámite.

                    Seguimiento: {FolioSeguimiento}.
                    Control: {FolioControlEstado}.
                    """,
                    tramite.FolioSeguimiento, tramite.FolioControlEstado);

                throw;
            }
        }

        return new ResultadoMigracion(existentes, insertados, porInsertar, conflictos);
    }

    private sealed record ResultadoMigracion(
        int Existentes, int Insertados, int PorInsertar, int Conflictos);
}
