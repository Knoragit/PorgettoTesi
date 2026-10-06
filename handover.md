# Handover — Restyling UI di Nora (VR Piano Learning)

Documento di passaggio per la **nuova sessione** che continua il lavoro di restyling della UI.
Scritto per un AI che deve sapere: dove siamo, cosa è fatto, cosa manca, cosa non si deve toccare.

> Breve separato per un altro scopo: `handover_questionario.md` (brief per progettare un questionario
> UX). Ignoralo per questo lavoro.

---

## 1. Regole non negoziabili

| Regola | Perché |
|---|---|
| **Non modificare mai `Progetto_Tesi/Assets/Scenes/SampleScene.unity`** | L'Editor Unity è aperto con la scena e con file di recovery in `Assets/_Recovery/`. Ogni edit manuale della scena viene sovrascritto. Tutto si applica **a runtime** da `Assets/Scripts/*.cs`. |
| **Non committare senza richiesta esplicita** | L'utente non ha chiesto commit in questa sessione. |
| **Codifica**: salvateli in **CRLF**, mai LF | Alcuni file erano finiti a LF e si staccavano dal resto del repo. |
| **La `ù` va scritta come `\u00F9`** dentro le stringhe C# | Scrivere `ù` literalmente produce U+FFFD (rettangolino) e rompe la ricerca per oggetto. I nomi degli oggetti in scena sono `TornaAlMen\u00F9`. |
| Verificare sempre in **Play Mode** | Molte scelte di design (contrasto, collisioni) non sono visibili in Editor. |

Repo: `C:\Users\vrlab\Desktop\Nora` — branch `main`, remote `https://github.com/Knoragit/PorgettoTesi.git`.
Unity: **6000.3.11f1**. Progetto: `Progetto_Tesi/`.

Ultimi commit: `7bd2168` glow pulsante · `64b9821` tutorial/restyling · `a420124` cerca e tastiera.

---

## 2. Palette e font attuali (fare riferimento a questi, non a quelli vecchi)

Definizione unica in `GameManager.cs:76-98`.

| Costante | Hex | Ruolo |
|---|---|---|
| `TanColor` | `#e5d4c0` | **Almond Cream** — pannelli chiari, testi su fondo scuro |
| `CoolSteel` | `#c5decd` | **Honeydew** — pulsanti chiari (prevalente) |
| `SteelBlue` | `#a1e8cc` | **Aquamarine** — hover/premuto dei pulsanti chiari |
| `IronGrey` | `#495159` | testo scuro su pannello chiaro; anche `SfondoScuro(alfa)` |
| `Successo` | `#65b891` | verde esito positivo (nuovo, aggiunto questa sessione) |
| `Errore` | `#cf5c36` | rosso mattone, esito negativo |
| `GlowColore` | `#2f9e7d` | alone dei pulsanti |

Font:
- **Archivo Black** → titoli, `FontTitolo`. Asset: `Assets/Resources/Fonts/Archivo Black SDF.asset`.
- **Inter** → corpo testo, `FontApp`.
- La selezione è in `TrovaFontConPriorita(...)` (`GameManager.cs:131`). **Attenzione:** a riga 116
  `FontApp` usa `"inter"` **prima** di `"archivo"`. Se inverti l'ordine, *tutto* il corpo testo
  diventa Archivo Black.

---

## 3. Stato del lavoro

### 3.1 Fatto e committato
- `7bd2168` alone (`GlowBottone`) sui pulsanti — forma rettangolare e circolare.
- `64b9821` connessione tastiera virtuale nel tutorial + restyling.

### 3.2 Fatto ma NON committato (8 script modificati)
`git diff --stat`: ~182 insert, 55 delete.

**`GameManager.cs`**
- `:85` `Successo` impostata su `#65b891`. Si propaga da sola a "COMPLETATO!",
  "ECCELLENTE, TUTORIAL COMPLETATO!" e al prefisso "■ Scaricato:".
- `:116` `FontApp` → priorità Inter (vedi nota sopra).
- `:644` `GeneraReport` portato a `y = 150` a runtime (in `IngrandisciBottoniUI()`).
- `:452-491` nuovi `ApplicaFontSizeMenu()`, `ImpostaFontSizeFigli()` e costanti:
  `FS_MENU=42`, `FS_RIPRESA=60`, `FS_GENERA_REPORT=55`, `FS_INDIETRO=FS_RIPRESA/4=15`.
  Chiamato in coda ad `ApplicaPalette()`.
  - `CopiaStileBottoneTorna()` gira **prima** e copia il font di scena (72): per questo il bottone
    del Tutorial viene reimpostato esplicitamente a riga 479.

