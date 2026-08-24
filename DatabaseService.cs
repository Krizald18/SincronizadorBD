using Microsoft.Data.SqlClient;
using SincronizadorBD.Models;
using System.Data;

namespace SincronizadorBD;

public class DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
{
	public async Task SincronizarAsync(CancellationToken cancellationToken)
	{
		var origenConnectionString = configuration.GetConnectionString("Origen");
		var destinoConnectionString = configuration.GetConnectionString("Destino");
		var tamañoLote = configuration.GetValue<int>("Sincronizacion:TamañoLote");

		if (tamañoLote <= 0)
			throw new InvalidOperationException("El tamaño del lote debe ser mayor que cero.");

		await using var origenConnection = new SqlConnection(origenConnectionString);
		await using var destinoConnection = new SqlConnection(destinoConnectionString);

		await origenConnection.OpenAsync(cancellationToken);
		await destinoConnection.OpenAsync(cancellationToken);

		logger.LogInformation("Conexiones de origen y destino abiertas.");
		logger.LogInformation("Tamaño de lote configurado: {tamañoLote}", tamañoLote);

		var checkpoint = await ObtenerCheckpointAsync(destinoConnection, cancellationToken);

		logger.LogInformation(
			"Checkpoint inicial tiene Fecha: {Fecha} e Id: {Id}",
			checkpoint.Fecha, checkpoint.Id);

		var totalEncontrados = 0;
		var totalInsertados = 0;
		var numeroLote = 0;

		while (!cancellationToken.IsCancellationRequested)
		{
			var registros = await ObtenerRegistrosAsync(
				origenConnection, checkpoint, tamañoLote, cancellationToken);

			logger.LogInformation(
				"Lote {Lote}: registros encontrados {Registros}", numeroLote + 1, registros.Count);

			if (registros.Count == 0) break;

			numeroLote++;

			await using var transaction = destinoConnection.BeginTransaction();

			try
			{
				var registrosInsertados = await InsertarLoteEnDestinoAsync(
					destinoConnection, transaction, registros, cancellationToken);

				logger.LogInformation(
					"Lote {Lote}: registros insertados {Registros}", numeroLote, registrosInsertados);

				var ultimoRegistro = registros[^1];

				await ActualizarCheckpointAsync(
					destinoConnection, transaction, ultimoRegistro.FechaRegistro, ultimoRegistro.Id,
					cancellationToken);

				logger.LogInformation(
					"Lote {Lote}: checkpoint actualizado a Fecha = {Fecha}, Id = {Id}",
					numeroLote, ultimoRegistro.FechaRegistro, ultimoRegistro.Id);

				await transaction.CommitAsync(cancellationToken);

				logger.LogInformation("Lote {Lote}: transacción confirmada correctamente.", numeroLote);

				checkpoint = new SincronizacionCheckpoint
				{
					Fecha = ultimoRegistro.FechaRegistro,
					Id = ultimoRegistro.Id
				};

				totalEncontrados += registros.Count;
				totalInsertados += registrosInsertados;
			}
			catch
			{
				try
				{
					await transaction.RollbackAsync(cancellationToken);

					logger.LogWarning("Lote {Lote}: la transacción fue revertida.", numeroLote);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Lote {Lote}: no fue posible revertir la transacción.", numeroLote);
				}
				throw;
			}
		}

		if (cancellationToken.IsCancellationRequested)
		{
			logger.LogInformation("Sincronización cancelada. Lotes procesados: {Lotes}", numeroLote);

			cancellationToken.ThrowIfCancellationRequested();
		}

		if (totalEncontrados == 0)
		{
			logger.LogInformation("No se encontraron registros nuevos.");

			return;
		}

		logger.LogInformation(
			"Sincronización finalizada. Lotes: {Lotes}, Encontrados: {Encontrados}, Insertados:" +
			"{Insertados}.", numeroLote, totalEncontrados, totalInsertados);
	}

