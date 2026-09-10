import os
import time
import json
import socket
import threading
import wave
import copy
import sqlite3
import hashlib
import urllib.request
import urllib.parse
from concurrent.futures import ThreadPoolExecutor
import numpy as np
import mido
import requests
import re

import matplotlib
matplotlib.use('Agg')
import matplotlib.patches as patches
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages

# ==========================================
# CONFIGURAZIONE RETE UDP & FILE
# ==========================================
UDP_IP = "127.0.0.1"
PORT_TO_UNITY = 5005
PORT_FROM_UNITY = 5006

DESKTOP_PATH = os.path.join(os.path.expanduser("~"), "Desktop", "Nora")
if not os.path.exists(DESKTOP_PATH):
    os.makedirs(DESKTOP_PATH)

# Numero massimo di risultati scaricati e salvati per ogni ricerca online
MAX_RESULTS = 4

# ==========================================
# CONFIGURAZIONE DATABASE LOCALE (SQLite)
# ==========================================
DB_PATH = os.path.join(DESKTOP_PATH, "nora_database.db")

def init_db():
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute('''CREATE TABLE IF NOT EXISTS midi_files
                 (id INTEGER PRIMARY KEY AUTOINCREMENT,
                  query TEXT,
                  filename TEXT UNIQUE,
                  title TEXT,
                  artist TEXT)''')
    # Migrazione: la versione precedente aveva UNIQUE su query, che impediva
    # di salvare più risultati per la stessa ricerca. Se rilevato, ricrea la
    # tabella con UNIQUE su filename e copia i dati esistenti.
    sql = c.execute("SELECT sql FROM sqlite_master WHERE type='table' AND name='midi_files'").fetchone()
    old_schema = bool(sql) and re.search(r'query[^,\n]*UNIQUE', sql[0], re.IGNORECASE)
    if old_schema:
        print("[DB] Migrazione tabella midi_files (UNIQUE query -> UNIQUE filename)...")
        c.execute('''CREATE TABLE midi_files_new
                     (id INTEGER PRIMARY KEY AUTOINCREMENT,
                      query TEXT,
                      filename TEXT UNIQUE,
                      title TEXT,
                      artist TEXT)''')
        c.execute('''INSERT OR IGNORE INTO midi_files_new (query, filename, title, artist)
                     SELECT query, filename, title, artist FROM midi_files''')
        c.execute("DROP TABLE midi_files")
        c.execute("ALTER TABLE midi_files_new RENAME TO midi_files")
    conn.commit()
    conn.close()

init_db()

# ==========================================
# MOTORE DI RICERCA ED ANALISI MIDI
# ==========================================
def has_piano_track(filepath):
    """Verifica se il file MIDI contiene tracce/programmi associati al pianoforte."""
    try:
        mid = mido.MidiFile(filepath)
        program_changes_seen = False
        
        for msg in mid:
            if msg.type == 'program_change':
                program_changes_seen = True
                # General MIDI: Program da 0 a 7 identificano la famiglia dei Pianoforti
                if 0 <= msg.program <= 7:
                    return True
                    
        # Se non ci sono istruzioni 'program_change', lo standard MIDI assegna
        # di default il suono del pianoforte (Program 0)
        if not program_changes_seen:
            return True
            
        return False
    except Exception as e:
        print(f"[ERRORE ANALISI MIDI] {e}")
        return False

def is_midi_safe_for_visualizer(filepath):
    """Rifiuta i MIDI 'a martello' (rip da videogame/sequencer) che saturano
    il visualizer: troppe note diverse ripremute senza tregua fanno sembrare
    tutti i tasti premuti e 'bloccati'. Ritorna (ok, motivo)."""
    try:
        mid = mido.MidiFile(filepath)
        pitches = set()
        note_msgs = 0
        for msg in mid:
            if msg.type in ('note_on', 'note_off'):
                note_msgs += 1
                if msg.type == 'note_on' and msg.velocity > 0:
                    pitches.add(msg.note)
        seconds = max(0.001, mid.length)
        evt_s = note_msgs / seconds

        if evt_s > 150.0:
            return False, f"flusso note estremo ({evt_s:.0f} evt/s) - rip/sequencer non adatto"
        if len(pitches) > 70 and evt_s > 60.0:
            return False, f"copertura tastiera ({len(pitches)} note) e flusso alto ({evt_s:.0f} evt/s)"
        return True, ""
    except Exception as e:
        print(f"[ERRORE ANALISI SICUREZZA] {e}")
        return True, ""

def extract_song_metadata(html):
    """Extracts (title, artist) from a freemidi.org download page HTML."""
    title = None
    artist = None

    # Title 1 (exact): <div class=download-title-container style=display:none>Title</div>
    m = re.search(
        r'class=["\']?download-title-container["\']?[^>]*style=["\']?display:none["\']?>([^<]+)',
        html
    )
    if not m:
        # Title 2 (fallback): <div class=download-title-container>\n<h1>Title Midi</h1>
        m = re.search(r'class=["\']?download-title-container["\']?[^>]*>\s*<h1>([^<]+)</h1>', html)
    if m:
        title = m.group(1).strip()
        if title.lower().endswith(' midi'):
            title = title[:-5].strip()

    # Artist: <a itemprop=item href="/artist-123-slug"> <span itemprop=name>Name</span>
    m = re.search(
        r'href=["\']?/artist-\d+-[^"\']*["\']?[^>]*>\s*<span itemprop=["\']?name["\']?>([^<]+)',
        html
    )
    if m:
        artist = m.group(1).strip()

    return title, artist
