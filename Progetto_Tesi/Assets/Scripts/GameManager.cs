using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;
using UnityEngine.EventSystems;

public class GameManager : MonoBehaviour
{
    public enum AppState { Onboarding, Menu, Tutorial, Osservatore, Seguimi, FaiTu }

    [Header("Stato dell'Applicazione")]
    public AppState statoAttuale = AppState.Onboarding;

    [Header("Riferimenti UI Gruppi")]
    public GameObject calibrationGroup;
    public GameObject menuGroup;
    public GameObject tutorialGroup;
    public GameObject observerGroup;
    public GameObject seguimiGroup;
    public GameObject faiTuGroup;

    [Header("UI Specifica Modalit� Fai Tu")]
    public GameObject faiTuBannerMessaggio;
    public GameObject faiTuReportMessaggio;

    [Header("Riferimenti UI Testi")]
    public TextMeshProUGUI tutorialText;

    private UdpReceiver udpReceiver;
    private int noteAttualmentePremute = 0;
    private float tempoUltimoRilascio = -1f;
    private int ultimaNotaMidi = -1;
    private float ultimaVelocita = 0f;
    public int sfidaAttuale = 0;

    // Gestione Note per la modalit� "Seguimi"
    private List<int> expectedNotes = new List<int>();
    private HashSet<int> userPressedNotes = new HashSet<int>();

    private bool inTransizione = false;
    private Coroutine faiTuBannerCoroutine;
    private Coroutine reportFeedbackCoroutine;

    // Sprite e materiale condivisi da TUTTI i bottoni (forma e aspetto uniformi).
    // Vengono ricavati dai bottoni statici della scena (menu/FaiTu) che usano la
    // sprite UI arrotondata built-in e il materiale di testo con ombra.
    private static Sprite sprArrotondato;
    private static Material matOmbra;

    public static Sprite SpriteArrotondato
    {
        get
        {
            if (sprArrotondato == null) sprArrotondato = TrovaSpriteArrotondata();
            return sprArrotondato;
        }
    }

    public static Material MaterialeTesto
    {
        get
        {
            if (matOmbra == null) matOmbra = TrovaMaterialeOmbra();
            return matOmbra;
        }
    }

    private static Sprite TrovaSpriteArrotondata()
    {
        Image[] immagini = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 1) La sprite di un bottone "Torna al Menù"/"Indietro" statico: e' il
        //    riferimento visivo che l'utente confronta con quello del tutorial.
        string[] nomiTorna = { "TornaAlMen\u00F9", "Bottone-Indietro", "GeneraReport" };
        foreach (Image img in immagini)
        {
            if (img.sprite == null) continue;
            foreach (string nome in nomiTorna)
            {
                if (img.gameObject.name == nome) return img.sprite;
            }
        }

