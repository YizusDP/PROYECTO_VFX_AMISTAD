# #Amistad

Corto animado en tiempo real (comedia, 2.5D) hecho en **Unity 6000.3.7f1 con URP 17.3**. Es el proyecto final de *Fundamentos Técnicos de VFX*, grupo 04, con el Ing. Carlos Aldair Román Balbuena.

- **Equipo:** Domínguez Palacios Jesús Alejandro (parte técnica) y Carrillo Rodríguez Guillermo (parte artística).
- **Duración:** 72.9 s.
- **Entrega:** 19 nov 2026. **Presentación:** 24 nov 2026.

## Cómo abrirlo

1. Clonar con Git LFS instalado: `git lfs install` y luego `git clone`. Las texturas, FBX, audio y lightmaps están en LFS.
2. Abrir la carpeta con **Unity 6000.3.7f1**.
3. Abrir `Assets/Scenes/ciudad.unity`. Es la única escena del build.
4. Dar **Play**. El Timeline principal (`Director_P1_Bosque`) arranca solo y corre el corto completo.

### Controles (en Play y en el build)

| Tecla | Acción |
|---|---|
| P o Espacio | Pausar o reanudar (congela Timeline, partículas y audio) |
| R | Reiniciar el corto |
| F | Mostrar u ocultar los FPS y los ms por cuadro |
| Esc | Salir |

> **Ojo al guardar la escena:** no guardes `ciudad.unity` con la vista previa del Timeline encendida. Unity guarda las poses y estados evaluados del Timeline como si fueran los valores normales. Cierra la ventana Timeline antes de guardar.

## Estructura

```
Assets/
  Scenes/ciudad.unity      Escena única: 0 · GENERAL, 1 · BOSQUE, 2 · TÍTULO, 3 · CIUDAD
  Scenes/ciudad/           Lightmaps y reflection probes horneados
  Timelines/               Timeline principal y uno por escena (ver abajo)
  Animaciones/             Clips .anim, controllers, máscaras y FBX de animación (Gato, Saludo, Bosque)
  Arte/Modelos/            Personajes, Escenarios, Elementos, Avioneta, Titulo
  Arte/2D/                 Skybox
  Audio/                   Audio final del corto
  Shaders/                 Shaders propios (Shader Graph y HLSL), sus materiales y texturas
  Scripts/                 Scripts de runtime (derrumbe horneado, aura, luces, cámara, control)
  Scripts/Editor/Video/    Exportador de video MP4 (Tools > Amistad > Revisión y pruebas)
  Iluminacion/             Lighting Settings del horneado
  Settings/                URP (PC_RPAsset y Mobile_RPAsset) y perfiles de volumen
  Terceros/ithappy/        Modelo del gato (Kitty) de ithappy
Herramientas_archivadas/   Herramientas de editor que armaron el corto, fuera de Assets (ver su LEEME)
Docs/                      Capturas de iluminación antes/después y scripts de apoyo
Blender/                   Gato_animaciones.blend (animaciones propias del gato)
```

## Timelines

El Timeline principal `Director_P1_CascadaTimeline` solo coordina: un Control Track por escena, el Cinemachine Track con los cortes y la pista de audio.

| Escena | Timeline | Contenido |
|---|---|---|
| 1 · Bosque | `CM1_CASCADATimeline` | Dolly de la cascada, gato (acicalarse, susto y carrera), explosión del bosque |
| 2 · Título | `CM2_TITULOTimeline` | Caída de las 8 letras, explosiones y polvo de impacto |
| 3 · Ciudad | `Director_P2_CiudadTimeline` | Caminatas de Memi y Ale, fuego, derrumbe del ruim.008, Godzilla, avioneta, Goku, encuentro, saludo y título final |

La Ciudad usa tiempo local: **tiempo local = tiempo del principal − 17.5 s**. Los directores de cada escena tienen Play On Awake apagado y solo los arranca el principal.

## Shaders propios

| Shader | Tipo | Uso |
|---|---|---|
| `WaterFall_Mejorado` | Shader Graph | Agua de la cascada |
| `Cascada/PozaAgua` | HLSL | Poza al pie de la cascada (profundidad, espuma) |
| `PolvoProcedural` | Shader Graph | Polvo de impacto de las letras, con Depth Fade |
| `Explosion`, `Explosion_Transparente`, `Flama` | Shader Graph | Explosiones y flamas |
| `FuegoVFX` | HLSL | Fuego, humo, brasas y calor de la ciudad |
| `TOON_Godzilla` | Shader Graph | Toon por bandas, Fresnel y emisión que pulsa |
| `Personajes/PersonajeToon` | HLSL | Piel y ropa toon de Memi y Ale, con línea de tinta |
| `AuraKi/SG_AuraKi`, `RayoKi` | Shader Graph / HLSL | Aura y rayo de Ki de Goku |
| `Caos/*` (Letras Radiactivas v2, GoteoRadiactivo, CharcoRadiactivo, ManchaSuelo) | HLSL | Título final con líquido radiactivo y suciedad del piso |

## Scripts de runtime

| Script | Qué hace |
|---|---|
| `Derrumbe/DerrumbeHorneado`, `DerrumbeTrack`, `DerrumbeClip` | Reproduce desde Timeline el derrumbe del edificio, simulado y horneado (360 piezas a 30 cps) |
| `AuraKi` | Ajusta el aura de Goku a su pose en cada cuadro |
| `LuzParpadeo`, `LuzRadiactiva` | Luces de fuego y radiación |
| `NieblaCiudad`, `SombrasCiudad` | Niebla y distancia de sombras por escena |
| `DisolvenciaCamaras` | Disolvencia entre cámaras |
| `MiradaFija`, `PosturaErguida`, `SaludoControl`, `VelocidadAnimator`, `CaminataSencilla` | Ajustes de los personajes en el encuentro y el saludo |
| `GirarHelice`, `VegetacionPiso` | Hélice de la avioneta, vegetación del bosque |
| `ControlReproduccion` | Controles del build: pausa, reinicio, FPS y salir |

## Créditos de recursos externos

El detalle completo, con la modificación hecha a cada recurso, está en la sección 9 de la documentación.

- Avioneta: Unity Asset Store, *Simple Airplane Controller* de HeneGames (ver `Arte/Modelos/Avioneta/ATRIBUCION.txt`).
- Gato: *Animals FREE* de ithappy (Unity Asset Store). Las animaciones son propias, hechas en Blender.
- Animaciones de caminata (Memi, Ale, Godzilla): Mixamo (Adobe).
- Escenarios: *city-ruins-environment*, *the-landscape-is-a-forest-in-the-mountains* y *mountain_terrain_02*, rocas *rock_set*.
- Godzilla y Goku: modelos descargados de internet, retexturizados, reescalados y con shaders y animaciones propios.
- Skybox: *skybox DEMO*.
