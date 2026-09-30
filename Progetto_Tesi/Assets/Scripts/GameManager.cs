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

    [Header("UI Specifica Modalità Fai Tu")]
    public GameObject faiTuBannerMessaggio;
    public GameObject faiTuReportMessaggio;

    [Header("Riferimenti UI Testi")]
    public TextMeshProUGUI tutorialText;

    private UdpReceiver udpReceiver;
    private float tempoUltimoRilascio = -1f;
    private int ultimaNotaMidi = -1;
    private float ultimaVelocita = 0f;
    public int sfidaAttuale = 0;

    // Anti-rimbalzo per il Tutorial: tiene traccia delle note realmente premute.
    // Una pressa forte può generare due note_on ravvicinati (sensore/martelletti):
    // il secondo viene ignorato e il conteggio resta affidabile (contatore derivato
    // dal set, non da incrementi -> auto-riparante anche con sovrapposizioni).
    private readonly HashSet<int> tastiTutorialPremuti = new HashSet<int>();
    // Prima nota della sfida staccato corrente (guardia anti auto-trigger).
    private int primaNotaStaccato = -1;
    // Contatore delle note della "Scala dinamica crescente" (SFIDA 2): servono
    // esattamente 4 note in successione e sempre piu' forti per concludere.
    private int noteScalaCrescente = 0;
    private float tempoUltimaNotaScala = -1f;
    // True mentre il bridge sta riproducendo l'esempio dimostrativo della sfida.
    private bool esempioInCorso = false;
    // Coroutine dell'esempio/sfida corrente del Tutorial: fermata se si abbandona
    // o si riapre il tutorial (evita coroutine orfane che bloccano la rilevazione).
    private Coroutine tutorialCoroutine = null;

    // Gestione Note per la modalità "Seguimi"
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

    // --- PALETTE PANNELIi/BOTTONI (Alabaster/Iron/BlueSlate/Pacific) ---
    public static readonly Color AlabasterGrey = new Color(0.8627f, 0.8627f, 0.8667f, 1f); // #dcdcdd
    public static readonly Color IronGrey = new Color(0.2745f, 0.2863f, 0.2980f, 1f);       // #46494c
    public static readonly Color BlueSlate = new Color(0.2980f, 0.3608f, 0.4078f, 1f);      // #4c5c68
    public static readonly Color PacificCyan = new Color(0.0980f, 0.5216f, 0.6314f, 1f);    // #1985a1

    // --- PALETTE SCRITTE COLORATE (invariate) ---
    public static readonly Color NavajoWhite = new Color(1.000f, 0.878f, 0.710f, 1f); // #ffe0b5
    public static readonly Color MutedOlive = new Color(0.643f, 0.686f, 0.412f, 1f);   // #a4af69
    public static readonly Color PaleAmber = new Color(0.929f, 0.898f, 0.502f, 1f);    // #ede580
    public static readonly Color Espresso = new Color(0.298f, 0.153f, 0.098f, 1f);      // #4c2719
    public static readonly Color LightCoral = new Color(0.898f, 0.420f, 0.439f, 1f);    // #e56b70

    // --- FONT (Inter) ---
    private static TMP_FontAsset fontApp;
    private static bool fontCercato;

    public static TMP_FontAsset FontApp
    {
        get
        {
            if (!fontCercato)
            {
                fontCercato = true;
                fontApp = TrovaFontApp();
            }
            return fontApp;
        }
    }

    private static TMP_FontAsset TrovaFontApp()
    {
        TMP_FontAsset[] fontAssets = Resources.LoadAll<TMP_FontAsset>("Fonts");
        foreach (TMP_FontAsset fa in fontAssets)
            if (fa != null && fa.name.ToLower().Contains("inter")) return fa;
        if (fontAssets.Length > 0 && fontAssets[0] != null) return fontAssets[0];

        Font[] fonts = Resources.LoadAll<Font>("Fonts");
        if (fonts.Length > 0 && fonts[0] != null)
        {
            TMP_FontAsset dinamico = TMP_FontAsset.CreateFontAsset(fonts[0]);
            if (dinamico != null) return dinamico;
        }

        return TMP_Settings.defaultFontAsset;
    }

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
            if (matOmbra == null)
            {
                matOmbra = CreaMaterialeOmbra(FontApp);
                if (matOmbra == null) matOmbra = TrovaMaterialeOmbra();
            }
            return matOmbra;
        }
    }

    // Materiale con ombra (underlay) costruito sul font attivo: clona il materiale
    // del font e attiva l'underlay, cosi' l'ombra resta corretta anche con Inter.
    private static Material CreaMaterialeOmbra(TMP_FontAsset font)
    {
        if (font == null || font.material == null) return null;

        Material m = new Material(font.material);
        m.name = font.name + " Drop Shadow";
        m.EnableKeyword("UNDERLAY_ON");
        m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.5f));
        m.SetFloat("_UnderlayOffsetX", 0.4f);
        m.SetFloat("_UnderlayOffsetY", -0.4f);
        m.SetFloat("_UnderlayDilate", 0.1f);
        m.SetFloat("_UnderlaySoftness", 0.15f);
        return m;
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

    // Applica a una label dei bottoni lo stile uniforme: font Inter, grassetto + ombra.
    public static void ApplicaStileTesto(TextMeshProUGUI label)
    {
        if (label == null) return;
        if (FontApp != null) label.font = FontApp;
        label.fontStyle = FontStyles.Bold;
        if (MaterialeTesto != null) label.fontSharedMaterial = MaterialeTesto;
    }

    private void Awake()
    {
        ConfiguraStatoIniziale();
    }

    void Start()
    {
        Application.targetFrameRate = 72;
        udpReceiver = FindFirstObjectByType<UdpReceiver>();
        CreaBottoneHomeTutorial();
        IngrandisciBottoniUI();
        SistemaTestiUI();

        GameObject raggioGO = new GameObject("RaggioPuntatore");
        raggioGO.transform.SetParent(this.transform, false);
        raggioGO.AddComponent<RaggioPuntatore>();

        StartCoroutine(UniformaBottoniTornaDopoFrame());
    }

    private void SistemaTestiUI()
    {
        if (tutorialText != null)
        {
            RectTransform rtTutorial = tutorialText.transform as RectTransform;
            if (rtTutorial != null)
            {
                rtTutorial.anchorMin = new Vector2(0.5f, 0.5f);
                rtTutorial.anchorMax = new Vector2(0.5f, 0.5f);
                rtTutorial.pivot = new Vector2(0.5f, 0.5f);
                rtTutorial.anchoredPosition = Vector2.zero;
                rtTutorial.sizeDelta = new Vector2(440f, 360f);
            }
            tutorialText.alignment = TextAlignmentOptions.Center;
            tutorialText.enableWordWrapping = true;
        }

        if (faiTuReportMessaggio != null)
        {
            TextMeshProUGUI reportText = faiTuReportMessaggio.GetComponent<TextMeshProUGUI>();
            if (reportText != null)
                reportText.text = "Report Generato!\n\nLo trovi nella cartella Sessioni";
        }
    }

    // Rende il bottone "Torna al Menù" identico in tutte le modalità: copia lo
    // stile di quello di FaiTu (gruppo x1, riferimento) su Tutorial/Osservatore/
    // Seguimi. Aspetta un frame perché SongListManager posiziona e aggancia i suoi
    // "Bottone-Indietro" nel proprio Start().
    private IEnumerator UniformaBottoniTornaDopoFrame()
    {
        yield return null;
        UniformaBottoniTorna();
        ApplicaPalette();
        ApplicaFontGlobale();
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

    // Copia forma ed etichetta del bottone di riferimento sul target, compensando
    // la diversa scala dei gruppi (FaiTu x1, Osservatore/Seguimi x4). NON copia i
    // colori: la palette viene applicata a parte da ApplicaPalette().
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
            imgDst.material = imgRif.material;
            imgDst.pixelsPerUnitMultiplier = imgRif.pixelsPerUnitMultiplier * comp;
        }

        TextMeshProUGUI txtRif = riferimento.GetComponentInChildren<TextMeshProUGUI>(true);
        TextMeshProUGUI txtDst = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (txtRif != null && txtDst != null)
        {
            txtDst.font = txtRif.font;
            txtDst.fontSharedMaterial = txtRif.fontSharedMaterial;
            txtDst.fontStyle = txtRif.fontStyle;
            txtDst.alignment = txtRif.alignment;
            txtDst.text = txtRif.text;
            txtDst.fontSize = txtRif.fontSize / comp;
        }
    }

    // Applica la palette ai bottoni statici, ai pannelli e ai titoli.
    private void ApplicaPalette()
    {
        // Bottoni del menu, Riancora, "Torna al Menù" e Genera Report:
        // Alabaster -> Pacific Cyan (hover), Iron Grey (conferma), testo nero.
        StileBottone(TrovaFiglio(menuGroup, "Bottone Tutorial"), AlabasterGrey, PacificCyan, IronGrey, Color.black);
        StileBottone(TrovaFiglio(menuGroup, "Bottone Osservatore"), AlabasterGrey, PacificCyan, IronGrey, Color.black);
        StileBottone(TrovaFiglio(menuGroup, "Bottone Seguimi"), AlabasterGrey, PacificCyan, IronGrey, Color.black);
        StileBottone(TrovaFiglio(menuGroup, "Bottone Fai Tu"), AlabasterGrey, PacificCyan, IronGrey, Color.black);
        StileBottone(TrovaFiglio(menuGroup, "BottoneRiancora"), AlabasterGrey, PacificCyan, IronGrey, Color.black);
        StileBottone(TrovaFiglio(faiTuGroup, "TornaAlMen\u00F9"), AlabasterGrey, PacificCyan, IronGrey, Color.black);
        StileBottone(TrovaFiglio(faiTuGroup, "GeneraReport"), AlabasterGrey, PacificCyan, IronGrey, Color.black);
        StileBottone(TrovaFiglio(tutorialGroup, "TornaAlMenu"), AlabasterGrey, PacificCyan, IronGrey, Color.black);

        // "Indietro" di Osservatore/Seguimi: gestiti a mano da IndietroFeedback.
        StileIndietro(TrovaFiglio(observerGroup, "Bottone-Indietro"));
        StileIndietro(TrovaFiglio(seguimiGroup, "Bottone-Indietro"));

        // Pannelli "Sfondo" di Tutorial e FaiTu: Blue Slate.
        ColoraPannelli(tutorialGroup);
        ColoraPannelli(faiTuGroup);

        // Titoli dei gruppi: Navajo (la calibrazione resta invariata).
        ColoraTitoli(menuGroup);
        ColoraTitoli(tutorialGroup);
        ColoraTitoli(observerGroup);
        ColoraTitoli(seguimiGroup);
        ColoraTitoli(faiTuGroup);
    }

    private static void StileBottone(Transform target, Color baseC, Color hoverC, Color pressedC, Color testo)
    {
        if (target == null) return;

        Image img = target.GetComponent<Image>();
        if (img != null) img.color = Color.white;

        Button btn = target.GetComponent<Button>();
        if (btn != null)
        {
            ColorBlock c = btn.colors;
            c.normalColor = baseC;
            c.highlightedColor = hoverC;
            c.pressedColor = pressedC;
            c.selectedColor = hoverC;
            c.disabledColor = baseC;
            c.colorMultiplier = 1f;
            btn.colors = c;
        }

        TextMeshProUGUI label = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.color = testo;
    }

    private static void StileIndietro(Transform target)
    {
        if (target == null) return;

        Image img = target.GetComponent<Image>();
        if (img != null) img.color = AlabasterGrey;

        Button btn = target.GetComponent<Button>();
        if (btn != null)
        {
            ColorBlock c = btn.colors;
            c.highlightedColor = PacificCyan;
            c.pressedColor = IronGrey;
            btn.colors = c;
        }

        IndietroFeedback fb = target.GetComponent<IndietroFeedback>();
        if (fb != null) fb.ImpostaColori(AlabasterGrey, PacificCyan);

        TextMeshProUGUI label = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.color = Color.black;
    }

    private static void ColoraPannelli(GameObject gruppo)
    {
        if (gruppo == null) return;
        foreach (Transform t in gruppo.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Sfondo") continue;
            Image img = t.GetComponent<Image>();
            if (img != null) img.color = BlueSlate;
        }
    }

    // Porta i titoli "Testo Titolo" su Navajo (la calibrazione resta invariata).
    private static void ColoraTitoli(GameObject gruppo)
    {
        if (gruppo == null) return;
        foreach (TextMeshProUGUI t in gruppo.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (t.name == "Testo Titolo") t.color = NavajoWhite;
        }
    }

    // Sostituisce il font di TUTTE le scritte (anche inattive) con Inter, mantenendo
    // l'ombra sulle label che l'avevano.
    private void ApplicaFontGlobale()
    {
        TMP_FontAsset fa = FontApp;
        if (fa == null) return;

        foreach (TextMeshProUGUI t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool avevaOmbra = t.fontSharedMaterial != null && t.fontSharedMaterial.name.Contains("Drop Shadow");
            t.font = fa;
            if (avevaOmbra) ApplicaStileTesto(t);
        }

        foreach (TextMeshPro t in FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            t.font = fa;
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

        // Se si abbandona il tutorial, ferma la riproduzione dell'esempio e ogni
        // coroutine pendente: mai coroutine orfane che bloccano la sfida.
        if (statoAttuale == AppState.Tutorial && nuovoStato != AppState.Tutorial)
        {
            if (tutorialCoroutine != null)
            {
                StopCoroutine(tutorialCoroutine);
                tutorialCoroutine = null;
            }
            if (esempioInCorso)
            {
                esempioInCorso = false;
                if (udpReceiver != null) udpReceiver.InviaComandoStopEsempio();
            }
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

        // Visibilità pannelli
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
        if (faiTuBannerMessaggio != null)
        {
            RectTransform bannerRt = faiTuBannerMessaggio.transform is RectTransform
                ? (RectTransform)faiTuBannerMessaggio.transform
                : null;
            if (bannerRt != null)
            {
                bannerRt.anchorMin = new Vector2(0.5f, 0.5f);
                bannerRt.anchorMax = new Vector2(0.5f, 0.5f);
                bannerRt.pivot = new Vector2(0.5f, 0.5f);
                bannerRt.anchoredPosition = Vector2.zero;
            }
            faiTuBannerMessaggio.SetActive(true);
        }
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
        tastiTutorialPremuti.Clear();
        primaNotaStaccato = -1;
        noteScalaCrescente = 0;
        tempoUltimaNotaScala = -1f;
        tempoUltimoRilascio = -1f;
        ultimaNotaMidi = -1;
        inTransizione = false;

        CambiaStato(AppState.Tutorial);
        if (tutorialCoroutine != null) StopCoroutine(tutorialCoroutine);
        tutorialCoroutine = StartCoroutine(AvviaSfidaConEsempio(1, "SFIDA 1:\nPremi un tasto molto delicatamente (Suona 'Piano')"));
    }

    // Chiamato dal bridge quando termina la riproduzione dell'esempio.
    public void EsempioCompletato()
    {
        esempioInCorso = false;
    }

    private static string TitoloSfida(int sfida)
    {
        switch (sfida)
        {
            case 2: return "Scala dinamica crescente";
            case 3: return "Staccato";
            case 4: return "Legato";
            default: return "Suona piano";
        }
    }

    // Ogni sfida mostra prima l'ESEMPIO (colonna + suono dal pianoforte, registrato
    // dal bridge) e solo dopo invita l'utente a provare, come da descrizione.
    private IEnumerator AvviaSfidaConEsempio(int sfida, string testoDescrizione)
    {
        inTransizione = true;
        sfidaAttuale = sfida;
        esempioInCorso = false;

        try
        {
            if (tutorialText != null)
            {
                tutorialText.text = "SFIDA " + sfida + "\n" + TitoloSfida(sfida)
                                    + "\n\nCome l'esempio che visualizzi";
            }

            yield return new WaitForSeconds(1.2f);

            if (udpReceiver != null)
            {
                udpReceiver.InviaComandoEsempio(sfida);
                esempioInCorso = true;
            }

            // Attende la fine dell'esempio. Timeout breve (3s) di sicurezza: se il
            // bridge non conferma (non riavviato, o senza supporto esempi) si passa
            // comunque alla prova dell'utente, mai un tutorial bloccato.
            if (tutorialText != null) tutorialText.text = "Riproduco l'esempio...";
            float timeout = Time.time + 3f;
            while (esempioInCorso && Time.time < timeout)
                yield return null;
            if (esempioInCorso)
            {
                Debug.Log("[TUTORIAL] Esempio non confermato dal bridge: passo alla prova.");
            }
            esempioInCorso = false;

            // Reset completo dello stato: la prova dell'utente parte da zero e i tasti
            // dell'esempio non devono influenzare la rilevazione.
            tastiTutorialPremuti.Clear();
            primaNotaStaccato = -1;
            noteScalaCrescente = 0;
            tempoUltimaNotaScala = -1f;
            tempoUltimoRilascio = -1f;
            ultimaNotaMidi = -1;
            ultimaVelocita = 0f;

            inTransizione = false;
            if (tutorialText != null) tutorialText.text = "Ora tocca a te";
            yield return new WaitForSeconds(1.5f);

            if (tutorialText != null) tutorialText.text = testoDescrizione;
        }
        finally
        {
            // inTransizione deve SEMPRE tornare false: nessun tutorial bloccabile.
            inTransizione = false;
        }
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
        label.font = FontApp != null
            ? FontApp
            : (TMP_Settings.defaultFontAsset != null
                ? TMP_Settings.defaultFontAsset
                : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF"));
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

    // --- RICEZIONE NOTE ATTESE DA PYTHON (MODALITÀ SEGUIMI) ---
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
            // Rimbalzo/doppio note_on della stessa nota già premuta: ignorato.
            // Con le pressioni forti i sensori possono reinviare la nota: senza
            // questa guardia lo Staccato veniva bloccato silenziosamente.
            if (!tastiTutorialPremuti.Add(nota))
            {
                Debug.Log("[TUTORIAL] Doppia pressione ignorata (nota " + nota + ")");
                return;
            }

            float kdt = (tempoUltimoRilascio > 0) ? (Time.time - tempoUltimoRilascio) : 0f;
            bool ceSovrapposizioneKOT = (tastiTutorialPremuti.Count > 1);

            switch (sfidaAttuale)
            {
                case 1:
                    if (velocity < 0.236f)
                    {
                        StartCoroutine(TransizioneSfidaCoroutine(2, "SFIDA 2:\nEsegui una Scala dinamica crescente di 4 note (note verso destra sempre pi\u00F9 forti)"));
                    }
                    break;

                case 2:
                    // Scala dinamica crescente: 4 note in successione (qualsiasi
                    // nota, senza vincolo di direzione) e sempre piu' forti, con
                    // tolleranza ~10%: un lieve calo non azzera la scala. Note
                    // troppo ravvicinate (<0.04s) sono ignorate come spurie; una
                    // pausa oltre i 3s fa ripartire il conteggio da 1.
                    if (ultimaNotaMidi == -1)
                    {
                        noteScalaCrescente = 1;
                        tempoUltimaNotaScala = Time.time;
                    }
                    else
                    {
                        float dtScala = Time.time - tempoUltimaNotaScala;
                        if (dtScala < 0.04f)
                        {
                            break;
                        }
                        if (dtScala > 3.0f)
                        {
                            noteScalaCrescente = 1;
                        }
                        else if (velocity >= ultimaVelocita * 0.90f)
                        {
                            noteScalaCrescente++;
                            if (noteScalaCrescente >= 4)
                            {
                                StartCoroutine(TransizioneSfidaCoroutine(3, "SFIDA 3:\nEsegui lo 'Staccato' (Suona le due note in rapida successione con un tocco brevissimo e staccato su ciascuna, come se i tasti scottassero)"));
                            }
                        }
                        else
                        {
                            noteScalaCrescente = 1;
                        }
                        tempoUltimaNotaScala = Time.time;
                    }
                    ultimaNotaMidi = nota;
                    ultimaVelocita = velocity;
                    break;

                case 3:
                    // Registra la prima nota dello staccato (solo alla prima pollice
                    // della sfida): la seconda deve essere diversa per avanzare.
                    if (tastiTutorialPremuti.Count == 1 && primaNotaStaccato == -1)
                        primaNotaStaccato = nota;

                    if (!ceSovrapposizioneKOT && tempoUltimoRilascio > 0 && kdt > 0.04f && kdt < 0.5f && nota != primaNotaStaccato)
                    {
                        StartCoroutine(TransizioneSfidaCoroutine(4, "SFIDA 4:\nEsegui il 'Legato' (suona la nota successiva prima di rilasciare la precedente)"));
                    }
                    else if (tempoUltimoRilascio > 0 && ceSovrapposizioneKOT)
                    {
                        StartCoroutine(TransizioneErroreCoroutine("Errore: Note sovrapposte!", "SFIDA 3:\nEsegui lo 'Staccato' (Suona le due note in rapida successione con un tocco brevissimo e staccato su ciascuna, come se i tasti scottassero)"));
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
        }

        if (action == "release")
        {
            tastiTutorialPremuti.Remove(nota);
            if (tastiTutorialPremuti.Count == 0) tempoUltimoRilascio = Time.time;
        }
    }

    private IEnumerator TransizioneSfidaCoroutine(int prossimaSfida, string testoNuovaSfida)
    {
        inTransizione = true;
        if (tutorialText != null) tutorialText.text = "<color=#a4af69><b>COMPLETATO!</b></color>\n\nOttimo lavoro! Preparati per la prossima sfida...";

        yield return new WaitForSeconds(2.5f);

        if (tutorialCoroutine != null) StopCoroutine(tutorialCoroutine);
        tutorialCoroutine = StartCoroutine(AvviaSfidaConEsempio(prossimaSfida, testoNuovaSfida));
        yield return tutorialCoroutine;
        tutorialCoroutine = null;
    }

    private IEnumerator TransizioneErroreCoroutine(string messaggioErrore, string testoSfidaDaRipristinare)
    {
        inTransizione = true;
        if (tutorialText != null) tutorialText.text = $"<color=#e56b70><b>{messaggioErrore}</b></color>\n\nRileggi bene le istruzioni e riprova.";

        yield return new WaitForSeconds(2.0f);

        // Riparte da zero anche il primo tasto dello staccato per il prossimo tentativo.
        primaNotaStaccato = -1;
        if (tutorialText != null) tutorialText.text = testoSfidaDaRipristinare;
        inTransizione = false;
    }

    private IEnumerator FineTutorialCoroutine()
    {
        inTransizione = true;
        sfidaAttuale = 0;
        tastiTutorialPremuti.Clear();
        primaNotaStaccato = -1;
        noteScalaCrescente = 0;
        tempoUltimaNotaScala = -1f;
        if (tutorialText != null) tutorialText.text = "<color=#a4af69><b>ECCELLENTE, TUTORIAL COMPLETATO!</b></color>\n \n Ora verrai reindirizzato al men\u00F9...";

        yield return new WaitForSeconds(3.5f);
        AttivaMenu();
        inTransizione = false;
    }
}