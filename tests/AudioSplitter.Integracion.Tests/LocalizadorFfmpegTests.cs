using AudioSplitter.Core;

namespace AudioSplitter.Integracion.Tests;

/// <summary>
/// El mensaje de "no se encontró FFmpeg" es lo único que ve un usuario cuando la
/// instalación está mal. Si ese texto es un muro ilegible, el error deja de ser accionable.
/// </summary>
public class LocalizadorFfmpegTests
{
    [Fact]
    public void LasRutasCandidatas_NoSeRepiten()
    {
        var rutas = LocalizadorFfmpeg.RutasCandidatas("ffmpeg");

        // El PATH del proceso y el del registro se solapan casi por completo: sin
        // deduplicar, el error listaba la misma carpeta tres o cuatro veces.
        Assert.Equal(rutas.Count, rutas.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void LaCarpetaDeLaAplicacion_SeBuscaPrimero()
    {
        var rutas = LocalizadorFfmpeg.RutasCandidatas("ffmpeg");

        // Es la decisión de arquitectura: lo empaquetado gana sobre lo que haya en la máquina.
        Assert.StartsWith(AppContext.BaseDirectory, rutas[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConMuchasRutas_ElMensajeSeRecorta()
    {
        var muchas = Enumerable.Range(1, 60).Select(i => $@"C:\carpeta{i}\ffmpeg.exe").ToList();

        var mensaje = LocalizadorFfmpeg.Explicar("ffmpeg.exe", muchas);

        Assert.Contains("Se buscó en 60 ubicaciones", mensaje);
        Assert.Contains("y 52 más del PATH", mensaje);

        // Un PATH de máquina de trabajo tiene decenas de entradas: el diálogo no puede
        // crecer sin límite ni desbordar la pantalla.
        Assert.True(mensaje.Split('\n').Length < 20, "El mensaje quedó demasiado largo.");
        Assert.DoesNotContain(@"C:\carpeta60\", mensaje);
    }

    [Fact]
    public void ConPocasRutas_SeListanTodas()
    {
        var pocas = new List<string> { @"C:\app\ffmpeg.exe", @"C:\otra\ffmpeg.exe" };

        var mensaje = LocalizadorFfmpeg.Explicar("ffmpeg.exe", pocas);

        Assert.Contains(@"C:\app\ffmpeg.exe", mensaje);
        Assert.Contains(@"C:\otra\ffmpeg.exe", mensaje);
        Assert.DoesNotContain("más del PATH", mensaje);
    }

    [Fact]
    public void ElMensaje_DiceQueHacer_AntesDeDondeBusco()
    {
        var mensaje = LocalizadorFfmpeg.Explicar("ffmpeg.exe", [@"C:\app\ffmpeg.exe"]);

        var solucion = mensaje.IndexOf("Copiá ffmpeg.exe", StringComparison.Ordinal);
        var listado = mensaje.IndexOf("Se buscó en", StringComparison.Ordinal);

        Assert.True(solucion >= 0, "El mensaje no dice cómo solucionarlo.");
        Assert.True(solucion < listado, "La solución tiene que ir antes que el listado de rutas.");
        Assert.Contains("PATH del sistema", mensaje);
    }
}
