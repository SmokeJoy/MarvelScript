// FactionManager.cs - Sistema che gestisce le fazioni e il combattimento tra eroi e villain
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using System.Threading;

namespace MarvelScript
{
    /// <summary>
    /// Classe statica che gestisce le fazioni (Eroi e Villain) e il combattimento tra di loro.
    /// Si occupa di registrare i pedoni nelle fazioni appropriate e di farli combattere.
    /// </summary>
    public static class FactionManager
    {
        // ✅ Liste statiche per memorizzare i pedoni per ogni fazione
        private static readonly HashSet<Ped> heroes = new HashSet<Ped>();      // Lista degli eroi
        private static readonly HashSet<Ped> villains = new HashSet<Ped>();    // Lista dei villain
        
        // Sistema di lock per la gestione concorrente
        private static readonly object factionLock = new object();
        private static readonly object cleanupLock = new object();
        private static readonly SemaphoreSlim pedProcessingSemaphore = new SemaphoreSlim(1, 1);

        // Percorso del file di log dove vengono registrate le azioni
        private static readonly string LOG_FILE = @"scripts/MarvelScript_Log.txt";

        // Flag per tracciare se le relazioni sono state inizializzate
        private static bool relationshipsInitialized = false;

        // Aggiunto per tracciare il tempo dell'ultimo combattimento
        private static readonly Dictionary<Ped, DateTime> lastCombatTime = new Dictionary<Ped, DateTime>();
        private static readonly TimeSpan COMBAT_TIMEOUT = TimeSpan.FromSeconds(60); // Aumentato a 60 secondi

        // Log di combattimento molto rari
        private static int combatLogCooldown = 0;
        private const int COMBAT_LOG_INTERVAL = 15; // Scritte ridottissime
        private const float MAX_COMBAT_DISTANCE = 100f; // Aumentato a 100f per allinearlo con SCAN_RADIUS
        private static readonly Random random = new Random();
        
        // Riduzione log registrazione
        private static readonly HashSet<uint> registeredHeroesLogged = new HashSet<uint>();
        private static readonly HashSet<uint> registeredVillainsLogged = new HashSet<uint>();
        
        // Aggiornamento meno frequente
        private static int combatUpdateCooldown = 0;
        private const int COMBAT_UPDATE_INTERVAL = 3;       // Ridotto al minimo per massima reattività
        
        // Pulizia pedoni morti
        private static DateTime lastDeadPedsCleanup = DateTime.MinValue;
        private static readonly TimeSpan DEAD_PEDS_CLEANUP_INTERVAL = TimeSpan.FromMinutes(1);
        
        // Solo 1 combattimento per ciclo
        private const int MAX_COMBATS_PER_CYCLE = 1;
        // Abbassato soglia
        private const int THRESHOLD_FORCE_DELETE = 30; // Aumentato a 30 per adattarsi al maggior numero di pedoni

        private const float SCAN_RADIUS = 100f;             // Raggio di scansione molto ampio
        private const float HERO_DETECTION_RADIUS = 120f;   // Detection eroi aumentato per copertura totale
        private const float VILLAIN_ATTACK_RADIUS = 50f;
        private const int VILLAIN_CRIME_CHANCE = 30;     // Probabilità di commettere crimini
        private const int VILLAIN_ATTACK_CHANCE = 80;    // Probabilità di attaccare

        // Cooldown per azioni dei pedoni
        private static readonly Dictionary<Ped, DateTime> pedCooldowns = new Dictionary<Ped, DateTime>();
        private static readonly TimeSpan PED_ACTION_COOLDOWN = TimeSpan.FromSeconds(10);
        private static readonly HashSet<Ped> configuredPeds = new HashSet<Ped>();
        private static DateTime lastConfigCheck = DateTime.MinValue;
        private static readonly TimeSpan CONFIG_CHECK_INTERVAL = TimeSpan.FromSeconds(30);

        // Costanti per il batch processing
        private const int CLEANUP_BATCH_SIZE = 5;
        private const int MAX_ACTIONS_PER_TICK = 30;        // Aumentato ulteriormente per massimizzare le azioni
        private static int actionsThisTick = 0;

