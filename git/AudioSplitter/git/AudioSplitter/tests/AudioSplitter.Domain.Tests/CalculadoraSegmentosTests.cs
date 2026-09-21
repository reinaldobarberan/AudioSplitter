using AudioSplitter.Domain;

namespace AudioSplitter.Domain.Tests;

public class CalculadoraSegmentosTests
{
    private static readonly TimeSpan TreintaMin = TimeSpan.FromMinutes(30);

    [Fact]
    public void EjemploDelDocumento_UnaHoraTreintaYCinco_DaCuatroSegmentos()
    {
        var plan = CalculadoraSegmentos.Calcular(new TimeSpan(1, 35, 0), TreintaMin);

        Assert.Equal(4, plan.Count);

        Assert.Equal(new TimeSpan(0, 0, 0),  plan[0].Inicio);
        Assert.Equal(TreintaMin,             plan[0].Duracion);
        Assert.Equal(new TimeSpan(0, 30, 0), plan[1].Inicio);
        Assert.Equal(TreintaMin,             plan[1].Duracion);
        Assert.Equal(new TimeSpan(1, 0, 0),  plan[2].Inicio);
        Assert.Equal(TreintaMin,             plan[2].Duracion);

        // El último conserva el resto, sin rellenar.
        Assert.Equal(new TimeSpan(1, 30, 0),  plan[3].Inicio);
        Assert.Equal(TimeSpan.FromMinutes(5), plan[3].Duracion);
    }

    [Fact]
    public void EjemploDelDocumento_NombresConCerosALaIzquierda()
    {
        var plan = CalculadoraSegmentos.Calcular(new TimeSpan(1, 35, 0), TreintaMin);

        Assert.Equal("audio_parte_001", plan[0].NombreBase);
        Assert.Equal("audio_parte_002", plan[1].NombreBase);
        Assert.Equal("audio_parte_003", plan[2].NombreBase);
        Assert.Equal("audio_parte_004", plan[3].NombreBase);
    }

    [Fact]
    public void DuracionMultiploExacto_NoGeneraSegmentoVacio()
    {
        var plan = CalculadoraSegmentos.Calcular(TimeSpan.FromMinutes(60), TreintaMin);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, p => Assert.Equal(TreintaMin, p.Duracion));
    }

    [Fact]
    public void DuracionMenorAlSegmento_DaUnSoloSegmentoConElResto()
    {
        var plan = CalculadoraSegmentos.Calcular(TimeSpan.FromMinutes(10), TreintaMin);

        var unico = Assert.Single(plan);
        Assert.Equal(TimeSpan.Zero, unico.Inicio);
        Assert.Equal(TimeSpan.FromMinutes(10), unico.Duracion);
        Assert.Equal("audio_parte_001", unico.NombreBase);
    }

    [Fact]
    public void ElRellenoCreceSiHayMasDeMilSegmentos_ParaQueElOrdenAlfabeticoSigaSiendoCronologico()
    {
        // 1001 segmentos de 1 minuto.
        var plan = CalculadoraSegmentos.Calcular(TimeSpan.FromMinutes(1001), TimeSpan.FromMinutes(1));

        Assert.Equal(1001, plan.Count);
        Assert.Equal("audio_parte_0001", plan[0].NombreBase);
        Assert.Equal("audio_parte_1001", plan[1000].NombreBase);

        var ordenados = plan.Select(p => p.NombreBase).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(plan.Select(p => p.NombreBase), ordenados);
    }

    [Fact]
    public void LaNumeracionArrancaEnUno()
    {
        var plan = CalculadoraSegmentos.Calcular(new TimeSpan(1, 35, 0), TreintaMin);

        Assert.Equal(new[] { 1, 2, 3, 4 }, plan.Select(p => p.Numero));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DuracionTotalNoPositiva_EsRechazada(int minutos)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalculadoraSegmentos.Calcular(TimeSpan.FromMinutes(minutos), TreintaMin));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void DuracionDeSegmentoNoPositiva_EsRechazada(int minutos)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalculadoraSegmentos.Calcular(TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(minutos)));
    }
}
