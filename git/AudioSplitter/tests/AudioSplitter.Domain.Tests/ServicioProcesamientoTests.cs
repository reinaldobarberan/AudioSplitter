using AudioSplitter.Contracts;

namespace AudioSplitter.Domain.Tests;

public class ServicioProcesamientoTests
{
    private const string Video = @"C:\videos\reunion.mp4";
    private const string Trabajo = @"D:\audio\salida";

    private static PeticionProceso Peticion(string video = Video, string trabajo = Trabajo, int minutos = 30)
        => new(video, trabajo, TimeSpan.FromMinutes(minutos));

    private static ArchivosFalsos ArchivosCon(string video = Video) => new() { Existentes = { video } };

    private static ServicioProcesamiento Servicio(
        ILectorMetadatos? lector = null, IExtractorAudio? extractor = null,
        ISegmentadorAudio? segmentador = null, ISistemaArchivos? archivos = null)
        => new(lector ?? new LectorFalso(), extractor ?? new ExtractorFalso(),
               segmentador ?? new SegmentadorFalso(), archivos ?? ArchivosCon());

    // ---------- Validación: Domain corta antes de molestar a Core ----------

    [Fact]
    public async Task VideoInexistente_FallaSinLlamarACore()
    {
        var lector = new LectorFalso();
        var extractor = new ExtractorFalso();
        var r = await Servicio(lector, extractor, archivos: new ArchivosFalsos()).ProcesarAsync(Peticion(), null, default);

        Assert.False(r.Exitoso);
        Assert.Contains("no existe", r.MotivoFallo);
        Assert.Equal(0, lector.Llamadas);
        Assert.Equal(0, extractor.Llamadas);
    }

    [Fact]
    public async Task FormatoNoSoportado_Falla()
    {
        const string txt = @"C:\videos\notas.txt";
        var r = await Servicio(archivos: ArchivosCon(txt)).ProcesarAsync(Peticion(video: txt), null, default);

        Assert.False(r.Exitoso);
        Assert.Contains("soportado", r.MotivoFallo);
    }

    [Fact]
    public async Task CarpetaNoEscribible_Falla()
    {
        var archivos = new ArchivosFalsos { Existentes = { Video }, Escribible = false };
        var r = await Servicio(archivos: archivos).ProcesarAsync(Peticion(), null, default);

        Assert.False(r.Exitoso);
        Assert.Contains("escritura", r.MotivoFallo);
    }

    [Fact]
    public async Task EspacioInsuficiente_Falla()
    {
        var archivos = new ArchivosFalsos { Existentes = { Video }, Libre = 1024, Tamano = 500L * 1024 * 1024 };
        var r = await Servicio(archivos: archivos).ProcesarAsync(Peticion(), null, default);

        Assert.False(r.Exitoso);
        Assert.Contains("spacio", r.MotivoFallo);
    }

    [Fact]
    public async Task DuracionDeSegmentoNoPositiva_Falla()
    {
        var r = await Servicio().ProcesarAsync(Peticion(minutos: 0), null, default);

        Assert.False(r.Exitoso);
        Assert.Contains("segmento", r.MotivoFallo);
    }

    [Fact]
    public async Task VideoSinDuracion_Falla()
    {
        var lector = new LectorFalso { Duracion = TimeSpan.Zero };
        var r = await Servicio(lector).ProcesarAsync(Peticion(), null, default);

        Assert.False(r.Exitoso);
        Assert.Contains("duración", r.MotivoFallo);
    }

    // ---------- Corrida feliz ----------

    [Fact]
    public async Task CorridaExitosa_DevuelveLosCuatroSegmentosDelEjemplo()
    {
        var r = await Servicio().ProcesarAsync(Peticion(), null, default);

        Assert.True(r.Exitoso);
        Assert.Null(r.MotivoFallo);
        Assert.Equal(4, r.Segmentos.Count);
        Assert.Equal(TimeSpan.FromMinutes(5), r.Segmentos[^1].Duracion);
    }

