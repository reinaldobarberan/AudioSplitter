using System.Reflection;

namespace AudioSplitter.UI;

/// <summary>
/// Acceso a las imágenes empotradas en el ensamblado. Se cargan una sola vez y se
/// reutilizan: nada de leer del disco ni de depender de archivos sueltos junto al .exe.
/// </summary>
internal static class Recursos
{
    private const string Raiz = "AudioSplitter.UI.Recursos.";

    private static readonly Lazy<Icon?> _icono = new(() => Cargar("icono.ico", s => new Icon(s)));
    private static readonly Lazy<Image?> _portada = new(() => Cargar("portada.jpg", Image.FromStream));
    private static readonly Lazy<Image?> _marca = new(() => Cargar("icono.png", Image.FromStream));

    /// <summary>Icono de la aplicación, o null si el recurso no está. Nunca lanza.</summary>
    public static Icon? Icono => _icono.Value;

    /// <summary>Imagen de portada para la pantalla de carga.</summary>
    public static Image? Portada => _portada.Value;

    /// <summary>El icono como imagen, para dibujarlo dentro de la ventana.</summary>
    public static Image? Marca => _marca.Value;

    private static T? Cargar<T>(string nombre, Func<Stream, T> construir) where T : class
    {
        try
        {
            using var flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream(Raiz + nombre);
            return flujo is null ? null : construir(flujo);
        }
        catch
        {
            // Una imagen que falta no puede impedir que la aplicación arranque.
            return null;
        }
    }
}