**`SongListManager.cs`**
- `:977` nuova costante `ScartoPlus = 40f` (prima inline `8f`), usata a `:995`.
  Il `+` passa da `x ≈ -55.75` a `x ≈ -23.75`.
- `:264` font "Indietro" `18f → 15f`. 15×4 = 60 = altezza apparente del "Torna al Menu" (gruppo ×4).
- `:1691` font canzoni runtime `16 → 14`.

**`BranoButtonUI.cs:22`** e **`BranoDinamicoUI.cs:17`** — autore `<size=80%>` → `<size=70%>`.

**Modifiche di palette preesistenti** (non di questa sessione, già nel diff):
`GlowBottone.cs`, `KeyFeedback.cs`, `ManualAncohor.cs`, `ScrollbarVisuale.cs`.

### 3.3 NON ancora applicato — piano approvato dall'utente
Vedi sezione 4. **Non è stato scritto nessun codice per questo piano.**

---

## 4. Piano approvato e non ancora applicato

L'utente ha chiesto: artisti e il messaggio "Report Generato / Lo trovi nella cartella Sessioni"
in **Almond Cream**, e ha approvato di scurire anche le righe canzone.

Il piano è pronto, con il codice esatto. **Le 4 modifiche vanno fatte in quest'ordine.**

### 4.1 `GameManager.cs:337` — messaggio Report in Almond Cream
```csharp
reportText.text = $"<color=#{Hex(TanColor)}>Report Generato!\n\nLo trovi nella cartella Sessioni</color>";
```
I tag `<color>` di TMP hanno la precedenza su `t.color`, quindi `ColoraTestiScuriPannelli()` non
può sovrascriverli. Stessa tecnica già usata e funzionante per gli artisti.

### 4.2 `GameManager.cs` in `ApplicaPalette()`, subito dopo la riga 441 — `istruzioneFaiTu` in Almond Cream
```csharp
// GruppoFaiTu non ha pannello "Sfondo": ColoraTestiScuriPannelli scurisce tutto
// su nero/passthrough VR. Il banner torna al colore chiaro.
TextMeshProUGUI istruzione = TrovaFiglio(faiTuBannerMessaggio, "istruzioneFaiTu")
    ?.GetComponentInChildren<TextMeshProUGUI>(true);
if (istruzione != null) istruzione.color = TanColor;
```
`istruzioneFaiTu` è nipote di `GruppoFaiTu` (passa da `FaiTuBanner`), e `TrovaFiglio()` controlla
**solo figli diretti** (`GameManager.cs:374`): per questo si usa il riferimento serializzato
`faiTuBannerMessaggio`, che punta proprio a `FaiTuBanner`.

### 4.3 `SongListManager.cs` `CreaBottoneBrano()` — righe canzone scure
- `:1670` `img.color`: `CoolSteel` → `IronGrey`
- `:1676-1677` `highlightedColor` / `pressedColor`: `SteelBlue` → nuovo `DeepTeal`
- `:1692` `label.color`: `Color.black` → **`Color.white`** (il titolo resta bianco)
- `:1700-1701` `coloreEtichettaBase` / `Hover`: nero → bianco · `:1702` `Premuto` resta bianco
- `:1703` `ImpostaColorePremuto`: `SteelBlue` → `DeepTeal`
- nuova costante in `GameManager.cs` (subito dopo `IronGrey`, riga 81):
  ```csharp
  public static readonly Color DeepTeal = new Color(0.1922f, 0.3020f, 0.4745f, 1f); // #314d79
  ```
  (è lo stesso colore dello sfondo camera, quindi resta in palette)

### 4.4 `BranoDinamicoUI.cs:17` e `BranoButtonUI.cs:22` — artisti in Almond Cream
```csharp
label.text = prefix + titolo + $"\n<size=70%><color=#{GameManager.Hex(GameManager.TanColor)}>" + autore + "</color></size>";
```
`titolo` **resta bianco**, `autore` diventa cream: gerarchia visiva mantenuta, entrambi leggibili.

---

## 5. Perché il piano è stato costruito così (non improvvisare i colori)

Contrasti calcolati (WCAG, testo normale → serve ≥ 4.5:1):