        // 2) Lo sfondo di un Button qualsiasi.
        Button[] bottoni = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Button b in bottoni)
        {
            Image im = b.targetGraphic as Image;
            if (im != null && im.sprite != null && im.sprite.name == "UISprite") return im.sprite;
        }
        foreach (Button b in bottoni)
        {
            Image im = b.targetGraphic as Image;
            if (im != null && im.sprite != null && im.sprite.border != Vector4.zero) return im.sprite;
        }

        // 3) Qualunque Image con una sprite arrotondata.
        foreach (Image img in immagini)
        {
            if (img.sprite != null && img.sprite.name == "UISprite") return img.sprite;
        }
        foreach (Image img in immagini)
        {
            if (img.sprite != null && img.sprite.border != Vector4.zero) return img.sprite;
        }
        return null;
    }

    private static Material TrovaMaterialeOmbra()
    {
        TextMeshProUGUI[] testi = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (TextMeshProUGUI t in testi)
        {
            Material m = t.fontSharedMaterial;
            if (m != null && m.name.Contains("Drop Shadow")) return m;
        }
        return null;
    }

    // Applica a una label dei bottoni lo stile uniforme: grassetto + ombra.
    public static void ApplicaStileTesto(TextMeshProUGUI label)
    {
        if (label == null) return;
        label.fontStyle = FontStyles.Bold;
        if (MaterialeTesto != null) label.fontSharedMaterial = MaterialeTesto;
    }

    private void Awake()
    {
        ConfiguraStatoIniziale();
    }

    void Start()
    {
        udpReceiver = FindFirstObjectByType<UdpReceiver>();
        CreaBottoneHomeTutorial();
        IngrandisciBottoniUI();

        GameObject raggioGO = new GameObject("RaggioPuntatore");
        raggioGO.transform.SetParent(this.transform, false);
        raggioGO.AddComponent<RaggioPuntatore>();

        StartCoroutine(UniformaBottoniTornaDopoFrame());
    }

    // Rende il bottone "Torna al Menù" identico in tutte le modalità: copia lo
    // stile di quello di FaiTu (gruppo x1, riferimento) su Tutorial/Osservatore/
    // Seguimi. Aspetta un frame perché SongListManager posiziona e aggancia i suoi
    // "Bottone-Indietro" nel proprio Start().
    private IEnumerator UniformaBottoniTornaDopoFrame()
    {
        yield return null;
        UniformaBottoniTorna();
    }

    private void UniformaBottoniTorna()
    {
        if (faiTuGroup == null) return;
        Transform rif = faiTuGroup.transform.Find("TornaAlMen\u00F9");
        if (rif == null) return;

        if (tutorialGroup != null)
        {
            Transform t = tutorialGroup.transform.Find("TornaAlMenu");
            CopiaStileBottoneTorna(t, rif);

            // Nel tutorial il testo va su due righe esplicite ("Torna al" / "Menù").
            TextMeshProUGUI txtTutorial = t != null
                ? t.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (txtTutorial != null) txtTutorial.text = "Torna al\nMen\u00F9";
        }

        CopiaStileBottoneTorna(TrovaFiglio(observerGroup, "Bottone-Indietro"), rif);
        CopiaStileBottoneTorna(TrovaFiglio(seguimiGroup, "Bottone-Indietro"), rif);
    }

    private static Transform TrovaFiglio(GameObject gruppo, string nome)
    {
        if (gruppo == null) return null;
        for (int i = 0; i < gruppo.transform.childCount; i++)
        {
            Transform f = gruppo.transform.GetChild(i);
            if (f.name == nome) return f;
        }
        return null;
    }

    // Copia forma, colori ed etichetta del bottone di riferimento sul target,
    // compensando la diversa scala dei gruppi (FaiTu x1, Osservatore/Seguimi x4).
    private static void CopiaStileBottoneTorna(Transform target, Transform riferimento)
    {
        if (target == null || riferimento == null) return;

        float scalaRif = riferimento.lossyScale.x;
        float comp = (scalaRif != 0f) ? target.lossyScale.x / scalaRif : 1f;
        if (comp <= 0f) comp = 1f;

        Image imgRif = riferimento.GetComponent<Image>();
        Image imgDst = target.GetComponent<Image>();
        if (imgRif != null && imgDst != null)
        {
            imgDst.sprite = imgRif.sprite;
            imgDst.type = imgRif.type;
            imgDst.color = imgRif.color;
            imgDst.material = imgRif.material;
            imgDst.pixelsPerUnitMultiplier = imgRif.pixelsPerUnitMultiplier * comp;
        }

        Button btnRif = riferimento.GetComponent<Button>();
        Button btnDst = target.GetComponent<Button>();
        if (btnRif != null && btnDst != null) btnDst.colors = btnRif.colors;

        TextMeshProUGUI txtRif = riferimento.GetComponentInChildren<TextMeshProUGUI>(true);
        TextMeshProUGUI txtDst = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (txtRif != null && txtDst != null)
        {
            txtDst.font = txtRif.font;
            txtDst.fontSharedMaterial = txtRif.fontSharedMaterial;
            txtDst.color = txtRif.color;
            txtDst.fontStyle = txtRif.fontStyle;
            txtDst.alignment = txtRif.alignment;
            txtDst.text = txtRif.text;
            txtDst.fontSize = txtRif.fontSize / comp;
        }
    }

    private void IngrandisciBottoniUI()
    {
        if (menuGroup != null)
        {
            foreach (Transform figlio in menuGroup.transform)
                figlio.localScale = Vector3.one * 1.15f;
        }

        if (faiTuGroup != null)
        {
            foreach (Transform figlio in faiTuGroup.transform)
            {
                if (figlio.name == "TornaAlMen\u00F9" || figlio.name == "GeneraReport")
                {
                    RectTransform rt = (RectTransform)figlio;
                    rt.sizeDelta = new Vector2(460f, 230f);
                    ApplicaStileTesto(figlio.GetComponentInChildren<TextMeshProUGUI>(true));
                }
            }
        }
    }

    private void ConfiguraStatoIniziale()
    {
        statoAttuale = AppState.Onboarding;

        if (calibrationGroup != null) calibrationGroup.SetActive(true);
        if (menuGroup != null) menuGroup.SetActive(false);
        if (tutorialGroup != null) tutorialGroup.SetActive(false);
        if (observerGroup != null) observerGroup.SetActive(false);
        if (seguimiGroup != null) seguimiGroup.SetActive(false);
        if (faiTuGroup != null) faiTuGroup.SetActive(false);
        if (faiTuReportMessaggio != null) faiTuReportMessaggio.SetActive(false);
    }

    public void CambiaStato(AppState nuovoStato)
    {
        if (statoAttuale == nuovoStato) return;

        StartCoroutine(DeselezionaFineFrame());

        SongListManager slm = FindFirstObjectByType<SongListManager>();
        if (slm != null) slm.RilasciaIndietro();

        // Reset tracciamento note seguimi
        expectedNotes.Clear();
        userPressedNotes.Clear();

        // Gestione uscite dagli stati precedenti
        if ((statoAttuale == AppState.Osservatore || statoAttuale == AppState.Seguimi) && udpReceiver != null)
        {
            udpReceiver.InviaComandoStop();
        }

        if (statoAttuale == AppState.FaiTu && udpReceiver != null)
        {
            udpReceiver.InviaComandoStopFaiTu();
        }

        if (faiTuBannerCoroutine != null) StopCoroutine(faiTuBannerCoroutine);
        if (reportFeedbackCoroutine != null) StopCoroutine(reportFeedbackCoroutine);

        if (faiTuReportMessaggio != null) faiTuReportMessaggio.SetActive(false);

        PianoVisualizer visualizer = FindFirstObjectByType<PianoVisualizer>();
        if (visualizer != null)
        {
            visualizer.ResetVisualizer();
            visualizer.PulisciNoteAtteseVisive();
        }

        statoAttuale = nuovoStato;

        // Visibilit� pannelli
        if (calibrationGroup != null) calibrationGroup.SetActive(statoAttuale == AppState.Onboarding);
        if (menuGroup != null) menuGroup.SetActive(statoAttuale == AppState.Menu);
        if (tutorialGroup != null) tutorialGroup.SetActive(statoAttuale == AppState.Tutorial);
        if (observerGroup != null) observerGroup.SetActive(statoAttuale == AppState.Osservatore);
        if (seguimiGroup != null) seguimiGroup.SetActive(statoAttuale == AppState.Seguimi);

        if (faiTuGroup != null)
        {
            faiTuGroup.SetActive(statoAttuale == AppState.FaiTu);

            if (statoAttuale == AppState.FaiTu)
            {
                if (udpReceiver != null) udpReceiver.InviaComandoStartFaiTu();
                faiTuBannerCoroutine = StartCoroutine(MostraBannerFaiTuTemporaneo());
            }
        }
    }

    private IEnumerator MostraBannerFaiTuTemporaneo()
    {
        if (faiTuBannerMessaggio != null) faiTuBannerMessaggio.SetActive(true);
        yield return new WaitForSeconds(5.0f);
        if (faiTuBannerMessaggio != null) faiTuBannerMessaggio.SetActive(false);
    }

    private IEnumerator DeselezionaFineFrame()
    {
        yield return null;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    // --- NAVIGAZIONE GLOBALE ---
    public void AttivaMenu() => CambiaStato(AppState.Menu);
    public void RitornaAlMenu() => CambiaStato(AppState.Menu);

    public void AttivaTutorial()
    {
        sfidaAttuale = 1;
        noteAttualmentePremute = 0;
        tempoUltimoRilascio = -1f;
        ultimaNotaMidi = -1;
        inTransizione = false;

        CambiaStato(AppState.Tutorial);
        if (tutorialText != null) tutorialText.text = "SFIDA 1:\nPremi un tasto molto delicatamente (Suona 'Piano')";
    }

    public void AttivaOsservatore() => CambiaStato(AppState.Osservatore);
    public void AttivaSeguimi() => CambiaStato(AppState.Seguimi);
    public void AttivaFaiTu() => CambiaStato(AppState.FaiTu);

    public void GeneraReportPDF()
    {
        if (udpReceiver != null) udpReceiver.InviaComandoGeneraReport();

        if (reportFeedbackCoroutine != null) StopCoroutine(reportFeedbackCoroutine);
        reportFeedbackCoroutine = StartCoroutine(MostraReportFeedbackTemporaneo());
    }

    private IEnumerator MostraReportFeedbackTemporaneo()
    {
        if (faiTuReportMessaggio != null) faiTuReportMessaggio.SetActive(true);

        // Attesa visualizzazione del messaggio di conferma
        yield return new WaitForSeconds(3.5f);

        if (faiTuReportMessaggio != null) faiTuReportMessaggio.SetActive(false);

        // Ritorno automatico al Menu
        AttivaMenu();
    }

    private void CreaBottoneHomeTutorial()
    {
        if (tutorialGroup == null) return;

        GameObject bottoneGO = new GameObject("TornaAlMenu", typeof(RectTransform));
        bottoneGO.transform.SetParent(tutorialGroup.transform, false);

        RectTransform rt = (RectTransform)bottoneGO.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-500f, 500f);
        rt.sizeDelta = new Vector2(460f, 230f);

        Image sfondo = bottoneGO.AddComponent<Image>();
        sfondo.sprite = SpriteArrotondato != null ? SpriteArrotondato : CreaSpriteBianco();
        sfondo.type = Image.Type.Sliced;

        Button bottone = bottoneGO.AddComponent<Button>();
        bottone.targetGraphic = sfondo;
        bottone.onClick.AddListener(AttivaMenu);

        GameObject labelGO = new GameObject("Testo", typeof(RectTransform));
        labelGO.transform.SetParent(bottoneGO.transform, false);

        RectTransform rtLabel = (RectTransform)labelGO.transform;
        rtLabel.anchorMin = Vector2.zero;
        rtLabel.anchorMax = Vector2.one;
        rtLabel.offsetMin = Vector2.zero;
        rtLabel.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = "Torna al\nMen\u00F9";
        label.font = TMP_Settings.defaultFontAsset != null
            ? TMP_Settings.defaultFontAsset
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        label.fontSize = 72;
        label.color = new Color(0.19607843f, 0.19607843f, 0.19607843f, 1f);
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = true;
        ApplicaStileTesto(label);
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

    public void AvviaRiancoraggio()
    {
        Debug.Log("[DEBUG] Avvio procedura di Riancoraggio...");

        PianoVisualizer visualizer = FindFirstObjectByType<PianoVisualizer>();
        if (visualizer != null)
        {
            ARAnchor oldAnchor = visualizer.GetComponent<ARAnchor>();
            if (oldAnchor != null) Destroy(oldAnchor);
        }

        CambiaStato(AppState.Onboarding);

        ManualAnchor calibrator = FindFirstObjectByType<ManualAnchor>();
        if (calibrator != null) calibrator.ResettaStatoCalibrazione();
    }

    // --- RICEZIONE NOTE ATTESE DA PYTHON (MODALIT� SEGUIMI) ---
    public void ImpostaNoteAttese(int[] notes)
    {
        if (statoAttuale != AppState.Seguimi) return;

        expectedNotes.Clear();
        if (notes != null && notes.Length > 0)
        {
            expectedNotes.AddRange(notes);
        }

        PianoVisualizer visualizer = FindFirstObjectByType<PianoVisualizer>();
        if (visualizer != null)
        {
            visualizer.MostraNoteAttese(notes);
        }

        ControllaNoteSeguimi();
    }

    private void ControllaNoteSeguimi()
    {
        if (expectedNotes.Count == 0) return;

        bool allMatched = true;
        foreach (int expected in expectedNotes)
        {
            if (!userPressedNotes.Contains(expected))
            {
                allMatched = false;
                break;
            }
        }

        if (allMatched)
        {
            expectedNotes.Clear();
            if (udpReceiver != null)
            {
                udpReceiver.InviaComandoSeguimiAvanti();
            }
        }
    }

    public void ValutaNota(int nota, float velocity, string action)
    {
        if (statoAttuale == AppState.Seguimi)
        {
            if (action == "press")
            {
                userPressedNotes.Add(nota);
                ControllaNoteSeguimi();
            }
            else if (action == "release")
            {
                userPressedNotes.Remove(nota);
            }
            return;
        }

        if (statoAttuale != AppState.Tutorial || inTransizione) return;

        if (action == "press")
        {
            float kdt = (tempoUltimoRilascio > 0) ? (Time.time - tempoUltimoRilascio) : 0f;
            bool ceSovrapposizioneKOT = (noteAttualmentePremute > 0);

            switch (sfidaAttuale)
            {
                case 1:
                    if (velocity < 0.236f)
                    {
                        StartCoroutine(TransizioneSfidaCoroutine(2, "SFIDA 2:\nEsegui una scala crescente (note verso destra sempre pi� forti)"));
                    }
                    break;

                case 2:
                    if (ultimaNotaMidi != -1 && nota == ultimaNotaMidi + 1 && velocity > ultimaVelocita)
                    {
                        StartCoroutine(TransizioneSfidaCoroutine(3, "SFIDA 3:\nEsegui lo 'Staccato' (lascia un netto distacco di silenzio tra le note)"));
                    }
                    ultimaNotaMidi = nota;
                    ultimaVelocita = velocity;
                    break;

                case 3:
                    if (!ceSovrapposizioneKOT && tempoUltimoRilascio > 0 && kdt > 0.05f && kdt < 0.35f)
                    {
                        StartCoroutine(TransizioneSfidaCoroutine(4, "SFIDA 4:\nEsegui il 'Legato' (suona la nota successiva prima di rilasciare la precedente)"));
                    }
                    else if (tempoUltimoRilascio > 0 && ceSovrapposizioneKOT)
                    {
                        StartCoroutine(TransizioneErroreCoroutine("Errore: Note sovrapposte!", "SFIDA 3:\nEsegui lo 'Staccato' (lascia un netto distacco di silenzio tra le note)"));
                    }
                    break;

                case 4:
                    if (ceSovrapposizioneKOT)
                    {
                        StartCoroutine(FineTutorialCoroutine());
                    }
                    else if (tempoUltimoRilascio > 0)
                    {
                        StartCoroutine(TransizioneErroreCoroutine("Errore: Note staccate!", "SFIDA 4:\nEsegui il 'Legato' (suona la nota successiva prima di rilasciare la precedente)"));
                    }
                    break;
            }
            noteAttualmentePremute++;
        }

        if (action == "release")
        {
            noteAttualmentePremute = Mathf.Max(0, noteAttualmentePremute - 1);
            if (noteAttualmentePremute == 0) tempoUltimoRilascio = Time.time;
        }
    }

    private IEnumerator TransizioneSfidaCoroutine(int prossimaSfida, string testoNuovaSfida)
    {
        inTransizione = true;
        if (tutorialText != null) tutorialText.text = "<color=#00FF00><b>COMPLETATO!</b></color>\n\nOttimo lavoro! Preparati per la prossima sfida...";

        yield return new WaitForSeconds(2.5f);

        sfidaAttuale = prossimaSfida;
        tempoUltimoRilascio = -1f;
        ultimaNotaMidi = -1;
        ultimaVelocita = 0f;
        noteAttualmentePremute = 0;

        if (tutorialText != null) tutorialText.text = testoNuovaSfida;
        inTransizione = false;
    }

    private IEnumerator TransizioneErroreCoroutine(string messaggioErrore, string testoSfidaDaRipristinare)
    {
        inTransizione = true;
        if (tutorialText != null) tutorialText.text = $"<color=#FF3333><b>{messaggioErrore}</b></color>\n\nRileggi bene le istruzioni e riprova.";

        yield return new WaitForSeconds(2.0f);

        if (tutorialText != null) tutorialText.text = testoSfidaDaRipristinare;
        inTransizione = false;
    }

    private IEnumerator FineTutorialCoroutine()
    {
        inTransizione = true;
        sfidaAttuale = 0;
        if (tutorialText != null) tutorialText.text = "<color=#00FF00><b>ECCELLENTE, TUTORIAL COMPLETATO!</b></color>\n \n Ora verrai reindirizzato al men�...";

        yield return new WaitForSeconds(3.5f);
        AttivaMenu();
        inTransizione = false;
    }
}