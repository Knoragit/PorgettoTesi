using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ScrollbarVisuale : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    public ScrollRect scroll;
    public RectTransform handle;
    public Image handleImg;
    public Image trackImg;

    private static readonly Color ColoreStabile = new Color(1f, 1f, 1f, 0.65f);
    private static readonly Color ColoreHover = new Color(0.85f, 0.95f, 1f, 1f);
    private static readonly Color ColoreDrag = new Color(0.40f, 0.70f, 1f, 1f);
    private static readonly Color TracciaStabile = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color TracciaAttiva = new Color(1f, 1f, 1f, 0.25f);

    private float targetNorm = 1f;
    private bool dragging = false;
    private bool hovering = false;
    private float lastDragEventTime = 0f;
    private ScrollRect.MovementType savedMovementType;

    private void Update()
    {
        if (scroll == null || handle == null || handleImg == null || trackImg == null) return;

        float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
        bool eccede = ContenutoEccede();

        if (handle.gameObject.activeSelf != eccede) handle.gameObject.SetActive(eccede);
        if (!eccede) return;

        AggiornaDimensioneHandle();

        if (dragging)
        {
            scroll.verticalNormalizedPosition = Mathf.Clamp01(targetNorm);
            handle.anchoredPosition = new Vector2(handle.anchoredPosition.x, PosizioneHandleY(targetNorm));

            if (Time.time - lastDragEventTime > 1f)
            {
                dragging = false;
                hovering = false;
                scroll.vertical = true;
                scroll.movementType = savedMovementType;
            }
        }
        else
        {
            float norm = Mathf.Clamp01(scroll.verticalNormalizedPosition);
            float targetY = PosizioneHandleY(norm);
            handle.anchoredPosition = new Vector2(handle.anchoredPosition.x, Mathf.Lerp(handle.anchoredPosition.y, targetY, k));
        }

        Color hCol = dragging ? ColoreDrag : (hovering ? ColoreHover : ColoreStabile);
        Color tCol = (dragging || hovering) ? TracciaAttiva : TracciaStabile;
        handleImg.color = Color.Lerp(handleImg.color, hCol, k);
        trackImg.color = Color.Lerp(trackImg.color, tCol, k);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = true;
        hovering = true;
        lastDragEventTime = Time.time;
        savedMovementType = scroll.movementType;
        scroll.movementType = ScrollRect.MovementType.Unrestricted;
        scroll.vertical = false;
        AggiornaTargetDaEvento(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        lastDragEventTime = Time.time;
        AggiornaTargetDaEvento(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        dragging = false;
        hovering = false;
        scroll.vertical = true;
        scroll.movementType = savedMovementType;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        dragging = true;
        hovering = true;
        lastDragEventTime = Time.time;
        savedMovementType = scroll.movementType;
        scroll.movementType = ScrollRect.MovementType.Unrestricted;
        scroll.vertical = false;
        AggiornaTargetDaEvento(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        dragging = false;
        hovering = false;
        scroll.vertical = true;
        scroll.movementType = savedMovementType;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
    }

    private void AggiornaTargetDaEvento(PointerEventData eventData)
    {
        if (scroll == null || trackImg == null) return;

        RectTransform trackRt = (RectTransform)trackImg.transform;
        float h = trackRt.rect.height;
        if (h <= 0.01f) return;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRt, eventData.position, eventData.pressEventCamera, out local)) return;

        float t = Mathf.Clamp01((local.y + h * 0.5f) / h);
        targetNorm = t;
    }

    private bool ContenutoEccede()
    {
        if (scroll.content == null || scroll.viewport == null) return false;
        return scroll.content.rect.height > scroll.viewport.rect.height + 0.5f;
    }

    private void AggiornaDimensioneHandle()
    {
        RectTransform trackRt = (RectTransform)trackImg.transform;
        float trackH = trackRt.rect.height;
        if (trackH <= 0.01f) return;

        float viewH = scroll.viewport.rect.height;
        float contH = scroll.content.rect.height;
        if (contH <= 0.01f) return;

        float ratio = Mathf.Clamp01(viewH / contH);
        float handleH = Mathf.Max(trackH * 0.08f, trackH * ratio);
        Vector2 sd = handle.sizeDelta;
        handle.sizeDelta = new Vector2(sd.x, handleH);
    }

    private float PosizioneHandleY(float norm)
    {
        RectTransform trackRt = (RectTransform)trackImg.transform;
        float trackH = trackRt.rect.height;
        float handleH = handle.sizeDelta.y;
        float min = handleH * 0.5f;
        float max = Mathf.Max(min, trackH - handleH * 0.5f);
        return Mathf.Lerp(min, max, Mathf.Clamp01(norm));
    }
}