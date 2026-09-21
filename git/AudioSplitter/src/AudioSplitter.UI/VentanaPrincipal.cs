using AudioSplitter.Bootstrap;
using AudioSplitter.Contracts;

namespace AudioSplitter.UI;

/// <summary>
/// Una sola ventana. No contiene reglas de negocio ni rutas de FFmpeg: traduce interacción
/// en una petición, y una respuesta en pantalla. El estado lo determina el resultado que
/// devuelve el dominio; la ventana solo refleja lo que recibe.
/// </summary>
public sealed class VentanaPrincipal : Form
{
    private const int AnchoBarra = 230;
    private const int AnchoContenido = 620;
    private const string Vacio = "Todavía no hay segmentos.";

    private static readonly int[] Presets = [15, 30, 60];

    private readonly TextBox _rutaVideo = Campo();
    private readonly TextBox _carpetaTrabajo = Campo();

    private readonly TarjetaOpcion _m4a = new()
    {
        Insignia = ".m4a", Titulo = "Sin recodificar",
        Detalle = "Rápido, sin pérdida.",
        Width = 302, Elegida = true
    };
    private readonly TarjetaOpcion _mp3 = new()
    {
        Insignia = ".mp3", Titulo = "Máxima compatibilidad",
        Detalle = "Recodifica. Abre en todos lados.",
        Width = 302
    };

    private readonly Segmentado _duracion = new()
    {
        Opciones = ["15 min", "30 min", "60 min", "Otro"], Elegido = 1, Width = 320
    };
    private readonly NumericUpDown _minutos = new()
    {
        Minimum = 1, Maximum = 600, Value = 30, Width = 90, Height = 30,
        Font = Tipografia.Cuerpo, BorderStyle = BorderStyle.FixedSingle,
        TextAlign = HorizontalAlignment.Center,
        BackColor = Paleta.Campo, ForeColor = Paleta.TextoFuerte, Visible = false
    };

    private readonly Button _examinarVideo = Botones.Secundario("Explorar…", 116);
    private readonly Button _examinarCarpeta = Botones.Secundario("Explorar…", 116);
    private readonly Button _procesar = Botones.Primario("Procesar  →", 164);
    private readonly Button _cancelar = Botones.Secundario("Cancelar", 116);
    private readonly Button _abrirSalida = Botones.Secundario("Abrir carpeta de salida", 200);

    private readonly BarraProgreso _barra = new() { Width = 500 };
    private readonly Label _porcentaje = Etiquetas.Texto("0 %", Tipografia.Numero, Paleta.TextoFuerte);
    private readonly Label _estado = Etiquetas.Texto(
        "Elegí un video y una carpeta de trabajo para empezar.", Tipografia.Cuerpo, Paleta.TextoSuave);

    private readonly ListBox _segmentos = new()
    {
        Width = 580, Height = 116, BorderStyle = BorderStyle.None,
        BackColor = Paleta.Superficie, ForeColor = Paleta.TextoFuerte,
        DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26,
        Font = Tipografia.Cuerpo, IntegralHeight = false
    };
    private readonly Label _tituloListado = Etiquetas.Seccion("Segmentos");
    private readonly Label _estadoFfmpeg = Etiquetas.Texto("Comprobando FFmpeg…", Tipografia.Cuerpo, Paleta.TextoSuave);

    private readonly IServicioProcesamiento _servicio;
    private CancellationTokenSource? _cancelacion;
    private string? _avisoFfmpeg;

