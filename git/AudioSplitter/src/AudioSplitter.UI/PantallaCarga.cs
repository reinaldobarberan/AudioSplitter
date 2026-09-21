using System.Drawing.Drawing2D;

namespace AudioSplitter.UI;

/// <summary>
/// Portada de arranque. Se muestra mientras se comprueba FFmpeg y se arma la ventana.
/// </summary>
/// <remarks>
/// El arranque real es casi instantáneo, así que la pantalla tiene un tiempo mínimo en
/// pantalla: sin él aparecería como un parpadeo. Es una decisión de marca, no una espera
/// técnica — si algún día el arranque se vuelve lento, el mínimo deja de notarse.
/// </remarks>
internal sealed class PantallaCarga : Form
{
    public static readonly TimeSpan TiempoMinimo = TimeSpan.FromMilliseconds(1100);

    private const int Ancho = 520;
    private const int AltoPie = 62;

    private readonly Image? _portada = Recursos.Portada;
    private readonly Label _mensaje = new()
    {
        Text = "Preparando…", AutoSize = true, Font = Tipografia.Cuerpo,
        ForeColor = Paleta.TextoSuave, BackColor = Color.Transparent
    };
    private readonly BarraProgreso _barra = new() { Width = Ancho - 48, Height = 4 };

    public PantallaCarga()
    {
        var altoImagen = _portada is null ? 180 : (int)Math.Round(Ancho * (_portada.Height / (double)_portada.Width));

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        BackColor = Paleta.Fondo;
        ClientSize = new Size(Ancho, altoImagen + AltoPie);
        DoubleBuffered = true;
        Icon = Recursos.Icono;

        _mensaje.Location = new Point(24, altoImagen + 14);
        _barra.Location = new Point(24, altoImagen + AltoPie - 20);
        _barra.Valor = 15;

        Controls.Add(_mensaje);
        Controls.Add(_barra);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Redondear();
    }

    /// <summary>Esquinas redondeadas: una ventana sin borde y cuadrada se ve tosca.</summary>
    private void Redondear()
    {
        using var path = Pintura.Redondeado(new Rectangle(0, 0, Width, Height), 12);
        Region?.Dispose();
        Region = new Region(path);
    }

    /// <summary>Avance meramente indicativo: no hay un porcentaje real que informar todavía.</summary>
    public void Avanzar(int porcentaje, string mensaje)
    {
        _barra.Valor = porcentaje;
        _mensaje.Text = mensaje;
        Refresh();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Pintura.Suavizar(e.Graphics);

        using (var fondo = new SolidBrush(Paleta.Barra))
            e.Graphics.FillRectangle(fondo, ClientRectangle);

        if (_portada is not null)
        {
            var alto = (int)Math.Round(Ancho * (_portada.Height / (double)_portada.Width));
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(_portada, new Rectangle(0, 0, Ancho, alto));
        }

        using (var lapiz = new Pen(Paleta.Borde))
            e.Graphics.DrawPath(lapiz, Pintura.Redondeado(new Rectangle(0, 0, Width - 1, Height - 1), 12));

        base.OnPaint(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _portada?.Dispose();
        base.Dispose(disposing);
    }
}
