# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Idioma

La documentación de este proyecto está en español. Responde y escribe documentación en español.

## Estructura

```
AudioSplitter.slnx          (.NET 10 usa formato .slnx, no .sln)
src/
  AudioSplitter.Contracts/  DTOs, interfaces, evento de progreso — CERO dependencias
  AudioSplitter.Core/       FFmpeg como proceso externo
  AudioSplitter.Domain/     orquestación, validación, helpers
  AudioSplitter.Bootstrap/  composition root: el unico que conoce las 4 capas
  AudioSplitter.UI/         WinForms (net10.0-windows)
tests/
  AudioSplitter.Domain.Tests/      xunit, sin FFmpeg (fakes)
  AudioSplitter.Integracion.Tests/ xunit, pipeline real con FFmpeg
```

Referencias reales: `Core -> Contracts`, `Domain -> Contracts`,
`Bootstrap -> Contracts + Domain + Core`, `UI -> Bootstrap + Domain + Contracts`.

Domain NO referencia Core: recibe la implementacion por inyeccion desde Bootstrap.
La referencia `Bootstrap -> Core` lleva `PrivateAssets=all` para que Core no fluya
a la UI en tiempo de compilacion.

## Qué es AudioSplitter

Toma un video, extrae su pista de audio y la parte en segmentos secuenciales de duración fija
(30 minutos por defecto), reportando progreso sin bloquear la ventana.

Stack previsto: C# / .NET · Windows Forms · FFmpeg invocado como proceso externo.

La fuente de verdad del diseño es `docs/Detalle_Arquitectura_AudioSplitter.md`. Léelo antes de
proponer estructura, nombres de proyectos o flujo de datos.

## Arquitectura: 4 capas, dependencias en una sola dirección

```
UI  ──▶  Domain  ──▶  Core
 │         │           │
 └─────────┴───────────┴──▶  Contracts   (no referencia a nadie)
```

| Proyecto | Rol | Responsabilidad |
|----------|-----|-----------------|
| `AudioSplitter.UI` | WinForms | Controles, diálogos de selección, `ProgressBar`, llamadas asíncronas al dominio. |
| `AudioSplitter.Domain` | Servicios | Orquesta el proceso, valida reglas de negocio, concentra helpers. |
| `AudioSplitter.Contracts` | Modelos / Interfaces | DTOs, interfaces de servicio, evento de progreso. **Cero dependencias.** |
| `AudioSplitter.Core` | Infraestructura | Invoca FFmpeg de forma nativa y asíncrona. Trabajo pesado. |

### Reglas invariantes

Estas son las que hay que defender en cada cambio. Violarlas rompe el diseño:

1. **Contracts no depende de nadie.** Si un cambio le agrega una referencia, está mal.
2. **FFmpeg vive solo en Core.** Si un archivo de UI o Domain menciona `ffmpeg`, la ruta,
   sus argumentos o su salida cruda, el código está en la capa equivocada.
3. **La UI no referencia Core.** Si alguien lo necesita, la responsabilidad está mal ubicada.
   Domain recibe la implementación de Core por inyección, contra la interfaz de Contracts.
4. **Todo el trabajo es asíncrono.** La ventana debe responder con archivos de varias horas.
5. **La ruta de trabajo es un dato, no una constante.** Se resuelve en ejecución, en cada corrida.
   Domain la normaliza; Core solo recibe rutas absolutas ya resueltas y ya validadas.
6. **La excepción técnica nunca llega cruda a la pantalla.** Core lanza el error técnico,
   Domain lo traduce a un resultado fallido con motivo de negocio, la UI muestra ese motivo.

### Contrato más frágil: el evento de progreso

El evento de progreso (porcentaje, etapa, segmento en curso, mensaje legible) se declara en
Contracts y atraviesa las tres capas: `Core observa ──▶ Domain publica ──▶ UI pinta`.

- Si la UI necesita un dato nuevo del avance, se agrega al evento en Contracts. Nunca se
  expone la salida cruda de FFmpeg hacia arriba.
- El evento se emite con intervalo mínimo acotado, para no saturar la interfaz.
- Cambiar Contracts es cambiar el acuerdo entre capas: trátalo como cambio de contrato,
  no como un refactor local.

### Segmentación

Bloques secuenciales de duración fija; el último conserva el resto sin rellenar. El corte es
**por tiempo, no por tamaño**. La numeración se rellena con ceros (`audio_parte_001`) para que
el orden alfabético coincida con el cronológico. Los 30 minutos son un parámetro que viaja en
la petición del proceso, no una constante escondida.

### Carpeta de trabajo

```
[carpeta elegida]
├── origen     video de entrada
├── temp       audio completo extraído
└── salida     audio_parte_001 … audio_parte_00N
```

Se crea si no existe y se verifica permiso de escritura antes de lanzar FFmpeg. Lo temporal se
limpia siempre — también ante error o cancelación. Al cancelar a mitad de corrida, los segmentos
ya escritos en `salida` se conservan; solo se borra `temp`.

## Comandos

```
dotnet build                                      # compilar la solución
dotnet test                                       # correr todos los tests
dotnet test --filter "FullyQualifiedName~NombreDelTest"   # correr un test puntual
dotnet run --project AudioSplitter.UI             # levantar la aplicación
```

