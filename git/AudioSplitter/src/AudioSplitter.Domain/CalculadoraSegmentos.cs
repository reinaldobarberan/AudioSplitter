using System.Globalization;
using AudioSplitter.Contracts;

namespace AudioSplitter.Domain;

/// <summary>
/// La regla de los 30 minutos. Bloques secuenciales de duración fija; el último conserva
/// el resto sin rellenar. El corte es por tiempo, no por tamaño.
/// Lógica pura: se prueba entera sin FFmpeg ni disco.
/// </summary>
public static class CalculadoraSegmentos
{
    private const string Prefijo = "audio_parte_";
    private const int RellenoMinimo = 3;

    public static IReadOnlyList<SegmentoPlanificado> Calcular(TimeSpan duracionTotal, TimeSpan duracionSegmento)
    {
        if (duracionTotal <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(duracionTotal), duracionTotal, "La duración total debe ser positiva.");

        if (duracionSegmento <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(duracionSegmento), duracionSegmento, "La duración de segmento debe ser positiva.");

        // División entera con techo, sobre ticks: evita el error de redondeo del punto flotante.
        var total = (int)((duracionTotal.Ticks + duracionSegmento.Ticks - 1) / duracionSegmento.Ticks);

        // El relleno crece con el total para que el orden alfabético siga siendo el cronológico.
        var relleno = Math.Max(RellenoMinimo, total.ToString(CultureInfo.InvariantCulture).Length);

        var plan = new List<SegmentoPlanificado>(total);
        for (var i = 0; i < total; i++)
        {
            var inicio = duracionSegmento * i;
            var restante = duracionTotal - inicio;
            var duracion = restante < duracionSegmento ? restante : duracionSegmento;
            var numero = i + 1;

            plan.Add(new SegmentoPlanificado(
                numero,
                inicio,
                duracion,
                Prefijo + numero.ToString(CultureInfo.InvariantCulture).PadLeft(relleno, '0')));
        }

        return plan;
    }
}
