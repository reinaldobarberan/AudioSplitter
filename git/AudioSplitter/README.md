<div align="center">

<img src="docs/imagen.jpg" alt="AudioSplitter" width="640">

**Extrae el audio de un video y lo parte en bloques de duración fija.**

</div>

---

## Qué hace

Toma un video, separa su pista de audio y la divide en segmentos secuenciales de duración
fija (30 minutos por defecto), sin dejar la ventana congelada mientras trabaja.

| | |
|---|---|
| **Extrae el audio** | Separa la pista del contenedor. Copia el stream sin recodificar cuando el formato lo permite. |
| **Segmenta por tiempo** | Bloques de duración fija; el último conserva el resto sin rellenar. El corte es por tiempo, no por tamaño. |
| **Informa el avance** | Cada progreso de FFmpeg se traduce en un evento que la interfaz consume sin bloquear el hilo de UI. |
| **Carpeta dinámica** | La ruta de trabajo se resuelve en ejecución, en cada corrida. Nada fijo en el código. |

Formatos de salida: `.m4a` (copia de stream, rápido y sin pérdida) o `.mp3`
(recodifica, máxima compatibilidad).

## Requisitos

- **.NET 10 SDK**
- **FFmpeg** — opcional: si no está en la máquina, el build lo descarga y lo verifica
- **Windows** — la interfaz es Windows Forms

## Cómo correrlo

```bash
dotnet run --project src/AudioSplitter.UI
```

```bash
dotnet test
```

Un test puntual:

```bash
dotnet test --filter "FullyQualifiedName~EjemploDelDocumento"
```

## Distribución

Para llevar la aplicación a otra máquina, que puede no tener .NET ni FFmpeg instalados:

```bash
dotnet publish src/AudioSplitter.UI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

La carpeta `dist/` queda con tres archivos y **no necesita nada instalado en el destino**:

```
dist/
  AudioSplitter.UI.exe    la aplicación y el runtime de .NET
  ffmpeg.exe
  ffprobe.exe
```

**No hace falta tener FFmpeg instalado.** Si la máquina que compila no lo tiene, el build lo
descarga una sola vez, verifica su checksum y lo deja en `.tools/` (ignorado por git). Las
compilaciones siguientes usan esa copia.

Orden de resolución, de mayor a menor prioridad:

1. `-p:FfmpegDir="D:\ruta\a\ffmpeg\bin"` o la variable de entorno `FFMPEG_DIR`
2. La caché del repositorio: `.tools/ffmpeg/<versión>/`
3. Una instalación del sistema: `C:\ffmpeg\bin` o `C:\Program Files\ffmpeg\bin`
4. Descarga verificada por SHA-256 (ver `build/Ffmpeg.targets`)

Para desactivar la descarga: `-p:DescargarFfmpeg=false`. En ese caso, si no hay FFmpeg en
ningún lado, la compilación avisa y la aplicación queda dependiendo del `PATH` del destino.

> La versión y su checksum están fijados en `build/Ffmpeg.targets`. Es a propósito: una URL
> *latest* no se puede verificar. Si el hash no coincide, el build **falla** en lugar de
> meter un binario desconocido en el producto.

> **Antes de distribuir**: los builds completos de FFmpeg suelen ser GPL. Distribuir los
> binarios junto a la aplicación arrastra obligaciones de licencia. Revisalo.

## Arquitectura

Cuatro capas con dependencias en una sola dirección, más una raíz de composición.

```
UI  ──▶  Domain  ──▶  (interfaces)  ◀──  Core
 │         │                              │
 └─────────┴──────────▶ Contracts ◀───────┘        Contracts no referencia a nadie

                    Bootstrap
        el único que conoce las cuatro a la vez
```

| Proyecto | Responsabilidad |
|----------|-----------------|
| `AudioSplitter.Contracts` | DTOs, interfaces y el evento de progreso. **Cero dependencias.** |
| `AudioSplitter.Core` | Invoca FFmpeg como proceso externo. No decide reglas. |
| `AudioSplitter.Domain` | Orquesta, valida y consolida. No conoce FFmpeg. |
| `AudioSplitter.Bootstrap` | Raíz de composición: arma el grafo de dependencias. |
| `AudioSplitter.UI` | Windows Forms. Traduce interacción en petición y respuesta en pantalla. |

### Reglas que el diseño sostiene

1. **Contracts no depende de nadie.** Permite reemplazar Core sin tocar UI ni Domain.
2. **FFmpeg vive solo en Core.** Ninguna otra capa conoce su ruta, sus argumentos ni su salida.
3. **Domain no referencia Core.** Recibe la implementación por inyección desde Bootstrap,
   y por eso se prueba entero sin FFmpeg instalado.
4. **La excepción técnica nunca llega cruda a la pantalla.** Domain la traduce a un motivo
   de negocio antes de devolverla.
5. **La regla de los bloques vive en un solo lugar** (`CalculadoraSegmentos`, en Domain).
   Core recibe el plan ya resuelto y solo lo ejecuta.

## Pruebas

**41 en total.**

| Proyecto | Qué cubre |
|----------|-----------|
| `AudioSplitter.Domain.Tests` | 32 tests de dominio con dobles. Corren **sin FFmpeg instalado**. |
| `AudioSplitter.Integracion.Tests` | 9 tests: 4 de extremo a extremo (generan su propio video con FFmpeg y verifican la salida con ffprobe; se saltan si FFmpeg no está) y 5 sobre la resolución de FFmpeg y su mensaje de error. |

## Estructura

```
src/
  AudioSplitter.Contracts/    DTOs, interfaces, evento de progreso
  AudioSplitter.Core/         FFmpeg como proceso externo
  AudioSplitter.Domain/       orquestación, validación, la regla de los bloques
  AudioSplitter.Bootstrap/    raíz de composición
  AudioSplitter.UI/           Windows Forms
tests/
  AudioSplitter.Domain.Tests/
  AudioSplitter.Integracion.Tests/
build/
  Ffmpeg.targets              resolución, descarga y empaquetado de FFmpeg
docs/
  Detalle_Arquitectura_AudioSplitter.md   diseño de origen
  imagen.jpg                              arte de marca
```

`.tools/` y `dist/` se generan al compilar y no se versionan.

## Carpeta de trabajo

La elige el usuario en cada corrida. Se crea si no existe:

```
[carpeta elegida]
├── temp      audio completo extraído  (se borra siempre al cerrar)
└── salida    audio_parte_001 … audio_parte_00N
```

Si el proceso se cancela a mitad de camino, **los segmentos ya escritos se conservan**.
Solo se limpia lo temporal.

## Pendiente

- **Revisar la licencia de FFmpeg antes de distribuir.** Los builds completos suelen ser GPL,
  y eso condiciona cómo puede publicarse la aplicación. No hay licencia definida para este
  proyecto todavía.
- Definir si se recuerda la última carpeta de trabajo entre ejecuciones.