def freemidi_search_candidates(query, max_results=MAX_RESULTS):
    """Estrae fino a max_results link candidati dalla pagina di ricerca freemidi.org."""
    try:
        headers = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
        }
        session = requests.Session()

        # 1. Search by scraping the HTML results page
        search_url = f"https://freemidi.org/search?q={urllib.parse.quote(query)}"
        resp = session.get(search_url, headers=headers, timeout=12)

        if resp.status_code != 200:
            return []

        # 2. Parse download3-{id}-{slug} links from the results (ordine pagina)
        links = list(dict.fromkeys(re.findall(r'download3-\d+-[\w-]+', resp.text)))
        return links[:max_results]

    except Exception as e:
        print(f"[ONLINE SEARCH ERROR] {e}")

    return []

def download_freemidi_candidate(slug):
    """Scarica un singolo candidato da freemidi.org. Ritorna la tupla o None."""
    try:
        headers = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
        }
        session = requests.Session()

        track_id = re.search(r'download3-(\d+)', slug).group(1)
        dl_page = f"https://freemidi.org/{slug}"

        # 3. Visit the download page to establish a session cookie and grab metadata
        page_response = session.get(dl_page, headers=headers, timeout=10)
        title, artist = extract_song_metadata(page_response.text)

        # 4. Download the .mid file via getter (requires Referer header)
        getter_url = f"https://freemidi.org/getter-{track_id}"
        file_response = session.get(
            getter_url,
            headers={**headers, 'Referer': dl_page},
            timeout=15
        )

        if file_response.status_code == 200 and file_response.content[:4] == b'MThd':
            filename = slug.replace('download3-', '') + ".mid"
            filepath = os.path.join(DESKTOP_PATH, filename)
            with open(filepath, 'wb') as f:
                f.write(file_response.content)
            return filepath, filename, title, artist

    except Exception as e:
        print(f"[DOWNLOAD MIDI ERROR] {slug}: {e}")

    return None

def bitmidi_search_candidates(query, max_results=MAX_RESULTS):
    """Best-effort: estrae i link candidati dalla pagina di ricerca di bitmidi.com."""
    try:
        headers = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
        }
        session = requests.Session()
        search_url = f"https://www.bitmidi.com/search?q={urllib.parse.quote(query)}"
        resp = session.get(search_url, headers=headers, timeout=12)
        if resp.status_code != 200:
            return []
        # Link pagina brano: slug che terminano con -mid (ordine di pagina)
        hrefs = re.findall(r'href="(/[^"]*-mid)"', resp.text)
        candidates = []
        for h in hrefs:
            if h not in candidates:
                candidates.append("https://www.bitmidi.com" + h)
        return candidates[:max_results]
    except Exception as e:
        print(f"[BITMIDI SEARCH ERROR] {e}")
    return []

def download_bitmidi_candidate(page_url):
    """Scarica il primo link .mid trovato nella pagina del brano bitmidi.com."""
    try:
        headers = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
        }
        session = requests.Session()
        resp = session.get(page_url, headers=headers, timeout=12)
        if resp.status_code != 200:
            return None

        mid_links = re.findall(r'(?:href|src)="(?:[^"]*)(/uploads/[^"]+\.mid)"', resp.text, re.IGNORECASE)
        if not mid_links:
            mid_links = re.findall(r'(?:href|src)="([^"]+\.mid[^"]*)"', resp.text, re.IGNORECASE)

        for link in mid_links:
            if not link.startswith("http"):
                link = "https://www.bitmidi.com" + link
            r = session.get(link, headers={**headers, 'Referer': page_url}, timeout=15)
            if r.status_code == 200 and r.content[:4] == b'MThd':
                filename = hashlib.md5(page_url.encode()).hexdigest()[:10] + ".mid"
                filepath = os.path.join(DESKTOP_PATH, filename)
                with open(filepath, 'wb') as f:
                    f.write(r.content)
                title = None
                artist = None
                m = re.search(r'<title>([^<]+)</title>', resp.text, re.IGNORECASE)
                if m:
                    title = re.sub(r'\s*\.mid\s*.*$', '', m.group(1), flags=re.IGNORECASE).strip()
                return filepath, filename, title, artist

    except Exception as e:
        print(f"[BITMIDI DOWNLOAD ERROR] {page_url}: {e}")

    return None

def download_validated_candidates(candidates, downloader):
    """Scarica i candidati in parallelo, filtrando quelli con parte pianoforte."""
    results = []
    if not candidates:
        return results
    with ThreadPoolExecutor(max_workers=len(candidates)) as executor:
        futures = [executor.submit(downloader, cand) for cand in candidates]
        for future in futures:
            try:
                result = future.result()
            except Exception as e:
                print(f"[PARALLEL DOWNLOAD ERROR] {e}")
                continue
            if not result:
                continue
            if has_piano_track(result[0]):
                safe, motivo = is_midi_safe_for_visualizer(result[0])
                if safe:
                    results.append(result)
                else:
                    if os.path.exists(result[0]):
                        os.remove(result[0])
                    print(f"[ERRORE FILTRO] Brano scartato per sicurezza: {motivo}")
            else:
                if os.path.exists(result[0]):
                    os.remove(result[0])
                print("[ERRORE FILTRO] Brano senza pianoforte scartato.")
    return results

