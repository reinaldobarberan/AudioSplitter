using System.Diagnostics;

namespace AudioSplitter.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var ventana = ConPortada();
        Application.Run(ventana);
    }

    /// <summary>
    /// Muestra la portada mientras se arma la ventana principal (que es donde se comprueba
    /// FFmpeg) y la mantiene el tiempo mínimo para que no aparezca como un parpadeo.
    /// </summary>
    private static VentanaPrincipal ConPortada()
    {
        var reloj = Stopwatch.StartNew();

        using var portada = new PantallaCarga();
        portada.Show();
        portada.Refresh();

        portada.Avanzar(45, "Comprobando FFmpeg…");
        var ventana = new VentanaPrincipal();

        portada.Avanzar(100, "Listo");

        // Espera activa cortísima, sin bloquear el bombeo de mensajes: la ventana sin
        // borde se quedaría sin pintar si durmiéramos el hilo de interfaz.
        var restante = PantallaCarga.TiempoMinimo - reloj.Elapsed;
        var fin = DateTime.UtcNow + (restante > TimeSpan.Zero ? restante : TimeSpan.Zero);

        while (DateTime.UtcNow < fin)
        {
            Application.DoEvents();
            Thread.Sleep(16);
        }

        portada.Close();
        return ventana;
    }
}
