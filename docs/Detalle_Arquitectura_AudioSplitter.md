# AudioSplitter — Arquitectura de 4 capas

**Documentación técnica · v1.0**
Extracción y segmentación automática de audio en bloques de 30 minutos, con FFmpeg y reporte de progreso asíncrono.

Stack: C# / .NET · Windows Forms · FFmpeg como proceso externo.

---

## 1. El sistema en una frase

Toma un video, devuelve su audio partido en segmentos secuenciales de 30 minutos, y nunca deja la ventana congelada mientras lo hace.

### Capacidades

| # | Capacidad | Descripción |
|---|-----------|-------------|
| 01 | Extrae el audio | Separa la pista de audio del contenedor de video de entrada, sin recodificar cuando el formato lo permite. |
| 02 | Segmenta cada 30 min | Divide la pista en partes secuenciales de duración fija; la última conserva el resto sin rellenar. |
| 03 | Informa el avance | Cada progreso de FFmpeg se traduce en un evento que la interfaz consume sin bloquear el hilo de UI. |
| 04 | Carpeta dinámica | El usuario elige dónde trabajar: la ruta se resuelve en ejecución, no vive fija en el código. |

---

## 2. Estructura de la solución

Un proyecto por capa. La separación es estricta: ningún proyecto asume el trabajo de otro.

| Proyecto | Rol | Responsabilidad |
|----------|-----|-----------------|
| `AudioSplitter.UI` | WinForms | Controles, selección de carpeta de trabajo, `ProgressBar` y llamadas asíncronas al dominio. |
| `AudioSplitter.Domain` | Servicios | Orquesta el proceso, valida reglas de negocio y concentra la lógica de los helpers. |
| `AudioSplitter.Contracts` | Modelos / Interfaces | DTOs, interfaces de servicio y eventos de progreso. **No depende de ninguna otra capa.** |
| `AudioSplitter.Core` | Infraestructura | Invoca FFmpeg de forma nativa y asíncrona; ejecuta el trabajo pesado del proceso. |

### Regla de dependencias

```
UI  ──▶  Domain  ──▶  Core
 │         │           │
 └─────────┴───────────┴──▶  Contracts   (no referencia a nadie)
```

Las referencias apuntan en una sola dirección. Al no depender de nadie, Contracts permite cambiar la implementación de Core sin tocar la UI.

> Punto de revisión: si alguien necesita referenciar `Core` desde la UI, la responsabilidad está mal ubicada.

---

## 3. Detalle por capa

### 3.1 `AudioSplitter.UI` — Presentación

Recoge la intención del usuario, dispara el proceso de forma asíncrona y refleja el avance.

- **Entrada del usuario** — selección del video de origen y de la carpeta de trabajo mediante diálogos del sistema.
- **Llamadas asíncronas** — el proceso se invoca sin bloquear el hilo de interfaz; los controles se deshabilitan mientras corre.
- **Avance visible** — `ProgressBar` y etiqueta de estado suscritas al evento de progreso que publica el dominio.

No contiene reglas de negocio ni rutas de FFmpeg: solo traduce interacción en una petición y una respuesta en pantalla.

> Regla práctica: si un archivo de la UI menciona `ffmpeg`, se movió mal el código.

### 3.2 `AudioSplitter.Domain` — Servicios

El cerebro del proceso: ordena los pasos, verifica que se puedan dar y consolida el resultado.

- **Orquestación** — encadena extracción y segmentación, y decide qué hacer ante un paso fallido.
- **Validación** — existencia del video, formato soportado, carpeta escribible y espacio disponible antes de empezar.
- **Helpers** — cálculo de segmentos, nomenclatura de archivos y normalización de rutas de trabajo.

Consume las interfaces declaradas en Contracts; recibe la implementación de Core por inyección, sin conocerla. Es la capa que se puede probar entera sin FFmpeg instalado, sustituyendo la implementación de Core.

### 3.3 `AudioSplitter.Contracts` — Contratos

El vocabulario común. Sin dependencias, sin lógica: solo la forma de lo que se pide y lo que se devuelve.

