namespace AudioSplitter.Contracts;

/// <summary>
/// Segmento planificado, todavía no escrito. Lo calcula Domain (la regla de los 30 minutos)
/// y lo ejecuta Core: así la regla vive en un solo lugar.
/// </summary>
public sealed record SegmentoPlanificado(
    int Numero,
    TimeSpan Inicio,
    TimeSpan Duracion,
    string NombreBase);

/// <summary>Descripción de un segmento efectivamente escrito en disco.</summary>
public sealed record SegmentoGenerado(
    int Numero,
    string RutaArchivo,
    TimeSpan Inicio,
    TimeSpan Duracion);

/// <summary>
/// Resultado consolidado de la corrida. Define la forma del fallo para las tres capas:
/// el motivo es de negocio, ya traducido por Domain. La excepción técnica nunca llega acá cruda.
/// </summary>
public sealed record ResultadoProceso(
    bool Exitoso,
    IReadOnlyList<SegmentoGenerado> Segmentos,
    string? MotivoFallo)
{
    public static ResultadoProceso Ok(IReadOnlyList<SegmentoGenerado> segmentos) =>
        new(true, segmentos, null);

    /// <summary>Fallo o cancelación. Los segmentos ya escritos se conservan y se informan.</summary>
    public static ResultadoProceso Fallo(string motivo, IReadOnlyList<SegmentoGenerado>? parciales = null) =>
        new(false, parciales ?? Array.Empty<SegmentoGenerado>(), motivo);
}
