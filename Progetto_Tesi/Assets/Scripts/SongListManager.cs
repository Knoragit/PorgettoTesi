using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class SongListManager : MonoBehaviour
{
    [Header("Contenitori (VerticalLayoutGroup) dei gruppi")]
    public RectTransform contenitoreObserver;
    public RectTransform contenitoreSeguimi;

    private UdpReceiver receiver;
    private GameManager gameManager;
    private TMP_FontAsset fontAsset;
    private Sprite bottoneSpr;
    private Sprite campoSpr;

    private GameObject barraObserver;
    private GameObject barraSeguimi;
    private RectTransform contenutoObserver;
    private RectTransform contenutoSeguimi;
    private IndietroFeedback indietroObserver;
    private IndietroFeedback indietroSeguimi;
    private readonly List<GameObject> bottoniObserver = new List<GameObject>();
    private readonly List<GameObject> bottoniSeguimi = new List<GameObject>();

    // Stato "modalità ricerca": nasconde l'elenco DB, mostra la tastiera e dopo OK
    // i bottoni mostrano SOLO i risultati della ricerca.
    private bool inRicerca = false;
    private string queryRicerca = "";
    private bool inAttesaRicerca = false;
    private RectTransform contenitoreRicerca;
    private GameObject elencoAttivo;
    private GameObject tastieraAttiva;
    private float larghezzaOrigRicerca = 100f;
    private float altezzaScorrRicerca = 300f;

    private GameManager.AppState statoPrecedente = GameManager.AppState.Onboarding;

    void Start()
    {
        receiver = FindFirstObjectByType<UdpReceiver>();
        gameManager = FindFirstObjectByType<GameManager>();

        // Se i contenitori non sono assegnati manualmente, li ricaviamo dal GameManager
        // (observerGroup e seguimiGroup sono i GameObject root dei due gruppi UI).
        if (gameManager != null)
        {
            if (contenitoreObserver == null && gameManager.observerGroup != null)
                contenitoreObserver = gameManager.observerGroup.GetComponent<RectTransform>();
            if (contenitoreSeguimi == null && gameManager.seguimiGroup != null)
                contenitoreSeguimi = gameManager.seguimiGroup.GetComponent<RectTransform>();
        }

        if (receiver == null) receiver = FindFirstObjectByType<UdpReceiver>();

        if (fontAsset == null)
            fontAsset = TMP_Settings.defaultFontAsset != null
                ? TMP_Settings.defaultFontAsset
                : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        Sprite bianco = CreaSpriteBianco();
        bottoneSpr = bianco;
        campoSpr = bianco;

        NascondiBottoniStatici(contenitoreObserver);
        NascondiBottoniStatici(contenitoreSeguimi);

        PreparaContenitore(contenitoreObserver);
        PreparaContenitore(contenitoreSeguimi);

        if (contenitoreObserver != null)
        {
            barraObserver = CreaBarraRicerca(contenitoreObserver, true);
            contenutoObserver = CreaElencoScorrevole(contenitoreObserver);
        }
        if (contenitoreSeguimi != null)
        {
            barraSeguimi = CreaBarraRicerca(contenitoreSeguimi, false);
            contenutoSeguimi = CreaElencoScorrevole(contenitoreSeguimi);
        }

        RiallineaBottoni(contenitoreObserver, barraObserver, null);
        RiallineaBottoni(contenitoreSeguimi, null, barraSeguimi);

        indietroObserver = AgganciaIndietro(contenitoreObserver);
        indietroSeguimi = AgganciaIndietro(contenitoreSeguimi);

        AggiornaGruppoAttivo();
    }

    // Attiva il feedback manuale (verde solo a hover reale) sul bottone "Indietro"
    // del gruppo, così non resta mai "appeso" dopo lo scroll della lista.
    private IndietroFeedback AgganciaIndietro(RectTransform contenitore)
    {
        if (contenitore != null)
        {
            for (int i = 0; i < contenitore.childCount; i++)
            {
                Transform figlio = contenitore.GetChild(i);
                if (figlio.name == "Bottone-Indietro")
                {
                    return figlio.gameObject.GetComponent<IndietroFeedback>()
                        ?? figlio.gameObject.AddComponent<IndietroFeedback>();
                }
            }
        }
        return null;
    }

    // Riporta i bottoni "Indietro" e tutti i bottoni delle canzoni al loro colore
    // base (defensivo: copre ogni refresh/ricerca/scroll che potrebbe aver lasciato
    // uno stato evidenziato residuo, incluso il drag partito sopra una canzone).
    public void RilasciaIndietro()
    {
        if (indietroObserver != null) indietroObserver.Rilascia();
        if (indietroSeguimi != null) indietroSeguimi.Rilascia();
        RilasciaCanzoni(bottoniObserver);
        RilasciaCanzoni(bottoniSeguimi);
    }

    private static void RilasciaCanzoni(List<GameObject> lista)
    {
        for (int i = 0; i < lista.Count; i++)
        {
            if (lista[i] == null) continue;
            IndietroFeedback fb = lista[i].GetComponent<IndietroFeedback>();
            if (fb != null) fb.Rilascia();
        }
    }

    // Riporta lo ScrollRect in cima: senza questo reset, se prima della ricerca ci si
    // era fermati in fondo alla lista piena (48 brani), i risultati/messaggi (content
    // corto, ancorato in alto) verrebbero renderizzati fuori dall'area della Viewport
    // e resterebbero invisibili dietro la maschera.
    private static void PortaScrollInCima(ScrollRect sr)
    {
        if (sr == null) return;
        sr.verticalNormalizedPosition = 1f;
        sr.StopMovement();
        sr.velocity = Vector2.zero;
    }

    private void NascondiBottoniStatici(RectTransform contenitore)
    {
        if (contenitore == null) return;
        for (int i = 0; i < contenitore.childCount; i++)
        {
            Transform figlio = contenitore.GetChild(i);
            if (figlio.GetComponent<BranoButtonUI>() != null)
            {
                figlio.gameObject.SetActive(false);
            }
        }
    }

    private void PreparaContenitore(RectTransform contenitore)
    {
        if (contenitore == null) return;

        VerticalLayoutGroup vlg = contenitore.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = contenitore.gameObject.AddComponent<VerticalLayoutGroup>();

        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 5f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = contenitore.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = contenitore.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private Sprite CreaSpriteBianco()
    {
        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color[] px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                             100f, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
    }

    void Update()
    {
        if (gameManager == null) return;

        if (gameManager.statoAttuale != statoPrecedente)
        {
            GameManager.AppState statoInUscita = statoPrecedente;
            statoPrecedente = gameManager.statoAttuale;
            TerminaRicerca(false);
            AggiornaGruppoAttivo();

            // Uscita dalle modalità esecutive (Osservatore/Seguimi): stop
            // difensivo così riproduzione e colonne vengono sempre fermate,
            // qualunque sia il percorso di navigazione verso il menu.
            if (statoInUscita == GameManager.AppState.Osservatore ||
                statoInUscita == GameManager.AppState.Seguimi)
            {
                FermaRiproduzione();
            }
        }
    }

    private void AggiornaGruppoAttivo()
    {
        if (gameManager == null || receiver == null) return;

        if (gameManager.statoAttuale == GameManager.AppState.Osservatore ||
            gameManager.statoAttuale == GameManager.AppState.Seguimi)
        {
            receiver.InviaComandoListaSongs();
        }
    }

    // ---------------------------------------------------------------
    // RICEZIONE DATI DAL BRIDGE (chiamati da UdpReceiver)
    // ---------------------------------------------------------------
    public void SongListRicevuta(UdpReceiver.SongData[] songs, bool suggest)
    {
        if (gameManager == null) return;

        List<GameObject> targetList;
        RectTransform elenco;

        if (inRicerca && contenitoreRicerca != null)
        {
            // In ricerca i risultati vanno SEMPRE nel contenitore da cui è partita
            // la ricerca (lo stato globale può non corrispondere al gruppo mostrato).
            if (contenitoreRicerca == contenitoreSeguimi)
            {
                targetList = bottoniSeguimi;
                elenco = contenutoSeguimi;
            }
            else
            {
                targetList = bottoniObserver;
                elenco = contenutoObserver;
            }
        }
        else if (gameManager.statoAttuale == GameManager.AppState.Osservatore)
        {
            targetList = bottoniObserver;
            elenco = contenutoObserver;
        }
        else if (gameManager.statoAttuale == GameManager.AppState.Seguimi)
        {
            targetList = bottoniSeguimi;
            elenco = contenutoSeguimi;
        }
        else
        {
            return;
        }

        if (elenco == null) return;

        // I suggerimenti live sostituiscono l'elenco, così mostriamo i risultati
        // filtrati. Per le liste complete (list_songs) mostriamo TUTTE le canzoni:
        // il cap a 12 vale solo per i suggerimenti, altrimenti i brani nuovi in
        // fondo alla lista non comparirebbero mai.
        int maxMostrati = 0;
        if (songs != null)
            maxMostrati = suggest ? Mathf.Min(songs.Length, 12) : songs.Length;

        RenderizzaLista(elenco, targetList, songs, maxMostrati);

        RiallineaBottoni(contenitoreObserver, barraObserver, barraSeguimi);
        RiallineaBottoni(contenitoreSeguimi, barraObserver, barraSeguimi);

        RilasciaIndietro();
    }

    // Renderizza una lista di canzoni nel contenitore indicato, distruggendo prima
    // i vecchi bottoni/messaggi. In ricerca gestisce anche il messaggio di stato.
    private void RenderizzaLista(RectTransform elenco, List<GameObject> targetList,
                                 UdpReceiver.SongData[] songs, int maxMostrati)
    {
        SvuotaBottoni(targetList);

        int mostrati = 0;
        if (songs != null)
        {
            for (int i = 0; i < maxMostrati; i++)
            {
                CreaBottoneBrano(elenco, songs[i].title, songs[i].artist, songs[i].id, targetList, false);
                mostrati++;
            }
        }

        // In modalità ricerca, se non c'è nessun match mostriamo un messaggio
        // chiaro al posto di una lista vuota. Durante il download online la
        // ricerca può richiedere qualche secondo: mostriamo l'attesa.
        if (inRicerca && mostrati == 0)
        {
            string testo = inAttesaRicerca
                ? "Ricerca in corso..."
                : "Nessun risultato per \"" + queryRicerca + "\"";
            CreaMessaggio(elenco, testo, targetList);
        }

        if (inRicerca)
        {
            string gruppo = (targetList == bottoniObserver) ? "Osservatore" : "Seguimi";
            ScrollRect sr = elenco.GetComponentInParent<ScrollRect>();
            float vPos = sr != null ? sr.verticalNormalizedPosition : -1f;
            float vpH = (sr != null && sr.viewport != null) ? sr.viewport.rect.height : 0f;
            UnityEngine.Debug.Log("[RICERCA] mostrati " + mostrati + " risultati in " + gruppo
                + " | scrollAttivo=" + (sr != null && sr.gameObject.activeInHierarchy)
                + " vPos=" + vPos.ToString("F2")
                + " contentRighe=" + elenco.childCount
                + " viewportH=" + vpH.ToString("F1")
                + " contentH=" + elenco.rect.height.ToString("F1"));
            PortaScrollInCima(sr);
        }

        RiallineaBottoni(contenitoreObserver, barraObserver, barraSeguimi);
        RiallineaBottoni(contenitoreSeguimi, barraObserver, barraSeguimi);

        RilasciaIndietro();
    }

    public void SearchResultRicevuta(string status, string filename, string title, string artist, string message)
    {
        if (gameManager == null) return;

        inAttesaRicerca = false;

        if (status == "success" && filename != null)
        {
            // Il file è ora nel DB. In modalità ricerca mostriamo SOLO i risultati
            // aggiornati (il suggerimento include il brano appena scaricato, con il suo id),
            // altrimenti ricarichiamo l'intera lista del DB.
            if (inRicerca && !string.IsNullOrEmpty(queryRicerca))
            {
                if (receiver != null) receiver.InviaComandoSuggerimento(queryRicerca);
            }
            else if (receiver != null)
            {
                receiver.InviaComandoListaSongs();
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("[RICERCA] " + (string.IsNullOrEmpty(message) ? "Nessun risultato." : message));

            // La ricerca online è terminata senza successo: aggiorna la lista così
            // il messaggio "Ricerca in corso..." diventa "Nessun risultato...".
            if (inRicerca && !string.IsNullOrEmpty(queryRicerca) && receiver != null)
            {
                receiver.InviaComandoSuggerimento(queryRicerca);
            }
        }

        RiallineaBottoni(contenitoreObserver, barraObserver, barraSeguimi);
        RiallineaBottoni(contenitoreSeguimi, barraObserver, barraSeguimi);
    }

    public void PlayResultRicevuta(string status, string message)
    {
        if (status == "error")
        {
            UnityEngine.Debug.LogWarning("[PLAY] " + message);
        }
    }

    // Avvia la ricerca: chiude la tastiera, mostra l'elenco (dimensione piena) e
    // invia prima i risultati immediati (suggerimento locale) poi la ricerca completa
    // (che scarica il file se non presente nel DB).
    private void AvviaRicerca(string testo)
    {
        if (receiver == null) receiver = FindFirstObjectByType<UdpReceiver>();
        if (receiver == null) return;

        queryRicerca = testo.Trim();
        inRicerca = true;
        inAttesaRicerca = true;

        if (elencoAttivo != null)
        {
            LayoutElement leScorr = elencoAttivo.GetComponent<LayoutElement>();
            if (leScorr != null) leScorr.preferredHeight = altezzaScorrRicerca;
            elencoAttivo.SetActive(true);
            PortaScrollInCima(elencoAttivo.GetComponent<ScrollRect>());
        }

        // Feedback immediato: svuota la lista del contenitore di ricerca e mostra
        // l'attesa mentre il bridge cerca nel DB locale e online. I risultati
        // arrivati sostituiranno il messaggio (SvuotaBottoni lo rimuove).
        List<GameObject> targetRicerca = bottoniObserver;
        RectTransform elencoRicerca = contenutoObserver;
        if (contenitoreRicerca == contenitoreSeguimi)
        {
            targetRicerca = bottoniSeguimi;
            elencoRicerca = contenutoSeguimi;
        }
        SvuotaBottoni(targetRicerca);
        if (elencoRicerca != null) CreaMessaggio(elencoRicerca, "Ricerca in corso...", targetRicerca);

        receiver.InviaComandoSuggerimento(queryRicerca);
        receiver.InviaComandoRicerca(queryRicerca);
        Debug.Log("[UDP] Ricerca avviata: \"" + queryRicerca + "\"");
    }

    // Uscita dalla modalità ricerca: chiude la tastiera, ripristina larghezza/scroll
    // originali e (se richiesto) ricarica la lista completa del DB.
    private void TerminaRicerca(bool ricaricaLista)
    {
        inRicerca = false;
        queryRicerca = "";
        inAttesaRicerca = false;

        if (tastieraAttiva != null) tastieraAttiva.SetActive(false);
        tastieraAttiva = null;

        if (contenitoreRicerca != null)
        {
            contenitoreRicerca.sizeDelta = new Vector2(larghezzaOrigRicerca, contenitoreRicerca.sizeDelta.y);
            contenitoreRicerca = null;
        }

        if (elencoAttivo != null)
        {
            LayoutElement leScorr = elencoAttivo.GetComponent<LayoutElement>();
            if (leScorr != null) leScorr.preferredHeight = altezzaScorrRicerca;
            elencoAttivo.SetActive(true);
            PortaScrollInCima(elencoAttivo.GetComponent<ScrollRect>());
            elencoAttivo = null;
        }

        if (ricaricaLista)
        {
            if (receiver == null) receiver = FindFirstObjectByType<UdpReceiver>();
            if (receiver != null) receiver.InviaComandoListaSongs();
        }

        RilasciaIndietro();
    }

    // Chiamata dai bottoni dei brani quando parte la riproduzione: esce dalla
    // modalità ricerca e ricarica la lista completa (lista iniziale + brano cercato).
    public void CanzoneAvviata()
    {
        if (!inRicerca) return;
        TerminaRicerca(true);
    }

    // Ferma l'esecuzione corrente (riproduzione + colonne) e pulisce il visualizzatore.
    private void FermaRiproduzione()
    {
        if (receiver == null) receiver = FindFirstObjectByType<UdpReceiver>();
        if (receiver != null) receiver.InviaComandoStop();

        PianoVisualizer vis = FindFirstObjectByType<PianoVisualizer>();
        if (vis != null)
        {
            vis.ResetVisualizer();
            vis.PulisciNoteAtteseVisive();
        }
    }

    private void SvuotaBottoni(List<GameObject> lista)
    {
        for (int i = 0; i < lista.Count; i++)
        {
            if (lista[i] != null) Destroy(lista[i]);
        }
        lista.Clear();
    }

    private void RiallineaBottoni(RectTransform contenitore, GameObject barra1, GameObject barra2)
    {
        if (contenitore == null) return;

        // Sposta la barra di ricerca del gruppo corrente in cima (sibling 0).
        if (barra1 != null && barra1.transform.parent == contenitore) barra1.transform.SetAsFirstSibling();
        if (barra2 != null && barra2.transform.parent == contenitore) barra2.transform.SetAsFirstSibling();

        // Il bottone "Indietro" viene sempre tenuto in fondo alla lista.
        for (int i = 0; i < contenitore.childCount; i++)
        {
            Transform figlio = contenitore.GetChild(i);
            if (figlio.name == "Bottone-Indietro")
            {
                figlio.SetAsLastSibling();
                break;
            }
        }

        // La tastiera virtuale resta l'ultimo figlio (in fondo, sotto l'elenco)
        // così viene disegnata sopra tutto e non copre mai i bottoni.
        for (int i = 0; i < contenitore.childCount; i++)
        {
            Transform figlio = contenitore.GetChild(i);
            if (figlio.name == "TastieraVirtuale")
            {
                figlio.SetAsLastSibling();
                break;
            }
        }
    }

    // ---------------------------------------------------------------
    // COSTRUZIONE UI RUNTIME
    // ---------------------------------------------------------------
    private RectTransform CreaElencoScorrevole(RectTransform parent)
    {
        // ScrollView (pannello a altezza fissa)
        GameObject scrollGO = CrearGOFiglio(parent, "ElencoBrani");
        RectTransform rtScroll = (RectTransform)scrollGO.transform;
        rtScroll.anchorMin = Vector2.zero;
        rtScroll.anchorMax = new Vector2(1f, 0f);
        rtScroll.pivot = new Vector2(0.5f, 0.5f);
        rtScroll.sizeDelta = new Vector2(0f, 300f);

        LayoutElement leScroll = scrollGO.AddComponent<LayoutElement>();
        leScroll.preferredHeight = 300f;

        ScrollRect scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.inertia = true;
        scroll.elasticity = 0.12f;
        scroll.scrollSensitivity = 40f;
        scroll.decelerationRate = 0.05f;

        // Viewport (maschera)
        GameObject vpGO = CrearGOFiglio(scrollGO.transform, "Viewport");
        // Azzera canzoni e bottoni "Indietro" all'inizio e alla fine di ogni drag
        // del contenuto (lo ScrollRect perde l'OnPointerUp della canzone premuta).
        vpGO.AddComponent<ScrollDragReset>();
        RectTransform rtVp = (RectTransform)vpGO.transform;
        rtVp.anchorMin = Vector2.zero;
        rtVp.anchorMax = Vector2.one;
        rtVp.offsetMin = Vector2.zero;
        rtVp.offsetMax = Vector2.zero;
        rtVp.pivot = new Vector2(0.5f, 0.5f);

        // Corsia dedicata della scrollbar (unita canvas): separa la "presa" del
        // pinch dai bottoni delle canzoni evitando ogni sovrapposizione.
        const float SB_LANE = 24f;
        const float SB_WIDTH = 8f;
        rtVp.offsetMax = new Vector2(-SB_LANE, 0f);

        RectMask2D mask = vpGO.AddComponent<RectMask2D>();

        Image sfondoVp = vpGO.AddComponent<Image>();
        sfondoVp.sprite = bottoneSpr;
        sfondoVp.type = Image.Type.Sliced;
        sfondoVp.color = new Color(0.05f, 0.06f, 0.09f, 0.55f);

        GameObject sbGO = CrearGOFiglio(scrollGO.transform, "Scrollbar");
        RectTransform rtSb = (RectTransform)sbGO.transform;
        rtSb.anchorMin = new Vector2(1f, 0f);
        rtSb.anchorMax = new Vector2(1f, 1f);
        rtSb.pivot = new Vector2(1f, 0.5f);
        rtSb.anchoredPosition = Vector2.zero;
        rtSb.sizeDelta = new Vector2(SB_LANE, 0f);

        Image bgImg = sbGO.AddComponent<Image>();
        bgImg.sprite = bottoneSpr;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(1f, 1f, 1f, 0.10f);
        bgImg.raycastPadding = Vector4.zero;

        GameObject handleGO = CrearGOFiglio(sbGO.transform, "Handle");
        RectTransform rtHandle = (RectTransform)handleGO.transform;
        rtHandle.anchorMin = Vector2.zero;
        rtHandle.anchorMax = Vector2.zero;
        rtHandle.pivot = new Vector2(0.5f, 0.5f);
        rtHandle.anchoredPosition = new Vector2(SB_LANE * 0.5f, 0f);
        rtHandle.sizeDelta = new Vector2(SB_WIDTH, 0f);

        Image handleImg = handleGO.AddComponent<Image>();
        handleImg.sprite = bottoneSpr;
        handleImg.type = Image.Type.Sliced;
        handleImg.color = new Color(1f, 1f, 1f, 0.65f);
        handleImg.raycastTarget = false;

        ScrollbarVisuale sbVisuale = sbGO.AddComponent<ScrollbarVisuale>();
        sbVisuale.scroll = scroll;
        sbVisuale.handle = rtHandle;
        sbVisuale.handleImg = handleImg;
        sbVisuale.trackImg = bgImg;

        // Content (VerticalLayoutGroup + ContentSizeFitter)
        GameObject contentGO = CrearGOFiglio(vpGO.transform, "Content");
        RectTransform rtContent = (RectTransform)contentGO.transform;
        rtContent.anchorMin = new Vector2(0f, 1f);
        rtContent.anchorMax = new Vector2(1f, 1f);
        rtContent.pivot = new Vector2(0.5f, 1f);
        rtContent.anchoredPosition = Vector2.zero;
        rtContent.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.spacing = 4f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = rtVp;
        scroll.content = rtContent;

        return rtContent;
    }

    private GameObject CreaBarraRicerca(RectTransform contenitore, bool isObserver)
    {
        GameObject root = new GameObject("BarraRicerca", typeof(RectTransform));
        root.transform.SetParent(contenitore, false);

        RectTransform rtRoot = (RectTransform)root.transform;
        rtRoot.anchorMin = Vector2.zero;
        rtRoot.anchorMax = new Vector2(1f, 0f);
        rtRoot.pivot = new Vector2(0.5f, 0.5f);
        rtRoot.sizeDelta = new Vector2(0f, 38f);

        LayoutElement leRoot = root.AddComponent<LayoutElement>();
        leRoot.preferredHeight = 38f;

        HorizontalLayoutGroup hlg = root.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 5f;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.padding = new RectOffset(0, 0, 0, 0);

        TMP_InputField input = CreaInputField(root.transform, isObserver);

        GameObject cercaGO = CrearGOFiglio(root.transform, "Cerca");
        LayoutElement leCerca = cercaGO.AddComponent<LayoutElement>();
        leCerca.preferredWidth = 84f;

        Image cercaImg = cercaGO.AddComponent<Image>();
        cercaImg.sprite = bottoneSpr;
        cercaImg.type = Image.Type.Sliced;
        cercaImg.color = new Color(0.30f, 0.60f, 0.90f, 1f);

        Button cercaBtn = cercaGO.AddComponent<Button>();
        cercaBtn.targetGraphic = cercaImg;

        GameObject labelGO = CrearGOFiglio(cercaGO.transform, "Label");
        RectTransform rtLabel = (RectTransform)labelGO.transform;
        rtLabel.anchorMin = Vector2.zero;
        rtLabel.anchorMax = Vector2.one;
        rtLabel.offsetMin = Vector2.zero;
        rtLabel.offsetMax = Vector2.zero;
        TextMeshProUGUI cercaLabel = labelGO.AddComponent<TextMeshProUGUI>();
        cercaLabel.text = "Cerca";
        cercaLabel.font = fontAsset;
        cercaLabel.fontSize = 15;
        cercaLabel.color = Color.white;
        cercaLabel.alignment = TextAlignmentOptions.Center;

        // Tastiera virtuale: in editor+Link TMP non puo' aprire la tastiera di sistema
        // della Quest, quindi digitiamo qui dentro. Diventa un figlio del contenitore
        // (in fondo, sotto l'elenco) e viene mostrata/nascosta dal pulsante Cerca.
        GameObject tastiera = CreaTastieraVirtuale(contenitore, input);
        tastiera.SetActive(false);

        cercaBtn.onClick.AddListener(() =>
        {
            // Appena si vuole cercare, fermiamo l'esecuzione in corso (colonne e
            // riproduzione) così non resta lo svolgimento di Osservatore/Seguimi.
            FermaRiproduzione();
            RilasciaIndietro();

            if (!inRicerca)
            {
                // ENTRA in modalità ricerca: nasconde i bottoni del DB, allarga
                // il gruppo e apre la tastiera.
                inRicerca = true;
                queryRicerca = "";
                contenitoreRicerca = contenitore;
                tastieraAttiva = tastiera;

                larghezzaOrigRicerca = contenitore.sizeDelta.x;
                Transform el = contenitore.Find("ElencoBrani");
                elencoAttivo = el != null ? el.gameObject : null;
                if (elencoAttivo != null)
                {
                    LayoutElement leScorr = elencoAttivo.GetComponent<LayoutElement>();
                    if (leScorr != null) altezzaScorrRicerca = leScorr.preferredHeight;
                    elencoAttivo.SetActive(false);
                }

                contenitore.sizeDelta = new Vector2(Mathf.Max(larghezzaOrigRicerca, 165f), contenitore.sizeDelta.y);
                tastiera.SetActive(true);
                tastiera.transform.SetAsLastSibling();
                input.ActivateInputField();
                Debug.Log("[UDP] Ricerca aperta: elenco nascosto, tastiera mostrata.");
            }
            else
            {
                // GIA' in ricerca: chiudi/riapri la tastiera mantenendo i risultati.
                tastiera.SetActive(!tastiera.activeSelf);
                if (tastiera.activeSelf)
                {
                    tastiera.transform.SetAsLastSibling();
                    if (elencoAttivo != null)
                    {
                        LayoutElement leScorr = elencoAttivo.GetComponent<LayoutElement>();
                        if (leScorr != null) leScorr.preferredHeight = 90f;
                        elencoAttivo.SetActive(true);
                        PortaScrollInCima(elencoAttivo.GetComponent<ScrollRect>());
                    }
                    input.ActivateInputField();
                }
                Debug.Log("[UDP] Ricerca: tastiera " + (tastiera.activeSelf ? "riaperta" : "chiusa") + " (risultati mantenuti).");
            }
        });

        return root;
    }

    private TMP_InputField CreaInputField(Transform parent, bool isObserver)
    {
        GameObject campoGO = CrearGOFiglio(parent, "Input");
        LayoutElement leCampo = campoGO.AddComponent<LayoutElement>();
        leCampo.flexibleWidth = 1f;

        Image sfondo = campoGO.AddComponent<Image>();
        sfondo.sprite = campoSpr;
        sfondo.type = Image.Type.Sliced;
        sfondo.color = new Color(0.12f, 0.12f, 0.15f, 0.95f);

        TMP_InputField input = campoGO.AddComponent<TMP_InputField>();
        input.targetGraphic = sfondo;

        GameObject ta = CrearGOFiglio(campoGO.transform, "TextArea");
        RectMask2D mask = ta.AddComponent<RectMask2D>();
        RectTransform rtTa = (RectTransform)ta.transform;
        rtTa.anchorMin = Vector2.zero;
        rtTa.anchorMax = Vector2.one;
        rtTa.offsetMin = new Vector2(6f, 4f);
        rtTa.offsetMax = new Vector2(-6f, -4f);

        // Placeholder
        GameObject phGO = CrearGOFiglio(ta.transform, "Placeholder");
        TextMeshProUGUI ph = phGO.AddComponent<TextMeshProUGUI>();
        RectTransform rtPh = (RectTransform)ph.transform;
        rtPh.anchorMin = Vector2.zero;
        rtPh.anchorMax = Vector2.one;
        rtPh.offsetMin = Vector2.zero;
        rtPh.offsetMax = Vector2.zero;
        ph.text = "Cerca un brano...";
        ph.font = fontAsset;
        ph.fontSize = 14;
        ph.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        ph.raycastTarget = false;

        // Text
        GameObject txtGO = CrearGOFiglio(ta.transform, "Text");
        TextMeshProUGUI txt = txtGO.AddComponent<TextMeshProUGUI>();
        RectTransform rtTxt = (RectTransform)txt.transform;
        rtTxt.anchorMin = Vector2.zero;
        rtTxt.anchorMax = Vector2.one;
        rtTxt.offsetMin = Vector2.zero;
        rtTxt.offsetMax = Vector2.zero;
        txt.font = fontAsset;
        txt.fontSize = 14;
        txt.color = Color.white;
        txt.raycastTarget = true;

        input.textComponent = txt;
        input.placeholder = ph;
        input.textViewport = rtTa;
        input.fontAsset = fontAsset;
        input.pointSize = 14;

        input.onValueChanged.AddListener((val) =>
        {
            // Durante la ricerca niente liste live: i risultati arrivano solo dopo OK.
            if (inRicerca) return;
            if (receiver == null) receiver = FindFirstObjectByType<UdpReceiver>();
            if (receiver != null)
            {
                if (string.IsNullOrEmpty(val))
                    receiver.InviaComandoListaSongs();
                else
                    receiver.InviaComandoSuggerimento(val);
            }
        });

        return input;
    }

    private GameObject CreaTastieraVirtuale(RectTransform contenitore, TMP_InputField input)
    {
        GameObject tastiera = new GameObject("TastieraVirtuale", typeof(RectTransform));
        tastiera.transform.SetParent(contenitore, false);

        RectTransform rt = (RectTransform)tastiera.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;

        // Partecipa al layout del contenitore: si posiziona in fondo, sotto l'elenco.
        LayoutElement le = tastiera.AddComponent<LayoutElement>();
        le.preferredHeight = 166f;

        Image sfondo = tastiera.AddComponent<Image>();
        sfondo.sprite = bottoneSpr;
        sfondo.type = Image.Type.Sliced;
        sfondo.color = new Color(0.05f, 0.06f, 0.10f, 0.95f);

        VerticalLayoutGroup vlg = tastiera.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        // Schermo readout: mostra live il testo digitato sulla tastiera.
        GameObject readGO = CrearGOFiglio(tastiera.transform, "Readout");
        LayoutElement leRead = readGO.AddComponent<LayoutElement>();
        leRead.preferredHeight = 28f;

        Image readImg = readGO.AddComponent<Image>();
        readImg.sprite = bottoneSpr;
        readImg.type = Image.Type.Sliced;
        readImg.color = new Color(0.02f, 0.03f, 0.05f, 0.98f);

        GameObject readLabelGO = CrearGOFiglio(readGO.transform, "Testo");
        RectTransform rtReadLabel = (RectTransform)readLabelGO.transform;
        rtReadLabel.anchorMin = Vector2.zero;
        rtReadLabel.anchorMax = Vector2.one;
        rtReadLabel.offsetMin = new Vector2(8f, 2f);
        rtReadLabel.offsetMax = new Vector2(-8f, -2f);
        TextMeshProUGUI readLabel = readLabelGO.AddComponent<TextMeshProUGUI>();
        readLabel.font = fontAsset;
        readLabel.fontSize = 17;
        readLabel.color = new Color(0.9f, 0.95f, 1f, 1f);
        readLabel.alignment = TextAlignmentOptions.Left;
        readLabel.text = "Scrivi il brano da cercare...";

        input.onValueChanged.AddListener((v) =>
        {
            readLabel.text = string.IsNullOrEmpty(v) ? "Scrivi il brano da cercare..." : "> " + v;
        });

        CreaRigaTastiera(tastiera.transform, input, 26f, "A", "B", "C", "D", "E", "F", "G", "H", "I", "J");
        CreaRigaTastiera(tastiera.transform, input, 26f, "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T");
        CreaRigaTastiera(tastiera.transform, input, 26f, "U", "V", "W", "X", "Y", "Z");
        CreaRigaTastiera(tastiera.transform, input, 26f, "BACK", "SP", "DEL", "AC", "OK");

        return tastiera;
    }

    private void CreaRigaTastiera(Transform parent, TMP_InputField input, float altezza, params string[] tasti)
    {
        GameObject riga = CrearGOFiglio(parent, "RigaTastiera");

        HorizontalLayoutGroup hlg = riga.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.childAlignment = TextAnchor.MiddleCenter;

        LayoutElement leRiga = riga.AddComponent<LayoutElement>();
        leRiga.preferredHeight = altezza;

        // Righe con pochi tasti (riga comandi BACK·SP·DEL·AC·OK) si allargano
        // fino a riempire tutta la larghezza disponibile.
        bool rigaLarga = tasti.Length <= 5;

        foreach (string t in tasti)
        {
            string tasto = t;

            GameObject te = CrearGOFiglio(riga.transform, "Tasto_" + tasto);

            LayoutElement le = te.AddComponent<LayoutElement>();
            if (rigaLarga)
            {
                le.preferredWidth = 0f;
                le.flexibleWidth = 1f;
            }
            else
            {
                le.preferredWidth = 10f;
                le.flexibleWidth = 0f;
            }
            le.preferredHeight = altezza;

            Image img = te.AddComponent<Image>();
            img.sprite = bottoneSpr;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.20f, 0.22f, 0.30f, 1f);

            Button btn = te.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;

            // Feedback visivo: ingrandisce e cambia colore al passaggio/pressione.
            te.AddComponent<KeyFeedback>();

            GameObject labGO = CrearGOFiglio(te.transform, "Label");
            RectTransform rtLab = (RectTransform)labGO.transform;
            rtLab.anchorMin = Vector2.zero;
            rtLab.anchorMax = Vector2.one;
            rtLab.offsetMin = Vector2.zero;
            rtLab.offsetMax = Vector2.zero;
            TextMeshProUGUI lab = labGO.AddComponent<TextMeshProUGUI>();
            lab.text = tasto;
            lab.font = fontAsset;
            lab.fontSize = rigaLarga ? 14f : 15f;
            lab.color = Color.white;
            lab.alignment = TextAlignmentOptions.Center;

            btn.onClick.AddListener(() => GestisciTastoTastiera(input, tasto, te));
        }
    }

    private void GestisciTastoTastiera(TMP_InputField input, string tasto, GameObject tastoGO)
    {
        if (input == null) return;

        // Qualsiasi tasto della tastiera virtuale pulisce il bottone "Indietro".
        RilasciaIndietro();

        switch (tasto)
        {
            case "BACK":
                {
                    Transform t = tastoGO.transform;
                    if (t.parent != null && t.parent.parent != null)
                        t.parent.parent.gameObject.SetActive(false);
                    TerminaRicerca(true);
                }
                break;
            case "DEL":
                if (input.text.Length > 0)
                    input.text = input.text.Substring(0, input.text.Length - 1);
                break;
            case "AC":
                input.text = "";
                break;
            case "SP":
                input.text += " ";
                break;
            case "OK":
                {
                    Transform t = tastoGO.transform;
                    if (t.parent != null && t.parent.parent != null)
                        t.parent.parent.gameObject.SetActive(false);
                    if (string.IsNullOrWhiteSpace(input.text))
                    {
                        TerminaRicerca(true);
                    }
                    else
                    {
                        AvviaRicerca(input.text);
                    }
                }
                break;
            default:
                input.text += tasto;
                break;
        }

        if (input.textComponent != null) input.MoveTextEnd(false);
        input.ActivateInputField();
    }

    private void CreaMessaggio(RectTransform elenco, string testo, List<GameObject> listaTarget)
    {
        GameObject msgGO = CrearGOFiglio(elenco, "Messaggio");
        RectTransform rt = (RectTransform)msgGO.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, 36f);

        LayoutElement le = msgGO.AddComponent<LayoutElement>();
        le.preferredHeight = 36f;

        TextMeshProUGUI lab = msgGO.AddComponent<TextMeshProUGUI>();
        lab.text = testo;
        lab.font = fontAsset;
        lab.fontSize = 14;
        lab.color = new Color(0.75f, 0.78f, 0.85f, 1f);
        lab.alignment = TextAlignmentOptions.Center;
        lab.raycastTarget = false;

        listaTarget.Add(msgGO);
    }

    private void CreaBottoneBrano(RectTransform elenco, string titolo, string autore, int id,
                                  List<GameObject> listaTarget, bool nuovoRisultato)
    {
        GameObject bottoneGO = CrearGOFiglio(elenco, "BottoneRisultato");

        RectTransform rt = (RectTransform)bottoneGO.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, 36f);

        LayoutElement le = bottoneGO.AddComponent<LayoutElement>();
        le.preferredHeight = 36f;

        Image img = bottoneGO.AddComponent<Image>();
        img.sprite = bottoneSpr;
        img.type = Image.Type.Sliced;
        img.color = new Color(0.16f, 0.17f, 0.22f, 1f);

        Button btn = bottoneGO.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock colori = btn.colors;
        colori.highlightedColor = new Color(0.28f, 0.35f, 0.52f, 1f);
        colori.pressedColor = new Color(0.19f, 0.24f, 0.36f, 1f);
        btn.colors = colori;

        // Hover manuale (niente ColorTint): evita che lo scroll illumini tutte le canzoni.
        bottoneGO.AddComponent<IndietroFeedback>();

        GameObject labelGO = CrearGOFiglio(bottoneGO.transform, "Testo");
        RectTransform rtLabel = (RectTransform)labelGO.transform;
        rtLabel.anchorMin = Vector2.zero;
        rtLabel.anchorMax = Vector2.one;
        rtLabel.offsetMin = new Vector2(6f, 2f);
        rtLabel.offsetMax = new Vector2(-6f, -2f);
        TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
        label.font = fontAsset;
        label.fontSize = 11;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Left;
        label.enableWordWrapping = false;

        BranoDinamicoUI dinamico = bottoneGO.AddComponent<BranoDinamicoUI>();
        dinamico.ImpostaBrano(titolo, autore, id, nuovoRisultato);

        listaTarget.Add(bottoneGO);
    }

    private GameObject CrearGOFiglio(Transform parent, string nome)
    {
        GameObject go = new GameObject(nome, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }
}