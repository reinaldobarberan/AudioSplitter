namespace AudioSplitter.Contracts;

/// <summary>Etapa actual de la corrida. Define el vocabulario de avance entre capas.</summary>
public enum EtapaProceso
{
    Metadatos,
    Extrayendo,
    Segmentando,
    Limpiando,
    Finalizado
}

/// <summary>
/// Evento de progreso: el contrato que atraviesa las tres capas.
/// Core observa, Domain publica (con intervalo mínimo acotado), UI pinta.
/// La UI nunca ve la salida cruda de FFmpeg: si necesita un dato nuevo, se agrega acá.
/// </summary>
public sealed record ProgresoProceso(
    EtapaProceso Etapa,
    int PorcentajeGlobal,
    int PorcentajeSegmento,
    int SegmentoActual,
    int TotalSegmentos,
    string Mensaje,
    /// <summary>
    /// Ruta del segmento que acaba de quedar escrito en disco, o null si este evento
    /// no cierra ninguno. Permite que el listado de la UI crezca en vivo.
    /// </summary>
    string? SegmentoListo = null);
