namespace SincronizadorBD;

public class Worker(
	ILogger<Worker> logger, DatabaseService databaseService, IConfiguration configuration
	) : BackgroundService
{
	private readonly ILogger<Worker> _logger = logger;
	private readonly DatabaseService _databaseService = databaseService;
	private readonly IConfiguration _configuration = configuration;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var intervaloMinutos = _configuration.GetValue<int>("Sincronizacion:IntervaloMinutos");
		var intervalo = TimeSpan.FromMinutes(intervaloMinutos);

		_logger.LogInformation(
			"Sincronizador configurado para ejecutarse cada {minutos} minutos", intervaloMinutos);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				_logger.LogInformation("Iniciando sincronización...");

				await _databaseService.SincronizarAsync(stoppingToken);

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

			if (stoppingToken.IsCancellationRequested)
				break;

			_logger.LogInformation("Próxima sincronización en {minutos} minutos.", intervaloMinutos);

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
