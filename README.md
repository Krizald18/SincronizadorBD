# SincronizadorBD

Servicio de Windows desarrollado en .NET 8 para migrar trámites de GobMX.

El servicio consulta `dbo.SFP_TRAMITES` en la base de origen y registra los trámites faltantes en destino mediante el procedimiento almacenado `dbo.GOBMX_GUARDAR_PAGOS`.

## Funcionamiento

El servicio realiza dos tareas:

1. Carga histórica: avanza gradualmente por los trámites existentes en origen, en lotes de hasta 1,000 registros.
2. Detección continua: identifica nuevos folios de control y los migra en las ejecuciones posteriores.

No utiliza la columna `Fecha` como marcador de avance, ya que la fecha de los equipos puede no estar sincronizada. El avance histórico se controla mediante `folioSeguimiento` y `folioControlEstado`; la detección continua se apoya en `dbo.SFP_FOLIOS`.

El estado local se guarda en:

```text
C:\ProgramData\SincronizadorBD\estado-sincronizacion.json
```

Ese archivo permite que el servicio continúe desde el último trámite procesado después de reinicios o interrupciones.

## Datos migrados

Por cada trámite faltante, el servicio asegura los registros requeridos en destino:

- `dbo.SFP_TRAMITES`
- `dbo.INT_PREELABORADOS_ENC`
- `dbo.INT_PREELABORADOS_DET`

La migración contempla trámites pendientes y pagados. Los datos del pago pueden actualizarse posteriormente mediante los procesos existentes del sistema SII.

Si el nombre supera los 50 caracteres, el servicio utiliza una ruta especial para conservarlo completo en `SFP_TRAMITES`, sin exceder la limitación del procedimiento almacenado legado.

## Requisitos

- Windows.
- .NET Runtime 8 para Windows x64.
- Acceso de red a las bases de datos de origen y destino.
- Permisos en origen: `SELECT`.
- Permisos en destino: `SELECT`, `INSERT` y `EXECUTE` sobre `dbo.GOBMX_GUARDAR_PAGOS`.

## Configuración

En producción se requiere crear manualmente este archivo:

```text
C:\ProgramData\SincronizadorBD\appsettings.Production.json
```

Debe contener como mínimo las cadenas de conexión:

```json
{
  "ConnectionStrings": {
    "Origen": "CADENA_DE_CONEXION_ORIGEN",
    "Destino": "CADENA_DE_CONEXION_DESTINO"
  }
}
```

No se deben guardar contraseñas en el repositorio, en `appsettings.json` ni en el archivo publicado.

La configuración base publicada contiene:

```json
{
  "Sincronizacion": {
    "IntervaloMinutos": 1,
    "TamanoLote": 1000,
    "ModoSimulacion": true
  }
}
```

El archivo externo puede reemplazar cualquier valor de esta sección. Para habilitar inserciones reales, agregar explícitamente lo siguiente al archivo externo:

```json
{
  "Sincronizacion": {
    "ModoSimulacion": false
  }
}
```

Se recomienda conservar `ModoSimulacion` en `true` hasta contar con autorización para iniciar la migración real.

Después de modificar la configuración externa se debe reiniciar el servicio.

## Diagnóstico

Para comprobar conexiones, permisos y lectura del estado local sin insertar información:

```powershell
dotnet run -- --diagnostico
```

Para simular un lote sin insertar datos ni guardar avance:

```powershell
dotnet run -- --simular-sincronizacion
```

## Publicación

Publicar el proyecto para Windows x64:

```powershell
dotnet publish .\SincronizadorBD.csproj -c Release -r win-x64 --self-contained false -o C:\Servicios\SincronizadorBD
```

El servidor debe contar con .NET Runtime 8 instalado.

## Instalación como servicio

Ejemplo de registro del servicio:

```powershell
sc.exe create SincronizadorBD binPath= '"C:\Servicios\SincronizadorBD\SincronizadorBD.exe"' start= demand
```

Iniciar el servicio:

```powershell
Start-Service SincronizadorBD
```

Detenerlo:

```powershell
Stop-Service SincronizadorBD
```

Consultar su estado:

```powershell
Get-Service SincronizadorBD
```

## Logs

Los registros se almacenan en:

```text
C:\ProgramData\SincronizadorBD\Logs
```

Para consultar las últimas líneas del log del día:

```powershell
Get-Content "$env:ProgramData\SincronizadorBD\Logs\Sincronizador-$(Get-Date -Format yyyyMMdd).log" -Tail 50
```

## Consideraciones operativas

- Las bases de datos no se modifican estructuralmente.
- El servicio solo consulta origen e inserta o consulta en destino.
- Un registro ya existente con el mismo `folioSeguimiento` se omite.
- Si existe el mismo `folioSeguimiento` con un `folioControlEstado` distinto, el servicio detiene el avance para evitar asociar datos incorrectamente.
- Antes de activar la migración real, ejecutar diagnóstico y simulación desde el servidor donde quedará instalado.
