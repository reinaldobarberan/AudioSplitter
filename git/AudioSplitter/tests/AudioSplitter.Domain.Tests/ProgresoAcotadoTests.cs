using AudioSplitter.Contracts;

namespace AudioSplitter.Domain.Tests;

public class ProgresoAcotadoTests
{
    private sealed class Coleccionista : IProgress<ProgresoProceso>
    {
        public List<ProgresoProceso> Vistos { get; } = [];
        public void Report(ProgresoProceso value) => Vistos.Add(value);
    }

    private static ProgresoProceso Evento(EtapaProceso etapa, int porcentaje = 0) =>
        new(etapa, porcentaje, porcentaje, 0, 0, "x");

    [Fact]
    public void RafagaDeLaMismaEtapa_SeAcotaAUnaSolaEmision()
    {
        var destino = new Coleccionista();
        var acotado = new ProgresoAcotado(destino, TimeSpan.FromSeconds(30));

        for (var i = 0; i < 500; i++)
            acotado.Report(Evento(EtapaProceso.Segmentando, i));

        Assert.Single(destino.Vistos);   // solo la primera pasa dentro del intervalo
    }

    [Fact]
    public void CadaCambioDeEtapa_PasaSiempre()
    {
        var destino = new Coleccionista();
        var acotado = new ProgresoAcotado(destino, TimeSpan.FromSeconds(30));

        acotado.Report(Evento(EtapaProceso.Metadatos));
        acotado.Report(Evento(EtapaProceso.Extrayendo));
        acotado.Report(Evento(EtapaProceso.Segmentando));
        acotado.Report(Evento(EtapaProceso.Limpiando));

        Assert.Equal(4, destino.Vistos.Count);
    }

    [Fact]
    public void ElCierre_NuncaSeDescarta()
    {
        var destino = new Coleccionista();
        var acotado = new ProgresoAcotado(destino, TimeSpan.FromSeconds(30));

        acotado.Report(Evento(EtapaProceso.Finalizado, 100));
        acotado.Report(Evento(EtapaProceso.Finalizado, 100));

        // Aunque caigan dentro del intervalo, el cierre pasa: la ventana no puede quedar mintiendo.
        Assert.Equal(2, destino.Vistos.Count);
        Assert.All(destino.Vistos, v => Assert.Equal(100, v.PorcentajeGlobal));
    }

    [Fact]
    public void PasadoElIntervalo_VuelveAEmitir()
    {
        var destino = new Coleccionista();
        var acotado = new ProgresoAcotado(destino, TimeSpan.FromMilliseconds(20));

        acotado.Report(Evento(EtapaProceso.Segmentando, 1));
        Thread.Sleep(60);
        acotado.Report(Evento(EtapaProceso.Segmentando, 2));

        Assert.Equal(2, destino.Vistos.Count);
    }
}
