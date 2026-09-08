using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics; // Necessario per avviare/chiudere processi esterni

public class UdpReceiver : MonoBehaviour
{
    public PianoVisualizer visualizer;
    private GameManager gameManager;
    private Queue<string> messageQueue = new Queue<string>();
    private Thread receiveThread;
    private UdpClient client;
    public int port = 5005;
    private bool isRunning = true;

    [Header("Configurazione Python")]
    [Tooltip("Nome del comando python o percorso dell'eseguibile (es. python o python3)")]
    public string pythonPath = "python";
    [Tooltip("Percorso assoluto o relativo dello script python (es. C:/Users/vrlab/Desktop/Nora/midi_bridge.py)")]
    public string scriptPath = "C:/Users/vrlab/Desktop/Nora/midi_bridge.py";

    private Process pythonProcess;

    void Awake()
    {
        AvviaScriptPython();
    }

    void Start()
    {
        gameManager = FindFirstObjectByType<GameManager>();

        isRunning = true;
        receiveThread = new Thread(new ThreadStart(ReceiveData))
        {
            IsBackground = true
        };
        receiveThread.Start();
    }

    private void AvviaScriptPython()
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = true, // Lascia a true per vedere la finestra del terminale con i log
                CreateNoWindow = false
            };

            pythonProcess = Process.Start(startInfo);
            UnityEngine.Debug.Log("<color=cyan>[UDP]</color> Script Python avviato automaticamente.");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[UDP ERRORE] Impossibile avviare Python: {e.Message}");
        }
    }

    private void ReceiveData()
    {
        try
        {
            client = new UdpClient(port);
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[UDP ERRORE] Impossibile aprire la porta UDP {port}: {e.Message}");
            return;
        }

        while (isRunning)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);
                lock (messageQueue)
                {
                    messageQueue.Enqueue(text);
                }
            }
            catch (SocketException) { break; }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError("[UDP ERRORE RICEZIONE]: " + e.Message);
                break;
            }
        }
    }

    void Update()
    {
        while (messageQueue.Count > 0)
        {
            string text = null;
            lock (messageQueue)
            {
                if (messageQueue.Count > 0)
                    text = messageQueue.Dequeue();
            }

            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    var json = JsonUtility.FromJson<MidiData>(text.Replace("'", "\""));
                    if (json != null)
                    {
                        if (json.action == "report_ready")
                        {
                            UnityEngine.Debug.Log($"<color=green>[REPORT PDF PRONTO]</color> File creato: {json.file}");
                        }
                        else if (json.action == "expect_notes")
                        {
                            if (gameManager != null) gameManager.ImpostaNoteAttese(json.notes);
                        }
                        // NUOVO BLOCCO DA AGGIUNGERE: Intercetta il comando di pulizia scena da Python
                        else if (json.action == "clear_scene")
                        {
                            if (visualizer != null)
                            {
                                visualizer.ResetVisualizer();
                                visualizer.PulisciNoteAtteseVisive();
                            }
                        }
                        else
                        {
                            // Qui entreranno solo "press" e "release"
                            if (visualizer != null) visualizer.OnNoteReceived(json.note, json.velocity, json.action);
                            if (gameManager != null) gameManager.ValutaNota(json.note, json.velocity, json.action);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError("Errore parsing JSON: " + e.Message);
                }
            }
        }
    }

    private void InviaJsonAPython(string jsonComando)
    {
        try
        {
            using (UdpClient sendClient = new UdpClient())
            {
                byte[] data = Encoding.UTF8.GetBytes(jsonComando);
                sendClient.Send(data, data.Length, "127.0.0.1", 5006);
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("[UDP ERRORE INVIO]: " + e.Message);
        }
    }

    public void InviaComandoRiproduzione(string nomeFileMidi, bool isSeguimi = false)
    {
        // 1. Prima di tutto blocchiamo la riproduzione precedente
        InviaComandoStop();

        // 2. Puliamo subito il visualizzatore in Unity per evitare sovrapposizioni di vecchie colonne
        PianoVisualizer visualizer = FindFirstObjectByType<PianoVisualizer>();
        if (visualizer != null)
        {
            visualizer.ResetVisualizer();
            visualizer.PulisciNoteAtteseVisive();
        }

        // 3. Mandiamo il comando al nuovo brano
        string azione = isSeguimi ? "play_song_follow" : "play_song";
        string json = "{\"action\":\"" + azione + "\", \"filename\":\"" + nomeFileMidi + "\"}";
        InviaJsonAPython(json);
        UnityEngine.Debug.Log($"[UDP] Richiesto brano ({azione}): {nomeFileMidi}");
    }

    public void InviaComandoStartFaiTu(string nomePerformance = "Improvvisazione_Libera")
    {
        string json = "{\"action\":\"start_fai_tu\", \"filename\":\"" + nomePerformance + "\"}";
        InviaJsonAPython(json);
    }

    public void InviaComandoStopFaiTu()
    {
        string json = "{\"action\":\"stop_fai_tu\"}";
        InviaJsonAPython(json);
    }

    public void InviaComandoGeneraReport()
    {
        string json = "{\"action\":\"generate_report\"}";
        InviaJsonAPython(json);
    }

    public void InviaComandoSeguimiAvanti()
    {
        string json = "{\"action\":\"next_step\"}";
        InviaJsonAPython(json);
    }

    public void InviaComandoStop()
    {
        string json = "{\"action\":\"stop_song\"}";
        InviaJsonAPython(json);
    }

    private void TerminaScriptPython()
    {
        try
        {
            InviaComandoStop();
            if (client != null) client.Close();

            if (pythonProcess != null && !pythonProcess.HasExited)
            {
                pythonProcess.Kill(); // Termina forzatamente il processo e chiude la finestra del terminale
                pythonProcess.Dispose();
                pythonProcess = null;
                UnityEngine.Debug.Log("<color=cyan>[UDP]</color> Processo Python terminato.");
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[UDP ERRORE] Chiusura Python fallita: {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        TerminaScriptPython();
    }

    [System.Serializable]
    public class MidiData
    {
        public int note;
        public float velocity;
        public string action;
        public int[] notes;
        public string file;
        public MidiData() { }
    }
}