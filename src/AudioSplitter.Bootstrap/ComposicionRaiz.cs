using AudioSplitter.Contracts;
using AudioSplitter.Core;
using AudioSplitter.Domain;

namespace AudioSplitter.Bootstrap;

/// <summary>
/// El único lugar que conoce las cuatro capas a la vez. Existe porque Domain recibe la
/// implementación de Core sin conocerla, y la UI tampoco puede referenciar Core: hacía falta
/// un tercero que armara el grafo.
/// </summary>
public static class ComposicionRaiz
{
    /// <summary>Arma el servicio de procesamiento con las implementaciones reales de Core.</summary>
    public static IServicioProcesamiento CrearServicio() =>
        new ServicioProcesamiento(
            new LectorMetadatosFfmpeg(),
            new ExtractorAudioFfmpeg(),
            new SegmentadorAudioFfmpeg(),
            new SistemaArchivosReal());

    /// <summary>
    /// Envuelve el progreso de la UI con el acotador de frecuencia (250 ms).
    /// La UI no tiene por qué saber que existe.
    /// </summary>
    public static IProgress<ProgresoProceso> Acotar(IProgress<ProgresoProceso> destino) =>
        new ProgresoAcotado(destino);

    /// <summary>Verifica que FFmpeg esté disponible antes de habilitar el proceso.</summary>
    public static bool FfmpegDisponible(out string detalle)
    {
        try
        {
            detalle = LocalizadorFfmpeg.Ffmpeg;
            _ = LocalizadorFfmpeg.Ffprobe;
            return true;
        }
        catch (FileNotFoundException ex)
        {
            detalle = ex.Message;
            return false;
        }
    }
}