def handle_song_request(query):
    """Gestisce l'intera pipeline di ricerca: DB Locale -> Online -> Filtro Piano -> Risposta UDP."""
    clean_query = query.strip().lower()
    if not clean_query:
        return

    print(f"\n[RICERCA BRANO] Richiesta per: '{clean_query}'")
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    
    # 1. Controllo nel Database Locale (Cache)
    c.execute("SELECT filename, title, artist FROM midi_files WHERE query=? ORDER BY id LIMIT 1", (clean_query,))
    row = c.fetchone()
    
    if row:
        filename, db_title, db_artist = row
        filepath = os.path.join(DESKTOP_PATH, filename)
        if os.path.exists(filepath):
            print(f"[DATABASE LOCALE] Trovato in memoria: {filename}")
            send_to_unity({"action": "search_result", "status": "success", "filename": filename, "title": db_title, "artist": db_artist})
            conn.close()
            return

    # 2. Ricerca sul Database Online: freemidi multi-risultato
    print("[ONLINE] Ricerca su freemidi.org in corso...")
    results = download_validated_candidates(
        freemidi_search_candidates(clean_query),
        download_freemidi_candidate
    )

    # 3. Fallback su bitmidi.com se freemidi non ha prodotto risultati validi
    if not results:
        print("[ONLINE] Nessun risultato freemidi valido, provo con bitmidi.com...")
        results = download_validated_candidates(
            bitmidi_search_candidates(clean_query),
            download_bitmidi_candidate
        )

    if not results:
        print("[ONLINE] Nessun risultato trovato.")
        send_to_unity({
            "action": "search_result", 
            "status": "error", 
            "message": "Canzone non trovata nel database online."
        })
        conn.close()
        return

    # 4. Salvataggio nel DB locale di tutti i brani validati
    for filepath, filename, title, artist in results:
        try:
            c.execute("INSERT OR IGNORE INTO midi_files (query, filename, title, artist) VALUES (?, ?, ?, ?)",
                      (clean_query, filename, title, artist))
        except Exception as e:
            print(f"[DB] Errore salvataggio {filename}: {e}")
    conn.commit()

    # 5. Risposta UDP con il primo brano valido (i suggerimenti mostreranno tutti)
    filepath, filename, title, artist = results[0]
    print(f"[SUCCESS] Salvati {len(results)} brano/i per '{clean_query}', primo: {filename}")
    send_to_unity({"action": "search_result", "status": "success", "filename": filename, "title": title, "artist": artist})
    
    conn.close()

# ==========================================
# GESTIONE PORTE MIDI
# ==========================================
inport = None
outport = None

def select_physical_midi_port(port_names, is_output=False):
    if not port_names:
        return None
    ignore_keywords = ["loopmidi", "virtual", "through"]
    if is_output:
        ignore_keywords.append("microsoft gs")

    for name in port_names:
        if not any(key in name.lower() for key in ignore_keywords):
            return name
    return port_names[0]

try:
    input_names = mido.get_input_names()
    output_names = mido.get_output_names()
    best_input = select_physical_midi_port(input_names, is_output=False)
    best_output = select_physical_midi_port(output_names, is_output=True)

    if best_input:
        inport = mido.open_input(best_input)
        print(f"[MIDI IN] Connesso a: '{best_input}'")
    if best_output:
        outport = mido.open_output(best_output)
        print(f"[MIDI OUT] Connesso a: '{best_output}'")
except Exception as e:
    print(f"[ERRORE MIDI] {e}")

# ==========================================
# STATO GLOBALE E STORICO SESSIONI
# ==========================================
sock_send = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
send_lock = threading.Lock()

is_recording_fai_tu = False
recorded_notes_fai_tu = [] 
active_pressed_notes = {}   

t_first_note_fai_tu = None
t_last_note_fai_tu = None

is_playing_observing = False
is_following = False
current_song_filename = "N/A"
follow_notes_sequence = []
follow_current_index = 0

recorded_follow_user_notes = [] 
active_pressed_follow = {}
current_song_reference_notes = [] 

history_follow_sessions = []

def send_to_unity(data_dict):
    msg = json.dumps(data_dict)
    with send_lock:
        sock_send.sendto(msg.encode('utf-8'), (UDP_IP, PORT_TO_UNITY))

def get_note_color_rgb(note, velocity):
    v = max(0.0, min(1.0, velocity))
    if note < 60:
        return (0.0, 1.0 - v, 1.0)
    else:
        return (1.0, 0.92 * (1.0 - v), 0.0)

def parse_song_info(filename):
    name_without_ext = os.path.splitext(filename)[0].replace("_", " ")
    if " - " in name_without_ext:
        parts = name_without_ext.split(" - ", 1)
        return {"title": parts[0].strip(), "artist": parts[1].strip()}
    elif "-" in name_without_ext:
        parts = name_without_ext.split("-", 1)
        return {"title": parts[0].strip(), "artist": parts[1].strip()}
    else:
        return {"title": name_without_ext.strip(), "artist": "Artista Non Specificato"}

def get_song_list():
    """Returns all songs stored in the local database."""
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute("SELECT id, query, filename, title, artist FROM midi_files ORDER BY id")
    rows = c.fetchall()
    conn.close()
    songs = []
    for rid, query, filename, title, artist in rows:
        # Usa i metadati reali se presenti; il parsing euristico solo se assenti entrambi
        if not title and not artist:
            info = parse_song_info(filename)
            title, artist = info["title"], info["artist"]
        elif not title:
            title = "Titolo Sconosciuto"
        elif not artist:
            artist = "Artista Non Specificato"
        songs.append({
            "id": rid,
            "query": query,
            "filename": filename,
            "title": title,
            "artist": artist
        })
    return songs

def get_filename_by_id(song_id):
    """Returns the filename associated with a song ID, or None if not found."""
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute("SELECT filename FROM midi_files WHERE id=?", (song_id,))
    row = c.fetchone()
    conn.close()
    return row[0] if row else None

