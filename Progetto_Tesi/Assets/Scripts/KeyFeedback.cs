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

    private static readonly Color ColoreBase = new Color(0.2745f, 0.2863f, 0.2980f, 1f);   // #46494c Iron Grey
    private static readonly Color ColoreHover = new Color(0.0980f, 0.5216f, 0.6314f, 1f);    // #1985a1 Pacific Cyan
    private static readonly Color ColorePremuto = new Color(0.2980f, 0.3608f, 0.4078f, 1f);  // #4c5c68 Blue Slate

    void Awake()
    {
        img = GetComponent<Image>();
        etichetta = GetComponentInChildren<TextMeshProUGUI>(true);
        scalaBase = transform.localScale;
        Applica(1f, ColoreBase, false);
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
        if (!premuto) Applica(1.14f, ColoreHover, true);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (!premuto) Rilascia();
    }

    public void OnPointerDown(PointerEventData e)
    {
        premuto = true;
        Applica(1.06f, ColorePremuto, false);
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