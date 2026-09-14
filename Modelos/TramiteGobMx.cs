namespace SincronizadorBD.Modelos;

public class TramiteGobMx
{
    public string FolioSeguimiento { get; set; } = string.Empty;
    public string FolioControlEstado { get; set; } = string.Empty;

    public string? IdTramite { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public DateTime? Fecha { get; set; }
    public string? Estatus { get; set; }
    public string? OrigenJson { get; set; }
    public decimal? ImportePagado { get; set; }
    public string? LineaCaptura { get; set; }
    public string? ReferenciaPago { get; set; }
    public DateTime? FechaPago { get; set; }
    public string? NumeroAutorizacion { get; set; }
    public string? CodigoBarras { get; set; }
    public string? Nombre { get; set; }
}
