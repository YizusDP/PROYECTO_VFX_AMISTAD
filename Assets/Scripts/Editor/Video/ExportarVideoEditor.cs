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
    const string Clave = "Amistad_ExportarVideo", ClaveRuta = "Amistad_ExportarRuta", ClaveAlto = "Amistad_ExportarAlto";
    const float Inicio = 0f, Fin = 72.9f, Fps = 30f;
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

    // alto = 1080 (1920x1080) o 2160 (4K, 3840x2160)
    public static void Exportar(int alto)
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Video] Sal de Play primero."); return; }
        var carpeta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));
        Directory.CreateDirectory(carpeta);
        SessionState.SetString(ClaveRuta, Path.Combine(carpeta, $"AMISTAD_{(alto >= 2160 ? "4K" : "1080p")}_{DateTime.Now:yyyy-MM-dd_HHmm}"));
        SessionState.SetInt(ClaveAlto, alto);
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
            Debug.Log("[Video] Listo: " + SessionState.GetString(ClaveRuta, "") + ".mp4");
        }
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
            enc.TargetBitRate = 60f;                       // Mbps
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
        ajustes.FrameRate = Fps;
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
