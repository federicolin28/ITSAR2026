using UnityEngine;

/// <summary>
/// Fa pulsare l'intensità dell'emission del materiale URP/Lit del cristallo
/// con un'onda sinusoidale, tra un valore minimo (spento) e uno massimo (acceso).
/// Da aggiungere all'oggetto del cristallo (che ha il Renderer).
/// </summary>
[RequireComponent(typeof(Renderer))]
public class PulseLight : MonoBehaviour
{
    [Header("Intensità emission")]
    [Tooltip("Intensità minima (cristallo 'spento').")]
    [SerializeField, Min(0f)] private float minIntensity = 0f;

    [Tooltip("Intensità massima (cristallo 'acceso').")]
    [SerializeField, Min(0f)] private float maxIntensity = 3f;

    [Header("Animazione")]
    [Tooltip("Pulsazioni complete al secondo (Hz).")]
    [SerializeField, Min(0f)] private float pulsesPerSecond = 0.5f;

    [Tooltip("Sfasamento iniziale della sinusoide, da 0 a 1 (utile per più cristalli sfalsati).")]
    [SerializeField, Range(0f, 1f)] private float phaseOffset = 0f;

    [Header("Colore")]
    [Tooltip("Colore base dell'emission. Se 'Use Material Color' è attivo viene letto dal materiale.")]
    [SerializeField, ColorUsage(false, true)] private Color emissionColor = Color.white;
    [SerializeField] private bool useMaterialColor = true;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private Material material;
    private Color baseColor;

    private void Awake()
    {
        // .material crea un'istanza del materiale solo per questo oggetto,
        // così non modifichiamo l'asset condiviso.
        material = GetComponent<Renderer>().material;
        material.EnableKeyword("_EMISSION");
        // Evita che il GI consideri l'emission statica e ignori le modifiche a runtime.
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        baseColor = useMaterialColor && material.HasProperty(EmissionColorId)
            ? NormalizeColor(material.GetColor(EmissionColorId))
            : emissionColor;
    }

    private void Update()
    {
        // Seno normalizzato 0..1
        float t = Mathf.Sin((Time.time * pulsesPerSecond + phaseOffset) * Mathf.PI * 2f) * 0.5f + 0.5f;
        float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);

        material.SetColor(EmissionColorId, baseColor * intensity);
    }

    /// <summary>
    /// Riporta un colore HDR a luminosità 1 mantenendo la tinta,
    /// così min/max sono valori d'intensità reali.
    /// </summary>
    private static Color NormalizeColor(Color c)
    {
        float max = Mathf.Max(c.r, c.g, c.b);
        return max > 0.0001f ? new Color(c.r / max, c.g / max, c.b / max, 1f) : Color.white;
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    private void OnValidate()
    {
        if (maxIntensity < minIntensity) maxIntensity = minIntensity;
    }
}