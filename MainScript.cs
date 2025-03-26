using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using System.Linq;

namespace MarvelScript
{
    /// <summary>
    /// Classe principale dello script. Scansiona pedoni e li registra.
    /// </summary>
    public class MainScript : Script
    {
        private bool isInitialized = false;
        private DateTime lastMissingCheck = DateTime.MinValue;
        private readonly TimeSpan missingCheckInterval = TimeSpan.FromMinutes(5);
        private readonly string LOG_FILE = @"scripts/MarvelScript_Log.txt";

        // Limiti e configurazioni
        private HashSet<uint> registeredModels = new HashSet<uint>();

        private int scanCooldown = 0;
        private const int SCAN_INTERVAL = 30;    // Aumentato a 30 tick (~75 secondi)
        private const float SCAN_RADIUS = 80f;   // Mantenuto a 80f per bilanciare il carico
        private const int MAX_PEDS_PER_SCAN = 10; // Aumentato a 10 per migliorare la copertura
        private const int MAX_TOTAL_PEDS = 30;   // Mantenuto a 30 per stabilità
        private const float CRITICAL_DISTANCE = 150f; // Distanza oltre la quale i pedoni vengono rimossi
        
        private Random random = new Random();

        public MainScript()
        {
            try
            {
                // Log di avvio
                File.WriteAllText(LOG_FILE, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Avvio MarvelScript...\n");

                // Evento Tick
                Tick += OnTick;
                Interval = 2500; // 2,5 secondi (puoi aumentare a 3000-4000 se serve)

                if (NpcModelMappingLoader.LoadMappings())
                {
                    isInitialized = true;
                    Notification.PostTicker("~g~MarvelScript caricato.", true, false);
                    Log("Inizializzato.");
                }
                else
                {
                    LogError("Errore inizializzazione sistema Marvel!");
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore critico costruttore: {ex.Message}");
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!isInitialized) return;

            try
            {
                // Scansione pedoni (meno frequente)
                if (scanCooldown <= 0)
                {
                    ScanAndRegisterNearbyPeds();
                    scanCooldown = SCAN_INTERVAL;
                }
                else
                {
                    scanCooldown--;
                }

                try
                {
                    // Aggiorna comportamenti e combattimenti (priorità alta)
                    FactionManager.UpdateCombat();
                }
                catch (Exception combatEx)
                {
                    LogError($"Errore aggiornamento comportamenti: {combatEx.Message}");
                }

                try
                {
                    // Pulizia e ottimizzazione (bassa priorità)
                    FactionManager.CleanupInvalidCharacters();
                }
                catch (Exception cleanupEx)
                {
                    LogError($"Errore pulizia caratteri: {cleanupEx.Message}");
                }

                // Check personaggi mancanti (molto bassa priorità)
                if (DateTime.Now - lastMissingCheck > missingCheckInterval)
                {
                    try
                    {
                        NpcModelMappingLoader.LogMissingCharacters();
                        lastMissingCheck = DateTime.Now;
                    }
                    catch (Exception checkEx)
                    {
                        LogError($"Errore check personaggi: {checkEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore critico OnTick: {ex.Message}");
                // Resetta i cooldown per evitare blocchi
                scanCooldown = 0;
                lastMissingCheck = DateTime.MinValue;
            }
        }

        private void ScanAndRegisterNearbyPeds()
        {
            try
            {
                if (Game.Player?.Character == null) return;

                // Verifica limite totale
                if (FactionManager.TotalFactionCount() >= MAX_TOTAL_PEDS)
                {
                    Log($"⚠️ Limite pedoni raggiunto ({MAX_TOTAL_PEDS})");
                    return;
                }

                var playerPos = Game.Player.Character.Position;
                
                // Ottimizza la scansione
                var peds = World.GetNearbyPeds(playerPos, SCAN_RADIUS)
                    .Where(p => p != null && p.Exists() && !p.IsPlayer)
                    .OrderByDescending(p => GetPedPriority(p))
                    .Take(MAX_PEDS_PER_SCAN)
                    .ToList();

                if (peds.Count == 0)
                {
                    return;
                }

                // Log pre-scansione
                Log($"🔍 Scansione {peds.Count} pedoni vicini");

                int processedCount = 0;
                int registeredCount = 0;

                foreach (var ped in peds)
                {
                    try
                    {
                        // Gestione pedoni morti
                        if (ped.IsDead)
                        {
                            CleanupDeadPed(ped);
                            continue;
                        }

                        // Registra pedone usando solo il mapping JSON
                        if (RegisterPedInFaction(ped))
                        {
                            registeredCount++;
                        }

                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        LogError($"Errore processando pedone: {ex.Message}");
                    }
                }

                // Log risultati
                if (processedCount > 0)
                {
                    Log($"📊 Scansione completata: {registeredCount} registrati su {processedCount} processati");
                }

                // Ottimizzazione pedoni distanti
                if (FactionManager.TotalFactionCount() < MAX_TOTAL_PEDS)
                {
                    OptimizePedManagement();
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore ScanAndRegisterNearbyPeds: {ex.Message}");
            }
        }

        private float GetPedPriority(Ped ped)
        {
            if (Game.Player?.Character == null || ped == null) return 0f;
            float distance = ped.Position.DistanceTo(Game.Player.Character.Position);
            return 1.0f / (1.0f + distance); // Più vicino = priorità più alta
        }

        private void OptimizePedManagement()
        {
            try
            {
                if (Game.Player?.Character == null) return;
                var playerPos = Game.Player.Character.Position;

                // Ridotto il raggio di scansione e limitato il numero di pedoni processati
                var distantPeds = World.GetNearbyPeds(playerPos, SCAN_RADIUS + 10f)
                    .Where(p => p != null && p.Exists() && !p.IsPlayer)
                    .Take(3)
                    .ToList();

                foreach (var ped in distantPeds)
                {
                    if (random.Next(100) < 70) // 70% chance di rimozione
                    {
                        ped.MarkAsNoLongerNeeded();
                    }
                }

                // Log ottimizzazione solo se effettivamente rimossi pedoni
                if (distantPeds.Count > 0)
                {
                    Log($"🔄 Ottimizzati {distantPeds.Count} pedoni distanti");
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore ottimizzazione pedoni: {ex.Message}");
            }
        }

        private void CleanupDeadPed(Ped ped)
        {
            try
            {
                if (ped != null && ped.Exists() && ped.IsDead)
                {
                    // 90% Delete
                    if (random.Next(100) < 90)
                    {
                        ped.Delete();
                    }
                    else
                    {
                        ped.MarkAsNoLongerNeeded();
                    }
                }
            }
            catch { /* Ignora errori */ }
        }

        private bool RegisterPedInFaction(Ped ped)
        {
            try
            {
                if (ped == null || !ped.Exists())
                {
                    Log("❌ Pedone non valido per registrazione (null o non esistente)");
                    return false;
                }

                if (FactionManager.TotalFactionCount() >= MAX_TOTAL_PEDS)
                {
                    Log("❌ Limite pedoni raggiunto, impossibile registrare");
                    return false;
                }

                uint hash = (uint)ped.Model.Hash;
                var mapping = NpcModelMappingLoader.GetMappingByHash(hash);
                
                if (mapping == null)
                {
                    // Log solo se è la prima volta che vediamo questo hash
                    if (registeredModels.Add(hash))
                    {
                        Log($"❌ Modello non presente in npc_model_mapping.json: Hash 0x{hash:X8}");
                    }
                    return false;
                }

                // Verifica che la fazione sia valida
                if (string.IsNullOrEmpty(mapping.Faction) || 
                    (mapping.Faction != "Hero" && mapping.Faction != "Villain"))
                {
                    Log($"❌ Fazione non valida per {mapping.Name}: {mapping.Faction}");
                    return false;
                }

                // Log pre-registrazione
                Log($"🔄 Tentativo registrazione {mapping.Name} come {mapping.Faction} (PowerType: {mapping.PowerType})");

                // Registra nella fazione appropriata
                bool registered = FactionManager.Register(ped, mapping.Faction);

                // Log risultato
                if (registered)
                {
                    Log($"✅ {mapping.Name} registrato come {mapping.Faction}");
                    
                    // Registra il modello come visto
                    registeredModels.Add(hash);
                    
                    return true;
                }
                else
                {
                    Log($"❌ Registrazione fallita per {mapping.Name} - Possibile duplicato o pedone non valido");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogError($"Errore RegisterPedInFaction: {ex.Message}");
                return false;
            }
        }

        private void Log(string msg)
        {
            try
            {
                File.AppendAllText(LOG_FILE, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [MainScript] {msg}\n");
            }
            catch { /* no-op */ }
        }

        private void LogError(string msg)
        {
            Log($"ERRORE: {msg}");
            Notification.PostTicker($"~r~{msg}", true, false);
        }
    }
} 