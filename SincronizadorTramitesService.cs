using Microsoft.Extensions.Options;
using SincronizadorBD.Modelos;

namespace SincronizadorBD;

public class SincronizadorTramitesService(
    RepositorioOrigenTramites repositorioOrigenTramites,
    RepositorioDestinoTramites repositorioDestinoTramites,
    EstadoSincronizacionService estadoSincronizacionService,
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
              "Modo simulación activo: no se insertarán datos ni se guardará avance.");
        }

        var tramites = await repositorioOrigenTramites.ObtenerTramites(cancellationToken);
        var resultado = await MigrarTramitesAsync(tramites, modoSimulacion, cancellationToken);

        if (modoSimulacion)
        {
            logger.LogInformation(
                """
                Sincronización simulada de los últimos cuatro días.
                Leídos: {Leidos}.
                Ya existentes: {Existentes}.
                Por insertar: {PorInsertar}.
                """,
                tramites.Count, resultado.Existentes, resultado.PorInsertar);

            return;
        }

        logger.LogInformation(
            """
            Sincronización de los últimos cuatro días termanada.
            Leídos: {Leidos}.
            Ya existentes: {Existentes}.
            Insertados: {Insertados}.
            """,
            tramites.Count, resultado.Existentes, resultado.PorInsertar);
    }

    private async Task<ResultadoMigracion> MigrarTramitesAsync(
        List<TramiteGobMx> tramites, bool modoSimulacion, CancellationToken cancellationToken)
    {
        var existentes = 0;
        var insertados = 0;
        var porInsertar = 0;

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
                    throw new InvalidOperationException(
                        $"""
                        Conflicto de folioSeguimiento detectado.

                        Seguimiento: {tramite.FolioSeguimiento}.
                        Control en origen: {tramite.FolioControlEstado}.
                        Control en destino: {folioControlEstadoDestino.Trim()}.

                        El destino sólo permite un registro por folioSeguimiento.
                        """);
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

        return new ResultadoMigracion(existentes, insertados, porInsertar);
    }

    private sealed record ResultadoMigracion(int Existentes, int Insertados, int PorInsertar);

    private async Task ProcesarLoteHistoricoAsync(
        EstadoSincronizacionLocal estado, int tamanoLote, bool modoSimulacion,
        CancellationToken cancellationToken)
    {
        if (estado.CargaHistoricaTerminada) return;

        var lote =
            await repositorioOrigenTramites.ObtenerLoteHistoricoAsync(
                estado.UltimoFolioSeguimientoHistorico, estado.UltimoFolioControlEstadoHistorico,
                tamanoLote, cancellationToken);

        if (lote.Count == 0)
        {
            if (!modoSimulacion)
            {
                estado.CargaHistoricaTerminada = true;
                await estadoSincronizacionService.GuardarAsync(estado, cancellationToken);
            }

            logger.LogInformation("No quedan trámites en la carga histórica.");
            return;
        }

        var resultado = await MigrarLoteAsync(lote, modoSimulacion, cancellationToken);

        if (modoSimulacion)
        {
            logger.LogInformation(
                """
                    Lote histórico simulado.
                    Leídos: {Leidos}.
                    Ya existentes: {Existentes}.
                    Por insertar: {PorInsertar}.
                """,
                lote.Count, resultado.Existentes, resultado.PorInsertar);

            return;
        }

        logger.LogInformation(
            """
                Lote histórico procesado.
                Leídos: {Leidos}.
                Ya existentes: {Existentes}.
                Insertados: {Insertados}.
            """,
            lote.Count, resultado.Existentes, resultado.Insertados);

        var ultimoTramite = lote[^1];

        estado.UltimoFolioSeguimientoHistorico = ultimoTramite.FolioSeguimiento;
        estado.UltimoFolioControlEstadoHistorico = ultimoTramite.FolioControlEstado;

        await estadoSincronizacionService.GuardarAsync(estado, cancellationToken);
    }

    private async Task ProcesarLoteContinuoAsync(
      EstadoSincronizacionLocal estado, int tamanoLote, CancellationToken cancellationToken)
    {
        var folioActual =
            await repositorioOrigenTramites.ObtenerFolioActualAsync(cancellationToken);

        if (folioActual <= estado.UltimoFolioDescubierto) return;

        var lote =
            await repositorioOrigenTramites.ObtenerLotePorFolioControlEstadoAsync(
                estado.UltimoFolioDescubierto, folioActual, tamanoLote, cancellationToken);

        if (lote.Count == 0)
        {
            estado.UltimoFolioDescubierto = folioActual;

            await estadoSincronizacionService.GuardarAsync(estado, cancellationToken);

            return;
        }

        var resultado = await MigrarLoteAsync(lote, modoSimulacion: false, cancellationToken);

        logger.LogInformation(
            """
                Lote continuo procesado.
                Leídos: {Leidos}.
                Ya existentes: {Existentes}.
                Insertados: {Insertados}.
            """,
            lote.Count, resultado.Existentes, resultado.Insertados);

        var ultimoFolioLeido = Convert.ToInt64(lote[^1].FolioControlEstado.Trim());

        estado.UltimoFolioDescubierto = lote.Count < tamanoLote ? folioActual : ultimoFolioLeido;

        await estadoSincronizacionService.GuardarAsync(estado, cancellationToken);
    }

    private async Task<ResultadoLote> MigrarLoteAsync(
      List<TramiteGobMx> lote, bool modoSimulacion, CancellationToken cancellationToken)
    {
        var existentes = 0;
        var insertados = 0;
        var porInsertar = 0;

        foreach (var tramite in lote)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folioControlEstadoDestino =
                await repositorioDestinoTramites.ObtenerFolioControlEstadoAsync(
                    tramite.FolioSeguimiento, cancellationToken);

            if (folioControlEstadoDestino is not null)
            {
                var esMismoFolio =
                    string.Equals(tramite.FolioControlEstado, folioControlEstadoDestino.Trim(),
                        StringComparison.Ordinal);

                if (!esMismoFolio)
                {
                    throw new InvalidOperationException(
                        $"""
                            Conflicto de folioSeguimiento detectado.

                            Seguimiento: {tramite.FolioSeguimiento}.
                            Control en origen: {tramite.FolioControlEstado}.
                            Control en destino: {folioControlEstadoDestino.Trim()}.

                            El destino sólo permite un registro por folioSeguimiento.
                            No se avanzó el estado local para este lote.
                        """);
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

        return new ResultadoLote(existentes, insertados, porInsertar);
    }

    private sealed record ResultadoLote(int Existentes, int Insertados, int PorInsertar);

}
