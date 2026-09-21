namespace AudioSplitter.Contracts;

/// <summary>
/// Lee metadatos del video. Implementado en Core (FFmpeg); Domain lo consume sin conocerlo.
/// </summary>
public interface ILectorMetadatos
{
    Task<TimeSpan> ObtenerDuracionAsync(string rutaVideo, CancellationToken ct);
}

/// <summary>
/// Separa la pista de audio del contenedor. Recibe rutas absolutas ya validadas por Domain.
/// Devuelve la ruta del audio completo en la carpeta temporal.
/// </summary>
public interface IExtractorAudio
{
    Task<string> ExtraerAsync(
        string rutaVideo,
        string carpetaTemp,
        FormatoAudio formato,
        IProgress<ProgresoProceso>? progreso,
        CancellationToken ct);
}

/// <summary>
/// Corta el audio en bloques secuenciales de duración fija. El corte es por tiempo, no por tamaño.
/// Emite cada segmento apenas termina de escribirse en disco: así el listado de la UI crece
/// en vivo y, ante una cancelación, Domain conserva los parciales ya confirmados.
/// </summary>
public interface ISegmentadorAudio
{
    IAsyncEnumerable<SegmentoGenerado> SegmentarAsync(
        string rutaAudio,
        string carpetaSalida,
        IReadOnlyList<SegmentoPlanificado> plan,
        IProgress<ProgresoProceso>? progreso,
        CancellationToken ct);
}

/// <summary>
/// Puerto de sistema de archivos. Existe para que Domain valide permisos y espacio
/// sin tocar disco en los tests.
/// </summary>
public interface ISistemaArchivos
{
    bool ArchivoExiste(string ruta);
    long TamanoArchivo(string ruta);
    void CrearCarpeta(string ruta);
    bool PuedeEscribir(string carpeta);
    long EspacioLibre(string carpeta);
    void EliminarCarpetaSiExiste(string ruta);
}

/// <summary>
/// Punto de entrada del dominio. Es lo único que la UI conoce del proceso.
/// Nunca lanza excepciones técnicas: devuelve un resultado con motivo de negocio.
/// </summary>
public interface IServicioProcesamiento
{
    Task<ResultadoProceso> ProcesarAsync(
        PeticionProceso peticion,
        IProgress<ProgresoProceso>? progreso,
        CancellationToken ct);
}
