# Aura de Ki y transformación a Super Saiyajin (toma CM4)

Efecto de carga de Ki y transformación de Goku mientras vuela junto a la avioneta (vista del piloto).
Todo se arma con **Tools > Amistad > Goku: aura Ki y SSJ** y se quita con **Tools > Amistad > Goku: quitar aura Ki**.

## Línea de tiempo (segundos del Timeline principal)

| Tiempo | Qué pasa |
|---|---|
| 35.0 – 38.93 | Carga de Ki: aura delgada blanco-azul que late cada vez más rápido (de 2.5 a 6 latidos/s), motas que se juntan hacia el cuerpo, lenguas azules y los primeros rayos |
| 38.93 | Cambio Goku_Base → Goku_SSJ: destello blanco que tapa el cambio de modelo, golpe de luz y estallido de chispas y llamas |
| 38.93 – 43.3 | Aura dorada de SSJ: cascarón de llamas toon, lenguas doradas, motas que suben y rayos |

Las curvas viven en `Assets/Assets/3d/Persoanjes/Goku/Aura/Goku_AuraKi.anim` (pista "Goku · aura Ki" del Timeline de la ciudad) y se pueden retocar en el editor de curvas del Timeline.

## Piezas

### 1. Shader Graph `Amistad/SG_AuraKi` (requerimiento 1: shader personalizado)
Archivo: `Assets/Assets/shaders/AuraKi/SG_AuraKi.shadergraph` (Unlit, transparente, aditivo, sin sombras). Se dibuja sobre un **cascarón**: una copia de la malla de Goku con los mismos huesos.
- **Ruido que sube:** Position (Object) → Tiling And Offset (el offset es Time × Velocidad hacia abajo) → Gradient Noise. Se usa en vértice y en píxel. Va en espacio del objeto: Goku vuela a la velocidad de la avioneta y con coordenadas de mundo el patrón "resbalaría" por el cuerpo.
- **Vértice:** Position + Normal × Grosor × Carga × ruido, más un empuje hacia arriba (Llamas × Carga × ruido): el aura se infla y forma lenguas.
- **Píxel:** Fresnel Effect × ruido × Carga. Dos Smoothstep cortan la silueta (Umbral) y una segunda banda (Umbral + Banda). Encima del cuerpo el fresnel es casi 0, así que Goku se ve completo: el aura solo aparece alrededor de su silueta. Junto al cuerpo usa el color interior (claro) y hacia afuera el color principal, que es el look toon.
- **Paleta:** Lerp entre los colores Ki (blanco-azul) y SSJ (dorado) con la propiedad Mezcla. Destello lleva todo al color de destello y sube el alpha para tapar el cambio de modelo.
- **Propiedades que anima el Timeline:** Carga, Mezcla y Destello.
- **Picos solo arriba:** el empuje hacia arriba se multiplica por saturate(Normal.y)², así las caras de abajo no se mueven.
- **Relleno:** un alpha mínimo (Relleno × Carga) que tiñe el interior del aura, como en la animación.
- Un solo Shader Graph, tres materiales:
  - `Aura_Envoltura`: la gota de aura estilo caricatura. Es una esfera escalada (12 × 19 × 12) en `GOKU/AURA_KI/VFX` que sigue la cadera de Goku; tiene la orilla dorada, picos arriba y relleno transparente.
  - `Aura_Brillo`: contorno blanco pegado al cuerpo.
  - `Aura_Llamas`: lenguas sutiles sobre la silueta.

### 2. Shader `Amistad/Rayo Ki`
Archivo: `Assets/Assets/shaders/RayoKi.shader`. Para LineRenderer: núcleo blanco de corte nítido, banda de color y halo suave. El rayo se "enciende" del origen a la punta al nacer, adelgaza al apagarse y parpadea por tramos.

### 3. VFX (requerimiento 2)
Partículas con el shader `Amistad/Fuego VFX` del proyecto (modo llama toon y modo brasa), controladas por Control Tracks:
- **Carga_Ki:** motas con velocidad radial negativa (el Ki se junta en Goku) y lenguas azules.
- **Estallido_SSJ:** una sola ráfaga de chispas y de llamas que frenan (limit velocity con dampen).
- **Aura_SSJ:** lenguas doradas desde un elipsoide alrededor del cuerpo y motas que suben.
- Todas simulan en espacio **local**: el aura viaja con Goku. En espacio mundo se quedaría atrás a la velocidad de la avioneta.

### 4. Iluminación (requerimiento 3)
Luz puntual del aura (sin sombras): azul al cargar, dorada en SSJ, con un temblor leve y un golpe de luz blanca en el destello. Ilumina a Goku y al ala de la avioneta.

### 5. Optimización (requerimiento 5)
- **Cascarón de un solo submesh:** los modelos de Goku tienen muchos materiales (un submesh por material). El cascarón junta todos los triángulos en un submesh, así cada malla cuesta 2 draw calls (brillo + llamas) en vez de 2 por cada material.
- **Cero costo cuando está apagado:** el script apaga los cascarones y la luz cuando la carga es 0.
- **MaterialPropertyBlock** en vez de copias de material: no se crean materiales en tiempo de ejecución.
- Luz sin sombras, partículas con `maxParticles` acotado (60–200) y sin sombras.
- Bounds del cascarón ampliados a mano para que el culling no corte las llamas.

### 6. Integración (requerimiento 6)
- El script `AuraKi` (`Assets/Scripts/AuraKi.cs`) recibe los valores de la pista de animación y los reparte al shader, a la luz y a los rayos.
- **Determinista:** los rayos usan un hash con semilla = número de golpe (`tiempo × golpesPorSegundo`), no `Random` ni `Time.time`. Las partículas tienen semilla fija. Al adelantar o regresar el Timeline todo se ve igual en el mismo segundo.
- Se actualiza en `OnDidApplyAnimationProperties`, que Unity llama justo después de que el Timeline escribe los valores, así la vista previa funciona sin dar Play.
- Las partículas siguen la cadera del modelo visible (Goku_Base o Goku_SSJ).

## Notas
- Correr la herramienta **después** de "CM4: primera persona en la avioneta + Goku SSJ". Si cambian los tiempos del CM4, hay que volver a correrla.
- La herramienta prende Read/Write en los FBX de Goku (necesario para copiar la malla del cascarón).
- Ajustes rápidos: grosor y largo de las llamas en los materiales `Assets/Assets/shaders/AuraKi/Aura_Llamas.mat` y `Aura_Brillo.mat`; cantidad y largo de los rayos en el componente `AuraKi` de `GOKU/AURA_KI`.
