using System.Diagnostics;

namespace AudioSplitter.Core;

/// <summary>
/// Resuelve dónde vive FFmpeg. Decisión de arquitectura: se empaqueta junto a la aplicación,
/// así que la carpeta del ejecutable manda. Se cae a PATH para que el proyecto corra en
/// desarrollo sin tener que copiar el binario a mano.
/// </summary>
public static class LocalizadorFfmpeg
{
    public static string Ffmpeg => Resolver("ffmpeg");
    public static string Ffprobe => Resolver("ffprobe");

    private static string Resolver(string nombre)
    {
        var exe = OperatingSystem.IsWindows() ? nombre + ".exe" : nombre;

        var carpetaApp = AppContext.BaseDirectory;
        foreach (var candidato in new[]
                 {
                     Path.Combine(carpetaApp, exe),
                     Path.Combine(carpetaApp, "ffmpeg", exe),
                     Path.Combine(carpetaApp, "runtimes", "ffmpeg", exe)
                 })
        {
            if (File.Exists(candidato)) return candidato;
        }

        if (EnPath(exe)) return exe;

        throw new FileNotFoundException(
            $"No se encontró {exe} junto a la aplicación ni en el PATH del sistema.", exe);
    }

    private static bool EnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator)
                   .Any(d => !string.IsNullOrWhiteSpace(d) && File.Exists(Path.Combine(d.Trim(), exe)));
    }
}

/// <summary>Salida cruda de una corrida de FFmpeg. No sale de Core.</summary>
internal sealed record ResultadoProceso(int CodigoSalida, string Salida, string Error);

/// <summary>
/// Lanza FFmpeg sin ventana, con redirección de salida, y espera su término de forma asíncrona.
/// Interpreta `-progress pipe:1` para traducir el avance a porcentaje.
/// </summary>
internal static class ProcesoFfmpeg
{
    public static async Task<ResultadoProceso> EjecutarAsync(
        string exe,
        string argumentos,
        TimeSpan? duracionTotal,
        Action<double>? avance,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, argumentos)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proceso = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var salida = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();

        proceso.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            salida.AppendLine(e.Data);
            if (duracionTotal is { } total && avance is not null)
                InterpretarAvance(e.Data, total, avance);
        };
        proceso.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };

        proceso.Start();
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();

        try
        {
            await proceso.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Cancelación: se corta el proceso para no dejarlo huérfano escribiendo en disco.
            try { if (!proceso.HasExited) proceso.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return new ResultadoProceso(proceso.ExitCode, salida.ToString(), error.ToString());
    }

    /// <summary>`-progress pipe:1` emite `out_time_us=` con el tiempo de audio ya procesado.</summary>
    private static void InterpretarAvance(string linea, TimeSpan total, Action<double> avance)
    {
        const string clave = "out_time_us=";
        if (!linea.StartsWith(clave, StringComparison.Ordinal)) return;

        if (!long.TryParse(linea.AsSpan(clave.Length), out var microsegundos) || microsegundos < 0) return;
        if (total.TotalMicroseconds <= 0) return;

        avance(Math.Clamp(microsegundos / total.TotalMicroseconds, 0d, 1d));
    }
}
