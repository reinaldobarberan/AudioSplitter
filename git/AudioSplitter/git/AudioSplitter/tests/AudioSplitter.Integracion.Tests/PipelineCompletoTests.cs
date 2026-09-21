using System.Diagnostics;
using AudioSplitter.Bootstrap;
using AudioSplitter.Contracts;

namespace AudioSplitter.Integracion.Tests;

/// <summary>
/// Prueba el pipeline real de punta a punta: FFmpeg incluido. Genera su propio video
/// sintético, así que no depende de archivos externos. Si FFmpeg no está disponible,
/// los tests se saltan en lugar de fallar.
/// </summary>
public sealed class PipelineCompletoTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(), "audiosplitter_it_" + Guid.NewGuid().ToString("N")[..8]);

    public PipelineCompletoTests() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); } catch { /* best effort */ }
    }

    [SkippableFact]
    public async Task VideoDeSesentaYCincoSegundos_SeParteEnTresBloquesDeTreinta()
    {
        Skip.IfNot(ComposicionRaiz.FfmpegDisponible(out _), "FFmpeg no disponible.");

        var video = await GenerarVideoAsync(segundos: 65);

        var servicio = ComposicionRaiz.CrearServicio();
        var vistos = new List<ProgresoProceso>();
        var progreso = new Progress<ProgresoProceso>(vistos.Add);

        var resultado = await servicio.ProcesarAsync(
            new PeticionProceso(video, _carpeta, TimeSpan.FromSeconds(30)), progreso, default);

        Assert.True(resultado.Exitoso, resultado.MotivoFallo);
        Assert.Equal(3, resultado.Segmentos.Count);

        // Los archivos existen de verdad y tienen contenido.
        foreach (var s in resultado.Segmentos)
        {
            Assert.True(File.Exists(s.RutaArchivo), "Falta " + s.RutaArchivo);
            Assert.True(new FileInfo(s.RutaArchivo).Length > 0, "Vacío " + s.RutaArchivo);
        }

        // Numeración con ceros, en orden.
        Assert.Equal(
            ["audio_parte_001", "audio_parte_002", "audio_parte_003"],
            resultado.Segmentos.Select(s => Path.GetFileNameWithoutExtension(s.RutaArchivo)));

        // El último conserva el resto, no se rellena a 30 s.
        Assert.Equal(TimeSpan.FromSeconds(5), resultado.Segmentos[^1].Duracion);

        // Lo temporal quedó limpio; la salida no.
        Assert.False(Directory.Exists(Path.Combine(_carpeta, "temp")));
        Assert.True(Directory.Exists(Path.Combine(_carpeta, "salida")));
    }

    [SkippableFact]
    public async Task ArchivoQueNoEsVideo_DevuelveFalloDeNegocio_NoExcepcion()
    {
        Skip.IfNot(ComposicionRaiz.FfmpegDisponible(out _), "FFmpeg no disponible.");

        var falso = Path.Combine(_carpeta, "roto.mp4");
        await File.WriteAllTextAsync(falso, "esto no es un video");

        var resultado = await ComposicionRaiz.CrearServicio().ProcesarAsync(
            new PeticionProceso(falso, _carpeta, TimeSpan.FromSeconds(30)), null, default);

        Assert.False(resultado.Exitoso);
        Assert.NotNull(resultado.MotivoFallo);
        // La salida cruda de FFmpeg no llega a la pantalla.
        Assert.DoesNotContain("Invalid data", resultado.MotivoFallo);
    }

    /// <summary>Video sintético con pista de audio, generado por FFmpeg.</summary>
    private async Task<string> GenerarVideoAsync(int segundos)
    {
        var destino = Path.Combine(_carpeta, "muestra.mp4");

        var args =
            $"-y -f lavfi -i testsrc=duration={segundos}:size=160x120:rate=5 " +
            $"-f lavfi -i sine=frequency=440:duration={segundos} " +
            $"-c:v libx264 -preset ultrafast -c:a aac -shortest \"{destino}\"";

        var psi = new ProcessStartInfo("ffmpeg", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var p = Process.Start(psi)!;
        var error = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        Assert.True(p.ExitCode == 0 && File.Exists(destino),
            "No se pudo generar el video de prueba: " + error);

        return destino;
    }

    [SkippableTheory]
    [InlineData(FormatoAudio.M4a, ".m4a")]
    [InlineData(FormatoAudio.Mp3, ".mp3")]
    public async Task ElFormatoElegido_ProduceArchivosRealesDeEseTipo(FormatoAudio formato, string extension)
    {
        Skip.IfNot(ComposicionRaiz.FfmpegDisponible(out _), "FFmpeg no disponible.");

        var video = await GenerarVideoAsync(segundos: 40);

        var resultado = await ComposicionRaiz.CrearServicio().ProcesarAsync(
            new PeticionProceso(video, _carpeta, TimeSpan.FromSeconds(30), formato), null, default);

        Assert.True(resultado.Exitoso, resultado.MotivoFallo);
        Assert.Equal(2, resultado.Segmentos.Count);

        foreach (var s in resultado.Segmentos)
        {
            Assert.EndsWith(extension, s.RutaArchivo);
            Assert.True(File.Exists(s.RutaArchivo), "Falta " + s.RutaArchivo);
            Assert.True(new FileInfo(s.RutaArchivo).Length > 0, "Vacio " + s.RutaArchivo);
        }

        // No alcanza con que la extension coincida: el contenido tiene que ser audio legible.
        var duracion = await DuracionAsync(resultado.Segmentos[0].RutaArchivo);
        Assert.InRange(duracion.TotalSeconds, 29, 31);
    }

    /// <summary>Lee la duracion real del archivo generado, con ffprobe.</summary>
    private static async Task<TimeSpan> DuracionAsync(string ruta)
    {
        var psi = new ProcessStartInfo("ffprobe",
            "-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"" + ruta + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var p = Process.Start(psi)!;
        var salida = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();

        return TimeSpan.FromSeconds(double.Parse(
            salida.Trim(), System.Globalization.CultureInfo.InvariantCulture));
    }
}
