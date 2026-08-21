using Serilog;
using SincronizadorBD;

var rutaBase = AppContext.BaseDirectory;
var rutaDirectorio = Path.Combine(rutaBase, "Logs");
var rutaArchivo = Path.Combine(rutaDirectorio, "Sincronizador-.log");

Directory.CreateDirectory(rutaDirectorio);

Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().WriteTo.File(
	rutaArchivo,
	rollingInterval: RollingInterval.Day,
	retainedFileCountLimit: 30,
	shared: true,
	flushToDiskInterval: TimeSpan.FromSeconds(1))
.CreateLogger();

try
{
	Log.Information("Iniciando SincronizadorBD...");
	Log.Information($"Directorio base: {rutaBase}");
	Log.Information($"Directorio de logs: {rutaDirectorio}");

	var builder = Host.CreateApplicationBuilder(args);

	builder.Services.AddWindowsService(options =>
	{
		options.ServiceName = "SincronizadorBD";
	});
	builder.Services.AddSerilog();
	builder.Services.AddSingleton<DatabaseService>();
	builder.Services.AddHostedService<Worker>();

	var host = builder.Build();

	host.Run();
}
catch (Exception ex)
{
	Log.Fatal(ex, "El sincronizador terminó inesperadamente.");
}
finally
{
	Log.CloseAndFlush();
}
