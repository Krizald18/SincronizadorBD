# SincronizadorBD

Servicio de Windows desarrollado en .NET 8 para migrar trámites de GobMX.

Cada hora consulta los trámites creados durante los últimos días configurados en la base de datos origen e inserta en destino únicamente los que aún no existen.

## Funcionamiento

En cada ejecución, el servicio consulta `SFP_TRAMITES` en origen con este rango de fechas:

- Día actual.
- Días anteriores configurados.

El periodo se calcula mediante la hora del servidor SQL de origen. Por ejemplo, un lunes se revisan los trámites de viernes, sábado, domingo y lunes si el valor por defecto de `DiasAtrasConsulta` se mantiene en 3.

Este margen cubre interrupciones breves, como una falla eléctrica durante un viernes.

Por cada trámite encontrado:

- Si no existe en destino, se inserta mediante el procedimiento `GOBMX_GUARDAR_PAGOS`.
- Si existe con el mismo `folioSeguimiento` y `folioControlEstado`, se omite.
- Si existe el mismo `folioSeguimiento` con un `folioControlEstado` distinto, se registra una advertencia y se omite; el servicio continúa con los demás trámites.

La tabla de destino no debe permitir más de un registro por `folioSeguimiento`, por lo que se ejecuta ésta lógica previa a ejecutar `GOGMX_GUARDAR_PAGOS`.

## Datos migrados

La inserción se realiza mediante el procedimiento almacenado existente:

```text
GOBMX_GUARDAR_PAGOS
```

El procedimiento mantiene los registros requeridos en destino:

- `SFP_TRAMITES`
- `INT_PREELABORADOS_ENC`
- `INT_PREELABORADOS_DET`

## Requisitos

- Windows.
- .NET Runtime 8 para Windows x64.
- Acceso de red a las bases de datos de origen y destino.
- Permiso `SELECT` en origen.
- Permisos `SELECT`, `INSERT` y `EXECUTE` sobre `GOBMX_GUARDAR_PAGOS` en destino.

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
    "IntervaloMinutos": 60,
    "DiasAtrasConsulta": 3,
    "ModoSimulacion": true
  }
}
```

`DiasAtrasConsulta` acepta cero o un valor positivo. El valor 0 consulta únicamente los trámites de hoy.

El archivo creado manualmente puede reemplazar estos valores. Para permitir inserciones reales, agregar explícitamente:

```json
{
  "Sincronizacion": {
    "ModoSimulacion": false
  }
}
```

Después de modificar el archivo de configuración se debe reiniciar el servicio.

## Diagnóstico y simulación

Para comprobar conexiones, permisos y lectura en el periodo de consulta configurado sin insertar información:

```powershell
dotnet run -- --diagnostico
```

Para simular la sincronización sin insertar datos:

```powershell
dotnet run -- --simular-sincronizacion
```

Se recomienda conservar `ModoSimulacion` en `true` hasta contar con autorización y un entorno confiable para la migración real.

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

Los conflictos de `folioSeguimiento` también se registran allí como advertencias.

Para consultar las últimas líneas del log del día:

```powershell
Get-Content "$env:ProgramData\SincronizadorBD\Logs\Sincronizador-$(Get-Date -Format yyyyMMdd).log" -Tail 50
```

## Consideraciones operativas

- Las bases de datos no se modifican estructuralmente.
- El servicio solo consulta origen e inserta o consulta en destino.
- El mismo periodo de consulta se revisa en cada ejecución.
- Antes de activar la migración real, ejecutar diagnóstico y simulación desde el servidor donde quedará instalado.
