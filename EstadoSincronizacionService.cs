using System.Text.Json;
using SincronizadorBD.Modelos;

namespace SincronizadorBD;

public class EstadoSincronizacionService(ILogger<EstadoSincronizacionService> logger)
{
    private static readonly JsonSerializerOptions opcionesJson = new()
    {
        WriteIndented = true
    };

    private readonly string rutaArchivo =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
                "SincronizadorBD",
                "estado-sincronizacion.json"
        );

    public async Task<EstadoSincronizacionLocal> CargarAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(rutaArchivo))
        {
            logger.LogInformation("No existe estado local. Se iniciará un estado vacío.");
            return new EstadoSincronizacionLocal();
        }

        await using var archivo = File.OpenRead(rutaArchivo);

        var estado =
            await JsonSerializer.DeserializeAsync<EstadoSincronizacionLocal>(
                archivo, opcionesJson, cancellationToken);

        return estado ??
            throw new InvalidOperationException(
              "El archivo de estado local está vacío o no es válido.");
    }

    public async Task GuardarAsync(
        EstadoSincronizacionLocal estado, CancellationToken cancellationToken)
    {
        var directorio =
            Path.GetDirectoryName(rutaArchivo) ??
              throw new InvalidOperationException(
                "No fue posible determinar el directorio del estado local");

        Directory.CreateDirectory(directorio);

        var rutaTemporal = rutaArchivo + ".tmp";

        await using (var archivo = File.Create(rutaTemporal))
        {
            await JsonSerializer.SerializeAsync(archivo, estado, opcionesJson, cancellationToken);
        }

        File.Move(rutaTemporal, rutaArchivo, overwrite: true);

        logger.LogInformation("Estado local guardado correctamente");
    }
}
