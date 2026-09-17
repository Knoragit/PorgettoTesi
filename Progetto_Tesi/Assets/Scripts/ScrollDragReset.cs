using UnityEngine;
using UnityEngine.EventSystems;

// Posto sul Viewport: azzera i bottoni all'inizio e alla fine di ogni drag del
// contenuto, cosi' i bottoni accesi durante lo scroll si spengono da soli.
public class ScrollDragReset : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    public void OnBeginDrag(PointerEventData eventData)
    {
        Rilascia();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        Rilascia();
    }

    private void Rilascia()
    {
        SongListManager slm = FindFirstObjectByType<SongListManager>();
        if (slm != null) slm.RilasciaIndietro();
    }
}