    public VentanaPrincipal()
    {
        _servicio = ComposicionRaiz.CrearServicio();

        Text = "AudioSplitter";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(AnchoBarra + AnchoContenido + 64, 790);
        BackColor = Paleta.Fondo;
        Font = Tipografia.Cuerpo;
        DoubleBuffered = true;
        Icon = Recursos.Icono;

        _segmentos.Items.Add(Vacio);
        _segmentos.DrawItem += DibujarSegmento;

        Armar();

        _m4a.Click += (_, _) => ElegirFormato(FormatoAudio.M4a);
        _mp3.Click += (_, _) => ElegirFormato(FormatoAudio.Mp3);
        _duracion.Cambio += (_, _) => AplicarPreset();
        _examinarVideo.Click += (_, _) => ElegirVideo();
        _examinarCarpeta.Click += (_, _) => ElegirCarpeta();
        _procesar.Click += async (_, _) => await ProcesarAsync();
        _cancelar.Click += (_, _) => _cancelacion?.Cancel();
        _abrirSalida.Click += (_, _) => AbrirCarpetaSalida();

        _cancelar.Enabled = false;
        _procesar.Enabled = false;

        VerificarFfmpeg();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Pintura.BarraDeTituloOscura(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_avisoFfmpeg is not { } aviso) return;

        _avisoFfmpeg = null;
        MessageBox.Show(this, aviso, "AudioSplitter", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // ---------- Layout ----------

    private void Armar()
    {
        // Dock.Fill va PRIMERO en el orden de inserción para que la barra lateral,
        // que se ancla a la izquierda, no le quede encima.
        Controls.Add(Contenido());
        Controls.Add(BarraLateral());
    }

    private Control BarraLateral()
    {
        var barra = new Panel { Dock = DockStyle.Left, Width = AnchoBarra, BackColor = Paleta.Barra };

        var marca = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Location = new Point(24, 26), BackColor = Color.Transparent
        };

        if (Recursos.Marca is { } logo)
        {
            marca.Controls.Add(new PictureBox
            {
                Image = logo, SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(44, 44), BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 12)
            });
        }

        marca.Controls.Add(Etiquetas.Texto("AudioSplitter", Tipografia.Marca, Paleta.TextoFuerte));
        marca.Controls.Add(new Label
        {
            Text = "Audio en bloques", AutoSize = true, Font = Tipografia.Subtitulo,
            ForeColor = Paleta.TextoTenue, Margin = new Padding(0, 4, 0, 0)
        });

        // Abajo va estado real, no navegación inventada: esta app tiene una sola pantalla.
        var pie = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Dock = DockStyle.Bottom, Padding = new Padding(24, 0, 0, 26),
            BackColor = Color.Transparent
        };
        pie.Controls.Add(_estadoFfmpeg);
        pie.Controls.Add(new Label
        {
            Text = "Proceso local · sin conexión", AutoSize = true, Font = Tipografia.Cuerpo,
            ForeColor = Paleta.TextoTenue, Margin = new Padding(0, 6, 0, 0)
        });

