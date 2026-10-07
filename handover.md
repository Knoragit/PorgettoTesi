# Handover — Stato del progetto NORA (tesi VR Piano Learning)

Documento di passaggio per un'**AI che scriverà la tesi**: contiene cosa è NORA, come è fatta,
dove sta ogni cosa nel codice, le regole del repo e i traguardi già raggiunti. Scrivere la tesi in
italiano, usando la nomenclatura del progetto (Vedi sezione 15 per la mappa lingua→codice).

> Esiste anche `handover_questionario.md`: brief per progettare un questionario UX (attività a
> parte); NON fa parte della tesi.

---

## 1. Cos'è il progetto

**NORA** è un'applicazione di apprendimento pianistico in **realtà virtuale**: l'utente indossa un
visore VR con *passthrough* (sfondo nero, camera `ClearFlags=SolidColor`) e suona un **pianoforte
MIDI reale**. L'app mostra una tastiera virtuale a colonne che reagisce in tempo reale alle note
eseguite, guida l'utente in esercizi su brani reali e produce, a fine esecuzione, un **report in PDF**
con analisi dell'esecuzione (dinamica, articolazione, tempo, note-note).

Architettura a **due processi** che dialogano in locale via **UDP**:
1. **App Unity** (`Progetto_Tesi/`) — UI, tutorial, visualizzatore, viewer del report. L'AI del gioco.
2. **Bridge Python** (`midi_bridge (1).py`) — MIDI in/out, riproduzione brani, registrazione,
   ricerca/scaricamento online di file MIDI, generazione PDF/PNG/WAV, database locale.

Tesi attuale: lavoro sul **report** (linguaggio porta a 5 pagine: cromagramma, radar, guida),
sul **tutorial** e sulla **UI finale** (palette pastiello, bottone +, viewer del report).

---

## 2. Ambiente e postazione

| Voce | Valore |
|---|---|
| S.O. | Windows |
| Unity | **6000.3.11f1** (Play Mode per verificare: molte scelte non si vedono in Editor) |
| Progetto Unity | `Progetto_Tesi/` |
| Bridge Python | `midi_bridge (1).py` (**ATTENZIONE:** `midi_bridge.py` è una copia vecchia, NON è quello attivo) |
| Repository | `C:\Users\vrlab\Desktop\Nora` — branch `main`, remote `https://github.com/Knoragit/PorgettoTesi.git` |
| Dipendenze Python | `mido`, `numpy`, `matplotlib`, `requests` (tutte installate) |
| Hardware richiesto | tastiera MIDI (porta fisica; il bridge ignora porte virtuali/loopback/through) + visore VR |

---

## 3. Regole non negoziabili (leggi PRIMA di toccare codice)

| Regola | Perché |
|---|---|
| **Non modificare mai `Progetto_Tesi/Assets/Scenes/SampleScene.unity`** | L'Editor è aperto sulla scena con recovery in `Assets/_Recovery/`. Tutto ciò che è UI si applica **a runtime** da `Assets/Scripts/*.cs`. |
| **File in CRLF**, mai LF; **niente BOM** | alcuni file erano diventati LF e si staccavano dal resto del repo |
| **La `ù` nelle stringhe C# si scrive `\u00F9`** | scriverla literalmente produce U+FFFD e rompe la ricerca per oggetto (i nomi in scena sono `TornaAlMen\u00F9`) |
| **Non committare senza richiesta esplicita** | mai `git commit`/`push` di propria iniziativa |
| File Python del root: **LF**, niente BOM, commenti in ASCII | convenzione attuale di `midi_bridge (1).py` e `simulatore_unity.py` |

---

## 4. Architettura e flusso dei dati