| Combinazione | Rapporto | Esito |
|---|---|---|
| Iron Grey `#495159` su Honeydew `#c5decd` | 5.6:1 | ✅ stato attuale degli artisti |
| **Almond Cream `#e5d4c0` su Honeydew `#c5decd`** | **1.01:1** | ❌ invisibile — motivo per cui gli artisti da soli non bastavano |
| Almond Cream su nero (camera) | 11.9:1 | ✅ |
| Iron Grey su nero | 2.4:1 | ❌ bug: `ConfermaReport` e `istruzioneFaiTu` erano quasi illeggibili |
| bianco su Iron Grey | 7.97:1 | ✅ titolo canzone |
| Almond Cream su Iron Grey | 5.51:1 | ✅ artista |
| Almond Cream su Deep Teal `#314d79` | 5.9:1 | ✅ hover |
| Honeydew `#c5decd` su Deep Teal | 5.67:1 | ✅ hover (stato attuale) |

**Attenzione — bug architetturale da non ripetere:** `GruppoFaiTu` **non ha nessun pannello `Sfondo`**
(figli: `FaiTuBanner`, `TornaAlMenù`, `GeneraReport`, `ConfermaReport`). Quindi `ColoraPannelli()`
non fa nulla e `ColoraTestiScuriPannelli()` scurisce **alla cieca** tutto ciò che è nel gruppo, anche
testi che stanno sul nero/passthrough VR. Il Tutorial invece ha il pannello `Sfondo` Tan e lì lo
scurimento è corretto. Se in futuro FaiTu riceve un pannello, rivedere la logica.

---

## 6. Rischi e cose da controllare in Play Mode

1. **Collisione `+` / "Cerca".** `+` e "Indietro" sono a `y = 125`, stessa fascia di `BarraRicerca`.
   Prima dell'intervento il `+` le toccava già (x `-84.5..-27` vs "Cerca" `-65..65`); ora che è a
   `x ≈ -23.75` la sovrapposizione è **maggiore**. Se si vede male, alzare la fascia di
   `+`/`Indietro` da `y=125` a circa `190` invece di ridurre lo scarto.
2. **Prefisso "■ Scaricato:"** in `Successo #65b891`: con le righe scure scende a **3.35:1**, sotto
   la soglia 4.5:1. L'utente non ha chiesto di correggerlo: se si alza `#65b891` o si scurisce il
   fondo, sistemarlo insieme.
3. **`Archivo Black SDF` è in `AtlasPopulationMode: 1` (Dynamic).** I glifi si generano on-demand:
   al primo titolo Archivo si può vedere un leggero scatto. Se si nota, in TMP mettere **Static** e
   ri-salvare l'asset (pre-genera i glifi).
4. **Il pannello del viewport resta chiaro** (Honeydew `@0.55`) anche con le righe scure: scelta
   voluta, la struttura del pannello resta visibile.
5. `GeneraReport` è a `y=150` **solo a runtime**: nell'Editor la scena mostra ancora `y=250`.
   Non è un bug, non "correggere" la scena.

---

## 7. Verifica di compilazione senza aprire Unity

`dotnet` **non è disponibile** su questa macchina. Si usa il mono e il csc di Unity, con il response
file che Unity stesso genera — è il metodo testato e dà `EXIT=0`.

```powershell
$ed = "C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Data"
$proj = "C:\Users\vrlab\Desktop\Nora\Progetto_Tesi"
$tmp = "C:\Users\vrlab\AppData\Local\Temp\opencode\nora-verify"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

# prende l'ultimo Assembly-CSharp.rsp scritto da Unity e ne cambia solo l'output
$src = Get-ChildItem "$proj\Library\Bee\artifacts" -Recurse -Filter "Assembly-CSharp.rsp" |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
(Get-Content $src.FullName) |
  ForEach-Object { if ($_ -match '^-out:') { "-out:`"$($tmp -replace '\\','/')/verify.dll`"" } else { $_ } } |
  Set-Content "$tmp\assembly.rsp" -Encoding UTF8

