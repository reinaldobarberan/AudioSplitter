using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AudioSplitter.UI;

/// <summary>
/// Tokens de diseño. Ningún color ni tamaño se escribe suelto en la ventana: si algo
/// se repite en dos lados, vive acá. Cambiar el acento de la app es cambiar una línea.
/// </summary>
internal static class Paleta
{
    public static readonly Color Fondo = Color.FromArgb(0x1B, 0x1B, 0x1D);
    public static readonly Color Barra = Color.FromArgb(0x14, 0x14, 0x16);
    public static readonly Color Superficie = Color.FromArgb(0x23, 0x23, 0x26);
    public static readonly Color Campo = Color.FromArgb(0x18, 0x18, 0x1A);
    public static readonly Color Borde = Color.FromArgb(0x33, 0x33, 0x38);
    public static readonly Color BordeSuave = Color.FromArgb(0x2A, 0x2A, 0x2E);

    public static readonly Color TextoFuerte = Color.FromArgb(0xF2, 0xF2, 0xF4);
    public static readonly Color TextoSuave = Color.FromArgb(0x9A, 0x9A, 0xA2);
    public static readonly Color TextoTenue = Color.FromArgb(0x6E, 0x6E, 0x76);

    public static readonly Color Acento = Color.FromArgb(0xE2, 0x3A, 0x0E);
    public static readonly Color AcentoClaro = Color.FromArgb(0xF2, 0x4F, 0x22);
    public static readonly Color AcentoOscuro = Color.FromArgb(0xB4, 0x2C, 0x08);
    /// <summary>Tinte del fondo cuando una tarjeta queda elegida.</summary>
    public static readonly Color AcentoTinte = Color.FromArgb(0x2B, 0x16, 0x12);

    public static readonly Color Exito = Color.FromArgb(0x35, 0xC7, 0x8A);
    public static readonly Color Error = Color.FromArgb(0xF2, 0x5C, 0x54);

    public static readonly Color Deshabilitado = Color.FromArgb(0x2E, 0x2E, 0x32);
}

internal static class Tipografia
{
    private const string Familia = "Segoe UI";

    public static readonly Font Titulo = new(Familia, 18F, FontStyle.Bold);
    public static readonly Font Marca = new(Familia, 13F, FontStyle.Bold);
    public static readonly Font Subtitulo = new(Familia, 9.5F);
    public static readonly Font Seccion = new(Familia, 8F, FontStyle.Bold);
    public static readonly Font Cuerpo = new(Familia, 9.5F);
    public static readonly Font CuerpoFuerte = new(Familia, 9.5F, FontStyle.Bold);
    public static readonly Font Numero = new(Familia, 15F, FontStyle.Bold);
    public static readonly Font Mono = new("Consolas", 9F);
}

internal static class Pintura
{
    /// <summary>Rectángulo redondeado. WinForms no lo trae: hay que armar el path a mano.</summary>
    public static GraphicsPath Redondeado(Rectangle r, int radio)
    {
        var path = new GraphicsPath();

        if (radio <= 0 || r.Width <= 0 || r.Height <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        var d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    public static void Suavizar(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }

    /// <summary>
    /// Pinta la barra de título en oscuro. Sin esto queda una franja blanca arriba de una
    /// ventana oscura, que es exactamente lo que delata a una app a medio terminar.
    /// </summary>
    public static void BarraDeTituloOscura(IntPtr handle)
    {
        const int UsarModoOscuro = 20;
        var activar = 1;

        try { DwmSetWindowAttribute(handle, UsarModoOscuro, ref activar, sizeof(int)); }
        catch (DllNotFoundException) { /* Windows viejo: se ignora */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamano);
}

/// <summary>
/// Panel con fondo redondeado y borde de un pixel. La "superficie" del layout.
/// </summary>
/// <remarks>
/// Las propiedades van marcadas como no serializables: estos controles se arman por código,
/// nunca desde el diseñador de Visual Studio, y el analizador WFO1000 lo exige explícito.
/// </remarks>
internal class Tarjeta : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radio { get; set; } = 8;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Relleno { get; set; } = Paleta.Superficie;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ColorBorde { get; set; } = Paleta.BordeSuave;

    public Tarjeta()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Pintura.Suavizar(e.Graphics);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Pintura.Redondeado(r, Radio);
        using var relleno = new SolidBrush(Relleno);
        using var lapiz = new Pen(ColorBorde);

        e.Graphics.FillPath(relleno, path);
        e.Graphics.DrawPath(lapiz, path);

        base.OnPaint(e);
    }
}

/// <summary>
/// Tarjeta elegible: insignia, título y una línea que explica el costo de la opción.
/// Reemplaza al desplegable porque muestra el compromiso en vez de esconderlo.
/// </summary>
internal sealed class TarjetaOpcion : Control
{
    private bool _elegida;
    private bool _encima;

