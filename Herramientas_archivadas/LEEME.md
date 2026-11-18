# Herramientas de editor archivadas

Scripts de editor (menú Tools > Amistad) que ya cumplieron su función. Se sacaron de `Assets/` para que no salgan en el menú
ni se compilen, pero se conservan como evidencia del pipeline técnico (requerimiento 6: integración de sistemas).
Lo que generaron (objetos, materiales, Timelines, datos horneados) sigue en la escena; quitarlas no cambia el corto.

Para volver a usar una: copiar su `.cs` y `.cs.meta` a `Assets/Scripts/Editor/`.

| Archivo | Menú | Qué hacía |
|---|---|---|
| AnalizarCascada.cs | Analizar cascada (consola) | Diagnóstico de la cascada en la consola |
| AnalizarEdificio.cs | Analizar edificio (ruim.008) | Diagnóstico del edificio antes del derrumbe |
| MedirEscena.cs | Medir tamaños (consola) | Medía tamaños para fijar la escala (1 m = 8.43 u) |
| CiudadDiagnosticoEditor.cs | Ciudad: diagnóstico | Listaba qué objetos tapaban la avenida |
| EncuadreMCEditor.cs | Encuadre de MC (derrumbe) | Toma de MC; obsoleta (MC se reemplazó por Memi y escribía en el Timeline principal) |
| CapturasTomaEditor.cs | Capturas de la toma de la ciudad | Capturas fuera de Play; reemplazada por "Capturas en Play" |
| MemiAleEditor.cs | Memi y Ale: importar | Importó a Memi y Ale como Humanoid a escala y puso a Memi en lugar de MC |
| VegetacionPisoEditor.cs | Crear vegetación en Piso_FOREST | Generó la vegetación procedural del bosque |
| FuegoVFXEditor.cs | Crear fuego VFX en CITY | Creó materiales y prefabs del fuego (shader Amistad/Fuego VFX) |
| DerrumbeEditor.cs | Derrumbar edificio (ruim.008) | Partió y horneó la simulación del derrumbe (DerrumbeHorneado) |
| TomaMemoEditor.cs | CM3_MEMO: recorrer toma | Armó la toma de Memi en la ciudad y el corte a CM3_GODZILLA |

## Archivadas el 17 nov 2026 (cierre del proyecto)

Al cerrar el corto se sacaron de `Assets/` todas las herramientas de editor que quedaban. Lo que generaron ya está guardado en `ciudad.unity`, en los Timelines y en los assets horneados, así que el corto se ve igual sin ellas. En `Assets/Scripts/Editor/Video/` solo se dejó el exportador de video (Tools > Amistad > Revisión y pruebas > Exportar video MP4).

Ojo: varias dependen entre sí (por ejemplo, `FinalTituloEditor` llama a `TituloEpicoEditor` y `AlturaPiesEditor` usa `MedirPiesEditor`). Si se regresa una, conviene regresar el grupo completo.

