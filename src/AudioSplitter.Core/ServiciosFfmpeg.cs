using System.Globalization;
using System.Runtime.CompilerServices;
using AudioSplitter.Contracts;

namespace AudioSplitter.Core;

/// <summary>Lee la duración del audio con ffprobe. Sin ella no hay plan ni porcentaje.</summary>
public sealed class LectorMetadatosFfmpeg : ILectorMetadatos
{
    public async Task<TimeSpan> ObtenerDuracionAsync(string rutaVideo, CancellationToken ct)
    {
        var args = "-v error -show_entries format=duration " +
                   $"-of default=noprint_wrappers=1:nokey=1 \"{rutaVideo}\"";

        var r = await ProcesoFfmpeg.EjecutarAsync(LocalizadorFfmpeg.Ffprobe, args, null, null, ct);

        if (r.CodigoSalida != 0)
            throw new InvalidOperationException($"ffprobe falló ({r.CodigoSalida}): {r.Error}");

        var texto = r.Salida.Trim();
        return double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var segundos)
            ? TimeSpan.FromSeconds(segundos)
            : TimeSpan.Zero;
    }
}

/// <summary>
/// Separa la pista de audio del contenedor. Intenta copiar sin recodificar; si el códec de
/// origen no entra en el contenedor de salida, recodifica a AAC.
/// </summary>
public sealed class ExtractorAudioFfmpeg : IExtractorAudio
{
    public async Task<string> ExtraerAsync(
        string rutaVideo, string carpetaTemp, FormatoAudio formato,
        IProgress<ProgresoProceso>? progreso, CancellationToken ct)
    {
        Directory.CreateDirectory(carpetaTemp);
        var destino = Path.Combine(carpetaTemp, "audio_completo" + formato.Extension());

        void Avance(double fraccion) => progreso?.Report(new ProgresoProceso(
            EtapaProceso.Extrayendo, (int)(fraccion * 100), (int)(fraccion * 100), 0, 0,
            "Extrayendo el audio…"));

        // Camino rápido: copiar la pista tal cual, sin recodificar. Funciona cuando el códec
        // de origen ya entra en el contenedor pedido (lo habitual con AAC hacia .m4a).
        if (await IntentarAsync(rutaVideo, destino, "-c:a copy", Avance, ct))
            return destino;

        // El códec no entra: se recodifica UNA sola vez, acá. Los segmentos salen después
        // por copia, así no se paga el costo N veces.
        var r = await ProcesoFfmpeg.EjecutarAsync(
            LocalizadorFfmpeg.Ffmpeg,
            Argumentos(rutaVideo, destino, Codificador(formato)),
            null, Avance, ct);

        if (r.CodigoSalida != 0)
            throw new InvalidOperationException(
                $"ffmpeg falló al extraer ({r.CodigoSalida}): {r.Error}");

        return destino;
    }

    private static async Task<bool> IntentarAsync(
        string origen, string destino, string codec, Action<double> avance, CancellationToken ct)
    {
        var r = await ProcesoFfmpeg.EjecutarAsync(
            LocalizadorFfmpeg.Ffmpeg, Argumentos(origen, destino, codec), null, avance, ct);

        return r.CodigoSalida == 0 && File.Exists(destino) && new FileInfo(destino).Length > 0;
    }

    private static string Argumentos(string origen, string destino, string codec) =>
        $"-y -nostats -progress pipe:1 -i \"{origen}\" -vn {codec} \"{destino}\"";

    /// <summary>Códec de recodificación por formato. Es conocimiento de FFmpeg: vive solo acá.</summary>
    private static string Codificador(FormatoAudio formato) => formato switch
    {
        FormatoAudio.M4a => "-c:a aac -b:a 192k",
        FormatoAudio.Mp3 => "-c:a libmp3lame -b:a 192k",
        _ => throw new ArgumentOutOfRangeException(nameof(formato), formato, "Formato no soportado.")
    };
}

/// <summary>
/// Corta el audio en bloques secuenciales. Emite cada segmento apenas queda escrito en disco,
/// para que la UI vea crecer el listado y una cancelación conserve lo ya confirmado.
/// </summary>
public sealed class SegmentadorAudioFfmpeg : ISegmentadorAudio
{
    public async IAsyncEnumerable<SegmentoGenerado> SegmentarAsync(
        string rutaAudio, string carpetaSalida, IReadOnlyList<SegmentoPlanificado> plan,
        IProgress<ProgresoProceso>? progreso, [EnumeratorCancellation] CancellationToken ct)
    {
        Directory.CreateDirectory(carpetaSalida);
        var extension = Path.GetExtension(rutaAudio);

        foreach (var (numero, inicio, duracion, nombre) in plan)
        {
            ct.ThrowIfCancellationRequested();

            var destino = Path.Combine(carpetaSalida, nombre + extension);

            var args = $"-y -nostats -progress pipe:1 " +
                       $"-ss {Formatear(inicio)} -t {Formatear(duracion)} " +
                       $"-i \"{rutaAudio}\" -c copy \"{destino}\"";

            var r = await ProcesoFfmpeg.EjecutarAsync(
                LocalizadorFfmpeg.Ffmpeg, args, duracion,
                f => progreso?.Report(new ProgresoProceso(
                    EtapaProceso.Segmentando,
                    (int)((numero - 1 + f) * 100 / plan.Count), (int)(f * 100),
                    numero, plan.Count, $"Segmentando · parte {numero} de {plan.Count}")),
                ct);

            if (r.CodigoSalida != 0)
                throw new InvalidOperationException($"ffmpeg falló en el segmento {numero} ({r.CodigoSalida}): {r.Error}");

            yield return new SegmentoGenerado(numero, destino, inicio, duracion);
        }
    }

    private static string Formatear(TimeSpan t) =>
        t.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}

/// <summary>Implementación real del puerto de sistema de archivos.</summary>
public sealed class SistemaArchivosReal : ISistemaArchivos
{
    public bool ArchivoExiste(string ruta) => File.Exists(ruta);

    public long TamanoArchivo(string ruta) => new FileInfo(ruta).Length;

    public void CrearCarpeta(string ruta) => Directory.CreateDirectory(ruta);

    public bool PuedeEscribir(string carpeta)
    {
        try
        {
            Directory.CreateDirectory(carpeta);
            var testigo = Path.Combine(carpeta, $".escritura_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testigo, string.Empty);
            File.Delete(testigo);
            return true;
        }
        catch { return false; }
    }

    public long EspacioLibre(string carpeta)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(carpeta))!).AvailableFreeSpace; }
        catch { return long.MaxValue; }
    }

    public void EliminarCarpetaSiExiste(string ruta)
    {
        if (Directory.Exists(ruta)) Directory.Delete(ruta, recursive: true);
    }
}
