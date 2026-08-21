namespace SincronizadorBD.Models;

public class Registro
{
	public int Id { get; set; }
	public string Nombre { get; set; } = string.Empty;
	public DateTime FechaRegistro { get; set; }
}