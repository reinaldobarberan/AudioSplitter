using AudioSplitter.Contracts;

namespace AudioSplitter.Domain;

/// <summary>
/// El cerebro del proceso: ordena los pasos, verifica que se puedan dar y consolida el resultado.
/// No conoce FFmpeg. Recibe las implementaciones de Core por inyección, contra las interfaces
/// de Contracts, y por eso se prueba entero sin FFmpeg instalado.
/// </summary>
public sealed class ServicioProcesamiento(
    ILectorMetadatos lector,
    IExtractorAudio extractor,
    ISegmentadorAudio segmentador,
    ISistemaArchivos archivos) : IServicioProcesamiento
{
    private static readonly string[] FormatosSoportados =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts"];

    public async Task<ResultadoProceso> ProcesarAsync(
        PeticionProceso peticion, IProgress<ProgresoProceso>? progreso, CancellationToken ct)
    {
        var carpetaTemp = Path.Combine(peticion.CarpetaTrabajo, "temp");
        var carpetaSalida = Path.Combine(peticion.CarpetaTrabajo, "salida");
        var escritos = new List<SegmentoGenerado>();

        try
        {
            ct.ThrowIfCancellationRequested();

            if (Validar(peticion) is { } motivo)
                return ResultadoProceso.Fallo(motivo);

            archivos.CrearCarpeta(carpetaTemp);
            archivos.CrearCarpeta(carpetaSalida);

            // 1. Metadatos: sin la duración total no se puede calcular el plan ni el porcentaje.
            Publicar(progreso, EtapaProceso.Metadatos, 0, 0, 0, 0, "Leyendo metadatos del video…");
            var duracionTotal = await lector.ObtenerDuracionAsync(peticion.RutaVideo, ct);

            if (duracionTotal <= TimeSpan.Zero)
                return ResultadoProceso.Fallo(
                    "El video no tiene pista de audio o su duración es cero.");

            var plan = CalculadoraSegmentos.Calcular(duracionTotal, peticion.DuracionSegmento);

            // 2. Extracción del audio completo a la carpeta temporal.
            Publicar(progreso, EtapaProceso.Extrayendo, 0, 0, 0, plan.Count, "Extrayendo el audio…");
            var rutaAudio = await extractor.ExtraerAsync(
                peticion.RutaVideo, carpetaTemp, peticion.Formato, progreso, ct);

            // 3. Segmentación. Cada segmento se informa apenas queda escrito en disco.
            await foreach (var segmento in segmentador
                .SegmentarAsync(rutaAudio, carpetaSalida, plan, progreso, ct)
                .WithCancellation(ct))
            {
                escritos.Add(segmento);
                Publicar(progreso, EtapaProceso.Segmentando,
                    PorcentajeGlobal(escritos.Count, plan.Count), 100,
                    segmento.Numero, plan.Count,
                    $"Segmentando · parte {segmento.Numero} de {plan.Count}",
                    segmento.RutaArchivo);
            }

            // La limpieza ocurre ANTES de anunciar el cierre: el último evento que ve
            // el usuario es "Listo", no "Limpiando".
            Publicar(progreso, EtapaProceso.Limpiando, 100, 100, plan.Count, plan.Count,
                "Limpiando temporales…");
            LimpiarTemp(carpetaTemp);

            Publicar(progreso, EtapaProceso.Finalizado, 100, 100, plan.Count, plan.Count,
                $"Listo · {escritos.Count} segmentos generados");

            return ResultadoProceso.Ok(escritos);
        }
        catch (OperationCanceledException)
        {
            // Los segmentos ya escritos se conservan; solo se borra lo temporal.
            return ResultadoProceso.Fallo("El proceso fue cancelado.", escritos);
        }
        catch (Exception ex)
        {
            // La excepción técnica no llega nunca cruda a la pantalla: se traduce acá.
            return ResultadoProceso.Fallo(Traducir(ex), escritos);
        }
        finally
        {
            // Red de seguridad: lo temporal se limpia igual ante error o cancelación.
            // Es idempotente, así que repetirlo tras una corrida feliz no molesta.
            LimpiarTemp(carpetaTemp);
        }
    }

    /// <summary>La limpieza nunca tumba la corrida: si falla, el resultado ya está decidido.</summary>
    private void LimpiarTemp(string carpetaTemp)
    {
        try { archivos.EliminarCarpetaSiExiste(carpetaTemp); }
        catch { /* intencionalmente silencioso */ }
    }

    private string? Validar(PeticionProceso p)
    {
        if (string.IsNullOrWhiteSpace(p.RutaVideo))
            return "No se seleccionó ningún archivo de video.";

        if (!archivos.ArchivoExiste(p.RutaVideo))
            return "El archivo de video no existe o no se puede leer.";

        var extension = Path.GetExtension(p.RutaVideo);
        if (!FormatosSoportados.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return $"Formato de video no soportado: {extension}. " +
                   $"Se admiten: {string.Join(", ", FormatosSoportados)}.";

        if (string.IsNullOrWhiteSpace(p.CarpetaTrabajo))
            return "No se seleccionó una carpeta de trabajo.";

        if (p.DuracionSegmento <= TimeSpan.Zero)
            return "La duración de segmento debe ser mayor a cero.";

        if (!archivos.PuedeEscribir(p.CarpetaTrabajo))
            return "No hay permiso de escritura en la carpeta de trabajo.";

        // Estimación conservadora: el audio extraído más los segmentos no superan el tamaño del video.
        var requerido = archivos.TamanoArchivo(p.RutaVideo);
        if (archivos.EspacioLibre(p.CarpetaTrabajo) < requerido)
            return "Espacio insuficiente en disco para completar el proceso.";

        return null;
    }

    private static string Traducir(Exception ex) => ex switch
    {
        FileNotFoundException => "No se encontró FFmpeg. Verificá la instalación de la aplicación.",
        UnauthorizedAccessException => "No hay permiso para escribir en la carpeta de trabajo.",
        IOException => "Error de disco al escribir los archivos de salida.",
        _ => "No se pudo completar el proceso. Revisá el video de entrada y volvé a intentar."
    };

    private static int PorcentajeGlobal(int hechos, int total) =>
        total <= 0 ? 0 : (int)Math.Round(hechos * 100.0 / total);

    private static void Publicar(
        IProgress<ProgresoProceso>? progreso, EtapaProceso etapa,
        int global, int segmento, int actual, int total, string mensaje,
        string? segmentoListo = null) =>
        progreso?.Report(new ProgresoProceso(
            etapa, global, segmento, actual, total, mensaje, segmentoListo));
}
