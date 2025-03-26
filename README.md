# 🦸‍♂️ MarvelScript - Mod per GTA V

Questo mod trasforma i pedoni di GTA V in eroi e villain che combattono tra loro, creando epiche battaglie per le strade di Los Santos.

## 📥 Installazione

1. **Prerequisiti**:
   - Grand Theft Auto V
   - [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/)
   - [ScriptHookVDotNet3](https://github.com/crosire/scripthookvdotnet/releases)

2. **File da installare**:
   Copia i seguenti file nella cartella `scripts` di GTA V (di solito in `Grand Theft Auto V/scripts/`):
   ```
   📄 MarvelScript.dll
   📄 Newtonsoft.Json.dll
   📄 npc_model_mapping.json
   ```

## 🎮 Funzionalità

### Sistema di Fazioni
- I pedoni vengono automaticamente assegnati alle fazioni Hero o Villain
- Le fazioni sono in costante conflitto tra loro
- I personaggi mantengono la loro fazione anche dopo il respawn

### Combattimento Automatico
- Gli eroi e i villain si cercano e combattono automaticamente
- Il raggio di rilevamento è di 100 unità intorno al giocatore
- I combattenti sono aggressivi e non fuggono dal combattimento
- Le battaglie continuano fino alla vittoria/sconfitta

### Notifiche e Logging
- Notifiche a schermo quando:
  - Il mod viene caricato
  - I modelli JSON vengono caricati
  - Si verificano errori
- File di log dettagliato in `scripts/MarvelScript_Log.txt` con:
  - Inizializzazione delle fazioni
  - Registrazione dei pedoni
  - Eventi di combattimento

## 🔧 Configurazione

### File npc_model_mapping.json
Il file contiene le informazioni sui personaggi nel formato:
```json
[
  {
    "Name": "Spider-Man",
    "Model": "s_m_y_cop_01",
    "Hash": "0x5E3DA4A4",
    "Description": "Your friendly neighborhood Spider-Man",
    "Faction": "Hero",
    "PowerType": "Agility"
  }
]
```

### Intervallo di Aggiornamento
- Il mod controlla i pedoni ogni 2 secondi
- Questo valore può essere modificato nel costruttore di `MainScript.cs`

## 📝 Log e Debug

Il file `MarvelScript_Log.txt` viene creato automaticamente in `scripts/` e contiene:
```
[HH:mm:ss] Inizializzate relazioni tra fazioni (Hero vs Villain)
[HH:mm:ss] Registrato HERO: 1980125469
[HH:mm:ss] Registrato VILLAIN: -356629458
[HH:mm:ss] 1980125469 (HERO) attacca -356629458 (VILLAIN)
```

## 🔍 Risoluzione Problemi

1. **Il mod non si carica**:
   - Verifica che ScriptHookV e ScriptHookVDotNet3 siano installati
   - Controlla che tutti i file siano nella cartella `scripts`
   - Verifica il file di log per errori

2. **I pedoni non combattono**:
   - Assicurati che il file JSON contenga hash validi
   - Avvicinati ai pedoni (raggio di 100 unità)
   - Controlla il file di log per errori di registrazione

3. **Errori di JSON**:
   - Verifica che il file sia in formato UTF-8
   - Controlla la sintassi JSON
   - Assicurati che tutti i campi obbligatori siano presenti

## 🛠️ Sviluppo

Per compilare il progetto:
1. Installa .NET Framework 4.8 Developer Pack
2. Esegui `build.bat` o usa Visual Studio
3. I file compilati si troveranno in `bin/Debug/`

## 📄 Licenza

Questo mod è distribuito sotto licenza MIT. Vedi il file `LICENSE` per i dettagli.

## 👥 Crediti

- ScriptHookV by Alexander Blade
- ScriptHookVDotNet by crosire
- Newtonsoft.Json by James Newton-King 