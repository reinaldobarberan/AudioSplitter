using System.Runtime.CompilerServices;
using AudioSplitter.Contracts;

namespace AudioSplitter.Domain.Tests;

internal sealed class LectorFalso : ILectorMetadatos
{
    public TimeSpan Duracion { get; init; } = new(1, 35, 0);
    public Exception? Explota { get; init; }
    public int Llamadas { get; private set; }

    public Task<TimeSpan> ObtenerDuracionAsync(string rutaVideo, CancellationToken ct)
    {
        Llamadas++;
        if (Explota is not null) throw Explota;
        return Task.FromResult(Duracion);
    }
}

internal sealed class ExtractorFalso : IExtractorAudio
{
    public Exception? Explota { get; init; }
    public int Llamadas { get; private set; }
    public FormatoAudio? FormatoPedido { get; private set; }

    public Task<string> ExtraerAsync(
        string rutaVideo, string carpetaTemp, FormatoAudio formato,
        IProgress<ProgresoProceso>? progreso, CancellationToken ct)
    {
        Llamadas++;
        FormatoPedido = formato;
        if (Explota is not null) throw Explota;
        return Task.FromResult(Path.Combine(carpetaTemp, "audio" + formato.Extension()));
    }
}

internal sealed class SegmentadorFalso : ISegmentadorAudio
{
    /// <summary>Cuántos segmentos emite antes de lanzar <see cref="Explota"/>. null = todos.</summary>
    public int? EmitirAntesDeExplotar { get; init; }
    public Exception? Explota { get; init; }
    public int Llamadas { get; private set; }

    public async IAsyncEnumerable<SegmentoGenerado> SegmentarAsync(
        string rutaAudio, string carpetaSalida, IReadOnlyList<SegmentoPlanificado> plan,
        IProgress<ProgresoProceso>? progreso, [EnumeratorCancellation] CancellationToken ct)
    {
        Llamadas++;
        var tope = EmitirAntesDeExplotar ?? plan.Count;

        for (var i = 0; i < plan.Count; i++)
        {
            if (i == tope && Explota is not null) throw Explota;

            var p = plan[i];
            // El segmentador hereda la extensión del audio ya extraído.
            yield return new SegmentoGenerado(
                p.Numero,
                Path.Combine(carpetaSalida, p.NombreBase + Path.GetExtension(rutaAudio)),
                p.Inicio,
                p.Duracion);
            await Task.Yield();
        }

        if (Explota is not null && tope >= plan.Count) throw Explota;
    }
}

internal sealed class ArchivosFalsos : ISistemaArchivos
{
    public HashSet<string> Existentes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Escribible { get; init; } = true;
    public long Libre { get; init; } = 100L * 1024 * 1024 * 1024;
    public long Tamano { get; init; } = 500L * 1024 * 1024;

    public List<string> Creadas { get; } = [];
    public List<string> Eliminadas { get; } = [];

    public bool ArchivoExiste(string ruta) => Existentes.Contains(ruta);
    public long TamanoArchivo(string ruta) => Tamano;
    public void CrearCarpeta(string ruta) => Creadas.Add(ruta);
    public bool PuedeEscribir(string carpeta) => Escribible;
    public long EspacioLibre(string carpeta) => Libre;
    public void EliminarCarpetaSiExiste(string ruta) => Eliminadas.Add(ruta);
}