- **Interfaces** — los servicios de extracción y segmentación se declaran aquí y se implementan en Core.
- **DTOs** — petición de proceso, descripción de cada segmento generado y resultado consolidado de la corrida.
- **Evento de progreso** — porcentaje, etapa actual, segmento en curso y mensaje legible para el usuario.

Cambiar este proyecto es cambiar el acuerdo entre capas: toda modificación se revisa como cambio de contrato. El evento de progreso es el contrato más frágil; conviene fijar su forma antes de escribir Core.

### 3.4 `AudioSplitter.Core` — Infraestructura

Donde ocurre el trabajo pesado: invocación nativa y asíncrona de FFmpeg como proceso externo.

- **Proceso externo** — lanza FFmpeg sin ventana, con redirección de salida, y espera su término de forma asíncrona.
- **Lectura del avance** — interpreta la salida de FFmpeg para saber en qué tiempo del audio va y traducirlo a porcentaje.
- **Cancelación** — un token de cancelación corta el proceso y limpia los archivos parciales de la carpeta temporal.

No decide reglas: recibe rutas y parámetros ya validados por Domain, y devuelve hechos o errores.

> Decisión pendiente: FFmpeg se distribuye junto a la aplicación o se resuelve por configuración. Definir antes de empaquetar.

---

## 4. Pantallas

### 4.1 Ventana principal

Una sola ventana. Todo el proceso ocurre sin cambiar de pantalla ni abrir diálogos intermedios.

