using System.Diagnostics;

namespace AudioSplitter.Core;

/// <summary>
/// Resuelve dónde vive FFmpeg. Decisión de arquitectura: se empaqueta junto a la aplicación,
/// así que la carpeta del ejecutable manda. El PATH queda como respaldo para desarrollo.
/// </summary>
public static class LocalizadorFfmpeg
{
    public static string Ffmpeg => Resolver("ffmpeg");
    public static string Ffprobe => Resolver("ffprobe");

    private static string Resolver(string nombre)
    {
        var exe = OperatingSystem.IsWindows() ? nombre + ".exe" : nombre;
        var buscados = new List<string>();

        foreach (var carpeta in CarpetasCandidatas())
        {
            if (string.IsNullOrWhiteSpace(carpeta)) continue;

            string candidato;
            try { candidato = Path.Combine(carpeta.Trim().Trim('"'), exe); }
            catch (ArgumentException) { continue; }   // entradas de PATH con caracteres inválidos

            buscados.Add(candidato);
            if (File.Exists(candidato)) return candidato;
        }

        throw new FileNotFoundException(
            $"No se encontró {exe}.\n\nSe buscó en:\n" +
            string.Join("\n", buscados.Select(b => "  · " + b)),
            exe);
    }

    private static IEnumerable<string> CarpetasCandidatas()
    {
        // 1. Junto a la aplicación: es donde queda al empaquetar, y no depende del entorno.
        var app = AppContext.BaseDirectory;
        yield return app;
        yield return Path.Combine(app, "ffmpeg");
        yield return Path.Combine(app, "runtimes", "ffmpeg");

        // 2. El PATH del proceso.
        foreach (var carpeta in Separar(Environment.GetEnvironmentVariable("PATH")))
            yield return carpeta;

        if (!OperatingSystem.IsWindows()) yield break;

        // 3. El PATH del registro. Un proceso hereda el entorno de quien lo lanzó, así que
        //    si el PATH se modificó después de iniciar sesión, el explorador de Windows
        //    (y todo lo que abra desde ahí) sigue viendo el valor viejo hasta reiniciar.
        //    Leerlo del registro salva ese caso sin pedirle nada al usuario.
        foreach (var ambito in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            string? almacenado = null;
            try { almacenado = Environment.GetEnvironmentVariable("PATH", ambito); }
            catch { /* sin permiso de lectura del registro: se ignora */ }

            foreach (var carpeta in Separar(almacenado))
                yield return carpeta;
        }
    }

    private static IEnumerable<string> Separar(string? path) =>
        string.IsNullOrEmpty(path)
            ? []
            : path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