`AudioSplitter.Domain` es la capa que se prueba entera **sin FFmpeg instalado**, sustituyendo la
implementación de Core por un doble contra la interfaz de Contracts. Ese es el objetivo de
testeo principal del proyecto.

## Decisiones cerradas

1. **Composition root** — proyecto `AudioSplitter.Bootstrap` aparte, que referencia las 4 capas
   y arma el contenedor DI. Resuelve la contradicción del documento original: Domain no conoce
   Core y la UI tampoco lo referencia, así que hacía falta un tercero que compusiera el grafo.
2. **Evento de progreso** — fijado en `Contracts/Progreso.cs`. Throttle de emisión mínima:
   250 ms, aplicado en Domain (es quien publica).
3. **FFmpeg** — se empaqueta junto a la aplicacion. PENDIENTE: revisar licencia LGPL/GPL
   antes de distribuir.

Sigue abierto: si se persiste la última carpeta de trabajo entre ejecuciones.

## Estado del desarrollo

Las 4 capas están implementadas y la aplicación corre. **36 tests en verde**:
32 de dominio (sin FFmpeg, con fakes) + 4 de integración (pipeline real con FFmpeg).

Notas que no se deducen leyendo el código:

- **La regla de los 30 minutos vive solo en `CalculadoraSegmentos` (Domain).** Core NO la
  recalcula: recibe `IReadOnlyList<SegmentoPlanificado>` ya resuelto. Un solo lugar que tocar.
- **División con techo sobre ticks**, no sobre `double`: el punto flotante rompe el conteo
  en duraciones límite.
- **El relleno de ceros crece con el total** (`audio_parte_0001` con más de 999 segmentos),
  o el orden alfabético deja de ser cronológico.
- **La limpieza de temp ocurre ANTES de publicar `Finalizado`**; si no, el último evento que
  ve el usuario es "Limpiando" en vez de "Listo". Hay un test que lo cubre.
- **`ProgresoAcotado` (250 ms)** deja pasar siempre los cambios de etapa y el cierre: perder
  un porcentaje intermedio es inocuo, perder "Listo" deja la ventana mintiendo.
- **`LocalizadorFfmpeg`** busca junto al ejecutable primero (decisión: empaquetado) y se cae
  a PATH para que el proyecto corra en desarrollo sin copiar el binario a mano.

### Formato de salida

`.m4a` (por defecto) o `.mp3`, elegido por el usuario y transportado en
`PeticionProceso.Formato`. La extensión la define `FormatoAudio.Extension()` en Contracts;
el códec de FFmpeg vive solo en Core.

- **Se recodifica UNA sola vez, en la extracción.** Los segmentos salen siempre por `-c copy`.
  Recodificar por segmento pagaría el costo N veces.
- **`.m4a` suele ser copia de stream** — los contenedores de video casi siempre traen AAC:
  rápido y sin pérdida. **`.mp3` obliga a recodificar siempre**, con pérdida generacional
  sobre un audio que ya venía comprimido. Se elige por compatibilidad, no por calidad,
  y el desplegable de la UI lo dice.
- Agregar un formato nuevo son tres puntos: el valor en `FormatoAudio`, su extensión en
  `Extension()`, y su códec en `ExtractorAudioFfmpeg.Codificador()`.

### Marca e identidad

El arte de origen es `docs/imagen.jpg` (banner 1024x559 con el icono en el centro).
De ahi salen los recursos del proyecto, empotrados en el ensamblado:

```
src/AudioSplitter.UI/Recursos/
  icono.ico     7 resoluciones (16-256), generado desde el recorte del banner
  icono.png     el recorte a 310x310, para dibujarlo dentro de la ventana
  portada.jpg   el banner completo, para la pantalla de carga
```

- El recorte del icono dentro del banner es `X=344, Y=84, lado=310`. Si se regenera,
  hay que MIRAR el resultado: con menos de ~310 se cortan las esquinas redondeadas.
- Un `.ico` lleva varias resoluciones porque Windows elige una distinta en cada lugar
  (16 en la barra de titulo, 32 en la barra de tareas, 256 en vistas grandes).
- Se cargan via `Recursos.cs` con `GetManifestResourceStream`. Si un recurso falta,
  devuelve null y la aplicacion arranca igual: una imagen ausente no tumba la app.
- `ApplicationIcon` en el csproj da el icono del .exe; `Form.Icon` da el de la ventana.
  Son dos cosas distintas y hacen falta las dos.

### Pantalla de carga

`PantallaCarga.cs` se muestra mientras se comprueba FFmpeg y se arma la ventana.

- El arranque real es casi instantaneo, asi que tiene un **tiempo minimo de 1100 ms**.
  Es una decision de marca, no una espera tecnica: sin el minimo seria un parpadeo.
- La espera usa `Application.DoEvents()` en bucle, NO `Thread.Sleep` a secas: una ventana
  sin borde se queda sin pintar si se bloquea el hilo de interfaz.
- El aviso de "FFmpeg no encontrado" se difiere a `OnShown` de la ventana principal.
  Mostrarlo en el constructor lo pondria sobre la portada, con la ventana principal
  todavia invisible detras.

### Pendiente

- Empaquetar `ffmpeg.exe` y `ffprobe.exe` en la salida del build, y revisar su licencia
  (el full build de gyan.dev es GPL: condiciona cómo se distribuye la aplicación).
- Definir si se persiste la última carpeta de trabajo entre ejecuciones.
