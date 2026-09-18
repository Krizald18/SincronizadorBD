using Microsoft.Extensions.Options;
using Serilog;
using SincronizadorBD;

var rutaBase = AppContext.BaseDirectory;
var rutaDatosAplicacion =
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SincronizadorBD");
var rutaLogs = Path.Combine(rutaDatosAplicacion, "Logs");
var rutaArchivoLogs = Path.Combine(rutaLogs, "Sincronizador-.log");

Directory.CreateDirectory(rutaLogs);

Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().WriteTo.File(
    rutaArchivoLogs,
    rollingInterval: RollingInterval.Day,
    retainedFileCountLimit: 30,
    shared: true,
    flushToDiskInterval: TimeSpan.FromSeconds(1))
.CreateLogger();

try
{
    Log.Information("Iniciando SincronizadorBD...");
    Log.Information($"Directorio base: {rutaBase}");
    Log.Information($"Directorio de logs: {rutaLogs}");

    var builder = Host.CreateApplicationBuilder(args);

    var rutaConfiguracionExterna = Path.Combine(rutaDatosAplicacion, "appsettings.Production.json");

    if (File.Exists(rutaConfiguracionExterna))
    {
        builder.Configuration.AddJsonFile(
            rutaConfiguracionExterna, optional: false, reloadOnChange: false);

        Log.Information(
            "Configuración externa cargada desde {RutaConfiguracionExterna}.",
            rutaConfiguracionExterna);
    }
    else if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            $"No se encontró la configuración externa requerida en {rutaConfiguracionExterna}.");
    }

    builder.Services.AddSerilog();
    builder.Services.AddOptions<OpcionesSincronizacion>()
        .BindConfiguration(OpcionesSincronizacion.Seccion)
        .Validate(
            opciones => opciones.IntervaloMinutos > 0,
            "Sincronizacion:IntervaloMinutos debe ser mayor que cero.")
        .Validate(
            opciones => opciones.DiasAtrasConsulta >= 0,
            "Sincronizacion:DiasAtrasConsulta no puede ser negativo.")
        .ValidateOnStart();
    builder.Services.AddSingleton<ConfiguracionConexiones>();
    builder.Services.AddSingleton<RepositorioTramitesOrigen>();
    builder.Services.AddSingleton<RepositorioTramitesDestino>();
    builder.Services.AddSingleton<SincronizadorTramitesService>();

    // Ejecutar Diagnóstico
    var modoDiagnostico = 
        args.Any(argumento =>
            string.Equals(argumento, "--diagnostico", StringComparison.OrdinalIgnoreCase));

    if (modoDiagnostico)
    {
        builder.Services.AddSingleton<DatabaseDiagnosticsService>();

        using var hostDiagnostico = builder.Build();

        var diagnostico = hostDiagnostico.Services.GetRequiredService<DatabaseDiagnosticsService>();

        await diagnostico.ProbarOrigenAsync(CancellationToken.None);
        await diagnostico.ProbarDestinoAsync(CancellationToken.None);

        return;
    }

    // Simular Sincronización
    var modoSimulacionManual =
        args.Any(argumento =>
            string.Equals(
                argumento, "--simular-sincronizacion", StringComparison.OrdinalIgnoreCase));

    if (modoSimulacionManual)
    {
        using var hostSimulacion = builder.Build();
        var opcionesSincronizacion =
            hostSimulacion.Services.GetRequiredService<IOptions<OpcionesSincronizacion>>().Value;
        var modoSimulacion = opcionesSincronizacion.ModoSimulacion;

        if (!modoSimulacion)
            throw new InvalidOperationException(
                "La simulación requiere Sincronizacion:ModoSimulacion = true.");

        var sincronizador =
          hostSimulacion.Services.GetRequiredService<SincronizadorTramitesService>();
        await sincronizador.SincronizarAsync(CancellationToken.None);

        return;
    }

    builder.Services.AddWindowsService(opciones =>
    {
        opciones.ServiceName = "SincronizadorBD";
    });

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
