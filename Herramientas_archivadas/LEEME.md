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
