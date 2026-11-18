using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// Tools > Amistad > Revisión y pruebas > Exportar video MP4 (sin audio)   (2026-11-11)
/// Graba el corto completo (Timeline principal, 0 – 72.9 s) con Unity Recorder: Game view a 1920x1080, 30 fps constantes,
/// MP4 H.264 calidad alta y SIN audio (el AudioSource con el audio guía no se graba).
/// Entra a Play, graba y sale de Play solo. El video queda en <proyecto>/Recordings/AMISTAD_<fecha>.mp4
/// Con fps constantes el Recorder fija Time.captureFramerate: aunque la PC vaya lenta, el video sale a velocidad real.
/// </summary>
[InitializeOnLoad]
public static class ExportarVideoEditor
{
    const string Clave = "Amistad_ExportarVideo", ClaveRuta = "Amistad_ExportarRuta", ClaveAlto = "Amistad_ExportarAlto", ClaveVista = "Amistad_ExportarVista", ClaveFps = "Amistad_ExportarFps";
    const float Inicio = 0f, Fin = 72.9f;
    static RecorderController control;

    static ExportarVideoEditor()
    {
        EditorApplication.playModeStateChanged -= Cambio;
        EditorApplication.playModeStateChanged += Cambio;
    }

    [MenuItem("Tools/Amistad/Revisión y pruebas/Exportar video MP4 (sin audio)", false, 950)]
    public static void Exportar() { Exportar(1080); }

    [MenuItem("Tools/Amistad/Revisión y pruebas/Exportar video MP4 4K (sin audio)", false, 951)]
    public static void Exportar4K() { Exportar(2160); }

    // Para la entrega: 4K a 60 fps y 100 Mbps. Luego se baja a 1080p con ffmpeg (supermuestreo: quita el serrucho y el ruido).
    [MenuItem("Tools/Amistad/Revisión y pruebas/Exportar video MP4 4K 60 fps (entrega, sin audio)", false, 952)]
    public static void Exportar4K60() { Exportar(2160, 60); }

    // alto = 1080 (1920x1080) o 2160 (4K, 3840x2160)
    public static void Exportar(int alto) { Exportar(alto, 30); }

    public static void Exportar(int alto, int fps)
    {
        SessionState.SetInt(ClaveFps, fps);
        if (EditorApplication.isPlaying) { Debug.LogError("[Video] Sal de Play primero."); return; }
        var carpeta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));
        Directory.CreateDirectory(carpeta);
        SessionState.SetString(ClaveRuta, Path.Combine(carpeta, $"AMISTAD_{(alto >= 2160 ? "4K" : "1080p")}{fps}_{DateTime.Now:yyyy-MM-dd_HHmm}"));
        SessionState.SetInt(ClaveAlto, alto);
        SessionState.SetInt(ClaveVista, TamanoVista(-1));   // el Recorder cambia el tamaño del Game view: se regresa al terminar
        SessionState.SetBool(Clave, true);
        Debug.Log("[Video] Entrando a Play para grabar 0 – 72.9 s…");
        EditorApplication.isPlaying = true;
    }

    static void Cambio(PlayModeStateChange estado)
    {
        if (!SessionState.GetBool(Clave, false)) return;
        if (estado == PlayModeStateChange.EnteredPlayMode) Empezar();
        else if (estado == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Clave, false);
            int vista = SessionState.GetInt(ClaveVista, -1);
            if (vista >= 0) TamanoVista(vista);
            Debug.Log("[Video] Listo: " + SessionState.GetString(ClaveRuta, "") + ".mp4");
        }
    }

    // Lee (indice < 0) o pone el tamaño seleccionado del Game view (p. ej. "Full HD"). Devuelve el índice que había.
    static int TamanoVista(int indice)
    {
        try
        {
            var tipo = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            var vistas = Resources.FindObjectsOfTypeAll(tipo);
            if (vistas.Length == 0) return -1;
            var prop = tipo.GetProperty("selectedSizeIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            int antes = (int)prop.GetValue(vistas[0], null);
            if (indice >= 0) { prop.SetValue(vistas[0], indice, null); ((EditorWindow)vistas[0]).Repaint(); }
            return antes;
        }
        catch { return -1; }
    }

    static void Empezar()
    {
        Application.runInBackground = true;   // que siga grabando aunque Unity no tenga el foco
        var ajustes = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var video = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        video.name = "AMISTAD";
        video.Enabled = true;
        int alto = SessionState.GetInt(ClaveAlto, 1080), ancho = alto * 16 / 9;
        var enc = new CoreEncoderSettings { Codec = CoreEncoderSettings.OutputCodec.MP4 };
        if (alto >= 2160)
        {
            // 4K: calidad personalizada con más bits (con "High" a 4K se ve comprimido en el fuego y el humo)
            enc.EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Custom;
            enc.TargetBitRate = SessionState.GetInt(ClaveFps, 30) >= 60 ? 100f : 60f;   // Mbps
            enc.EncodingProfile = CoreEncoderSettings.H264EncodingProfile.High;
            enc.KeyframeDistance = 30;
        }
        else enc.EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High;
        video.EncoderSettings = enc;
        video.CaptureAlpha = false;
        video.CaptureAudio = false;                      // sin el audio guía
        video.ImageInputSettings = new GameViewInputSettings { OutputWidth = ancho, OutputHeight = alto };
        video.OutputFile = SessionState.GetString(ClaveRuta, "Recordings/AMISTAD");
        ajustes.AddRecorderSettings(video);
        ajustes.SetRecordModeToTimeInterval(Inicio, Fin);
        ajustes.FrameRatePlayback = FrameRatePlayback.Constant;
        ajustes.FrameRate = SessionState.GetInt(ClaveFps, 30);
        ajustes.CapFrameRate = true;

        control = new RecorderController(ajustes);
        control.PrepareRecording();
        if (!control.StartRecording()) { Debug.LogError("[Video] No se pudo empezar a grabar."); Terminar(); return; }
        EditorApplication.update -= Vigilar;
        EditorApplication.update += Vigilar;
    }

    static void Vigilar()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Vigilar; return; }
        if (control != null && control.IsRecording()) return;
        Terminar();
    }

    static void Terminar()
    {
        EditorApplication.update -= Vigilar;
        control = null;
        EditorApplication.isPlaying = false;
    }
}