| Archivo | Menú (Tools > Amistad > …) |
|---|---|
| AgarreEditor.cs | 3 · Encuentro, saludo y final/Solo: agarre (dedos sin atravesarse) |
| AlturaPiesEditor.cs | 3 · Encuentro, saludo y final/Solo: pies al piso (mide en Play y corrige alturas) · Revisión y pruebas/Pies al piso: aplicar última medición |
| AvionetaEditor.cs | 2 · Ciudad/Godzilla: pasa la avioneta |
| BordeLuzEditor.cs | 4 · Iluminación/Borde de luz en Ale y Memi (rim light) · 4 · Iluminación/Borde de luz: quitar (materiales originales) |
| BosqueCamarasEditor.cs | 1 · Bosque/Cascada + disolvencia al gato · Revisión y pruebas/Capturas del bosque |
| BosqueExplosionEditor.cs | 1 · Bosque/Explosión del bosque: a tiempo con el gato + humo y polvo |
| CaminataAleEditor.cs | 2 · Ciudad/Caminata de Ale (43-46 s) |
| CapturasPlayEditor.cs | Revisión y pruebas/Capturas en Play (toma de la ciudad, tiempo real) |
| CapturasTiemposEditor.cs | Revisión y pruebas/Capturas por tiempo (Play, saltando) |
| CascadaPozaEditor.cs | 1 · Bosque/Cascada: poza, espuma y shader |
| ChoqueAvionetaEditor.cs | 2 · Ciudad/Godzilla llega + choque de la avioneta (43-46 s) |
| CiudadAmpliadaEditor.cs | 2 · Ciudad/Ampliar (más grande) · Revisión y pruebas/Ciudad: capturas de revisión |
| CiudadCaosEditor.cs | 2 · Ciudad/Caos (fuego, polvo, suciedad, radiación) · Revisión y pruebas/Ciudad: capturas del caos |
| CiudadTomasEditor.cs | 2 · Ciudad/Memi de perfil + Ale + recorrer derrumbe |
| CuerpoTimelineEditor.cs | 3 · Encuentro, saludo y final/Solo: cuerpo de Ale y Memi en el Timeline (preview) |
| DiagExplosionEditor.cs | zz Diagnóstico explosión |
| EncuentroEditor.cs | 3 · Encuentro, saludo y final/Escena 7: encuentro (46-49 s) |
| EnfrentamientoEditor.cs | 3 · Encuentro, saludo y final/Solo: enfrentamiento (mirada fija, puños, viento) |
| FinalTituloEditor.cs | 3 · Encuentro, saludo y final/Solo: final (explosión, negro y título) |
| ForcejeoEditor.cs | 3 · Encuentro, saludo y final/Solo: forcejeo más fuerte (59.6-62 s) |
| FuegoToonEditor.cs | 2 · Ciudad/Fuego estilo caricatura · Revisión y pruebas/Ciudad: probar fuego caricatura |
| GatoAnimacionEditor.cs | 1 · Bosque/Gato: acicalarse, susto y carrera |
| GatoKittyEditor.cs | 1 · Bosque/Kitty reemplaza al gato (CM1) |
| GatoRevisionEditor.cs | Revisión y pruebas/Gato: revisar modelo · Revisión y pruebas/Gato: revisar Kitty (ithappy) |
| GatoSentadoCorreccion.cs | 1 · Bosque/Gato: corregir patas traseras (sentado) |
| GokuAuraEditor.cs | 2 · Ciudad/Goku: aura Ki y SSJ · 2 · Ciudad/Goku: quitar aura Ki |
| GokuPruebaEditor.cs | Revisión y pruebas/Goku: prueba de transformaciones |
| GokuVueloEditor.cs | 2 · Ciudad/CM4: primera persona en la avioneta + Goku SSJ |
| GokuVueloFixEditor.cs | Revisión y pruebas/Goku: revisar vuelo · 2 · Ciudad/Goku: corregir vuelo |
| IluminacionEditor.cs | 4 · Iluminación/Hornear (prepara + hornea en segundo plano) · 4 · Iluminación/Solo preparar (sin hornear) |
| ManosDefinicionEditor.cs | 3 · Encuentro, saludo y final/Manos con más definición (Ale y Memi) · 3 · Encuentro, saludo y final/Manos con más definición: quitar (malla original) |
| MedirPiesEditor.cs | Revisión y pruebas/Medir pies y manos (Play, cada 0.1 s) |
| NuclearDetalleEditor.cs | 3 · Encuentro, saludo y final/Solo: detalle de la explosión nuclear (humo y temblor) |
| PersonajesToonEditor.cs | 4 · Iluminación/Ale y Memi: piel toon (sin look de plástico) |
| PinturaPersonajesEditor.cs | 4 · Iluminación/Ale y Memi: repintar texturas (hornear) |
| PosturaMemiEditor.cs | 3 · Encuentro, saludo y final/Postura erguida de Memi |
| SaludoAjustesEditor.cs | 3 · Encuentro, saludo y final/Solo: media cara de Ale + tomas de las manos |
| SaludoEpicoEditor.cs | 3 · Encuentro, saludo y final/Escena 8: saludo épico + final (49-72.9 s) |
| TituloEpicoEditor.cs | 3 · Encuentro, saludo y final/Título final épico (chispas y fundido a negro) |
| _OrdenarUnaVezEditor.cs | Sin menú. Uso único (8 nov): ordenó el Timeline principal y la jerarquía en carpetas |
