using Microsoft.Extensions.Options;

namespace SincronizadorBD;

public class Worker(
    ILogger<Worker> logger, IOptions<OpcionesSincronizacion> opcionesSincronizacion,
    SincronizadorTramitesService sincronizadorTramitesService) : BackgroundService
{
    private readonly ILogger<Worker> _logger = logger;
    private readonly SincronizadorTramitesService _sincronizadorTramitesService =
        sincronizadorTramitesService;
    private readonly OpcionesSincronizacion _opcionesSincronizacion = opcionesSincronizacion.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervaloMinutos = _opcionesSincronizacion.IntervaloMinutos;
        var intervalo = TimeSpan.FromMinutes(intervaloMinutos);

        _logger.LogInformation(
            "Sincronizador configurado para ejecutarse cada {minutos} minutos", intervaloMinutos);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Iniciando sincronización...");
                await _sincronizadorTramitesService.SincronizarAsync(stoppingToken);
                _logger.LogInformation("Sincronización terminada correctamente.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("El servicio está siendo detenido.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la sincronización.");
            }

            if (stoppingToken.IsCancellationRequested) break;

            _logger.LogInformation(
              "Próxima sincronización en {minutos} minutos.", intervaloMinutos);

            try
            {
                await Task.Delay(intervalo, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
