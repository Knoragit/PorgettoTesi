> **NOTA (aggiornata):** questo file è stato spostato qui da `handover.md` quando `handover.md` è
> stato riscritto per il lavoro di restyling della UI. Contiene un brief per una sessione diversa
> e **alcune parti sono ormai datate**: la sezione 3 "Palette colori" e la sezione 11 descrivono la
> palette vecchia (Alabaster / Pacific Cyan / Blue Slate / Navajo) e il font unico Inter.
> Oggi la palette è **Almond Cream / Honeydew / Aquamarine / Iron Grey** e i titoli usano
> **Archivo Black**. Per lo stato attuale del codice vedi `handover.md`.

# Handover — Questionario test utente per Nora (VR Piano Learning)

Questo documento è scritto per un altro AI il cui compito sarà **progettare e redigere un questionario per testare l'applicazione seguita qui** con utenti reali. Contiene il quadro completo dell'app (modalità, flussi d'uso, interazione, punti di attenzione UX) e le indicazioni su cosa il questionario deve rilevare.

## 1. Panoramica del progetto

- **Nora** = applicazione VR per imparare a suonare il pianoforte (probabile progetto di tesi, contenuti in italiano).
- **Hardware**: Meta Quest, hand tracking attivo; la UI si clicca in VR con **pinch** e un **laser bianco** (visibile solo puntando un bottone).
- **Stack**: Unity (URP 17.3.0) + Meta XR SDK 201.0.0, **scena unica** (`Progetto_Tesi/Assets/Scenes/SampleScene.unity`). Tutta la UI dinamica è creata/modificata a **runtime** (`Assets/Scripts/*.cs`), mai toccando la scena.
- **Backend Python**: `midi_bridge (1).py` — ponte UDP che gestisce la libreria MIDI, la ricerca, la riproduzione e i report di sessione. Unity invia comandi (`list`, ricerca, `play/id`, `stop`, start/stop FaiTu, `genera report`, `seguimiAvanti`).
- **Font**: Inter (bold + ombra) usato globalmente su tutte le scritte.

### Cartelle e dati
- `Nora/Canzoni/` — ~70 file `.mid` (libreria, indicizzati nel DB).
- `Nora/Sessioni/` — report/sessioni di gioco (`Sessione_<timestamp>/`).
- `Nora/nora_database.db` — DB SQLite (ricerca/playback; il DB salva solo il nome file, i path sono ricostruiti a runtime).
- Git: repo `Knoragit/PorgettoTesi`, branch `main`. Ultimo commit: `22db433 "+ per osservatore/seguimi"`. Modifiche non ancora committate: `.gitignore`, `midi_bridge (1).py`, spostamento file in `Canzoni/`/`Sessioni/`. `Inter/` e `handover.md` sono **esclusi volutamente** dal repo.

## 2. Le 6 modalità dell'app (flussi utente)

L'utente naviga tra stati tramite `GameManager.CambiaStato`. Stati: `Onboarding, Menu, Tutorial, Osservatore, Seguimi, FaiTu`.

1. **Onboarding (calibrazione/ancoraggio)**: si ancorano/posizionano gli oggetti AR (piano visualizzato) sulla superficie reale tramite `ManualAnchor`. Richiamabile anche dal Menu con "Riancora".
2. **Menu principale**: 5 bottoni — Tutorial, Osservatore, Seguimi, Fai Tu, Riancora (tutti Alabaster base / Pacific Cyan hover / Iron press, testo nero). La modalità Osservatore/Seguimi dentro il gruppo mostra il mini-bottone "Indietro".
3. **Tutorial**: 4 sfide sui gesti espressivi del tocco, rilevate dalla velocità MIDI delle note:
   - SFIDA 1: tocco molto delicato ("piano").
   - SFIDA 2: scala crescente di 4 note sempre più forti.
   - SFIDA 3: "staccato" (note brevi, NON sovrapposte).
   - SFIDA 4: "legato" (nota successiva PRIMA di rilasciare la precedente) vs accordo.
   - Feedback testuale colorato: `COMPLETATO!` / `Errore: ...` con ripristino dell'istruzione; al termine reindirizza al Menu.
4. **Osservatore**: si sceglie una canzone (ricerca + lista) → la canzone suona e le **colonne del `PianoVisualizer`** mostrano il flusso/le note da osservare (modalità "ascolto-guida").
5. **Seguimi**: si sceglie una canzone → l'app mostra le **note attese** (colonne); l'utente suona e quando le note corrispondono l'app avanza alla parte successiva (`seguimiAvanti`).
6. **Fai Tu**: improvvisazione libera; pulsante "Genera Report" → genera PDF in `Sessioni/` e ritorna al Menu.

## 3. UI di Osservatore/Seguimi (focus del test)

Gestita da `SongListManager`, con:
- **Lista canzoni** scorrevole (scrollbar) + **barra di ricerca** con suggerimenti in tempo reale (DB) + **tastiera virtuale** per scrivere il nome + bottone "Cerca".
- **Bottone "Indietro"** ("Torna al Menù") in stile unificato con le altre modalità.
- **Feature recente — menù collassabile "+"**: quando parte una canzone, la barra di ricerca + lista crollano con mini-animazione (~0.25 s) dentro un **cerchio "+"** posizionato sotto "Indietro" (diametro = altezza bottone). Premendo "+" il menù si riapre **e** la riproduzione/colonne si fermano. All'ingresso in Osservatore/Seguimi il menù è sempre aperto.
- All'ingresso di una nuova modalità la riproduzione precedente si ferma (stop in uscita da Oss/Seg e FaiTu).

### Palette colori (coerente in tutta l'app)
- Alabaster `#dcdcdd` (base bottoni), Pacific Cyan `#1985a1` (hover), Iron Grey `#46494c` (premuto), Blue Slate `#4c5c68` (pannelli sfondo). Titoli: Navajo `#ffe0b5`. Scritte colorate: Muted Olive `#a4af69` (completato), Light Coral `#e56b70` (errore), Pale Amber `#ede580`, Espresso `#4c2719`.

## 4. Modello di interazione (contesto per le domande)

- Si punta con la mano, **hover** = colore che cambia in Pacific Cyan, **press** = Iron Grey; si conferma con **pinch**.
- Il **laser bianco** appare solo sopra i bottoni attivi (nascosto in Onboarding e mentre si suona).
- Canvas World Space 1000×800 davanti all'utente; gruppi Oss/Seg scalati ×4 compensati a runtime (le misure a schermo sono quelle "umane").
- Bottoni già ingranditi per l'usabilità: menu ×1.15, FaiTu 460×230, Oss/Seg solo in larghezza.

## 5. Punti di attenzione UX candidati per il questionario

1. **Facilità di click**: precisione del pinch/laser, dimensione e area cliccabile dei bottoni, miss-hit.
2. **Feedback visivo**: chiarezza del change-colore su hover/press; il laser è di aiuto o disturba?
3. **Comprensibilità dei flussi**: differenza percepita tra Osservatore, Seguimi, Fai Tu (cosa devo fare in ognuna?).
4. **Ricerca canzoni**: tastiera virtuale, scritta del titolo, suggerimenti, scrollbar — quanto è naturale in VR?
5. **Menù collassabile "+"**: scopribilità, comprensione della chiusura automatica, ri-apertura con stop.
6. **Leggibilità testi in VR**: font Inter, dimensioni, contrasto, distanza.
7. **Tutorial**: chiarezza delle istruzioni delle 4 sfide; messaggi di errore; gratificazione al completamento.
8. **PianoVisualizer**: la metafora visiva delle colonne/note è compresa?
9. **Comfort fisico**: durata, stanchezza, nausea, fatica a tenere le braccia.
10. **Attrito tecnico**: calibrazione/ancoraggio, riconoscimento mano, stabilità del piano AR.

## 6. Requisiti del questionario (per l'AI successiva)

- **Italiano**, adatto a utenti **anche non musicisti e non esperti VR**.
- Struttura consigliata:
  1. **Dati personali/esperienza**: familiarità con pianoforte/musica, familiarità con la VR, età (facoltativo).
  2. **Task-based**: lista di compiti da far eseguire PRIMA di rispondere (es. "Apri Osservatore e cerca una canzone di Mozart", "Avvia la canzone e osserva le colonne", "Chiudi/riapri il menù col +", "Prova il Tutorial fino alla Sfida 2", "Genera il report in Fai Tu", "Riancora il piano").
  3. **Scala standardizzata**: SUS (System Usability Scale) per punteggio usabilità complessiva.
  4. **Domande custom Likert 5–7 punti**, una sezione per modalità (Tutorial/Osservatore/Seguimi/FaiTu) e per le feature (ricerca, tastiera, scrollbar, "+", laser, colonne).
  5. **Domande aperte qualitative**: cosa ha funzionato, cosa ha confuso, cosa manca.
- Se serve, proporre anche **protocollo di test** (numero utenti, durata, ordine delle task) e una versione Google Form/PDF.
- Facoltativo: correlare le risposte con dati reali di sessione (`Sessioni/`, errori tutorial) se raccolti.

## 7. Riferimenti file

- `Progetto_Tesi/Assets/Scripts/GameManager.cs` — stati, modalità, sfide tutorial, palette, font, stili bottoni.
- `Progetto_Tesi/Assets/Scripts/SongListManager.cs` — lista/ricerca/scrollbar/tastiera + collapse "+".
- `Progetto_Tesi/Assets/Scripts/BranoDinamicoUI.cs` / `BranoButtonUI.cs` — click sui brani (avvio/stop).
- `Progetto_Tesi/Assets/Scripts/UdpReceiver.cs` — comandi UDP verso il backend Python.
- `Progetto_Tesi/Assets/Scripts/PianoVisualizer.cs` — colonne/visualizzazione (NON toccare per il questionario).
- `Progetto_Tesi/Assets/Scripts/RaggioPuntatore.cs` — laser.
- `Progetto_Tesi/Assets/Scripts/IndietroFeedback.cs` — hover/press dei bottoni "Indietro" e "+".
- `Progetto_Tesi/Assets/Scripts/ManualAncohor.cs` — calibrazione/ancoraggio.
- `Progetto_Tesi/Assets/Scripts/ScrollbarVisuale.cs`, `ScrollDragReset.cs`, `KeyFeedback.cs` — scrollbar e tastiera virtuale.
- `midi_bridge (1).py` — backend (path: `Canzoni/`, `Sessioni/`, `nora_database.db`).
- `Progetto_Tesi/Assets/Scenes/SampleScene.unity` — **SOLO LETTURA**.
- Commit storici utili: `0974d57 raggio funzionante`, `cdb7a88 bottoni sistemati`, `c84a721 palette e UI`, `73ad01b scritte aggiustate`, `22db433 + per osservatore/seguimi`.