    public TarjetaOpcion()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Height = 68;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Insignia { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Titulo { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Detalle { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Elegida
    {
        get => _elegida;
        set { if (_elegida == value) return; _elegida = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _encima = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _encima = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Pintura.Suavizar(e.Graphics);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var borde = _elegida ? Paleta.Acento : _encima ? Paleta.Borde : Paleta.BordeSuave;
        var fondo = _elegida ? Paleta.AcentoTinte : Paleta.Superficie;

        using (var path = Pintura.Redondeado(r, 8))
        using (var brocha = new SolidBrush(fondo))
        using (var lapiz = new Pen(borde, _elegida ? 1.6f : 1f))
        {
            e.Graphics.FillPath(brocha, path);
            e.Graphics.DrawPath(lapiz, path);
        }

        // Insignia: cuadrito con la extensión, en acento cuando está elegida.
        var caja = new Rectangle(14, (Height - 34) / 2, 48, 34);
        using (var path = Pintura.Redondeado(caja, 6))
        using (var brocha = new SolidBrush(_elegida ? Paleta.Acento : Paleta.Campo))
            e.Graphics.FillPath(brocha, path);

        using (var tinta = new SolidBrush(_elegida ? Color.White : Paleta.TextoSuave))
        using (var centrado = new StringFormat
               { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            e.Graphics.DrawString(Insignia, Tipografia.Seccion, tinta, caja, centrado);

        var x = caja.Right + 14;

        using (var tinta = new SolidBrush(Paleta.TextoFuerte))
            e.Graphics.DrawString(Titulo, Tipografia.CuerpoFuerte, tinta, x, 15);

        using (var tinta = new SolidBrush(Paleta.TextoSuave))
            e.Graphics.DrawString(Detalle, Tipografia.Cuerpo, tinta, x, 36);
    }
}

/// <summary>
/// Control segmentado: varias opciones pegadas, una activa. Para valores cortos y
/// excluyentes, donde un desplegable sería un clic de más.
/// </summary>
internal sealed class Segmentado : Control
{
    private string[] _opciones = [];
    private int _elegido;
    private int _encima = -1;

    public event EventHandler? Cambio;

    public Segmentado()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Height = 34;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string[] Opciones
    {
        get => _opciones;
        set { _opciones = value ?? []; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Elegido
    {
        get => _elegido;
        set
        {
            var v = Math.Clamp(value, -1, _opciones.Length - 1);
            if (_elegido == v) return;
            _elegido = v;
            Invalidate();
            Cambio?.Invoke(this, EventArgs.Empty);
        }
    }

    private int AnchoCelda => _opciones.Length == 0 ? Width : Width / _opciones.Length;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = AnchoCelda <= 0 ? -1 : Math.Min(e.X / AnchoCelda, _opciones.Length - 1);
        if (i != _encima) { _encima = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _encima = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && AnchoCelda > 0)
            Elegido = Math.Min(e.X / AnchoCelda, _opciones.Length - 1);
        base.OnMouseDown(e);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_opciones.Length == 0) return;

        Pintura.Suavizar(e.Graphics);

        var marco = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Pintura.Redondeado(marco, 6))
        using (var brocha = new SolidBrush(Paleta.Superficie))
        using (var lapiz = new Pen(Paleta.BordeSuave))
        {
            e.Graphics.FillPath(brocha, path);
            e.Graphics.DrawPath(lapiz, path);
        }

        using var centrado = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        for (var i = 0; i < _opciones.Length; i++)
        {
            var celda = new Rectangle(i * AnchoCelda, 0, AnchoCelda, Height - 1);

            if (i == _elegido)
            {
                var relleno = Rectangle.Inflate(celda, -3, -3);
                using var path = Pintura.Redondeado(relleno, 4);
                using var brocha = new SolidBrush(Enabled ? Paleta.Acento : Paleta.Deshabilitado);
                e.Graphics.FillPath(brocha, path);
            }

            var color = i == _elegido ? Color.White
                      : i == _encima ? Paleta.TextoFuerte
                      : Paleta.TextoSuave;

            using var tinta = new SolidBrush(Enabled ? color : Paleta.TextoTenue);
            e.Graphics.DrawString(_opciones[i], Tipografia.Cuerpo, tinta, celda, centrado);
        }
    }
}

/// <summary>
/// Barra de progreso pintada a mano. La nativa ignora los colores cuando los estilos
/// visuales están activos, así que no hay forma de que acompañe la paleta.
/// </summary>
internal sealed class BarraProgreso : Control
{
    private int _valor;
    private Color _color = Paleta.Acento;

    public BarraProgreso()
    {
        // SupportsTransparentBackColor es obligatorio: Control (a diferencia de Panel) no
        // acepta un fondo transparente si no se declara, y tira ArgumentException al asignarlo.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                 | ControlStyles.SupportsTransparentBackColor, true);
        Height = 8;
        BackColor = Color.Transparent;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Valor
    {
        get => _valor;
        set
        {
            var acotado = Math.Clamp(value, 0, 100);
            if (_valor == acotado) return;
            _valor = acotado;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Color
    {
        get => _color;
        set { _color = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Pintura.Suavizar(e.Graphics);

        var radio = Height / 2;
        var pista = new Rectangle(0, 0, Width - 1, Height - 1);

        using (var path = Pintura.Redondeado(pista, radio))
        using (var brocha = new SolidBrush(Paleta.Campo))
        using (var lapiz = new Pen(Paleta.BordeSuave))
        {
            e.Graphics.FillPath(brocha, path);
            e.Graphics.DrawPath(lapiz, path);
        }

        if (_valor <= 0) return;

        // Mínimo del alto para que el 1 % no se vea como un punto deforme.
        var ancho = Math.Max(Height, (int)(pista.Width * (_valor / 100.0)));
        var avance = new Rectangle(0, 0, ancho, Height - 1);

        using (var path = Pintura.Redondeado(avance, radio))
        using (var brocha = new LinearGradientBrush(
                   new Rectangle(0, 0, Math.Max(ancho, 1), Height),
                   _color, ControlPaint.Light(_color, 0.2f), LinearGradientMode.Horizontal))
            e.Graphics.FillPath(brocha, path);
    }
}

internal static class Botones
{
    /// <summary>Acción principal: una sola por pantalla.</summary>
    public static Button Primario(string texto, int ancho = 150)
    {
        var b = Base(texto, ancho);
        b.Font = Tipografia.CuerpoFuerte;
        Tenir(b, habilitado: true);
        b.FlatAppearance.MouseOverBackColor = Paleta.AcentoClaro;
        b.FlatAppearance.MouseDownBackColor = Paleta.AcentoOscuro;
        b.EnabledChanged += (_, _) => Tenir(b, b.Enabled);
        return b;

        static void Tenir(Button b, bool habilitado)
        {
            b.BackColor = habilitado ? Paleta.Acento : Paleta.Deshabilitado;
            b.ForeColor = habilitado ? Color.White : Paleta.TextoTenue;
        }
    }

    /// <summary>Acción secundaria: borde suave, sin peso visual.</summary>
    public static Button Secundario(string texto, int ancho = 120)
    {
        var b = Base(texto, ancho);
        b.Font = Tipografia.Cuerpo;
        b.BackColor = Paleta.Superficie;
        b.ForeColor = Paleta.TextoFuerte;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Paleta.Borde;
        b.FlatAppearance.MouseOverBackColor = Paleta.Campo;
        b.EnabledChanged += (_, _) =>
            b.ForeColor = b.Enabled ? Paleta.TextoFuerte : Paleta.TextoTenue;
        return b;
    }

    private static Button Base(string texto, int ancho)
    {
        var b = new Button
        {
            Text = texto,
            Width = ancho,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0, 0, 8, 0)
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}

internal static class Etiquetas
{
    /// <summary>Rótulo de sección, en versalitas: separa bloques sin agregar peso.</summary>
    public static Label Seccion(string texto) => new()
    {
        Text = texto.ToUpperInvariant(),
        AutoSize = true,
        Font = Tipografia.Seccion,
        ForeColor = Paleta.TextoTenue,
        Margin = new Padding(2, 0, 0, 7)
    };

    public static Label Texto(string texto, Font fuente, Color color) => new()
    {
        Text = texto,
        AutoSize = true,
        Font = fuente,
        ForeColor = color,
        Margin = new Padding(0)
    };
}

/// <summary>Línea divisoria de un pixel.</summary>
internal sealed class Division : Control
{
    public Division()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var lapiz = new Pen(Paleta.BordeSuave);
        e.Graphics.DrawLine(lapiz, 0, 0, Width, 0);
    }
}