def scan_local_folder():
    """Registra nel DB locale tutti i file .mid già presenti nella cartella Nora."""
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    if not os.path.isdir(DESKTOP_PATH):
        print(f"[SCAN CARTELLA] Cartella non trovata: {DESKTOP_PATH}")
        conn.close()
        return 0
    aggiunti = 0
    for nome_file in os.listdir(DESKTOP_PATH):
        if not nome_file.lower().endswith(".mid"):
            continue
        info = parse_song_info(nome_file)
        query = nome_file.lower()
        filepath = os.path.join(DESKTOP_PATH, nome_file)
        safe, motivo = is_midi_safe_for_visualizer(filepath)
        if not safe:
            print(f"[SCAN CARTELLA] Brano a rischio saltato ({motivo}): {nome_file}")
            continue
        try:
            c.execute(
                "INSERT OR IGNORE INTO midi_files (query, filename, title, artist) VALUES (?, ?, ?, ?)",
                (query, nome_file, info["title"], info["artist"])
            )
            if c.rowcount and c.rowcount > 0:
                aggiunti += 1
        except Exception as e:
            print(f"[SCAN CARTELLA] Errore per {nome_file}: {e}")
    conn.commit()
    conn.close()
    print(f"[SCAN CARTELLA] Aggiunti {aggiunti} brani locali al database.")
    return aggiunti

def suggest_songs(query, limit=20):
    """Ricerca parziale (Live) nel DB locale per i suggerimenti mentre si digita."""
    clean = query.strip().lower()
    if not clean:
        return []
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    like = f"%{clean}%"
    c.execute(
        "SELECT id, query, filename, title, artist FROM midi_files "
        "WHERE title LIKE ? OR artist LIKE ? OR filename LIKE ? OR query LIKE ? "
        "ORDER BY id LIMIT ?",
        (like, like, like, like, limit)
    )
    rows = c.fetchall()
    conn.close()
    songs = []
    for rid, query, filename, title, artist in rows:
        songs.append({
            "id": rid,
            "query": query,
            "filename": filename,
            "title": title,
            "artist": artist
        })
    return songs

# ==========================================
# SINTETIZZATORE AUDIO WAV
# ==========================================
def generate_wav_from_midi_notes(notes_list, output_filepath, sample_rate=44100):
    if not notes_list:
        print("[AUDIO WARN] Lista note vuota, genero un tono di test.")
        notes_list = [{"note": 60, "vel": 0.8, "start": 0.0, "end": 1.0}]

    t0 = min(n["start"] for n in notes_list)
    t_end = max(n["end"] for n in notes_list)
    
    total_duration = max(2.0, (t_end - t0) + 1.0)
    total_samples = int(total_duration * sample_rate)
    audio_buffer = np.zeros(total_samples, dtype=np.float32)

    for n in notes_list:
        start_sec = n["start"] - t0
        raw_dur = n["end"] - n["start"]
        dur_sec = max(0.3, raw_dur)
        
        freq = 440.0 * (2.0 ** ((n["note"] - 69) / 12.0))
        vel = max(0.2, min(1.0, n["vel"]))

        num_samples_note = int(dur_sec * sample_rate)
        t_note = np.linspace(0, dur_sec, num_samples_note, endpoint=False)

        sine_fundamental = np.sin(2 * np.pi * freq * t_note)
        sine_harmonic = 0.3 * np.sin(2 * np.pi * 2 * freq * t_note)
        raw_wave = (sine_fundamental + sine_harmonic) * vel

        attack_len = int(min(0.01 * sample_rate, num_samples_note / 2))
        envelope = np.ones(num_samples_note, dtype=np.float32)
        if attack_len > 0:
            envelope[:attack_len] = np.linspace(0, 1, attack_len)
        
        decay = np.exp(-3.0 * t_note / dur_sec)
        note_audio = raw_wave * envelope * decay

        start_idx = int(start_sec * sample_rate)
        end_idx = start_idx + len(note_audio)

        if end_idx <= total_samples:
            audio_buffer[start_idx:end_idx] += note_audio

    max_val = np.max(np.abs(audio_buffer))
    if max_val > 0:
        audio_buffer = (audio_buffer / max_val) * 0.85
    else:
        t_fallback = np.linspace(0, 1.0, sample_rate, endpoint=False)
        audio_buffer[:sample_rate] = 0.5 * np.sin(2 * np.pi * 440.0 * t_fallback)

    audio_int16 = (audio_buffer * 32767).astype(np.int16)
    with wave.open(output_filepath, 'wb') as wf:
        wf.setnchannels(1) 
        wf.setsampwidth(2) 
        wf.setframerate(sample_rate)
        wf.writeframes(audio_int16.tobytes())

    print(f"[AUDIO SUCCESS] WAV generato ed udibile in: {output_filepath}")