        // Sistema di fallback per crash
        private static int consecutiveCrashes = 0;
        private static DateTime lastCrashTime = DateTime.MinValue;
        private static bool inFallbackMode = false;
        private static readonly TimeSpan FALLBACK_DURATION = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Inizializza le relazioni tra le fazioni
        /// </summary>
        public static void InitializeRelationships()
        {
            if (relationshipsInitialized) return;

            try
            {
                // Crea i gruppi di relazione
                Function.Call(Hash.ADD_RELATIONSHIP_GROUP, "HEROES");    // Registra gruppo eroi
                Function.Call(Hash.ADD_RELATIONSHIP_GROUP, "VILLAINS");  // Registra gruppo villain
                
                int heroGroup = Function.Call<int>(Hash.GET_HASH_KEY, "HEROES");
                int villainGroup = Function.Call<int>(Hash.GET_HASH_KEY, "VILLAINS");
                int playerGroup = Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER");

                // Imposta le relazioni tra i gruppi (5 = odio massimo, 0 = neutrale)
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, heroGroup, villainGroup);    // Eroi odiano villain
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, villainGroup, heroGroup);    // Villain odiano eroi
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, villainGroup, playerGroup);  // Villain odiano player
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 0, heroGroup, playerGroup);     // Eroi neutrali con player

                relationshipsInitialized = true;
                Log("✅ Relazioni tra gruppi inizializzate correttamente");
            }
            catch (Exception ex)
            {
                LogError($"Errore inizializzazione relazioni: {ex.Message}");
            }
        }

        /// <summary>
        /// Restituisce il numero totale di pedoni registrati nelle fazioni
        /// </summary>
        /// <returns>Conteggio totale di eroi + villain</returns>
        public static int TotalFactionCount()
        {
            return heroes.Count + villains.Count;
        }

        /// <summary>
        /// Registra un pedone nella fazione corretta (Eroe o Villain)
        /// </summary>
        /// <param name="ped">Il pedone da registrare</param>
        /// <param name="faction">La fazione ("Hero" o "Villain")</param>
        /// <returns>true se la registrazione è riuscita, false altrimenti</returns>
        public static bool Register(Ped ped, string faction)
        {
            try
            {
                if (!IsValid(ped)) return false;

                // Rimuovi da altre fazioni prima
                heroes.Remove(ped);
                villains.Remove(ped);

                bool isHero = faction.Equals("Hero", StringComparison.OrdinalIgnoreCase);
                
                if (isHero)
                {
                    heroes.Add(ped);
                    Log($"✅ Registrato eroe: {GetPedInfo(ped)}");
                }
                else if (faction.Equals("Villain", StringComparison.OrdinalIgnoreCase))
                {
                    villains.Add(ped);
                    Log($"☠️ Registrato villain: {GetPedInfo(ped)}");
                }
                else
                {
                    return false;
                }

                // Configura solo se non è già stato configurato
                if (!configuredPeds.Contains(ped))
                {
                    ConfigureCombatBehavior(ped, isHero);
                    configuredPeds.Add(ped);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError($"Errore registrazione: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Rimuove i personaggi non più validi dalle liste e pulisce i pedoni morti
        /// </summary>
        public static void CleanupInvalidCharacters()
        {
            try
            {
                // Rimuove SOLO i ped completamente invalidi
                int removedHeroes = heroes.RemoveWhere(p => p == null || !p.Exists());
                int removedVillains = villains.RemoveWhere(p => p == null || !p.Exists());
                
                // Pulisci anche la cache dei configurati
                configuredPeds.RemoveWhere(p => p == null || !p.Exists());

                if (removedHeroes > 0 || removedVillains > 0)
                {
                    Log($"🧹 Rimossi {removedHeroes} eroi e {removedVillains} villain non validi");
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore pulizia: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifica se un pedone sta commettendo un crimine
        /// </summary>
        private static bool IsPedCommittingCrime(Ped ped)
        {
            if (!IsValid(ped)) return false;
            
            try
            {
                return ped.IsShooting || 
                       ped.IsInCombat || 
                       ped.IsRagdoll ||
                       ped.Health < ped.MaxHealth * 0.7f ||
                       Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, ped.Handle) ||
                       Function.Call<bool>(Hash.IS_PED_TRYING_TO_ENTER_A_LOCKED_VEHICLE, ped.Handle) ||
                       Function.Call<bool>(Hash.IS_PED_PERFORMING_STEALTH_KILL, ped.Handle) ||
                       Function.Call<bool>(Hash.IS_PED_SHOOTING, ped.Handle) ||
                       Function.Call<bool>(Hash.IS_PED_AIMING_FROM_COVER, ped.Handle) ||
                       Function.Call<bool>(Hash.IS_PED_RELOADING, ped.Handle);
            }
            catch (Exception ex)
            {
                LogError($"Errore IsPedCommittingCrime: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Simula un evento criminale per i villain
        /// </summary>
        private static void SimulateCrimeEvent(Ped villain)
        {
            if (!IsValid(villain)) return;

            try
            {
                var nearbyPeds = World.GetNearbyPeds(villain.Position, VILLAIN_ATTACK_RADIUS)
                    .Where(p => p != null && IsValid(p) && !villains.Contains(p) && !heroes.Contains(p))
                    .ToList();

                if (nearbyPeds.Count > 0)
                {
                    var target = nearbyPeds[random.Next(nearbyPeds.Count)];
                    
                    // Scelta casuale del tipo di crimine con probabilità diverse
                    int crimeType = random.Next(100);
                    
                    if (crimeType < 40) // 40% probabilità di sparare
                    {
                        Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, villain.Handle, true);
                        villain.Task.ShootAt(target, -1);
                    }
                    else if (crimeType < 70) // 30% probabilità di combattimento corpo a corpo
                    {
                        villain.Task.Combat(target);
                        Function.Call(Hash.SET_PED_COMBAT_RANGE, villain.Handle, 0); // Combattimento ravvicinato
                    }
                    else if (crimeType < 85) // 15% probabilità di rubare veicolo
                    {
                        var nearbyVehicle = World.GetNearbyVehicles(villain.Position, 20f)
                            .FirstOrDefault(v => v != null && v.Exists());
                        if (nearbyVehicle != null)
                        {
                            villain.Task.EnterVehicle(nearbyVehicle, VehicleSeat.Driver);
                            Function.Call(Hash.SET_PED_INTO_VEHICLE, villain.Handle, nearbyVehicle.Handle, -1);
                        }
                    }
                    else // 15% probabilità di lanciare oggetti/molotov
                    {
                        Function.Call(Hash.TASK_THROW_PROJECTILE, villain.Handle, 
                            target.Position.X, target.Position.Y, target.Position.Z);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore SimulateCrimeEvent: {ex.Message}");
            }
        }

        /// <summary>
        /// Configura un pedone per il combattimento, impostando tutti gli attributi necessari
        /// </summary>
        private static void ConfigureCombatBehavior(Ped ped, bool isHero)
        {
            if (!IsValid(ped)) return;

            try
            {
                // Super potenziamento base
                Function.Call(Hash.SET_PED_MAX_HEALTH, ped.Handle, 2000);           // Salute enormemente aumentata
                ped.Health = ped.MaxHealth;
                Function.Call(Hash.SET_PED_ARMOUR, ped.Handle, 100);               // Armatura massima
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped.Handle, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, ped.Handle, false);
                Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, ped.Handle, false);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, ped.Handle, false);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 281, false);   // FLEE_WHEN_INJURED
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 46, false);    // FLEE_WHEN_THREATENED
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 42, false);    // FLEE_WHEN_IN_VEHICLE
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 292, false);   // FLEE_FROM_COMBAT
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 128, false);   // PANIC_FROM_EVENTS

                // Attributi di combattimento potenziati
                Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped.Handle, 100);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);    // AlwaysFight
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);   // CanFightArmedPeds
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 1, true);    // CanUseCover
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 2, true);    // CanDoDrivebys
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 50, true);   // CanUseVehicles
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 0, true);    // SupportInCombat
                Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 3);           // Statico e letale
                Function.Call(Hash.SET_PED_COMBAT_RANGE, ped.Handle, 2);             // Far range
                Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 100);
                Function.Call(Hash.SET_PED_FIRING_PATTERN, ped.Handle, 0xC6EE6B4C);  // Pattern aggressivo

                // Configurazione specifica per tipo
                if (isHero)
                {
                    // Eroi: super reattivi e resistenti
                    Function.Call(Hash.SET_PED_SEEING_RANGE, ped.Handle, HERO_DETECTION_RADIUS);
                    Function.Call(Hash.SET_PED_HEARING_RANGE, ped.Handle, HERO_DETECTION_RADIUS);
                    Function.Call(Hash.SET_PED_VISUAL_FIELD_PERIPHERAL_RANGE, ped.Handle, HERO_DETECTION_RADIUS);
                    Function.Call(Hash.SET_PED_HIGHLY_PERCEPTIVE, ped.Handle, true);
                    Function.Call(Hash.SET_PED_VISUAL_FIELD_MIN_ANGLE, ped.Handle, -90f);
                    Function.Call(Hash.SET_PED_VISUAL_FIELD_MAX_ANGLE, ped.Handle, 90f);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 58, true);   // AlwaysFight
                    Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped.Handle, 3);            // Professional
                }
                else
                {
                    // Villain: ultra letali
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 52, true);   // APCs
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 1424, true); // BlindFireChance
                    Function.Call(Hash.SET_PED_COMBAT_RANGE, ped.Handle, 3);             // Maximum range
                    Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped.Handle, 3);           // Professional
                    Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 3);          // Statico
                    Function.Call(Hash.SET_PED_TARGET_LOSS_RESPONSE, ped.Handle, 2);     // Never lose target
                }

                // Persistenza e protezione finale
                Function.Call(Hash.SET_PED_STEERS_AROUND_PEDS, ped.Handle, false);
                Function.Call(Hash.SET_PED_STEERS_AROUND_OBJECTS, ped.Handle, false);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);

                Log($"⚙️ Configurato super comportamento per {(isHero ? "eroe" : "villain")}: {GetPedInfo(ped)}");
            }
            catch (Exception ex)
            {
                LogError($"Errore configurazione comportamento: {ex.Message}");
            }
        }

        /// <summary>
        /// Aggiorna il combattimento tra fazioni
        /// </summary>
        public static void UpdateCombat()
        {
            if (combatUpdateCooldown > 0)
            {
                combatUpdateCooldown--;
                return;
            }

            actionsThisTick = 0;
            combatUpdateCooldown = COMBAT_UPDATE_INTERVAL;

            try
            {
                // Pulizia solo per NPC completamente invalidi
                CleanupInvalidCharacters();

                // 1. Villain – Attaccano solo target non in combattimento
                foreach (var villain in villains.Where(IsAlive))
                {
                    if (actionsThisTick >= MAX_ACTIONS_PER_TICK) break;

                    // Se il villain è già in combattimento, mantieni il suo stato
                    if (villain.IsInCombat) continue;

                    var targets = World.GetNearbyPeds(villain.Position, SCAN_RADIUS)
                                     .Where(p => IsValid(p) && 
                                               !villains.Contains(p) && 
                                               p != villain && 
                                               !p.IsDead &&
                                               !p.IsInCombat)  // Solo target non in combattimento
                                     .OrderBy(p => villain.Position.DistanceTo(p.Position))
                                     .Take(3)
                                     .ToList();

                    if (targets.Any())
                    {
                        var target = targets[new Random().Next(targets.Count)];
                        villain.Task.ClearAllImmediately();
                        villain.Task.Combat(target);
                        actionsThisTick++;
                        
                        string targetType = heroes.Contains(target) ? "eroe" : "civile";
                        Log($"☠️ Villain {GetPedInfo(villain)} attacca {targetType} {GetPedInfo(target)}");
                    }
                }

                // 2. Hero – Intervengono contro minacce (IGNORA PLAYER)
                foreach (var hero in heroes.Where(IsAlive))
                {
                    if (actionsThisTick >= MAX_ACTIONS_PER_TICK) break;

                    // Se l'eroe è già in combattimento, mantieni il suo stato
                    if (hero.IsInCombat) continue;

                    var threats = World.GetNearbyPeds(hero.Position, HERO_DETECTION_RADIUS)
                                     .Where(p => 
                                         IsValid(p) &&
                                         !heroes.Contains(p) &&
                                         p != Game.Player.Character &&  // Ignora completamente il player
                                         !p.IsDead &&
                                         (
                                             p.IsInCombat ||
                                             Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ANY_PED, p.Handle) ||
                                             villains.Contains(p)
                                         ))
                                     .OrderBy(p => hero.Position.DistanceTo(p.Position))
                                     .Take(3)
                                     .ToList();

                    if (threats.Any())
                    {
                        var threat = threats[new Random().Next(threats.Count)];
                        hero.Task.ClearAllImmediately();
                        hero.Task.Combat(threat);
                        actionsThisTick++;
                        
                        string threatType = villains.Contains(threat) ? "villain" : 
                                          threat.IsInCombat ? "combattente" : "aggressore";
                        
                        Log($"🛡️ Hero {GetPedInfo(hero)} interviene contro {threatType} {GetPedInfo(threat)}");
                    }
                }

                Log($"🔄 Tick completato → {actionsThisTick} azioni");
            }
            catch (Exception ex)
            {
                LogError($"Errore UpdateCombat: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifica se un pedone è valido (esiste, non è morto, non è il giocatore)
        /// </summary>
        /// <param name="ped">Il pedone da verificare</param>
        /// <returns>true se il pedone è valido, false altrimenti</returns>
        private static bool IsValid(Ped ped)
        {
            return ped != null && ped.Exists() && !ped.IsDead && !ped.IsPlayer;
        }
        
        /// <summary>
        /// Verifica se un pedone è vivo e utilizzabile per il combattimento
        /// </summary>
        private static bool IsAlive(Ped ped)
        {
            return IsValid(ped) && !ped.IsRagdoll;
        }

        private static string GetPedInfo(Ped ped)
        {
            if (!IsValid(ped)) return "PED_INVALIDO";
            return $"[0x{ped.Model.Hash:X8}]";
        }

        private static bool IsStable(Ped ped)
        {
            if (!IsValid(ped)) return false;

            try
            {
                // Debug avanzato per stati instabili
                if (ped.IsRagdoll)
                    Log($"🔍 {GetPedInfo(ped)} → RAGDOLL");
                if (!ped.IsOnFoot)
                    Log($"🔍 {GetPedInfo(ped)} → !IsOnFoot");
                if (Function.Call<bool>(Hash.IS_PED_GETTING_UP, ped.Handle))
                    Log($"🔍 {GetPedInfo(ped)} → GETTING_UP");
                if (Function.Call<bool>(Hash.IS_PED_FALLING, ped.Handle))
                    Log($"🔍 {GetPedInfo(ped)} → FALLING");
                if (Function.Call<bool>(Hash.IS_PED_CLIMBING, ped.Handle))
                    Log($"🔍 {GetPedInfo(ped)} → CLIMBING");
                if (Function.Call<bool>(Hash.IS_PED_JUMPING_OUT_OF_VEHICLE, ped.Handle))
                    Log($"🔍 {GetPedInfo(ped)} → JUMPING_OUT_OF_VEHICLE");
                if (Function.Call<bool>(Hash.IS_PED_DIVING, ped.Handle))
                    Log($"🔍 {GetPedInfo(ped)} → DIVING");
                
                // Controlli completi di stabilità
                return !ped.IsDead &&
                       !ped.IsRagdoll &&
                       !Function.Call<bool>(Hash.IS_PED_GETTING_UP, ped.Handle) &&
                       !Function.Call<bool>(Hash.IS_PED_FALLING, ped.Handle) &&
                       !Function.Call<bool>(Hash.IS_PED_CLIMBING, ped.Handle) &&
                       !Function.Call<bool>(Hash.IS_PED_JUMPING_OUT_OF_VEHICLE, ped.Handle) &&
                       !Function.Call<bool>(Hash.IS_PED_DIVING, ped.Handle) &&
                       ped.IsOnFoot;
            }
            catch (Exception ex)
            {
                LogError($"Errore in IsStable: {ex.Message}");
                return false;
            }
        }

        private static void StartCombat(Ped attacker, Ped target)
        {
            try
            {
                if (!IsAlive(attacker) || !IsAlive(target)) return;

                // Verifica timeout combattimento
                if (lastCombatTime.ContainsKey(attacker))
                {
                    if (DateTime.Now - lastCombatTime[attacker] < COMBAT_TIMEOUT)
                        return;
                }

                // Usa TaskInvoker.Combat invece del metodo obsoleto
                attacker.Task.Combat(target);
                lastCombatTime[attacker] = DateTime.Now;

                // Log combattimento rimosso per ridurre scritture su disco
                // if (!attacker.IsInCombat && combatLogCooldown == 0)
                // {
                //     var attackerInfo = NpcModelMappingLoader.GetMappingByHash((uint)attacker.Model.Hash);
                //     var targetInfo = NpcModelMappingLoader.GetMappingByHash((uint)target.Model.Hash);
                // 
                //     if (attackerInfo != null && targetInfo != null)
                //     {
                //         Log($"⚔️ Combattimento: {attackerInfo.Name} vs {targetInfo.Name}");
                //     }
                // }
            }
            catch (Exception ex)
            {
                LogError($"Errore avvio combattimento: {ex.Message}");
            }
        }

        private static void LogCombatStats()
        {
            try
            {
                int activeHeroes = heroes.Count(h => IsAlive(h) && h.IsInCombat);
                int activeVillains = villains.Count(v => IsAlive(v) && v.IsInCombat);
                int totalRegistered = heroes.Count + villains.Count;

                // Log solo se ci sono effettivamente combattenti attivi
                if (activeHeroes + activeVillains > 0)
                {
                    Log($"📊 Combattimenti: {activeHeroes + activeVillains} in corso | Totale pedoni: {totalRegistered}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore log statistiche: {ex.Message}");
            }
        }

        /// <summary>
        /// Aggiunge un messaggio al file di log
        /// </summary>
        /// <param name="message">Il messaggio da loggare</param>
        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LOG_FILE, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [FactionManager] {message}\n");
            }
            catch { /* Ignora errori di logging */ }
        }

        private static void LogError(string message)
        {
            Log($"ERRORE: {message}");
        }

        /// <summary>
        /// Imposta lo stato attivo/inattivo della guerra.
        /// Svuota le liste dei personaggi registrati quando disattivata.
        /// </summary>
        public static void SetWarActive(bool active)
        {
            if (!active)
            {
                // Pulizia intensiva: elimina completamente tutti i pedoni attivi
                foreach (var hero in heroes.ToList())
                {
                    if (hero != null && hero.Exists())
                    {
                        hero.Delete();
                    }
                }
                
                foreach (var villain in villains.ToList())
                {
                    if (villain != null && villain.Exists())
                    {
                        villain.Delete();
                    }
                }
                
                // Se disattivata, svuota le liste
                heroes.Clear();
                villains.Clear();
                lastCombatTime.Clear(); // Pulisce anche i timer di combattimento
                
                // Pulisce eventuali pedoni in combattimento
                ClearAllCombatTasks();
                
                Log("👋 Pulizia completa: tutti i personaggi rimossi dal mondo");
            }
            else
            {
                // Se riattivata, inizializza le relazioni (per sicurezza)
                InitializeRelationships();
                Log("🚀 Sistema inizializzato: personaggi verranno registrati nuovamente");
            }
        }
        
        /// <summary>
        /// Pulisce tutti i task di combattimento per pedoni esistenti
        /// </summary>
        private static void ClearAllCombatTasks()
        {
            try
            {
                // Pulisce qualsiasi combattimento in corso per gli eroi
                foreach (var hero in heroes.ToList())
                {
                    if (hero != null && hero.Exists())
                    {
                        hero.Task.ClearAll();
                    }
                }
                
                // Pulisce qualsiasi combattimento in corso per i villain
                foreach (var villain in villains.ToList())
                {
                    if (villain != null && villain.Exists())
                    {
                        villain.Task.ClearAll();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore pulizia task combattimento: {ex.Message}");
            }
        }

        private static void LogPedState(Ped ped, string fase)
        {
            try
            {
                if (!IsValid(ped)) return;

                var info = new[]
                {
                    $"Fase: {fase}",
                    $"Model: 0x{ped.Model.Hash:X8}",
                    $"Pos: {ped.Position}",
                    $"InCombat: {ped.IsInCombat}",
                    $"Ragdoll: {ped.IsRagdoll}",
                    $"Tasks: {(ped.Task != null ? "Active" : "None")}",
                    $"Health: {ped.Health}/{ped.MaxHealth}",
                    $"RelGroup: {Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle)}",
                    $"IsFleeing: {Function.Call<bool>(Hash.IS_PED_FLEEING, ped.Handle)}",
                    $"BlockEvents: {ped.BlockPermanentEvents}"
                };

                Log($"🔍 STATO PED [{GetPedInfo(ped)}]: {string.Join(" | ", info)}");
            }
            catch (Exception ex)
            {
                LogError($"Errore LogPedState: {ex.Message}");
            }
        }

        private static void HandlePotentialCrash()
        {
            consecutiveCrashes++;
            
            if (consecutiveCrashes >= 3)
            {
                // Attiva modalità fallback
                inFallbackMode = true;
                lastCrashTime = DateTime.Now;
                
                // Pulizia di emergenza
                ClearAllCombatTasks();
                CleanupInvalidCharacters();
                
                // Reset contatori e cooldown
                actionsThisTick = 0;
                combatUpdateCooldown = COMBAT_UPDATE_INTERVAL;
                
                Log("⚠️ Modalità fallback attivata: sistema in pausa per 30 secondi");
                Notification.PostTicker("~r~Sistema in modalità fallback per 30 secondi", true, false);
            }
        }
    }
} 