```
                +-------------------------------------------+
                |  APP UNITY (la scena, runtime)             |
                |  GameManager · SongListManager · Viewer    |
                +-----------+-------------------------------+
                            | UDP JSON (loopback 127.0.0.1)
              in 5005 (Unity ascolta) · out 5006 (bridge ascolta)
                            |
                +-----------+-------------------------------+
                |  BRIDGE PYTHON  midi_bridge (1).py        |
                |  MIDI in/out · registrazione · ricerca    |
                |  generazione PDF/PNG/WAV · database       |
                +-------------------------------------------+
                            |
                        tastiera MIDI (reale o, per i brani, file MIDI riprodotti)
```

Cartelle e file chiave (`Desktop\Nora\`):
- `Sessioni\Sessione_YYYYMMDD_HHMMSS\` → report di ogni esecuzione (`Report_Completo_Nora.pdf`,
  `pagina_N.png`, `Registrazione_Audio.wav`).
- `Canzoni\` → file `.mid` scaricati o locali.
- `nora_database.db` → cache ricerche (`tabella midi_files`).
- `Raleway\static\` → font dei PDF (Regular + Bold), registrato a mano per matplotlib.
- `simulatore_unity.py` → **simulatore di Unity** per testare il bridge senza visore (v. §12).

---

## 5. Le modalità di gioco (sono l'oggetto della tesi)

| Modalità | Trigger (azione UDP) | Comportamento |
|---|---|---|
| **Osservatore** | `play_song` | Il bridge riproduce il brano sul piano MIDI e invia le note a Unity come colonne (dallo stesso file MIDI). |
| **Seguimi** | `play_song_follow` | Il bridge estrae dal MIDI le **note di riferimento**, le raggruppa in accordi e le propone una alla volta con `expect_notes` (nota: il confronto considera il tempo **relativo**, v. §8). L'utente le esegue; le note suonate vengono registrate in `history_follow_sessions`. A fine brano: `clear_scene`. |
| **Fai Tu** | `start_fai_tu` / `stop_fai_tu` | Registrazione **libera**: l'utente suona e ogni nota (press/release) viene memorizzata con tempo di parete (wall-clock) e **mano** (assegnata da Unity con `note_hand`, altrimenti assunta `nota<60` = sinistra). |
| **Tutorial** | `play_example` (sfida 1–4) / `stop_example` | Esempi dimostrativi dal **Do centrale (MIDI 60)**: 1 = tocco piano su Do (nota lunga, vel 0.18); 2 = scala Do–Re–Mi–Fa in **crescendo** (vel 0.25→0.55); 3 = **staccato** (due note brevi ben separate); 4 = **legato Do→Mi** (Mi attacca mentre il Do è ancora tenuto, due attacchi distinti). Il bridge suona e spedisce le note a Unity, poi `clear_scene` + `example_done`. |

Dopo **Fai Tu** l'utente può generare il report (`generate_report`); il flusso è in §7.

---

## 6. Protocollo UDP (contratto esatto tra Unity e bridge)

### 6.1 Unity → bridge (porta **5006**)
| Azione | Payload | Note |
|---|---|---|
| `ping` | — | heartbeat; il bridge risponde `bridge_ready` |
| `list_songs` | — | → risponde `song_list` con tutti i brani del DB |
| `search_song` | `query` | ricerca completa (locale → online) → `search_result` |
| `search_suggest` | `query` | ricerca parziale "live" solo locale → `song_list` con `suggest=true` |
| `play_song` / `play_song_follow` | `id` oppure `filename` | Unity invia sempre prima `stop_song` e ripulisce il visualizzatore |
| `next_step` | — | Seguimi: accordo successivo |
| `stop_song` | — | ferma tutto, il bridge risponde `clear_scene` |
| `play_example` | `sfida` (1–4) / `stop_example` | — |
| `start_fai_tu` | `filename` | esempio payload: `"Improvvisazione_Libera"` |
| `stop_fai_tu` | — | — |
| `note_hand` | `note`, `hand` (`left`/`right`) | assegnazione mano per la registrazione Fai Tu |
| `generate_report` | — | il bridge esegue (e blocca duplicati entro 5 s) |

### 6.2 Bridge → Unity (porta **5005**)
| Azione | Payload | Uso in Unity |
|---|---|---|
| `bridge_ready` | — | handshake: appena il listener è attivo (e a ogni `ping`) |
| `report_ready` | `file`, `folder`, `pages` | apre il viewer del report (v. §7) |
| `expect_notes` | `notes` (array MIDI) | evidenzia in Seguimi le note da suonare |
| `clear_scene` | — | azzera il visualizzatore e le note attese |
| `song_list` | `count`, `songs[]`, `suggest` (bool) | lista brani o suggerimenti |
| `search_result` | `status` (`success`/`error`), `filename`, `title`, `artist`, `message` | esito ricerca |
| `play_result` | `status`, `message` | esito riproduzione (es. "Brano non trovato") |
| `example_done` | — | fine esempio tutorial |
| `press` / `release` | `note`, `velocity` | **stream di note** al visualizzatore (colonne) |

Formato: **JSON con doppi apici** (Unity usa `JsonUtility`; il fallback con apici singoli serve solo
per vecchi messaggi del bridge). Nota: i messaggi `press`/`release` non hanno azioni dedicate nel
listener UDP del bridge: le note vere passano solo dal loop MIDI (`midi_input_loop`). Il simulatore
replica questo comportamento (v. §12).

---

## 7. Il report (PDF + PNG) — generazione e visualizzazione

### 7.1 Generazione (`generate_unified_report()` nel bridge)
1. Sessione: cartella `Sessioni\Sessione_<timestamp>\`.
2. Sintetizza `Registrazione_Audio.wav` dalle note Fai Tu (v. §8).
3. Compone il PDF con `matplotlib` (Agg backend, font **Raleway** con fallback DejaVu Sans):
   - **Pagina 1 — FAI TU**: cromagramma/piano-roll con colori per mano e velocità, legenda
     "MANO SINISTRA/DESTRA", feedback testuale (articolazione %, suggerimenti su dinamica,
     bilanciamento, fraseggio). Se non ci sono note: "Nessuna nota registrata nella modalità Fai Tu."
   - Per ogni sessione Seguimi con note utente valide (2 pagine):
     - **Chromagram**: in alto il **target** (brano originale), in basso la **tua esecuzione**;
     - **Radar + valutazione**: impostazione "Impronta Digitale" a 5 assi (v. §8) con sagoma
       Target vs Utente, blocco "dinamica / articolazione / tempo e ritmica / allineamento
       nota-per-nota" e guida alla lettura.
4. Ogni pagina viene anche salvata come `pagina_N.png` (**dpi=120**).
5. Invia `report_ready` con `folder` e `pages`.

### 7.2 Visualizzazione in Unity (`GameManager.cs`)
- `GeneraReportPDF()`: nasconde il viewer, invia `generate_report`, mostra il banner
  "Generazione report in corso..." con **timeout 120 s** (→ "Report non disponibile. Riprova." → menu).
- `ReportPronto(folder, pages)`: cancella il timeout, carica `pagina_1..pages.png` dalla cartella
  (`CaricaPagineReport`), apre il viewer sulla prima pagina. Se 0 pagine → "Report non trovato.
  Cerca Report_Completo_Nora.pdf nella cartella Sessione...".
- **Viewer** (costruito a runtime): immagine pagina 600×777 a `y=85`; pulsanti `Indietro`/`Avanti`
  280×90 a `y=-355`; `Chiudi` 600×80 a `y=-440`. **Navigazione simmetrica**: `Indietro` nascosto a
  pagina 1, `Avanti` nascosto all'ultima (`AggiornaPaginaReport()`), deciso così per il PDF finale.
- `Chiudi` → banner "Lo trovi sulla cartella Sessioni..." → ritorno automatico al menu dopo 3.5 s.

### 7.3 Messaggi del report online su pannello banner
`MostraBannerReport(testo)` riusa il banner Steel Blue del Fai Tu (pannello `CreaBannerModalita`,
testo spostato a `y=20`): i messaggi di attesa/errore/conferma del report compaiono lì, non più
su `ConfermaReport`.

---

## 8. Analisi dell'esecuzione (algoritmi usati nella tesi)

Definiti in `midi_bridge (1).py` (funzioni ora commentate in inglese):

- **Cromagramma**: rect per nota; **colore per mano**: sinistra cyan `(0, 1-v, 1)`, destra ambra
  `(1, 0.92·(1-v), 0)` con `v`=velocity 0..1 (`get_note_color_rgb`). Unità ritmica in secondi.
- **Indice articolazione** (`classifica_articolazione`): per ogni nota calcola `ratio = durata/gap`
  (gap = distanza dall'onset successivo); `ratio >= 0.85` → **legato**, `ratio <= 0.55` → **staccato**,
  altrimenti **medio**. Restituisce conteggi ed etichette.
- **Allineamento nota-nota** (`allinea_note`): appaia note utente e riferimento **per pitch, in
  ordine cronologico**, su tempo **relativo** (ciascuna serie normalizzata al proprio inizio, perché
  le note utente sono su wall-clock e quelle di riferimento su tempo MIDI). Tolleranza `tol=0.35 s`;
  `ref_window` limita il confronto al tratto eseguito (così "mancate" misura la guida nel periodo
  effettivamente suonato). Output: `matched`, `missed`, `extra`, `vel_ratios`, `dur_ratios`,
  `onset_errors`.
- **Impronta Digitale / radar a 5 assi** (`compute_radar_metrics`): Marcato (peso medio,
  `min(1, mean_vel·1.2)`), Varietà dinamica (`min(1, std_vel·4)`), Articolazione
  (`min(1, mean_dur·2)`), Rubato (`min(1, std_ioi·3)`), Bilanciamento mani
  (`min(1, (mean_dx/(mean_sx+0.001))·0.5)` se la sx suona, altrimenti 0.5).
- **WAV sintetizzato** (`generate_wav_from_midi_notes`): per ogni nota seno fondamentale +
  armonica a 0.3, inviluppo attack lineare + decay esponenziale; normalizzazione al 85%; 44100 Hz, mono.
- **Commenti automatici**: messaggi dinamica/articolazione/tempo derivati da soglie sugli indicatori
  sopra (es. tocco "più leggero" se la velocity media scende sotto -0.1).

---

## 9. Ricerca e download di brani online

`handle_song_request(query)` (pipeline): **1)** cache SQLite locale → **2)** freemidi.org
(scraping `download3-{id}-{slug}`, download via `getter-{id}` con header Referer) → **3)** fallback
bitmidi.com → **4)** salvataggio nel DB → **5)** risposta `search_result`.

Validazione di ogni download (`download_validated_candidates`, in **parallelo** con
`ThreadPoolExecutor`):
- `has_piano_track()`: presenza di `program_change` nei programmi 0–7 (piano family); se nessun
  `program_change`, si assume pianoforte.
- `is_midi_safe_for_visualizer()`: scarta i "MIDI a martello" (sequencer/rip): >60 note simultanee,
  note fuori dai 21..108 (88 tasti), >150 eventi/s, o copertura estesa + alto flusso.

DB locale (`init_db`): tabella `midi_files(id, query, filename UNIQUE, title, artist)`; migrazione
automatica dalla versione precedente (UNIQUE su query) se rilevata. `scan_local_folder()` registra i
`.mid` di `Canzoni\` all'avvio. `parse_song_info()` ricava "Titolo - Artista" dal nome file.

---

## 10. Il bridge Python — struttura (`midi_bridge (1).py`, 1566 righe)

Tutti i commenti sono **in inglese**, con docstring per funzione. Sezioni principali:
- Config UDP (`5005`→Unity, `5006`←Unity), cartelle, registrazione font, `MAX_RESULTS=4`.
- `init_db` / ricerca MIDI: `has_piano_track`, `is_midi_safe_for_visualizer`, i 4 motori ricerca/
  download freemidi/bitmidi, `download_validated_candidates`, `handle_song_request`.
- Porte MIDI: `select_physical_midi_port` (salta loopmidi/virtual/through), apertura `inport`/`outport`.
- Stato globale + storico: `send_to_unity` (sock+lock), colori, `parse_song_info`, `get_song_list`,
  `get_filename_by_id`, `scan_local_folder`, `suggest_songs`.
- Sintetizzatore WAV; caricamento brano (`load_follow_sequence`, accordi = note a <0.05 s di scarto);
  `send_next_follow_step`; `midi_input_loop` (stream note + registrazioni); `play_observing_thread`;
  `play_tutorial_example`; analisi (`compute_radar_metrics`, `classifica_articolazione`,
  `allinea_note`); `generate_unified_report`; `udp_command_listener` (dispatch di tutte le azioni §6.1).
- `__main__`: scan cartella + thread MIDI + thread UDP + sleep infinito.

Thread del bridge: `midi_input_loop` solo se esiste `inport`; i comandi `play_*` girano in thread
daemon dedicati.

---

## 11. Gli script Unity principali (`Assets/Scripts/`)

| Script | Ruolo |
|---|---|
| `GameManager.cs` | stati (`AppState` Menu/FaiTu/…), palette (v. §14), font, tutorial, banner, **viewer report** (Carica/ScaricaPagine, Avanti/Indietro/Chiudi), `ValutaNota` per Seguimi. **Il più toccato.** |
| `UdpReceiver.cs` | processo Python (avvio/terminazione `taskkill /T /F`), cuore **heartbeat** (`ping` ogni 1 s fino a `bridge_ready`, poi ogni 5 s; 3 fallimenti → non pronto), smistamento JSON (coda thread-safe), invio comandi a 5006. |
| `SongListManager.cs` | lista brani, ricerca, suggerimenti live, segnaposto `+`, righe canzone dinamiche (`CreaBottoneBrano`), scrollbar, tastiera. |
| `PianoVisualizer.cs` | colonne del piano — **non toccarlo** |
| `BranoDinamicoUI.cs`, `BranoButtonUI.cs` | testo/clic righe canzone (i 8 statici in scena sono codice morto a runtime) |
| `GlowBottone.cs`, `ScrollbarVisuale.cs`, `KeyFeedback.cs`, `IndietroFeedback.cs`, `ManualAncohor.cs`, `RaggioPuntatore.cs` | alone pulsanti, scrollbar, tasti, hover manuali, ancoraggio, laser |

---

## 12. Il simulatore di Unity (`simulatore_unity.py`, ~490 righe)

Usato **senza visore** per provare il bridge end-to-end. Simula fedelmente Unity attuale:
- **Handshake/heartbeat** identici a `UdpReceiver` (ping → `bridge_ready`, 3 miss → non pronto).
- Stessi comandi (§6.1) e stesse risposte (§6.2); menu **italiano** a numeri.
- **Flusso report**: banner "Generazione report in corso..." + timeout 120 s; su `report_ready`
  carica `pagina_N.png`, apre il viewer con navigazione `a`/`i`/`c` **simmetrica** e `o` per aprire il
  PNG nel visualizzatore di sistema; Chiudi → messaggio → menu dopo 3.5 s.
- **Note simulate** (`3`/`16`): press+release sintetiche e note automatiche; attive **solo localmente**
  (il bridge registra solo note reali da tastiera). In Seguimi sceglie spesso una nota attesa per
  mostrare il matching.
- Avvio a richiesta di `midi_bridge (1).py` in una console separata; all'uscita `stop_song` +
  `taskkill /T /F` (niente processi orfani). File: **LF, ASCII-only, no BOM** (come il bridge).

---

## 13. Traguardi e stato attuale

### 13.1 Fatto, committato e pushato (branch `main`)
- `01a552c` **"prodotto finale 2"** (104 file) — comprende: le 4 modifiche UI finali (testo banner a
  `y=20`, viewer con bottoni sotto il PDF, navigazione **simmetrica** Indietro/Avanti, messaggi del
  report sul banner), i commenti inglesi del bridge e il nuovo `simulatore_unity.py`.
- Cronologia utile: `d83fc8b` tutorial no bug · `8852d53` colonne tasti neri · `c17d30a` pannello
  ancoraggio · `7bd2168` glow pulsanti · `64b9821` tutorial + restyling · `a420124` cerca e tastiera ·
  `0406c3a` Seguimi senza bug … precedenti.

### 13.2 Fatto, NON committato (stato attuale)
- **`midi_bridge (1).py`: commenti tutti tradotti in inglese + riepilogo per funzione** (nessuna riga
  di codice cambiata: diff = soli commenti/docstring). Verificato: `py_compile` OK, LF/no BOM.
- **`simulatore_unity.py`: riscritto** (v. §12). Verificato con test di integrazione reale:
  bridge avviato → `bridge_ready` → `generate_report` → `report_ready` → viewer pagina 1/1
  (Indietro e Avanti nascosti, simmetrico) → uscita pulita (nessun processo orfano).

### 13.3 Nota sul diff della scena
Nell'ultimo commit è incluso un diff preesistente non nostro (`m_fontStyle` 1→0
in `SampleScene.unity`): non era stato creato dalle nostre modifiche ma è stato committato con il
push. **Non toccare comunque la scena** (regola §3).

---

## 14. Verifica di compilazione senza aprire Unity

`dotnet` non è disponibile: si usa il compilatore di Unity (Roslyn) col response file generato da
Unity. Eseguire **dalla root `Progetto_Tesi\`**:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Data\MonoBleedingEdge\lib\mono\msbuild\Current\bin\Roslyn\csc.exe" "@Library\Bee\artifacts\1300b0aE.dag\Assembly-CSharp.rsp"
```

`EXIT=0` atteso; restano **4 warning innocui `CS0618`** (`enableWordWrapping is obsolete` in
`GameManager.cs` e `SongListManager.cs`) e qualche `CS8032` dei source generator di Unity. Il nome
del response file (`1300b0aE.dag`) può cambiare: cercare l'ultimo `Assembly-CSharp.rsp` in
`Library\Bee\artifacts` se non dovesse esistere.

Bridge/simulatore: `python -m py_compile "midi_bridge (1).py" ` + `python -m py_compile simulatore_unity.py`
(pulire poi la `__pycache__` creata).

---

## 15. Mappa dei nomi (italiano tema ⇄ codice)

| Tesi (italiano) | Codice/nome tecnico |
|---|---|
| Modalità Osservatore (ascolto della riproduzione) | `Osservatore` (azioni `play_song`, stream note) |
| Modalità Seguimi (follow-me, note guidate) | `Seguimi` (`play_song_follow`, `expect_notes`) |
| Modalità Libera / Fai Tu | `FaiTu` (`start_fai_tu`, `note_hand`) |
| Tutorial / esempi dimostrativi | `play_example` sfida 1–4 |
| Visualizzatore / colonne | `PianoVisualizer`, messaggi `press`/`release` |
| Report | `generate_unified_report` → `report_ready` → viewer |
| Impronta Digitale (radar 5 assi) | `compute_radar_metrics` |
| Allineamento nota-per-nota | `allinea_note` |
| Articolazione | `classifica_articolazione` |
| Cromagramma / piano-roll | figure `fig1`, `fig_crom`, `fig_analisi` |
| Tastiera/pianoforte reale | porta MIDI (mido `inport`/`outport`) |
| Blocco con "troppi eventi" | `is_midi_safe_for_visualizer` |
| Palette dell'app | `GameManager.cs:76-98` (Almond Cream, Honeydew, Aquamarine, Iron Grey, Successo, Errore, Glow) |