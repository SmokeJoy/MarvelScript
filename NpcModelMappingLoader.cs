// NpcModelMappingLoader.cs - Gestisce il caricamento e la ricerca delle informazioni sui pedoni dal file JSON
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using GTA.UI;

namespace MarvelScript
{
    /// <summary>
    /// Classe che rappresenta il mapping tra un modello di pedone e le sue caratteristiche.
    /// Ogni istanza rappresenta un personaggio Marvel con le sue proprietà.
    /// </summary>
    public class NpcModelMapping
    {
        public string Name { get; set; }        // Nome del personaggio Marvel (es. "Spider-Man")
        public string Model { get; set; }       // Nome del modello GTA V (es. "s_m_y_cop_01")
        public string Hash { get; set; }        // Hash numerico del modello in formato stringa (es. "0x62018559")
        public string Description { get; set; } // Descrizione del personaggio e suoi poteri
        public string Faction { get; set; }     // Fazione di appartenenza (Hero/Villain)
        public string PowerType { get; set; }   // Tipo di potere (es. "Strength", "Technology")

        /// <summary>
        /// Converte l'hash in formato stringa (es. "0x62018559") in un valore uint
        /// </summary>
        public uint GetHashValue()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Hash))
                    return 0;

                string hexValue = Hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Hash.Substring(2)
                    : Hash;

                if (string.IsNullOrWhiteSpace(hexValue))
                    return 0;

                return Convert.ToUInt32(hexValue, 16);
            }
            catch (Exception ex)
            {
                Notification.PostTicker($"~r~Errore conversione hash {Hash}: {ex.Message}", true, false);
                return 0;
            }
        }

        /// <summary>
        /// Valida che tutti i campi obbligatori siano presenti e corretti.
        /// Logga eventuali errori per facilitare il debug.
        /// </summary>
        /// <returns>true se il personaggio è valido, false altrimenti</returns>
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();

            // Validazione rigorosa del nome
            if (string.IsNullOrEmpty(Name))
                errors.Add($"Nome mancante per hash {Hash}");
            else if (Name.Length < 2)
                errors.Add($"Nome '{Name}' troppo corto (min 2 caratteri) per hash {Hash}");

            // Validazione rigorosa del modello
            if (string.IsNullOrEmpty(Model))
                errors.Add($"Modello mancante per {Name} ({Hash})");
            else if (!Model.Contains("_"))
                errors.Add($"Formato modello non valido per {Name}: {Model} (richiesto underscore)");

            // Validazione rigorosa dell'hash
            if (string.IsNullOrEmpty(Hash))
                errors.Add($"Hash non valido (vuoto) per {Name}");
            else
            {
                uint hashValue = GetHashValue();
                if (hashValue == 0)
                    errors.Add($"Hash non valido (0) per {Name}: {Hash}");
                else if (hashValue == uint.MaxValue)
                    errors.Add($"Hash non valido (overflow) per {Name}: {Hash}");
            }

            // Validazione rigorosa della fazione
            if (string.IsNullOrEmpty(Faction))
                errors.Add($"Fazione mancante per {Name} ({Hash})");
            else if (!Faction.Equals("Hero", StringComparison.OrdinalIgnoreCase) && 
                     !Faction.Equals("Villain", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Fazione '{Faction}' non valida per {Name}. Deve essere 'Hero' o 'Villain'");

            // Validazione PowerType
            if (string.IsNullOrEmpty(PowerType))
                errors.Add($"PowerType mancante per {Name} ({Hash})");

            return !errors.Any();
        }

        public List<string> GetValidationErrors()
        {
            List<string> errors;
            if (IsValid(out errors))
            {
                return new List<string>();
            }
            return errors;
        }

        public bool IsValid()
        {
            List<string> errors;
            return IsValid(out errors);
        }

        public override string ToString()
        {
            return $"{Name} ({Model}, {Hash}, {Faction})";
        }
    }

    /// <summary>
    /// Classe statica che gestisce il caricamento e la validazione dei 106 personaggi Marvel.
    /// Si occupa di caricare il file JSON, validare ogni personaggio e fornire metodi di ricerca.
    /// </summary>
    public static class NpcModelMappingLoader
    {
        private const int EXPECTED_CHARACTER_COUNT = 106;
        private static readonly string JSON_PATH = "scripts/npc_model_mapping.json";
        private static readonly string LOG_FILE = "scripts/MarvelScript_Log.txt";
        private static List<NpcModelMapping> mappings = new List<NpcModelMapping>();
        private static readonly HashSet<uint> detectedHashes = new HashSet<uint>();
        private static readonly Dictionary<uint, DateTime> firstDetectionTime = new Dictionary<uint, DateTime>();
        private static bool isMappingsLoaded = false; // Flag per evitare caricamenti ripetuti
        private static readonly HashSet<uint> unknownHashesLogged = new HashSet<uint>(); // Per evitare log ripetuti di hash non trovati

        /// <summary>
        /// Carica e valida i 106 personaggi dal file JSON.
        /// Esegue controlli rigorosi su ogni personaggio e sulla struttura complessiva.
        /// </summary>
        /// <returns>true se tutti i 106 personaggi sono validi, false altrimenti</returns>
        public static bool LoadMappings()
        {
            // Se i mapping sono già stati caricati con successo, restituisci true immediatamente
            if (isMappingsLoaded && mappings.Count == EXPECTED_CHARACTER_COUNT)
            {
                return true;
            }

            try
            {
                Log("🔄 Inizializzazione caricamento personaggi...");

                // Reset dello stato
                mappings.Clear();
                detectedHashes.Clear();
                firstDetectionTime.Clear();
                isMappingsLoaded = false;

                // Verifica esistenza file
                if (!File.Exists(JSON_PATH))
                {
                    LogError($"File {JSON_PATH} non trovato!");
                    return false;
                }

                // Leggi e valida il JSON
                string jsonContent = File.ReadAllText(JSON_PATH);
                if (string.IsNullOrEmpty(jsonContent))
                {
                    LogError($"File {JSON_PATH} vuoto!");
                    return false;
                }

                // Deserializza il JSON
                mappings = JsonConvert.DeserializeObject<List<NpcModelMapping>>(jsonContent);
                if (mappings == null || mappings.Count == 0)
                {
                    LogError("Nessun personaggio trovato nel file JSON");
                    return false;
                }

                // Verifica numero esatto di personaggi
                if (mappings.Count != EXPECTED_CHARACTER_COUNT)
                {
                    LogError($"Numero personaggi non valido: trovati {mappings.Count}, attesi {EXPECTED_CHARACTER_COUNT}");
                    return false;
                }

                // Verifica duplicati e validità
                var hashSet = new HashSet<uint>();
                int heroCount = 0, villainCount = 0;
                
                foreach (var mapping in mappings)
                {
                    var errors = mapping.GetValidationErrors();
                    if (errors.Count > 0)
                    {
                        foreach (var error in errors)
                        {
                            LogError($"Validazione fallita per {mapping.Name}: {error}");
                        }
                        continue;
                    }

                    uint hash = mapping.GetHashValue();
                    if (!hashSet.Add(hash))
                    {
                        LogError($"Hash duplicato trovato: {mapping.Hash} per {mapping.Name}");
                        continue;
                    }

                    if (mapping.Faction == "Hero") heroCount++;
                    else if (mapping.Faction == "Villain") villainCount++;
                }

                // Log statistiche fazioni
                Log($"📊 Distribuzione fazioni:");
                Log($"- Eroi: {heroCount}");
                Log($"- Villain: {villainCount}");

                List<string> validationErrors;
                if (mappings.All(m => m.IsValid(out validationErrors)))
                {
                    Log($"✅ {EXPECTED_CHARACTER_COUNT} personaggi caricati e validati con successo");
                    Notification.PostTicker($"~g~MarvelScript: {EXPECTED_CHARACTER_COUNT} personaggi pronti", true, false);
                    isMappingsLoaded = true; // Imposta il flag a true dopo il caricamento riuscito
                    return true;
                }
                else
                {
                    LogError("Alcuni personaggi non sono validi. Controlla il log per i dettagli.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore caricamento mappings: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cerca un personaggio Marvel in base all'hash del suo modello.
        /// </summary>
        /// <param name="hash">Hash del modello da cercare</param>
        /// <returns>Il personaggio trovato o null se non presente nei 106 ufficiali</returns>
        public static NpcModelMapping GetMappingByHash(uint hash)
        {
            try
            {
                // Carica i mapping se necessario e non ancora caricati
                if (!isMappingsLoaded && !LoadMappings()) 
                    return null;

                // Cerca il personaggio
                var mapping = mappings.FirstOrDefault(m => m.GetHashValue() == hash);
                
                // Log solo se non trovato (aiuta a identificare modelli mancanti) e solo la prima volta
                if (mapping == null && !unknownHashesLogged.Contains(hash))
                {
                    unknownHashesLogged.Add(hash);
                    // Log($"Hash non trovato nei 106 personaggi: 0x{hash:X8}");
                }

                return mapping;
            }
            catch (Exception ex)
            {
                LogError($"Errore ricerca hash 0x{hash:X8}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Restituisce la lista completa dei 106 personaggi Marvel.
        /// </summary>
        /// <returns>Lista dei personaggi o null se il caricamento è fallito</returns>
        public static List<NpcModelMapping> GetAllMappings()
        {
            try
            {
                // Carica i mapping se necessario e non ancora caricati
                if (!isMappingsLoaded && !LoadMappings()) 
                    return null;

                return mappings;
            }
            catch (Exception ex)
            {
                LogError($"Errore accesso mappings: {ex.Message}");
                return null;
            }
        }

        public static void RegisterDetectedHash(uint hash)
        {
            if (!detectedHashes.Contains(hash))
            {
                detectedHashes.Add(hash);
                firstDetectionTime[hash] = DateTime.Now;
                
                var mapping = GetMappingByHash(hash);
                if (mapping != null)
                {
                    // Log($"👁 Rilevato nuovo personaggio: {mapping.Name} ({mapping.Faction})");
                }
            }
        }

        public static void LogMissingCharacters()
        {
            // Metodo svuotato per non mostrare messaggi sui personaggi mancanti
            // Lo lasciamo vuoto in modo che possa essere chiamato senza generare errori
        }

        /// <summary>
        /// Aggiunge un messaggio al log con timestamp e prefisso.
        /// </summary>
        private static void Log(string message)
        {
            try
            {
                // Crea la directory se non esiste
                Directory.CreateDirectory(Path.GetDirectoryName(LOG_FILE));
                
                // Formatta il messaggio con timestamp e prefisso
                string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                
                // Append al file di log
                File.AppendAllText(LOG_FILE, logMessage + Environment.NewLine);
            }
            catch { /* Ignora errori di scrittura */ }
        }

        private static void LogError(string message)
        {
            Log($"❌ ERRORE: {message}");
            Notification.PostTicker($"~r~{message}", true, false);
        }
    }
} 