Push-Location $proj
& "$ed\MonoBleedingEdge\bin\mono.exe" "$ed\MonoBleedingEdge\lib\mono\msbuild\Current\bin\Roslyn\csc.exe" "@$tmp\assembly.rsp"
$code = $LASTEXITCODE
Pop-Location
"EXIT=$code"
```

Note:
- va eseguito **dalla root del progetto**: il response file contiene percorsi relativi.
- `EXIT=0` è il risultato atteso. Restano 4 warning `CS0618 enableWordWrapping is obsolete`
  (preesistenti, in `GameManager.cs:330` e `SongListManager.cs:1508,1586,1694`) e alcuni
  `CS8032` sui source generator di Unity: **rumore innocuo**, non sono errori.
- ricordarsi di cancellare `$tmp` e il `verify.dll` a fine verifica.

---

## 8. Git — cosa includere e cosa no

`git status` attuale:

**Modificati (8 script)** — di cui 4 toccati in questa sessione
(`GameManager`, `SongListManager`, `BranoButtonUI`, `BranoDinamicoUI`) e 4 con modifiche palette
preesistenti (`GlowBottone`, `KeyFeedback`, `ManualAncohor`, `ScrollbarVisuale`).

**Non tracciati, da escludere** salvo richiesta contraria:
- `.gitignore`, i 5 `.mid` cancellati, `Sessione_20260914_151724/` (audio + PDF cancellati)
- `Inter/`, `Archivo_Black/`, `pulsante-lungo.png`, `pulsante-tondo.png`

**Da decidere prima di un commit:**
- `Progetto_Tesi/Assets/Resources/Fonts/Archivo Black SDF.asset` (+ `.meta`) — **serve** se i titoli
  usano Archivo. Includere anche `ArchivoBlack-Regular.ttf` e `ArchivoBlack-OFL.txt`.
- `Progetto_Tesi/Assets/TextMesh Pro/Fonts/ArchivoBlack-Regular.ttf` — **copia duplicata** del TTF,
  già presente in `Resources/Fonts/`. Non serve: decidere se eliminarla o ignorarla.

---

## 9. Appendice — dove sta cosa (scene)

`SampleScene.unity` (9383 righe). Gerarchia utile:

```
Canvas (World Space 1000x800, camera 826430833 ClearFlags=SolidColor nero)
├── GruppoFaiTu            (RT 295709251)  ← nessun pannello "Sfondo"
│   ├── FaiTuBanner        (272302851)  → faiTuBannerMessaggio
│   │   └── istruzioneFaiTu (384309739) "Ora tocca a te.."   TMP 384309741
│   ├── "TornaAlMenù"      (908371066)  x=-500 y=500
│   ├── GeneraReport       (1566154527) x=-500 y=250 (runtime → y=150)
│   └── ConfermaReport     (203106221)  x=0 y=0 600x500  TMP 203106223
│                                                  → faiTuReportMessaggio
└── (observerGroup / seguimiGroup: contengono gli 8 bottoni statici BranoButtonUI)
```

Note utili:
- `faiTuReportMessaggio` → `ConfermaReport` · `faiTuBannerMessaggio` → `FaiTuBanner`. Entrambi già
  serializzati in `GameManager` (riga 25-26), non serve cercarli a mano.
- **Gli 8 `BranoButtonUI` nella scena sono codice morto a runtime**: stanno solo nei contenitori
  Observer/Seguimi e `NascondiBottoniStatici()` li disattiva (`SongListManager.cs:97-98`).
  Le righe canzone visibili nascono **solo** da `CreaBottoneBrano()`. Cambiare `BranoButtonUI.cs`
  è comunque utile per l'anteprima in Editor.
- I due contenitori sono `contenitoreObserver` e `contenitoreSeguimi` (`SongListManager.cs:11-12`).

---

## 10. Indice dei file di codice toccati

| File | Cosa contiene |
|---|---|
| `Assets/Scripts/GameManager.cs` | stati, palette, font, stili pulsanti, tutorial, FaiTu. **Toccatissimo.** |
| `Assets/Scripts/SongListManager.cs` | lista/ricerca/scrollbar/tastiera, righe canzone, `+`. **Toccatissimo.** |
| `Assets/Scripts/BranoDinamicoUI.cs` | testo e click delle righe canzone runtime |
| `Assets/Scripts/BranoButtonUI.cs` | pulsanti canzone statici (morti a runtime) |
| `Assets/Scripts/IndietroFeedback.cs` | hover/press manuali, scala, colori etichetta |
| `Assets/Scripts/GlowBottone.cs` | alone sui pulsanti |
| `Assets/Scripts/ScrollbarVisuale.cs`, `KeyFeedback.cs` | scrollbar, tasti tastiera |
| `Assets/Scripts/PianoVisualizer.cs` | colonne del piano — **non toccare** |
| `Assets/Scripts/UdpReceiver.cs` | comandi UDP verso `midi_bridge (1).py` |
| `Assets/Scripts/ManualAncohor.cs`, `RaggioPuntatore.cs` | calibrazione, laser |