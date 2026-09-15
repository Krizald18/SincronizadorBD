namespace SincronizadorBD.Modelos;

public class EstadoSincronizacionLocal
{
    public bool CargaHistoricaTerminada { get; set; }
    public string? UltimoFolioSeguimientoHistorico { get; set; }
    public string? UltimoFolioControlEstadoHistorico { get; set; }
    public long UltimoFolioDescubierto { get; set; }
}
