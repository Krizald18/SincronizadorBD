using Microsoft.Data.SqlClient;
using SincronizadorBD.Models;
using System.Data;

namespace SincronizadorBD;

public class DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
{
	public async Task SincronizarAsync()
	{
		var origenConnectionString = configuration.GetConnectionString("Origen");
		var destinoConnectionString = configuration.GetConnectionString("Destino");

		await using var origenConnection = new SqlConnection(origenConnectionString);
		await using var destinoConnection = new SqlConnection(destinoConnectionString);

		await origenConnection.OpenAsync();
		await destinoConnection.OpenAsync();

		logger.LogInformation("Conexiones de origen y destino abiertas.");

		var checkpoint = await ObtenerCheckpointAsync(destinoConnection);

		logger.LogInformation(
			"Checkpoint actual tiene Fecha: {Fecha} e Id: {Id}",
			checkpoint.Fecha, checkpoint.Id);

		var registros = new List<Registro>();

		var sqlOrigen = """
			SELECT Id, Nombre, FechaRegistro
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

		await using (var comandoOrigen = new SqlCommand(sqlOrigen, origenConnection))
		{
			comandoOrigen.Parameters
				.Add("@FechaCheckpoint", SqlDbType.DateTime2).Value = checkpoint.Fecha;
			comandoOrigen.Parameters
				.Add("@IdCheckpoint", SqlDbType.Int).Value = checkpoint.Id;

			await using var reader = await comandoOrigen.ExecuteReaderAsync();

			while (await reader.ReadAsync())
			{
				registros.Add(new Registro
				{
					Id = reader.GetInt32(0),
					Nombre = reader.GetString(1),
					FechaRegistro = reader.GetDateTime(2)
				});
			}
		}

		logger.LogInformation("Registros encontrados en origen: {registros}.", registros.Count);

		if (registros.Count == 0)
		{
			logger.LogInformation("No se encontraron registros nuevos.");
			return;
		}

		await using var transaction = destinoConnection.BeginTransaction();

		try
		{
			var registrosInsertados =
				await InsertarLoteEnDestinoAsync(destinoConnection, transaction, registros);

			logger.LogInformation("Registros insertados: {Registros}.", registrosInsertados);

			var ultimoRegistro = registros[^1];

			await ActualizarCheckpointAsync(
				destinoConnection, transaction, ultimoRegistro.FechaRegistro, ultimoRegistro.Id);

			logger.LogInformation(
				"Checkpoint actualizado a Fecha = {Fecha}, Id = {Id}",
				ultimoRegistro.FechaRegistro, ultimoRegistro.Id);

			await transaction.CommitAsync();

			logger.LogInformation("Transacción confirmada correctamente.");
			logger.LogInformation(
				"Sincronización finalizada. Encontrados: {Encontrados}, Insertados: {Insertados}.",
				registros.Count, registrosInsertados);
		}
		catch
		{
			await transaction.RollbackAsync();

			logger.LogWarning("La transacción fue revertida.");

			throw;
		}
	}

	private async Task<SincronizacionCheckpoint> ObtenerCheckpointAsync(SqlConnection connection)
	{
		const string sql = """
			SELECT UltimaFechaProcesada, UltimoIdProcesado
			FROM SincronizacionEstado
			WHERE Id = 1;
		""";

		await using var command = new SqlCommand(sql, connection);
		await using var reader = await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			throw new InvalidOperationException("No se encontró el estado de sincronización.");
		}

		return new SincronizacionCheckpoint
		{
			Fecha = reader.GetDateTime(0),
			Id = reader.GetInt32(1)
		};
	}

	private async Task ActualizarCheckpointAsync(
		SqlConnection connection, SqlTransaction transaction, DateTime nuevaFecha, int nuevoId)
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

		await command.ExecuteNonQueryAsync();
	}

	private async Task<int> InsertarLoteEnDestinoAsync(
		SqlConnection connection, SqlTransaction transaction, List<Registro> registros)
	{
		if (registros.Count == 0)
			return 0;

		const string crearTablaTemporal = """
			CREATE TABLE #RegistrosNuevos
			(
				Id INT NOT NULL,
				Nombre NVARCHAR(200) NOT NULL,
				FechaRegistro DATETIME2(7) NOT NULL
			);
		""";

		await using (var command = new SqlCommand(crearTablaTemporal, connection, transaction))
		{
			await command.ExecuteNonQueryAsync();
		}

		var tabla = new DataTable();

		tabla.Columns.Add("Id", typeof(int));
		tabla.Columns.Add("Nombre", typeof(string));
		tabla.Columns.Add("FechaRegistro", typeof(DateTime));

		foreach (var registro in registros)
		{
			tabla.Rows.Add(registro.Id, registro.Nombre, registro.FechaRegistro);
		}

		using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction))
		{
			bulkCopy.DestinationTableName = "#RegistrosNuevos";
			bulkCopy.ColumnMappings.Add("Id", "Id");
			bulkCopy.ColumnMappings.Add("Nombre", "Nombre");
			bulkCopy.ColumnMappings.Add("FechaRegistro", "FechaRegistro");

			await bulkCopy.WriteToServerAsync(tabla);
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

		return await insertCommand.ExecuteNonQueryAsync();
	}
}
