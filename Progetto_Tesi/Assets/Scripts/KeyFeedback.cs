using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Feedback visivo per i tasti della tastiera virtuale: quando il cursore/controller
// ci passa sopra il tasto si ingrandisce leggermente e cambia fortemente colore,
// così è più facile vedere quale tasto si sta per premere.
public class KeyFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                           IPointerDownHandler, IPointerUpHandler
{
    private Image img;
    private Vector3 scalaBase = Vector3.one;
    private bool premuto = false;

    private static readonly Color ColoreBase = new Color(0.20f, 0.22f, 0.30f, 1f);
    private static readonly Color ColoreHover = new Color(0.45f, 0.78f, 1.00f, 1f);
    private static readonly Color ColorePremuto = new Color(0.16f, 0.46f, 0.80f, 1f);

    void Awake()
    {
        img = GetComponent<Image>();
        scalaBase = transform.localScale;
        Applica(1f, ColoreBase);
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
        if (!premuto) Applica(1.14f, ColoreHover);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (!premuto) Rilascia();
    }

    public void OnPointerDown(PointerEventData e)
    {
        premuto = true;
        Applica(1.06f, ColorePremuto);
    }

    public void OnPointerUp(PointerEventData e)
    {
        premuto = false;
        Applica(1.14f, ColoreHover);
    }

    private void Rilascia()
    {
        premuto = false;
        Applica(1f, ColoreBase);
    }

    private void Applica(float fattoreScala, Color colore)
    {
        transform.localScale = scalaBase * fattoreScala;
        if (img != null) img.color = colore;
    }
}