```
┌─ AudioSplitter ─────────────────────────────────────────────┐
│                                                             │
│  Archivo de video    [ C:\…\reunion.mp4      ] [ Examinar ] │
│  Carpeta de trabajo  [ D:\audio\salida       ] [ Examinar ] │
│  Segmento            [ 30 min ]  [ Procesar ]  [ Cancelar ] │
│                                                             │
│  [██████████████████░░░░░░░░░░░]                      62 %  │
│  Segmentando · parte 3 de 4                                 │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ audio_parte_001 · 30:00                             │    │
│  │ audio_parte_002 · 30:00                             │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

1. Origen y destino se eligen con diálogos del sistema; el campo queda de solo lectura.
2. La duración del segmento es un parámetro visible, con 30 minutos por defecto.
3. `Procesar` solo se habilita con ambas rutas válidas; `Cancelar` solo durante la corrida.
4. Barra y porcentaje se alimentan del mismo evento de progreso, no de un temporizador.
5. El listado crece a medida que cada segmento termina de escribirse en disco.

*Wireframe conceptual, no maqueta final: define controles y jerarquía, no colores ni tipografía.*

### 4.2 Estados de la ventana

La UI no cambia de pantalla: cambia qué está habilitado y qué muestra la barra de estado.

| Estado | Comportamiento |
|--------|----------------|
| **Inicial** | Campos vacíos, `Procesar` deshabilitado, listado oculto. Solo `Examinar` responde. |
| **En proceso** | Entradas bloqueadas, `Cancelar` activo, barra y etapa actualizándose por evento. |
| **Finalizado** | Listado completo, acceso a la carpeta de salida y campos habilitados de nuevo. |
| **Error o cancelado** | Mensaje claro del motivo, temporales limpiados y la ventana vuelve a estado inicial. |

El estado lo determina el resultado que devuelve el dominio, no la UI: la ventana solo refleja lo que recibe. Los segmentos ya escritos se conservan si se cancela a mitad de corrida; solo se borra lo temporal.

---

## 5. Flujo del proceso

| # | Paso | Qué ocurre | Capa |
|---|------|-----------|------|
| 01 | Selección y validación | Se eligen video y carpeta de trabajo; se verifican rutas, formato y permisos. | UI → Domain |
| 02 | Lectura de metadatos | Se obtiene la duración total del audio para calcular cuántos segmentos habrá. | Core |
| 03 | Extracción del audio | La pista se separa del contenedor y queda en la carpeta temporal de trabajo. | Core |
| 04 | Segmentación en bloques | FFmpeg corta cada 30 minutos y escribe los archivos numerados en la salida. | Core |
| 05 | Cierre y reporte | Se limpia lo temporal y se devuelve el listado de segmentos generados. | Domain → UI |

Los pasos 2 a 4 son los que reportan progreso; 1 y 5 son instantáneos desde la perspectiva del usuario.

---

## 6. Reporte de progreso

De la salida de FFmpeg al `ProgressBar`, sin que la interfaz se quede esperando:

```
Core observa  ──▶  Domain publica  ──▶  UI pinta
```

- **Core observa** — lee la salida del proceso y calcula el avance sobre la duración total ya conocida.
- **Domain publica** — convierte ese avance en el evento definido en Contracts, con etapa y mensaje.
- **UI pinta** — recibe el evento en el hilo de interfaz y actualiza barra, porcentaje y estado.

Consideraciones:

- **Dos niveles de avance** — progreso del segmento actual y progreso global de la corrida, en una sola barra por decisión de diseño.
- **Frecuencia acotada** — el evento se emite con intervalo mínimo para no saturar la interfaz con cientos de actualizaciones.

La UI nunca toca la salida cruda de FFmpeg: si necesita un dato nuevo, se agrega al evento en Contracts.

---

## 7. La regla de los 30 minutos

Bloques secuenciales de duración fija; el último conserva lo que queda. Ejemplo con un audio de 1 h 35 min:

| Segmento | Inicio | Duración | Archivo de salida |
|----------|--------|----------|-------------------|
| 1 | 00:00:00 | 30:00 | `audio_parte_001` |
| 2 | 00:30:00 | 30:00 | `audio_parte_002` |
| 3 | 01:00:00 | 30:00 | `audio_parte_003` |
| 4 | 01:30:00 | 05:00 | `audio_parte_004` |

El corte es por tiempo, no por tamaño. La numeración se rellena con ceros para que el orden alfabético coincida con el cronológico. La duración de 30 minutos es un parámetro, no una constante escondida: viaja en la petición del proceso.

---

## 8. Carpeta de trabajo dinámica

La ruta la elige el usuario en cada corrida. Nada queda fijo en el código ni en el instalador.

```
[carpeta elegida]
├── origen     video de entrada
├── temp       audio completo extraído
└── salida     audio_parte_001 … audio_parte_00N
```

- **Se crea si no existe** — la estructura se genera al inicio del proceso y se verifica permiso de escritura antes de lanzar FFmpeg.
- **Lo temporal se limpia** — el audio intermedio se elimina al cerrar la corrida, incluso si el proceso termina con error o cancelación.
- **Una ruta, una responsabilidad** — la UI entrega la ruta base; Domain la normaliza; Core solo recibe rutas absolutas ya resueltas.

> Decisión pendiente: si se recuerda la última carpeta usada entre ejecuciones.

---

## 9. Validaciones y manejo de errores

Cada capa atiende sus propios errores y entrega hacia arriba un mensaje que el usuario pueda entender.

| Capa | Qué verifica | Qué entrega si falla |
|------|--------------|----------------------|
| UI | Que haya video y carpeta seleccionados antes de habilitar el botón de inicio. | Aviso en pantalla, sin llamar al dominio. |
| Domain | Formato soportado, permisos de escritura, espacio en disco y duración válida. | Resultado fallido con motivo de negocio. |
| Core | Disponibilidad de FFmpeg, código de salida del proceso y archivos efectivamente escritos. | Excepción técnica con la salida del proceso. |
| Contracts | Nada en ejecución: define la forma del resultado fallido para las tres capas. | — |

> Regla: la excepción técnica no llega nunca cruda a la pantalla; Domain la traduce antes.

---

## 10. Decisiones que sostienen el diseño

1. **Contracts no depende de nadie** — permite reemplazar Core sin tocar UI ni Domain.
2. **FFmpeg vive solo en Core** — ninguna otra capa conoce su ruta, sus argumentos ni su salida.
3. **Todo el trabajo es asíncrono** — la ventana responde incluso con archivos de varias horas.
4. **La ruta es un dato, no una constante** — la carpeta de trabajo se resuelve en ejecución, en cada corrida.

### Pendientes de cierre

- Confirmar la forma definitiva del evento de progreso (campos y frecuencia).
- Definir si FFmpeg se empaqueta con la aplicación o se resuelve por configuración.
- Definir si se persiste la última carpeta de trabajo usada.
