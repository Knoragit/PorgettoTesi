using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BranoDinamicoUI : MonoBehaviour
{
    private int songId = -1;

    public void ImpostaBrano(string titolo, string autore, int id, bool nuovoRisultato = false)
    {
        songId = id;

        TextMeshProUGUI label = GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            string prefix = nuovoRisultato ? "<color=#00FF00><b>■ Scaricato: </b></color>" : "";
            label.text = prefix + titolo + "\n<size=80%><color=#aaaaaa>" + autore + "</color></size>";
        }

        Button btn = GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(AlClick);
        }
    }

    private void AlClick()
    {
        PianoVisualizer visualizer = FindFirstObjectByType<PianoVisualizer>();
        if (visualizer != null)
        {
            visualizer.ResetVisualizer();
            visualizer.PulisciNoteAtteseVisive();
        }

        UdpReceiver receiver = FindFirstObjectByType<UdpReceiver>();
        GameManager gm = FindFirstObjectByType<GameManager>();
        bool isSeguimi = (gm != null && gm.statoAttuale == GameManager.AppState.Seguimi);

        if (receiver != null && songId >= 0)
        {
            receiver.InviaComandoRiproduzioneConId(songId, isSeguimi);
        }

        // Se eravamo in modalità ricerca, la canzone scelta è partita: torna la
        // lista completa (lista iniziale + il brano cercato).
        SongListManager slm = FindFirstObjectByType<SongListManager>();
        if (slm != null) slm.CanzoneAvviata();
    }
}