# ==========================================
# CARICAMENTO BRANO TARGET
# ==========================================
def load_follow_sequence(filename):
    global follow_notes_sequence, follow_current_index, current_song_reference_notes, current_song_filename
    current_song_filename = filename
    filepath = os.path.join(DESKTOP_PATH, filename)
    if not os.path.exists(filepath):
        print(f"[ERRORE] File MIDI non trovato: {filepath}")
        return

    mid = mido.MidiFile(filepath)
    current_song_reference_notes = []

    active_ref_notes = {}
    accumulated_time = 0.0

    for msg in mid:
        accumulated_time += msg.time
        if msg.type == 'note_on' and msg.velocity > 0:
            active_ref_notes[msg.note] = {"start": accumulated_time, "vel": msg.velocity / 127.0}
        elif msg.type == 'note_off' or (msg.type == 'note_on' and msg.velocity == 0):
            if msg.note in active_ref_notes:
                ref_info = active_ref_notes.pop(msg.note)
                dur = max(0.05, accumulated_time - ref_info["start"])
                current_song_reference_notes.append({
                    "note": msg.note,
                    "vel": ref_info["vel"],
                    "start": ref_info["start"],
                    "dur": dur
                })

    current_song_reference_notes.sort(key=lambda x: x["start"])

    grouped_chords = []
    current_chord = []
    last_t = -1.0

    for n in current_song_reference_notes:
        t = n["start"]
        note = n["note"]
        if last_t < 0 or (t - last_t) < 0.05:
            current_chord.append(note)
        else:
            grouped_chords.append(current_chord)
            current_chord = [note]
        last_t = t

    if current_chord:
        grouped_chords.append(current_chord)

    follow_notes_sequence = grouped_chords
    follow_current_index = 0

def send_next_follow_step():
    global follow_current_index, is_following
    if follow_current_index < len(follow_notes_sequence):
        chord_notes = follow_notes_sequence[follow_current_index]
        send_to_unity({"action": "expect_notes", "notes": chord_notes})
        follow_current_index += 1
    else:
        send_to_unity({"action": "clear_scene"})
        is_following = False
        print("[SEGUIMI] Brano completato!")

# ==========================================
# LISTENER MIDI INPUT
# ==========================================
def midi_input_loop():
    global is_recording_fai_tu, t_first_note_fai_tu, t_last_note_fai_tu
    if not inport:
        return

    for msg in inport:
        if msg.type in ['note_on', 'note_off']:
            note = msg.note
            vel = msg.velocity / 127.0
            action = "press" if (msg.type == 'note_on' and msg.velocity > 0) else "release"
            now = time.time()

            send_to_unity({"note": note, "velocity": vel, "action": action})

            if is_recording_fai_tu:
                if action == "press":
                    if t_first_note_fai_tu is None:
                        t_first_note_fai_tu = now
                    active_pressed_notes[note] = {"vel": vel, "start": now}
                elif action == "release" and note in active_pressed_notes:
                    start_info = active_pressed_notes.pop(note)
                    recorded_notes_fai_tu.append({
                        "note": note,
                        "vel": start_info["vel"],
                        "start": start_info["start"],
                        "end": now
                    })
                    t_last_note_fai_tu = now

            if is_following:
                if action == "press":
                    active_pressed_follow[note] = {"vel": vel, "start": now}
                elif action == "release" and note in active_pressed_follow:
                    start_info = active_pressed_follow.pop(note)
                    note_obj = {
                        "note": note,
                        "vel": start_info["vel"],
                        "start": start_info["start"],
                        "end": now
                    }
                    recorded_follow_user_notes.append(note_obj)
                    if history_follow_sessions:
                        history_follow_sessions[-1]["user_notes"].append(note_obj)

# ==========================================
# RIPRODUZIONE OSSERVATORE
# ==========================================
def play_observing_thread(filename):
    global is_playing_observing, current_song_filename
    
    if is_playing_observing:
        is_playing_observing = False
        time.sleep(0.15)

    current_song_filename = filename
    filepath = os.path.join(DESKTOP_PATH, filename)
    if not os.path.exists(filepath):
        print(f"[ERRORE AUDIO] File non trovato: {filepath}")
        return

    if outport:
        for ch in range(16):
            outport.send(mido.Message('control_change', channel=ch, control=123, value=0))
    send_to_unity({"action": "clear_scene"})

    is_playing_observing = True
    mid = mido.MidiFile(filepath)

    try:
        start_real_time = time.time()
        for msg in mid:
            if not is_playing_observing:
                break
            
            if msg.time > 0:
                target_time = start_real_time + msg.time
                while time.time() < target_time:
                    if not is_playing_observing:
                        break
                    time.sleep(0.001)
                start_real_time = target_time

            if not is_playing_observing:
                break

            if outport and not msg.is_meta:
                outport.send(msg)

            if msg.type in ['note_on', 'note_off']:
                vel = msg.velocity / 127.0
                action = "press" if (msg.type == 'note_on' and msg.velocity > 0) else "release"
                send_to_unity({"note": msg.note, "velocity": vel, "action": action})
                
    except Exception as e:
        print(f"[ERRORE PLAYING] {e}")
    finally:
        is_playing_observing = False
        if outport:
            for ch in range(16):
                outport.send(mido.Message('control_change', channel=ch, control=123, value=0))
        send_to_unity({"action": "clear_scene"})
        print(f"[PLAYING] Terminato o interrotto: {filename}")

# ==========================================
# GENERAZIONE REPORT PDF
# ==========================================
last_report_time = 0

