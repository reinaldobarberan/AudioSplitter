using System.Diagnostics;
using AudioSplitter.Contracts;

namespace AudioSplitter.Domain;

/// <summary>
/// Acota la frecuencia del evento de progreso para no saturar la interfaz con cientos
/// de actualizaciones por segundo. Deja pasar siempre los cambios de etapa y el cierre:
/// perder un porcentaje intermedio es inocuo, perder "Listo" deja la ventana mintiendo.
/// </summary>
public sealed class ProgresoAcotado : IProgress<ProgresoProceso>
{
    public static readonly TimeSpan IntervaloPorDefecto = TimeSpan.FromMilliseconds(250);

    private readonly IProgress<ProgresoProceso> _destino;
    private readonly TimeSpan _intervalo;
    private readonly Stopwatch _reloj = Stopwatch.StartNew();
    private readonly Lock _candado = new();

    private TimeSpan _ultimaEmision = TimeSpan.MinValue;
    private EtapaProceso? _ultimaEtapa;

    public ProgresoAcotado(IProgress<ProgresoProceso> destino, TimeSpan? intervalo = null)
    {
        _destino = destino ?? throw new ArgumentNullException(nameof(destino));
        _intervalo = intervalo ?? IntervaloPorDefecto;
    }

    public void Report(ProgresoProceso valor)
    {
        bool emitir;

        lock (_candado)
        {
            var ahora = _reloj.Elapsed;
            var cambioDeEtapa = _ultimaEtapa != valor.Etapa;
            var esCierre = valor.Etapa is EtapaProceso.Finalizado;

            emitir = cambioDeEtapa || esCierre || (ahora - _ultimaEmision) >= _intervalo;

            if (emitir)
            {
                _ultimaEmision = ahora;
                _ultimaEtapa = valor.Etapa;
            }
        }

        if (emitir) _destino.Report(valor);
    }
}
