using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Feedback visivo per i tasti della tastiera virtuale: quando il cursore/controller
// ci passa sopra il tasto si ingrandisce leggermente e cambia fortemente colore,
// così è più facile vedere quale tasto si sta per premere.
public class KeyFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                           IPointerDownHandler, IPointerUpHandler
{
    private Image img;
    private TextMeshProUGUI etichetta;
    private Vector3 scalaBase = Vector3.one;
    private bool premuto = false;

    private static readonly Color ColoreBase = new Color(0.6046f, 0.6784f, 0.7490f, 1f);   // #9aadbf Cool Steel
    private static readonly Color ColoreHover = new Color(0.4275f, 0.5961f, 0.7294f, 1f);   // #6d98ba Steel Blue
    private static readonly Color ColorePremuto = new Color(0.4275f, 0.5961f, 0.7294f, 1f);  // #6d98ba Steel Blue

    private const float FattoreHover = 1.18f;    // ingrandimento al passaggio del raggio
    private const float FattorePremuto = 1.08f;

    void Awake()
    {
        img = GetComponent<Image>();
        etichetta = GetComponentInChildren<TextMeshProUGUI>(true);
        scalaBase = transform.localScale;
        Applica(1f, ColoreBase, true);
    }

    void OnEnable()
    {
        Rilascia();
    }

    void OnDisable()
    {
        Rilascia();
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (!premuto) Applica(FattoreHover, ColoreHover, false);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (!premuto) Rilascia();
    }

    public void OnPointerDown(PointerEventData e)
    {
        premuto = true;
        Applica(FattorePremuto, ColorePremuto, false);
    }

    public void OnPointerUp(PointerEventData e)
    {
        premuto = false;
        Rilascia();
    }

    private void Rilascia()
    {
        premuto = false;
        Applica(1f, ColoreBase, false);
    }

    private void Applica(float fattoreScala, Color colore, bool testoScuro)
    {
        transform.localScale = scalaBase * fattoreScala;
        if (img != null) img.color = colore;
        if (etichetta != null) etichetta.color = testoScuro ? Color.black : Color.white;
    }
}