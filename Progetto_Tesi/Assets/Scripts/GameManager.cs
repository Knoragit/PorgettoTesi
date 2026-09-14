using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;

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

    private void Awake()
    {
        ConfiguraStatoIniziale();
    }

    void Start()
    {
        udpReceiver = FindFirstObjectByType<UdpReceiver>();
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

        // Ritorno automatico al Men�
        AttivaMenu();
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