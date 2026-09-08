import socket
import json
import threading
import time

# Configurazione indirizzi (Porte opposte a quelle del backend)
UDP_IP = "127.0.0.1"
PORT_TO_PYTHON = 5006   # Porta di ascolto del backend
PORT_FROM_PYTHON = 5005 # Porta dove il backend trasmette i dati

sock_send = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

def ascolta_backend():
    """Ascolta i messaggi UDP restituiti dal backend Python."""
    sock_recv = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock_recv.bind((UDP_IP, PORT_FROM_PYTHON))
    print("[SIMULATORE UNITY] In ascolto sulla porta", PORT_FROM_PYTHON, "...\n")

    last_note_report = time.time()
    note_count = 0
    last_note = None

    while True:
        try:
            data, _ = sock_recv.recvfrom(2048)
            msg = json.loads(data.decode('utf-8'))
            action = msg.get("action")

            if action == "song_list":
                songs = msg.get("songs", [])
                print(f"\n=== BRANI DISPONIBILI ({len(songs)}) ===")
                for s in songs:
                    print(f"  [{s['id']}] {s.get('title', '?')} - {s.get('artist', '?')}  ({s['filename']})")
                print("=" * 30)
                print("> Seleziona comando: ", end="")
            elif action in ["press", "release"]:
                note_count += 1
                last_note = msg
                now = time.time()
                # Riepilogo compatto ogni 2 secondi per non spammare il terminale
                if now - last_note_report >= 2.0:
                    print(f"\n[NOTE ATTIVE] {note_count} eventi nota in {now - last_note_report:.0f}s "
                          f"(ultima: note={last_note.get('note')} {last_note.get('action')})", end="")
                    last_note_report = now
                    note_count = 0
            else:
                print(f"\n[RICEVUTO DA PYTHON] => {msg}\n> Seleziona comando: ", end="")
        except Exception as e:
            print(f"[ERRORE RICEZIONE] {e}")

def invia_comando(dizionario):
    msg = json.dumps(dizionario)
    sock_send.sendto(msg.encode('utf-8'), (UDP_IP, PORT_TO_PYTHON))

if __name__ == "__main__":
    t = threading.Thread(target=ascolta_backend, daemon=True)
    t.start()

    print("=== PANNELLO DI CONTROLLO SIMULATORE UNITY ===")
    print("1: Avvia 'Fai Tu' (start_fai_tu)")
    print("2: Ferma 'Fai Tu' (stop_fai_tu)")
    print("3: Genera Report (generate_report)")
    print("4: Suona Brano 'Osservatore' (play_song)")
    print("5: Suona Brano 'Seguimi' (play_song_follow)")
    print("6: Prossimo step Seguimi (next_step)")
    print("7: Ferma Brani (stop_song)")
    print("8: CERCA E SCARICA CANZONE (search_song)")
    print("9: ELENCA BRANI DISPONIBILI (list_songs)")
    print("0: Esci")

    while True:
        scelta = input("\n> Seleziona comando: ")
        
        if scelta == "1":
            invia_comando({"action": "start_fai_tu"})
        elif scelta == "2":
            invia_comando({"action": "stop_fai_tu"})
        elif scelta == "3":
            invia_comando({"action": "generate_report"})
        elif scelta == "4":
            brano_id = input("ID del brano (usa 9 per vedere la lista): ")
            invia_comando({"action": "play_song", "id": int(brano_id)})
        elif scelta == "5":
            brano_id = input("ID del brano (usa 9 per vedere la lista): ")
            invia_comando({"action": "play_song_follow", "id": int(brano_id)})
        elif scelta == "6":
            invia_comando({"action": "next_step"})
        elif scelta == "7":
            invia_comando({"action": "stop_song"})
        elif scelta == "8":
            query = input("Digita il titolo o l'artista della canzone da cercare: ")
            invia_comando({"action": "search_song", "query": query})
        elif scelta == "9":
            invia_comando({"action": "list_songs"})
        elif scelta == "0":
            break
        else:
            print("Comando non valido.")