        barra.Controls.Add(pie);
        barra.Controls.Add(marca);
        return barra;
    }

    private Control Contenido()
    {
        var area = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent,
            Padding = new Padding(32, 28, 32, 28), AutoScroll = true
        };

        var columna = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            BackColor = Color.Transparent
        };

        columna.Controls.Add(Etiquetas.Texto("Dividir audio", Tipografia.Titulo, Paleta.TextoFuerte));
        columna.Controls.Add(new Label
        {
            Text = "Extrae la pista de un video y la parte en bloques secuenciales.",
            AutoSize = true, Font = Tipografia.Subtitulo, ForeColor = Paleta.TextoSuave,
            Margin = new Padding(0, 6, 0, 0)
        });
        columna.Controls.Add(new Division { Width = AnchoContenido, Margin = new Padding(0, 20, 0, 22) });

        columna.Controls.Add(Etiquetas.Seccion("¿Qué formato de salida?"));
        var formatos = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(0, 0, 0, 22), BackColor = Color.Transparent
        };
        _m4a.Margin = new Padding(0, 0, 12, 0);
        _mp3.Margin = new Padding(0);
        formatos.Controls.Add(_m4a);
        formatos.Controls.Add(_mp3);
        columna.Controls.Add(formatos);

        columna.Controls.Add(Etiquetas.Seccion("Archivo de video"));
        columna.Controls.Add(Fila(_rutaVideo, _examinarVideo));

        columna.Controls.Add(Etiquetas.Seccion("Carpeta de trabajo"));
        columna.Controls.Add(Fila(_carpetaTrabajo, _examinarCarpeta));

        columna.Controls.Add(Etiquetas.Seccion("Duración de cada bloque"));
        var tiempo = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(0, 0, 0, 22), BackColor = Color.Transparent
        };
        _duracion.Margin = new Padding(0, 0, 12, 0);
        tiempo.Controls.Add(_duracion);
        tiempo.Controls.Add(_minutos);
        columna.Controls.Add(tiempo);

        var acciones = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(0, 0, 0, 24), BackColor = Color.Transparent
        };
        acciones.Controls.Add(_procesar);
        acciones.Controls.Add(_cancelar);
        columna.Controls.Add(acciones);

        columna.Controls.Add(PanelAvance());
        columna.Controls.Add(PanelSalida());

        area.Controls.Add(columna);
        return area;
    }

    private static Control Fila(Control campo, Control boton)
    {
        var fila = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(0, 0, 0, 22), BackColor = Color.Transparent
        };
        campo.Margin = new Padding(0, 3, 12, 0);
        boton.Margin = new Padding(0);
        fila.Controls.Add(campo);
        fila.Controls.Add(boton);
        return fila;
    }

    private Control PanelAvance()
    {
        var tarjeta = new Tarjeta
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(AnchoContenido, 0), Margin = new Padding(0, 0, 0, 14)
        };

        var contenido = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18, 16, 18, 16), ColumnCount = 2, RowCount = 2,
            BackColor = Color.Transparent
        };
        contenido.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contenido.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _barra.Margin = new Padding(0, 12, 16, 10);
        contenido.Controls.Add(_barra, 0, 0);
        contenido.Controls.Add(_porcentaje, 1, 0);

        _estado.Margin = new Padding(0, 2, 0, 0);
        contenido.Controls.Add(_estado, 0, 1);
        contenido.SetColumnSpan(_estado, 2);

        tarjeta.Controls.Add(contenido);
        return tarjeta;
    }

    private Control PanelSalida()
    {
        var tarjeta = new Tarjeta
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(AnchoContenido, 0), Margin = new Padding(0)
        };

        var contenido = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Padding = new Padding(18, 14, 18, 14), BackColor = Color.Transparent
        };

        _tituloListado.Margin = new Padding(0, 0, 0, 8);
        _abrirSalida.Margin = new Padding(0, 12, 0, 0);
        _abrirSalida.Visible = false;

        contenido.Controls.Add(_tituloListado);
        contenido.Controls.Add(_segmentos);
        contenido.Controls.Add(_abrirSalida);

        tarjeta.Controls.Add(contenido);
        return tarjeta;
    }

    private static TextBox Campo() => new()
    {
        ReadOnly = true, Width = 468, Height = 30, BorderStyle = BorderStyle.FixedSingle,
        BackColor = Paleta.Campo, ForeColor = Paleta.TextoFuerte, Font = Tipografia.Cuerpo
    };

    /// <summary>Fila del listado: nombre a la izquierda, duración en gris a la derecha.</summary>
    private void DibujarSegmento(object? remitente, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        Pintura.Suavizar(e.Graphics);

        var seleccionado = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var fondo = new SolidBrush(seleccionado ? Paleta.AcentoTinte : Paleta.Superficie))
            e.Graphics.FillRectangle(fondo, e.Bounds);

        var texto = _segmentos.Items[e.Index]?.ToString() ?? string.Empty;
        var caja = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height);
        using var formato = new StringFormat { LineAlignment = StringAlignment.Center };

        if (texto == Vacio)
        {
            using var tenue = new SolidBrush(Paleta.TextoTenue);
            e.Graphics.DrawString(texto, Tipografia.Cuerpo, tenue, caja, formato);
            return;
        }

        var partes = texto.Split(" · ", 2);

        using (var tinta = new SolidBrush(Paleta.TextoFuerte))
            e.Graphics.DrawString(partes[0], Tipografia.Mono, tinta, caja, formato);

        if (partes.Length <= 1) return;

        formato.Alignment = StringAlignment.Far;
        using var suave = new SolidBrush(Paleta.TextoSuave);
        e.Graphics.DrawString(partes[1], Tipografia.Cuerpo, suave, caja, formato);
    }

    // ---------- Entrada del usuario ----------

    private void ElegirFormato(FormatoAudio formato)
    {
        _m4a.Elegida = formato == FormatoAudio.M4a;
        _mp3.Elegida = formato == FormatoAudio.Mp3;
    }

    private FormatoAudio FormatoElegido => _mp3.Elegida ? FormatoAudio.Mp3 : FormatoAudio.M4a;

    /// <summary>"Otro" descubre el campo numérico; los presets lo ocultan y fijan el valor.</summary>
    private void AplicarPreset()
    {
        var esOtro = _duracion.Elegido >= Presets.Length;
        _minutos.Visible = esOtro;

        if (!esOtro && _duracion.Elegido >= 0)
            _minutos.Value = Presets[_duracion.Elegido];
    }

    private void ElegirVideo()
    {
        using var dialogo = new OpenFileDialog
        {
            Title = "Elegí el video",
            Filter = "Videos|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts|Todos|*.*"
        };
        if (dialogo.ShowDialog(this) != DialogResult.OK) return;

        _rutaVideo.Text = dialogo.FileName;

        // Atajo cómodo: si todavía no hay carpeta elegida, se propone la del video.
        if (_carpetaTrabajo.Text.Length == 0)
            _carpetaTrabajo.Text = Path.GetDirectoryName(dialogo.FileName) ?? string.Empty;

        RevisarHabilitacion();
    }

    private void ElegirCarpeta()
    {
        using var dialogo = new FolderBrowserDialog { Description = "Elegí la carpeta de trabajo" };
        if (dialogo.ShowDialog(this) != DialogResult.OK) return;

        _carpetaTrabajo.Text = dialogo.SelectedPath;
        RevisarHabilitacion();
    }

    private void RevisarHabilitacion()
    {
        _procesar.Enabled = _rutaVideo.Text.Length > 0 && _carpetaTrabajo.Text.Length > 0;

        if (_procesar.Enabled && _estado.ForeColor == Paleta.TextoSuave)
            Mensaje("Todo listo para procesar.", Paleta.TextoSuave);
    }

    // ---------- Corrida ----------

    private async Task ProcesarAsync()
    {
        _segmentos.Items.Clear();
        _cancelacion = new CancellationTokenSource();
        EstadoEnProceso();

        var progreso = ComposicionRaiz.Acotar(new Progress<ProgresoProceso>(Pintar));
        var peticion = new PeticionProceso(
            _rutaVideo.Text,
            _carpetaTrabajo.Text,
            TimeSpan.FromMinutes((double)_minutos.Value),
            FormatoElegido);

        // El dominio nunca lanza: devuelve un resultado con motivo de negocio.
        var resultado = await _servicio.ProcesarAsync(peticion, progreso, _cancelacion.Token);

        _cancelacion.Dispose();
        _cancelacion = null;

        if (resultado.Exitoso) EstadoFinalizado(resultado);
        else EstadoFallido(resultado);
    }

    /// <summary>Progress&lt;T&gt; despacha en el hilo de UI: acá solo se pinta.</summary>
    private void Pintar(ProgresoProceso p)
    {
        _barra.Valor = p.PorcentajeGlobal;
        _porcentaje.Text = p.PorcentajeGlobal + " %";
        Mensaje(p.Mensaje, Paleta.TextoSuave);

        if (p.SegmentoListo is { } ruta)
            _segmentos.Items.Add(Path.GetFileName(ruta));
    }

    private void Mensaje(string texto, Color color)
    {
        _estado.Text = texto;
        _estado.ForeColor = color;
    }

    // ---------- Estados de la ventana ----------

    private void EstadoEnProceso()
    {
        Habilitar(entradas: false);
        _barra.Color = Paleta.Acento;
        _barra.Valor = 0;
        _porcentaje.Text = "0 %";
        _porcentaje.ForeColor = Paleta.TextoFuerte;
        _abrirSalida.Visible = false;
        _tituloListado.Text = "SEGMENTOS";
    }

    private void EstadoFinalizado(ResultadoProceso resultado)
    {
        Habilitar(entradas: true);
        _barra.Color = Paleta.Exito;
        _barra.Valor = 100;
        _porcentaje.Text = "100 %";
        _porcentaje.ForeColor = Paleta.Exito;
        Mensaje($"Listo · {resultado.Segmentos.Count} segmentos en la carpeta de salida.", Paleta.Exito);

        Listar(resultado);
        _tituloListado.Text = $"SEGMENTOS · {resultado.Segmentos.Count}";
        _abrirSalida.Visible = true;
    }

    private void EstadoFallido(ResultadoProceso resultado)
    {
        Habilitar(entradas: true);
        _barra.Color = Paleta.Error;
        _barra.Valor = 0;
        _porcentaje.Text = "0 %";
        _porcentaje.ForeColor = Paleta.Error;

        var motivo = resultado.MotivoFallo ?? "El proceso no pudo completarse.";

        // Los segmentos ya escritos se conservan: solo se borra lo temporal.
        if (resultado.Segmentos.Count > 0)
        {
            motivo += $" Se conservaron {resultado.Segmentos.Count} segmentos ya escritos.";
            Listar(resultado);
            _tituloListado.Text = $"SEGMENTOS CONSERVADOS · {resultado.Segmentos.Count}";
            _abrirSalida.Visible = true;
        }
        else
        {
            _segmentos.Items.Clear();
            _segmentos.Items.Add(Vacio);
            _tituloListado.Text = "SEGMENTOS";
        }

        Mensaje(motivo, Paleta.Error);
        MessageBox.Show(this, motivo, "AudioSplitter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void Listar(ResultadoProceso resultado)
    {
        _segmentos.BeginUpdate();
        _segmentos.Items.Clear();

        foreach (var s in resultado.Segmentos)
            _segmentos.Items.Add($"{Path.GetFileName(s.RutaArchivo)} · {s.Duracion:hh\\:mm\\:ss}");

        _segmentos.EndUpdate();
    }

    private void Habilitar(bool entradas)
    {
        _examinarVideo.Enabled = entradas;
        _examinarCarpeta.Enabled = entradas;
        _minutos.Enabled = entradas;
        _duracion.Enabled = entradas;
        _m4a.Enabled = entradas;
        _mp3.Enabled = entradas;
        _cancelar.Enabled = !entradas;
        _procesar.Enabled = entradas && _rutaVideo.Text.Length > 0 && _carpetaTrabajo.Text.Length > 0;
    }

    private void AbrirCarpetaSalida()
    {
        var salida = Path.Combine(_carpetaTrabajo.Text, "salida");
        if (!Directory.Exists(salida)) return;

        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(salida) { UseShellExecute = true });
    }

    private void VerificarFfmpeg()
    {
        if (ComposicionRaiz.FfmpegDisponible(out var detalle))
        {
            _estadoFfmpeg.Text = "FFmpeg detectado";
            _estadoFfmpeg.ForeColor = Paleta.Exito;
            return;
        }

        _estadoFfmpeg.Text = "FFmpeg no encontrado";
        _estadoFfmpeg.ForeColor = Paleta.Error;

        Mensaje("FFmpeg no está disponible.", Paleta.Error);
        _examinarVideo.Enabled = false;
        _examinarCarpeta.Enabled = false;
        _procesar.Enabled = false;

        // El aviso se guarda para OnShown: mostrarlo ahora lo pondria sobre la portada,
        // con la ventana principal todavia invisible detras.
        _avisoFfmpeg = detalle;
    }
}