	private async Task<List<Registro>> ObtenerRegistrosAsync(
		SqlConnection connection, SincronizacionCheckpoint checkpoint, int tamañoLote,
		CancellationToken cancellationToken)
	{
		const string sql = """
			SELECT TOP (@TamañoLote) Id, Nombre, FechaRegistro
			FROM TablaOrigen
			WHERE
				FechaRegistro > @FechaCheckpoint
				OR
				(
					FechaRegistro = @FechaCheckpoint
					AND Id > @IdCheckpoint
				)
			ORDER BY FechaRegistro, Id;
		""";

		var registros = new List<Registro>(tamañoLote);

		await using var command = new SqlCommand(sql, connection);

		command.Parameters.Add("@TamañoLote", SqlDbType.Int).Value = tamañoLote;
		command.Parameters.Add("@FechaCheckpoint", SqlDbType.DateTime2).Value = checkpoint.Fecha;
		command.Parameters.Add("@IdCheckpoint", SqlDbType.Int).Value = checkpoint.Id;

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);

		while (await reader.ReadAsync(cancellationToken))
		{
			registros.Add(new Registro
			{
				Id = reader.GetInt32(0),
				Nombre = reader.GetString(1),
				FechaRegistro = reader.GetDateTime(2)
			});
		}

		return registros;
	}

	private async Task<SincronizacionCheckpoint> ObtenerCheckpointAsync(
		SqlConnection connection, CancellationToken cancellationToken)
	{
		const string sql = """
			SELECT UltimaFechaProcesada, UltimoIdProcesado
			FROM SincronizacionEstado
			WHERE Id = 1;
		""";

		await using var command = new SqlCommand(sql, connection);
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);

		if (!await reader.ReadAsync(cancellationToken))
			throw new InvalidOperationException("No se encontró el estado de sincronización.");

		return new SincronizacionCheckpoint
		{
			Fecha = reader.GetDateTime(0),
			Id = reader.GetInt32(1)
		};
	}

	private async Task ActualizarCheckpointAsync(
		SqlConnection connection, SqlTransaction transaction, DateTime nuevaFecha, int nuevoId,
		CancellationToken cancellationToken)
	{
		const string sql = """
			UPDATE SincronizacionEstado
			SET
				UltimaFechaProcesada = @NuevaFecha,
				UltimoIdProcesado = @NuevoId
			WHERE Id = 1;
		""";

		await using var command = new SqlCommand(sql, connection, transaction);

		command.Parameters.Add("@NuevaFecha", SqlDbType.DateTime2).Value = nuevaFecha;
		command.Parameters.Add("@NuevoId", SqlDbType.Int).Value = nuevoId;

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private async Task<int> InsertarLoteEnDestinoAsync(
		SqlConnection connection, SqlTransaction transaction, List<Registro> registros,
		CancellationToken cancellationToken)
	{
		if (registros.Count == 0)
			return 0;

		const string crearTablaTemporal = """
			IF OBJECT_ID('tempdb..#RegistrosNuevos') IS NOT NULL
				DROP TABLE #RegistrosNuevos;

			CREATE TABLE #RegistrosNuevos
			(
				Id INT NOT NULL,
				Nombre NVARCHAR(200) NOT NULL,
				FechaRegistro DATETIME2(7) NOT NULL
			);
		""";

		await using (var command = new SqlCommand(crearTablaTemporal, connection, transaction))
		{
			await command.ExecuteNonQueryAsync(cancellationToken);
		}

		var tabla = new DataTable();

		tabla.Columns.Add("Id", typeof(int));
		tabla.Columns.Add("Nombre", typeof(string));
		tabla.Columns.Add("FechaRegistro", typeof(DateTime));

		foreach (var registro in registros)
		{
			cancellationToken.ThrowIfCancellationRequested();

			tabla.Rows.Add(registro.Id, registro.Nombre, registro.FechaRegistro);
		}

		using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction))
		{
			bulkCopy.DestinationTableName = "#RegistrosNuevos";

			bulkCopy.ColumnMappings.Add("Id", "Id");
			bulkCopy.ColumnMappings.Add("Nombre", "Nombre");
			bulkCopy.ColumnMappings.Add("FechaRegistro", "FechaRegistro");

			await bulkCopy.WriteToServerAsync(tabla, cancellationToken);
		}

		const string insertarNuevos = """
			INSERT INTO TablaDestino (Id, Nombre, FechaRegistro)
			SELECT N.Id, N.Nombre, N.FechaRegistro
			FROM #RegistrosNuevos N
			WHERE NOT EXISTS (
				SELECT 1
				FROM TablaDestino D
				WHERE D.Id = N.Id
			);
		""";

		await using var insertCommand = new SqlCommand(insertarNuevos, connection, transaction);

		return await insertCommand.ExecuteNonQueryAsync(cancellationToken);
	}
}