def generate_unified_report():
    global last_report_time
    now = time.time()
    
    if now - last_report_time < 5.0:
        print("[REPORT IGNORED] Richiesta duplicata bloccata.")
        return
    
    last_report_time = now

    timestamp_str = time.strftime("%Y%m%d_%H%M%S")
    session_dir = os.path.join(DESKTOP_PATH, f"Sessione_{timestamp_str}")
    os.makedirs(session_dir, exist_ok=True)

    pdf_filepath = os.path.join(session_dir, "Report_Completo_Nora.pdf")
    wav_filepath = os.path.join(session_dir, "Registrazione_Audio.wav")

    local_fai_tu_notes = copy.deepcopy(recorded_notes_fai_tu)

    generate_wav_from_midi_notes(local_fai_tu_notes, wav_filepath)

    try:
        with PdfPages(pdf_filepath) as pdf:
            fig1 = plt.figure(figsize=(8.5, 11))
            fig1.suptitle("Report - Modalità Fai Tu", fontsize=15, fontweight='bold', y=0.97)

            gs1 = fig1.add_gridspec(3, 1, height_ratios=[1.0, 1.2, 1.1], left=0.12, right=0.88, top=0.93, bottom=0.05, hspace=0.45)

            if local_fai_tu_notes:
                t0 = min(n["start"] for n in local_fai_tu_notes)
                notes_data = [{
                    "note": n["note"],
                    "vel": n["vel"],
                    "start": n["start"] - t0,
                    "end": n["end"] - t0
                } for n in local_fai_tu_notes]

                velocities = [n["vel"] for n in notes_data]
                durations = [max(0.05, n["end"] - n["start"]) for n in notes_data]
                starts = sorted([n["start"] for n in notes_data])

                std_vel = float(np.std(velocities)) if len(velocities) > 1 else 0.0
                mean_vel = float(np.mean(velocities)) if velocities else 0.5
                vel_sx = [n["vel"] for n in notes_data if n["note"] < 60]
                vel_dx = [n["vel"] for n in notes_data if n["note"] >= 60]
                mean_sx = float(np.mean(vel_sx)) if vel_sx else 0.0
                mean_dx = float(np.mean(vel_dx)) if vel_dx else 0.0
                
                iois = np.diff(starts) if len(starts) > 1 else [0.0]
                std_ioi = float(np.std(iois)) if len(iois) > 1 else 0.0
                mean_dur = float(np.mean(durations)) if durations else 0.2

                ax1 = fig1.add_subplot(gs1[0])
                ax1.set_title("Cromagramma dell'Esecuzione (Piano Roll)", fontsize=10, pad=8)
                max_end = 1.0
                for n in notes_data:
                    dur = max(0.1, n["end"] - n["start"])
                    if (n["start"] + dur) > max_end:
                        max_end = n["start"] + dur
                    color = get_note_color_rgb(n["note"], n["vel"])
                    rect = patches.Rectangle((n["start"], n["note"] - 0.4), dur, 0.8,
                                             linewidth=0.5, edgecolor='black', facecolor=color)
                    ax1.add_patch(rect)
                ax1.set_xlim(0, max_end + 0.5)
                ax1.set_ylim(20, 109)
                ax1.set_ylabel("Pitch MIDI", fontsize=8)
                ax1.set_xlabel("Tempo (secondi)", fontsize=8)
                ax1.tick_params(axis='both', labelsize=8)
                ax1.grid(True, linestyle='--', alpha=0.3)

                ax_radar = fig1.add_subplot(gs1[1], polar=True)
                ax_radar.set_title("Impronta Digitale dell'Espressività (Firma Unica)", fontsize=10, pad=12)
                
                categories = ['Marcato\n(Peso)', 'Varietà\nDinamica', 'Articolazione\n(Legato)', 'Rubato\n(Flessibilità)', 'Bilanciamento\nMani']
                N = len(categories)
                
                v1 = min(1.0, mean_vel * 1.2)
                v2 = min(1.0, std_vel * 4.0)
                v3 = min(1.0, mean_dur * 2.0)
                v4 = min(1.0, std_ioi * 3.0)
                v5 = min(1.0, (mean_dx / (mean_sx + 0.001)) * 0.5) if mean_sx > 0 else 0.5

                values = [v1, v2, v3, v4, v5]
                values += values[:1]

                angles = [n / float(N) * 2 * np.pi for n in range(N)]
                angles += angles[:1]

                ax_radar.plot(angles, values, linewidth=2, linestyle='solid', color='#9b59b6')
                ax_radar.fill(angles, values, color='#9b59b6', alpha=0.35)
                ax_radar.set_xticks(angles[:-1])
                ax_radar.set_xticklabels(categories, fontsize=7.5)
                ax_radar.set_ylim(0, 1)

                ax2 = fig1.add_subplot(gs1[2])
                ax2.axis('off')

                soglia_vel = 0.12
                soglia_ioi = 0.15
                suggerimenti = []

                if std_vel < soglia_vel:
                    suggerimenti.append("La tua esecuzione è dinamicamente piatta. Prova ad accentuare le note chiave.")
                else:
                    suggerimenti.append("Ottima escursione dinamica: tocco vivo e ricco di sfumature.")

                if mean_sx > mean_dx and mean_sx > 0.0:
                    suggerimenti.append("La mano sinistra copre la melodia. Riduci il volume dell'accompagnamento.")
                else:
                    suggerimenti.append("Buon equilibrio: la melodia (destra) risalta sull'accompagnamento.")

                if std_ioi < soglia_ioi:
                    suggerimenti.append("Il fraseggio è meccanico. Prova a rallentare leggermente a fine frase.")
                else:
                    suggerimenti.append("Ottima flessibilità ritmica e naturalezza nel fraseggio (rubato).")

                testo_completo = (
                    "GUIDA ALL'INTERPRETAZIONE DELL'IMPRONTA DIGITALE:\n"
                    "Il grafico polare mappa 5 dimensioni dello stile esecutivo (da 0.0 a 1.0):\n"
                    "• Marcato (Peso): Intensità media del tocco.\n"
                    "• Varietà Dinamica: Escursione e contrasto tra i piani e i forti.\n"
                    "• Articolazione (Legato): Durata e sovrapposizione delle note.\n"
                    "• Rubato (Flessibilità): Deviazione espressiva dal tempo metronomico.\n"
                    "• Bilanciamento Mani: Peso relativo tra accompagnamento (SX) e melodia (DX).\n\n"
                    "FEEDBACK SULLA TUA ESECUZIONE:\n"
                    f"• Dinamica: {suggerimenti[0]}\n"
                    f"• Bilanciamento: {suggerimenti[1]}\n"
                    f"• Fraseggio: {suggerimenti[2]}"
                )

                ax2.text(0.0, 0.95, testo_completo, transform=ax2.transAxes, fontsize=8,
                         verticalalignment='top', bbox=dict(boxstyle='round', facecolor='#f8f9fa', edgecolor='#d3d3d3', alpha=0.9))
            else:
                ax = fig1.add_subplot(1, 1, 1)
                ax.axis('off')
                ax.text(0.5, 0.5, "Nessuna nota registrata nella modalità Fai Tu.", horizontalalignment='center', fontsize=12)

            pdf.savefig(fig1)
            plt.close(fig1)

            valid_sessions = [s for s in history_follow_sessions if len(s.get("user_notes", [])) > 0]

            for s_idx, session in enumerate(valid_sessions, 1):
                title_info = session["title_info"]
                ref_notes = session["reference_notes"]
                usr_notes = session["user_notes"]

                fig_song = plt.figure(figsize=(8.5, 11))
                fig_song.suptitle(f"Analisi Comparativa #{s_idx}: Modalità Seguimi", fontsize=15, fontweight='bold', y=0.97)

                gs_song = fig_song.add_gridspec(4, 1, height_ratios=[0.3, 1.0, 1.0, 1.1], left=0.12, right=0.88, top=0.93, bottom=0.05, hspace=0.40)

                ax_info = fig_song.add_subplot(gs_song[0])
                ax_info.axis('off')
                info_text = f"BRANO: {title_info['title']}\nARTISTA: {title_info['artist']}"
                ax_info.text(0.5, 0.5, info_text, transform=ax_info.transAxes, fontsize=11, fontweight='bold',
                             horizontalalignment='center', verticalalignment='center',
                             bbox=dict(boxstyle='round,pad=0.5', facecolor='#e1f5fe', edgecolor='#0288d1', alpha=0.9))

                u_t0 = usr_notes[0]["start"]
                max_u_t = max((n["start"] - u_t0) for n in usr_notes)
                time_limit = max(max_u_t + 1.5, 5.0)

                ax_target = fig_song.add_subplot(gs_song[1])
                ax_target.set_title("1. Target (Brano Originale - Osservatore)", fontsize=10, pad=6)
                if ref_notes:
                    for n in ref_notes:
                        if n["start"] <= time_limit:
                            dur = n.get("dur", 0.3)
                            color = get_note_color_rgb(n["note"], n["vel"])
                            rect = patches.Rectangle((n["start"], n["note"] - 0.4), dur, 0.8, 
                                                     facecolor=color, edgecolor=color, linewidth=0.1)
                            ax_target.add_patch(rect)
                
                ax_target.set_xlim(0, time_limit)
                ax_target.set_ylim(20, 109)
                ax_target.set_ylabel("Pitch", fontsize=8)
                ax_target.tick_params(axis='both', labelsize=8)
                ax_target.grid(True, linestyle='--', alpha=0.3)

                ax_user = fig_song.add_subplot(gs_song[2])
                ax_user.set_title("2. Tua Esecuzione (Modalità Seguimi)", fontsize=10, pad=6)
                for n in usr_notes:
                    t_rel = n["start"] - u_t0
                    dur = max(0.05, n.get("end", n["start"] + 0.3) - n["start"])
                    color = get_note_color_rgb(n["note"], n["vel"])
                    rect = patches.Rectangle((t_rel, n["note"] - 0.4), dur, 0.8, 
                                             facecolor=color, edgecolor='black', linewidth=0.3)
                    ax_user.add_patch(rect)

                ax_user.set_xlim(0, time_limit)
                ax_user.set_ylim(20, 109)
                ax_user.set_xlabel("Tempo (secondi)", fontsize=8)
                ax_user.set_ylabel("Pitch", fontsize=8)
                ax_user.tick_params(axis='both', labelsize=8)
                ax_user.grid(True, linestyle='--', alpha=0.3)

                ax_comp = fig_song.add_subplot(gs_song[3])
                ax_comp.axis('off')

                ref_vel_mean = np.mean([n["vel"] for n in ref_notes]) if ref_notes else 0.5
                usr_vel_mean = np.mean([n["vel"] for n in usr_notes]) if usr_notes else 0.5
                diff_vel = usr_vel_mean - ref_vel_mean
                
                if diff_vel > 0.1:
                    commento_dinamica = "Tocco più marcato e incisivo rispetto all'originale."
                elif diff_vel < -0.1:
                    commento_dinamica = "Tocco più leggero e delicato rispetto all'originale."
                else:
                    commento_dinamica = "Dinamica e peso del tocco perfettamente in linea con il brano originale."

                ref_dur_mean = np.mean([n.get("dur", 0.3) for n in ref_notes]) if ref_notes else 0.3
                usr_dur_mean = np.mean([n["end"] - n["start"] for n in usr_notes]) if usr_notes else 0.3
                diff_dur = usr_dur_mean - ref_dur_mean

                if diff_dur < -0.1:
                    commento_articolazione = "Esecuzione tendente allo staccato/sgranato (note più brevi del riferimento)."
                elif diff_dur > 0.1:
                    commento_articolazione = "Esecuzione molto legata e sostenuta (tasti tenuti a lungo)."
                else:
                    commento_articolazione = "Articolazione (durata e fraseggio delle note) bilanciata ed in linea con l'originale."

                commento_tempo = "Ottima aderenza al ritmo e alla sequenza delle note guida." if len(usr_notes) > 5 else "Esecuzione parziale o con pause rilevanti."

                text_comparativo = (
                    f"VALUTAZIONE PRESTAZIONALE PER '{title_info['title'].upper()}':\n\n"
                    f"• DINAMICA (VOLUME E TOCCO):\n"
                    f"  - Target Velocity Media: {ref_vel_mean:.2f} | Tua Velocity Media: {usr_vel_mean:.2f}\n"
                    f"  - Analisi: {commento_dinamica}\n\n"
                    f"• ARTICOLAZIONE (STACCATO / LEGATO):\n"
                    f"  - Durata Media Note Target: {ref_dur_mean:.2f}s | Tua Durata Media: {usr_dur_mean:.2f}s\n"
                    f"  - Analisi: {commento_articolazione}\n\n"
                    f"• TEMPO E RITMICA:\n"
                    f"  - Analisi: {commento_tempo}\n\n"
                    "SINTESI GENERALE:\n"
                    "L'esecuzione mostra la capacità di adattarsi alla struttura del brano di riferimento "
                    "mantenendo gli elementi espressivi personali dell'esecutore."
                )
                ax_comp.text(0.0, 0.95, text_comparativo, transform=ax_comp.transAxes, fontsize=8.5,
                             verticalalignment='top', bbox=dict(boxstyle='round', facecolor='#eef2f5', edgecolor='#c5d0d8', alpha=0.9))

                pdf.savefig(fig_song)
                plt.close(fig_song)

        print(f"[REPORT OK] Generato correttamente in: {session_dir}")
        send_to_unity({"action": "report_ready", "file": pdf_filepath, "folder": session_dir})

    except Exception as e:
        print(f"[ERRORE PDF] {e}")