    [Fact]
    public async Task CorridaExitosa_CreaLaEstructuraDeCarpetas()
    {
        var archivos = ArchivosCon();
        await Servicio(archivos: archivos).ProcesarAsync(Peticion(), null, default);

        Assert.Contains(archivos.Creadas, c => c.EndsWith("temp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archivos.Creadas, c => c.EndsWith("salida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CorridaExitosa_LimpiaLoTemporal()
    {
        var archivos = ArchivosCon();
        await Servicio(archivos: archivos).ProcesarAsync(Peticion(), null, default);

        Assert.Contains(archivos.Eliminadas, e => e.EndsWith("temp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CorridaExitosa_PublicaProgresoYTerminaEnCien()
    {
        var vistos = new List<ProgresoProceso>();
        var progreso = new Progress<ProgresoProceso>(vistos.Add);

        var r = await Servicio().ProcesarAsync(Peticion(), progreso, default);

        Assert.True(r.Exitoso);
        await Task.Delay(50); // Progress<T> despacha de forma asíncrona
        Assert.NotEmpty(vistos);
        Assert.Equal(EtapaProceso.Finalizado, vistos[^1].Etapa);
        Assert.Equal(100, vistos[^1].PorcentajeGlobal);
    }

    // ---------- Errores y cancelación ----------

    [Fact]
    public async Task ExcepcionTecnicaDeCore_SeTraduce_NoSePropaga()
    {
        var extractor = new ExtractorFalso { Explota = new InvalidOperationException("ffmpeg exit code 1") };

        var r = await Servicio(extractor: extractor).ProcesarAsync(Peticion(), null, default);

        Assert.False(r.Exitoso);
        Assert.NotNull(r.MotivoFallo);
        // El motivo es de negocio: la salida cruda de FFmpeg no llega a la pantalla.
        Assert.DoesNotContain("exit code", r.MotivoFallo);
    }

    [Fact]
    public async Task ErrorDeCore_LimpiaLoTemporalIgual()
    {
        var archivos = ArchivosCon();
        var extractor = new ExtractorFalso { Explota = new InvalidOperationException("boom") };

        await Servicio(extractor: extractor, archivos: archivos).ProcesarAsync(Peticion(), null, default);

        Assert.Contains(archivos.Eliminadas, e => e.EndsWith("temp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancelacion_ConservaLosSegmentosYaEscritos()
    {
        var segmentador = new SegmentadorFalso
        {
            EmitirAntesDeExplotar = 2,
            Explota = new OperationCanceledException()
        };

        var r = await Servicio(segmentador: segmentador).ProcesarAsync(Peticion(), null, default);

        Assert.False(r.Exitoso);
        Assert.Contains("ancel", r.MotivoFallo);
        Assert.Equal(2, r.Segmentos.Count);   // los dos ya escritos sobreviven
    }

    [Fact]
    public async Task CancelacionPrevia_NoLlamaACore()
    {
        var lector = new LectorFalso();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var r = await Servicio(lector).ProcesarAsync(Peticion(), null, cts.Token);

        Assert.False(r.Exitoso);
        Assert.Equal(0, lector.Llamadas);
    }

    // ---------- Formato de salida ----------

    [Theory]
    [InlineData(FormatoAudio.M4a, ".m4a")]
    [InlineData(FormatoAudio.Mp3, ".mp3")]
    public async Task ElFormatoElegido_LlegaHastaLosArchivosDeSalida(FormatoAudio formato, string extension)
    {
        var extractor = new ExtractorFalso();
        var peticion = new PeticionProceso(Video, Trabajo, TimeSpan.FromMinutes(30), formato);

        var r = await Servicio(extractor: extractor).ProcesarAsync(peticion, null, default);

        Assert.True(r.Exitoso, r.MotivoFallo);
        Assert.Equal(formato, extractor.FormatoPedido);
        Assert.All(r.Segmentos, s => Assert.EndsWith(extension, s.RutaArchivo));
    }

    [Fact]
    public async Task SinFormatoExplicito_ElDefectoEsM4a()
    {
        var extractor = new ExtractorFalso();

        await Servicio(extractor: extractor).ProcesarAsync(Peticion(), null, default);

        // m4a por defecto: es el único que puede copiarse sin recodificar.
        Assert.Equal(FormatoAudio.M4a, extractor.FormatoPedido);
    }

    [Fact]
    public async Task ElNombreDeLosSegmentos_NoCambiaConElFormato()
    {
        var mp3 = await Servicio().ProcesarAsync(
            new PeticionProceso(Video, Trabajo, TimeSpan.FromMinutes(30), FormatoAudio.Mp3), null, default);

        Assert.Equal(
            ["audio_parte_001", "audio_parte_002", "audio_parte_003", "audio_parte_004"],
            mp3.Segmentos.Select(s => Path.GetFileNameWithoutExtension(s.RutaArchivo)));
    }
}
