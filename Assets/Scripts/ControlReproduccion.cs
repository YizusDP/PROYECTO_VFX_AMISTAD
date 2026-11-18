using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// Controles de reproducción del corto en el build (manual de usuario, sección 7.2):
///   P o Espacio = pausar / reanudar · R = reiniciar · F = mostrar u ocultar FPS · Esc = salir.
/// La pausa congela Timeline, partículas y animación (Time.timeScale) y el audio (AudioListener.pause).
public class ControlReproduccion : MonoBehaviour
{
    public bool mostrarFps = false;

    bool pausado;
    float msSuavizado;
    GUIStyle estilo;

    void Update()
    {
        var k = Keyboard.current;
        msSuavizado = Mathf.Lerp(msSuavizado, Time.unscaledDeltaTime * 1000f, 0.05f);
        if (k == null) return;

        if (k.pKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) Pausar(!pausado);
        if (k.rKey.wasPressedThisFrame) Reiniciar();
        if (k.fKey.wasPressedThisFrame) mostrarFps = !mostrarFps;
        if (k.escapeKey.wasPressedThisFrame) Salir();
    }

    void Pausar(bool p)
    {
        pausado = p;
        Time.timeScale = p ? 0f : 1f;
        AudioListener.pause = p;
    }

    void Reiniciar()
    {
        Pausar(false);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void Salir()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnDestroy() { Time.timeScale = 1f; AudioListener.pause = false; }

    void OnGUI()
    {
        if (!mostrarFps && !pausado) return;
        if (estilo == null) estilo = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        estilo.normal.textColor = Color.white;
        if (mostrarFps)
        {
            float fps = msSuavizado > 0f ? 1000f / msSuavizado : 0f;
            GUI.Label(new Rect(16, 12, 400, 32), $"{fps:0} FPS  ·  {msSuavizado:0.0} ms", estilo);
        }
        if (pausado) GUI.Label(new Rect(16, 44, 400, 32), "PAUSA  (P para seguir)", estilo);
    }
}
