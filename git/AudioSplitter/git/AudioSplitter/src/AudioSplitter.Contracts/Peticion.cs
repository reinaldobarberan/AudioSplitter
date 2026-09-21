namespace AudioSplitter.Contracts;

/// <summary>
/// Formato de los archivos de salida.
/// </summary>
/// <remarks>
/// <see cref="M4a"/> suele ser copia directa del stream: la mayoría de los contenedores de
/// video traen AAC, así que no se recodifica y no hay pérdida.
/// <see cref="Mp3"/> obliga siempre a recodificar, con pérdida generacional sobre un audio
/// que ya venía comprimido. Se elige por compatibilidad, no por calidad.
/// </remarks>
public enum FormatoAudio
{
    M4a,
    Mp3
}

public static class FormatoAudioExtensiones
{
    /// <summary>Extensión del archivo de salida. Es contrato, no detalle de FFmpeg.</summary>
    public static string Extension(this FormatoAudio formato) => formato switch
    {
        FormatoAudio.M4a => ".m4a",
        FormatoAudio.Mp3 => ".mp3",
        _ => throw new ArgumentOutOfRangeException(nameof(formato), formato, "Formato no soportado.")
    };
}

/// <summary>
/// Petición de proceso. La duración de segmento y el formato viajan acá: son parámetros,
/// no constantes escondidas. La carpeta de trabajo se resuelve en ejecución.
/// </summary>
public sealed record PeticionProceso(
    string RutaVideo,
    string CarpetaTrabajo,
    TimeSpan DuracionSegmento,
    FormatoAudio Formato = FormatoAudio.M4a)
{
    public static readonly TimeSpan DuracionSegmentoPorDefecto = TimeSpan.FromMinutes(30);
}