# ==========================================
# LISTENER UDP COMANDI DA UNITY
# ==========================================
def udp_command_listener():
    global is_following, is_playing_observing, is_recording_fai_tu
    
    sock_recv = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock_recv.bind((UDP_IP, PORT_FROM_UNITY))
    print(f"[UDP LISTENER] Avviato ed in ascolto sulla porta {PORT_FROM_UNITY}...")

    while True:
        try:
            data, addr = sock_recv.recvfrom(2048)
            text = data.decode('utf-8')
            cmd = json.loads(text)
            action = cmd.get("action")

            if action == "play_song":
                song_id = cmd.get("id")
                filename = cmd.get("filename")
                if song_id is not None:
                    filename = get_filename_by_id(song_id)
                if not filename:
                    send_to_unity({
                        "action": "play_result",
                        "status": "error",
                        "message": "Brano non trovato nel database."
                    })
                    continue
                threading.Thread(target=play_observing_thread, args=(filename,), daemon=True).start()

            elif action == "play_song_follow":
                song_id = cmd.get("id")
                filename = cmd.get("filename")
                if song_id is not None:
                    filename = get_filename_by_id(song_id)
                if not filename:
                    send_to_unity({
                        "action": "play_result",
                        "status": "error",
                        "message": "Brano non trovato nel database."
                    })
                    continue
                is_following = True
                recorded_follow_user_notes.clear()
                active_pressed_follow.clear()
                
                load_follow_sequence(filename)
                
                new_session = {
                    "filename": filename,
                    "title_info": parse_song_info(filename),
                    "reference_notes": copy.deepcopy(current_song_reference_notes),
                    "user_notes": []
                }
                history_follow_sessions.append(new_session)
                
                send_next_follow_step()

            elif action == "list_songs":
                songs = get_song_list()
                send_to_unity({"action": "song_list", "count": len(songs), "songs": songs})
                print(f"[LISTA BRANI] Inviate {len(songs)} canzoni a Unity.")

            elif action == "next_step":
                send_next_follow_step()

            elif action == "stop_song":
                is_playing_observing = False
                is_following = False
                send_to_unity({"action": "clear_scene"})

            elif action == "start_fai_tu":
                is_recording_fai_tu = True
                recorded_notes_fai_tu.clear()
                active_pressed_notes.clear()
                t_first_note_fai_tu = None
                t_last_note_fai_tu = None
                print("[FAI TU] Registrazione avviata.")

            elif action == "stop_fai_tu":
                is_recording_fai_tu = False
                print("[FAI TU] Registrazione fermata.")

            elif action == "generate_report":
                is_recording_fai_tu = False
                generate_unified_report()

            elif action == "search_song":
                query = cmd.get("query", "")
                threading.Thread(target=handle_song_request, args=(query,), daemon=True).start()

            elif action == "search_suggest":
                query = cmd.get("query", "")
                songs = suggest_songs(query)
                send_to_unity({"action": "song_list", "count": len(songs), "songs": songs, "suggest": True})
                print(f"[SUGGERIMENTI] Inviate {len(songs)} corrispondenze per '{query}'.")

        except Exception as e:
            print(f"[ERRORE UDP] {e}")

# ==========================================
# MAIN ENTRY POINT
# ==========================================
if __name__ == "__main__":
    scan_local_folder()

    t_midi = threading.Thread(target=midi_input_loop, daemon=True)
    t_udp = threading.Thread(target=udp_command_listener, daemon=True)

    t_midi.start()
    t_udp.start()

    print("[SYSTEM READY] Nora MIDI Bridge attivo.")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nChiusura bridge.")