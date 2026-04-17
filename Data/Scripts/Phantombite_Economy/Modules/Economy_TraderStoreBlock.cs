using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions; // For StoreItemTypes
using VRageMath;
using VRage.Utils;
using PhantombiteEconomy.Core;

namespace PhantombiteEconomy.Modules
{
    /// <summary>
    /// M04 - TraderStoreBlock
    /// Verwaltet TraderStore Blocks mit Full Trading (Buy + Sell)
    /// 
    /// Features (Phase 1 - Custom Data Only):
    /// - Auto-Discovery von TraderStore Blocks
    /// - Custom Data Template Deployment
    /// - Custom Data Validation
    /// - Error Mode: Block OFF + DangerZone Display
    /// - Regular Polling (alle 5 Sekunden)
    /// 
    /// STANDALONE: Läuft komplett unabhängig von M05!
    /// ERROR ISOLATION: Jeder Block komplett isoliert - ein Fehler betrifft nur EINEN Block!
    /// </summary>
    public class TraderStoreBlockModule : IModule
    {
        public string ModuleName => "Economy_TraderStore";

        // Logger von Economy_Command
        private EconomyCommandModule _logger;
        private const string MODULE = "Economy_TraderStore";

        public void SetLogger(EconomyCommandModule logger) { _logger = logger; }

        // Block Detection
        private const string BLOCK_SUBTYPE = "TraderStore";
        private List<IMyStoreBlock> _storeBlocks = new List<IMyStoreBlock>();

        // Polling
        private int _updateCounter = 0;
        private const int UPDATE_INTERVAL = 300;
        private bool _forceMaintenanceOnce = false;

        // State
        private bool _initialized = false;

        // Wiederverwendbare Listen — vermeidet GC-Druck im Poll-Pfad
        private HashSet<string>                                        _reuseCategories   = new HashSet<string>();
        private Dictionary<string, CatalogItem>                        _reuseCatalog      = new Dictionary<string, CatalogItem>();
        private List<VRage.Game.ModAPI.Ingame.MyInventoryItem>         _reuseInvItems     = new List<VRage.Game.ModAPI.Ingame.MyInventoryItem>();
        private List<VRage.Game.ModAPI.IMyStoreItem>                   _reuseStoreItems   = new List<VRage.Game.ModAPI.IMyStoreItem>();
        private StoreConfig                                            _reuseConfig       = new StoreConfig();

        public void Init()
        {
            try
            {
                _logger?.Debug(MODULE, "Initializing...");

                if (!MyAPIGateway.Multiplayer.IsServer)
                {
                    MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] TraderStoreBlock: Client detected - Module disabled");
                    return;
                }

                // Cache initial befüllen
                FindStoreBlocks();

                // Events für automatische Cache-Pflege
                MyAPIGateway.Entities.OnEntityAdd    += OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;

                _initialized = true;
                _logger?.Debug(MODULE, $"Initialized — {_storeBlocks.Count} blocks cached");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR in Init:\n{ex}");
            }
        }

        public void Update()
        {
            if (!_initialized || !MyAPIGateway.Multiplayer.IsServer)
                return;

            try
            {
                _updateCounter++;
                
                if (_updateCounter >= UPDATE_INTERVAL)
                {
                    _updateCounter = 0;
                    PollStoreBlocks();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR in Update:\n{ex}");
            }
        }

        public void SaveData()
        {
            // No data to save yet
        }

        /// <summary>
        /// Erzwingt sofortigen Poll aller Blöcke mit einmaligem Maintenance-Override
        /// Wird von !sem forcerefresh aufgerufen
        /// </summary>
        public void ForceRefresh()
        {
            if (!_initialized)
                return;

            _forceMaintenanceOnce = true;
            PollStoreBlocks();
            _logger?.Debug(MODULE, "ForceRefresh executed");
        }

        public void Close()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] TraderStoreBlock: Closing...");

                if (MyAPIGateway.Entities != null)
                {
                    MyAPIGateway.Entities.OnEntityAdd    -= OnEntityAdd;
                    MyAPIGateway.Entities.OnEntityRemove -= OnEntityRemove;
                }

                _storeBlocks.Clear();
                _initialized = false;

                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] TraderStoreBlock: Closed");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR in Close:\n{ex}");
            }
        }

        #region Block Detection & Polling

        /// <summary>
        /// Pollt alle Store Blocks und prüft Custom Data
        /// </summary>
        private void PollStoreBlocks()
        {
            try
            {
                // Cache nutzen — kein World-Scan mehr
                foreach (var block in _storeBlocks)
                {
                    if (block == null || block.Closed)
                        continue;

                    ProcessBlock(block);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR in PollStoreBlocks:\n{ex}");
            }
            finally
            {
                _forceMaintenanceOnce = false;
            }
        }

        /// <summary>
        /// Verarbeitet einen einzelnen Block (ISOLIERT!)
        /// </summary>
        private void ProcessBlock(IMyStoreBlock block)
        {
            try
            {
                string customData = block.CustomData;

                // STEP 1: CustomData leer? → Template deployen
                if (string.IsNullOrWhiteSpace(customData))
                {
                    DeployCustomDataTemplate(block);
                    // WICHTIG: Nach Deploy das Template sofort verarbeiten!
                    customData = block.CustomData; // Re-read CustomData
                }

                // STEP 2: CustomData validieren
                ValidationResult result = ValidateCustomData(customData);

                if (!result.IsValid)
                {
                    // ERROR: NUR dieser Block geht in Error Mode!
                    EnterErrorMode(block, result.ErrorMessage);
                    return;
                }

                // STEP 3: CustomData parsen
                StoreConfig config = ParseCustomData(customData);

                // STEP 4: Sync Items from Catalogs
                // Orders are created together with items in SyncCategoryItems()
                SyncStoreItems(block, config);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR processing '{block.CustomName}':\n{ex}");
                EnterErrorMode(block, "Internal processing error");
            }
        }

        /// <summary>
        /// Findet alle TraderStore Blocks im Spiel
        /// </summary>
        private void FindStoreBlocks()
        {
            try
            {
                _storeBlocks.Clear();

                HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities)
                {
                    IMyCubeGrid grid = entity as IMyCubeGrid;
                    if (grid == null)
                        continue;

                    List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks, b => IsTraderStoreBlock(b));

                    foreach (var slimBlock in blocks)
                    {
                        IMyStoreBlock storeBlock = slimBlock.FatBlock as IMyStoreBlock;
                        if (storeBlock != null && !_storeBlocks.Contains(storeBlock))
                        {
                            _storeBlocks.Add(storeBlock);
                        }
                    }
                }

                if (_storeBlocks.Count > 0)
                {
                    _logger?.Trace(MODULE, $"Found {_storeBlocks.Count} TraderStore blocks");
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR in FindStoreBlocks:\n{ex}");
            }
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            try
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null) return;

                grid.OnBlockAdded += OnBlockAdded;

                // Bereits vorhandene Blöcke auf dem neuen Grid prüfen
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks, b => IsTraderStoreBlock(b));
                foreach (var slim in blocks)
                {
                    var storeBlock = slim.FatBlock as IMyStoreBlock;
                    if (storeBlock != null && !_storeBlocks.Contains(storeBlock))
                        _storeBlocks.Add(storeBlock);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR in OnEntityAdd:\n{ex}");
            }
        }

        private void OnEntityRemove(IMyEntity entity)
        {
            try
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null) return;

                grid.OnBlockAdded -= OnBlockAdded;
                _storeBlocks.RemoveAll(b => b.CubeGrid == grid);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR in OnEntityRemove:\n{ex}");
            }
        }

        private void OnBlockAdded(IMySlimBlock slim)
        {
            try
            {
                if (!IsTraderStoreBlock(slim)) return;
                var storeBlock = slim.FatBlock as IMyStoreBlock;
                if (storeBlock != null && !_storeBlocks.Contains(storeBlock))
                    _storeBlocks.Add(storeBlock);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR in OnBlockAdded:\n{ex}");
            }
        }

        /// <summary>
        /// Prüft ob ein Block ein TraderStore ist
        /// </summary>
        private bool IsTraderStoreBlock(IMySlimBlock block)
        {
            if (block?.FatBlock == null)
                return false;

            return block.FatBlock is IMyStoreBlock && 
                   block.BlockDefinition.Id.SubtypeName.Contains(BLOCK_SUBTYPE);
        }

        #endregion

        #region Custom Data Management

        /// <summary>
        /// Deployed Custom Data Template in Block
        /// </summary>
        private void DeployCustomDataTemplate(IMyStoreBlock block)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine("# TRADER STORE - Configuration");
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine();
                
                sb.AppendLine("[StoreConfig]");
                sb.AppendLine("# Maintenance Mode (per Block!)");
                sb.AppendLine("# true  = Items spawn IMMEDIATELY (for Testing/Configuration)");
                sb.AppendLine("# false = Items spawn ONLY at Refresh Events (normal operation)");
                sb.AppendLine("Maintenance=false");
                sb.AppendLine();
                
                sb.AppendLine("[Sell_Categories]");
                sb.AppendLine("Sell_Ore=false");
                sb.AppendLine("Sell_Ingots=false");
                sb.AppendLine("Sell_Components=false");
                sb.AppendLine("Sell_Tools=false");
                sb.AppendLine("Sell_Weapons=false");
                sb.AppendLine("Sell_Ammo=false");
                sb.AppendLine("Sell_Food=false");
                sb.AppendLine("Sell_Seeds=false");
                sb.AppendLine("Sell_Consumables=false");
                sb.AppendLine("Sell_Prototech=false");
                sb.AppendLine();
                
                sb.AppendLine("[Sell_Whitelist]");
                sb.AppendLine("#Beispiele Unten");
                sb.AppendLine();
                
                sb.AppendLine("[Sell_Blacklist]");
                sb.AppendLine("#Beispiele Unten");
                sb.AppendLine();
                
                sb.AppendLine("[Buy_Categories]");
                sb.AppendLine("Buy_Ore=false");
                sb.AppendLine("Buy_Ingots=false");
                sb.AppendLine("Buy_Components=false");
                sb.AppendLine("Buy_Tools=false");
                sb.AppendLine("Buy_Weapons=false");
                sb.AppendLine("Buy_Ammo=false");
                sb.AppendLine("Buy_Food=false");
                sb.AppendLine("Buy_Seeds=false");
                sb.AppendLine("Buy_Consumables=false");
                sb.AppendLine("Buy_Prototech=false");
                sb.AppendLine();
                
                sb.AppendLine("[Buy_Whitelist]");
                sb.AppendLine("#Beispiele Unten");
                sb.AppendLine();
                
                sb.AppendLine("[Buy_Blacklist]");
                sb.AppendLine("#Beispiele Unten");
                sb.AppendLine();
                
                sb.AppendLine("[White & Blacklist Beispiele]");
                sb.AppendLine("#Ore:Ice");
                sb.AppendLine("#Ingots:Iron");
                sb.AppendLine("#Components:SteelPlate");
                sb.AppendLine("#Tools:Welder");
                sb.AppendLine("#Weapons:BasicHandHeldLauncher");
                sb.AppendLine("#Ammo:Missile200mm");
                sb.AppendLine("#Food:MealPack_Chili");
                sb.AppendLine("#Seeds:WheatSeeds");
                sb.AppendLine("#Consumables:Medkit");
                sb.AppendLine("#Prototech:PrototechCapacitor");

                block.CustomData = sb.ToString();
                
                _logger?.Debug(MODULE, $"Deployed CustomData template to '{block.CustomName}'");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR in DeployCustomDataTemplate:\n{ex}");
            }
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validation Result
        /// </summary>
        private class ValidationResult
        {
            public bool IsValid;
            public string ErrorMessage;

            public ValidationResult(bool isValid, string errorMessage = "")
            {
                IsValid = isValid;
                ErrorMessage = errorMessage;
            }
        }

        /// <summary>
        /// Store Configuration from CustomData
        /// </summary>
        private class StoreConfig
        {
            public bool Maintenance = false;

            public bool Sell_Ore, Sell_Ingots, Sell_Components, Sell_Tools;
            public bool Sell_Weapons, Sell_Ammo, Sell_Food, Sell_Seeds;
            public bool Sell_Consumables, Sell_Prototech;

            public bool Buy_Ore, Buy_Ingots, Buy_Components, Buy_Tools;
            public bool Buy_Weapons, Buy_Ammo, Buy_Food, Buy_Seeds;
            public bool Buy_Consumables, Buy_Prototech;

            public List<string> Sell_Whitelist = new List<string>();
            public List<string> Sell_Blacklist = new List<string>();
            public List<string> Buy_Whitelist  = new List<string>();
            public List<string> Buy_Blacklist  = new List<string>();

            public void Reset()
            {
                Maintenance = false;
                Sell_Ore = Sell_Ingots = Sell_Components = Sell_Tools = false;
                Sell_Weapons = Sell_Ammo = Sell_Food = Sell_Seeds = false;
                Sell_Consumables = Sell_Prototech = false;
                Buy_Ore = Buy_Ingots = Buy_Components = Buy_Tools = false;
                Buy_Weapons = Buy_Ammo = Buy_Food = Buy_Seeds = false;
                Buy_Consumables = Buy_Prototech = false;
                Sell_Whitelist.Clear();
                Sell_Blacklist.Clear();
                Buy_Whitelist.Clear();
                Buy_Blacklist.Clear();
            }
        }
        /// <summary>
        /// Catalog Item Entry
        /// </summary>
        private class CatalogItem
        {
            public string ItemName;
            public string TypeId;
            public string SubtypeId;
            public int Amount;
            public int Price;
        }

        /// <summary>
        /// Validiert Custom Data
        /// </summary>
        private ValidationResult ValidateCustomData(string customData)
        {
            try
            {
                // Check required sections
                if (!customData.Contains("[Sell_Categories]"))
                    return new ValidationResult(false, "[Sell_Categories] section missing");

                if (!customData.Contains("[Sell_Whitelist]"))
                    return new ValidationResult(false, "[Sell_Whitelist] section missing");

                if (!customData.Contains("[Sell_Blacklist]"))
                    return new ValidationResult(false, "[Sell_Blacklist] section missing");

                if (!customData.Contains("[Buy_Categories]"))
                    return new ValidationResult(false, "[Buy_Categories] section missing");

                if (!customData.Contains("[Buy_Whitelist]"))
                    return new ValidationResult(false, "[Buy_Whitelist] section missing");

                if (!customData.Contains("[Buy_Blacklist]"))
                    return new ValidationResult(false, "[Buy_Blacklist] section missing");

                // Validate Boolean values
                string[] lines = customData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string currentSection = "";

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        continue;
                    }

                    // Only validate in Category sections
                    if (currentSection != "Sell_Categories" && currentSection != "Buy_Categories")
                        continue;

                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = trimmed.Substring(0, equalsIndex).Trim();
                    string value = trimmed.Substring(equalsIndex + 1).Trim();

                    // Check if value is valid boolean
                    if (value.ToLower() != "true" && value.ToLower() != "false")
                    {
                        return new ValidationResult(false, $"Invalid boolean value for '{key}': '{value}' (must be 'true' or 'false')");
                    }
                }

                return new ValidationResult(true);
            }
            catch (Exception ex)
            {
                return new ValidationResult(false, $"Validation exception: {ex.Message}");
            }
        }

        #endregion

        #region Custom Data Parsing

        /// <summary>
        /// Parst CustomData in StoreConfig
        /// </summary>
        private StoreConfig ParseCustomData(string customData)
        {
            _reuseConfig.Reset();
            string currentSection = "";

            try
            {
                string[] lines = customData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        continue;
                    }

                    // Whitelist/Blacklist vor dem =-Check prüfen (Einträge haben kein =)
                    if (currentSection == "Sell_Whitelist")
                    {
                        _reuseConfig.Sell_Whitelist.Add(trimmed);
                        continue;
                    }
                    else if (currentSection == "Sell_Blacklist")
                    {
                        _reuseConfig.Sell_Blacklist.Add(trimmed);
                        continue;
                    }
                    else if (currentSection == "Buy_Whitelist")
                    {
                        _reuseConfig.Buy_Whitelist.Add(trimmed);
                        continue;
                    }
                    else if (currentSection == "Buy_Blacklist")
                    {
                        _reuseConfig.Buy_Blacklist.Add(trimmed);
                        continue;
                    }

                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = trimmed.Substring(0, equalsIndex).Trim();
                    string value = trimmed.Substring(equalsIndex + 1).Trim();

                    // Parse StoreConfig
                    if (currentSection == "StoreConfig")
                    {
                        if (key == "Maintenance")
                        {
                            bool boolValue;
                            bool.TryParse(value, out boolValue);
                            _reuseConfig.Maintenance = boolValue;
                        }
                    }
                    // Parse Sell_Categories
                    else if (currentSection == "Sell_Categories")
                    {
                        bool boolValue;
                        bool.TryParse(value, out boolValue);

                        if (key == "Sell_Ore") _reuseConfig.Sell_Ore = boolValue;
                        else if (key == "Sell_Ingots") _reuseConfig.Sell_Ingots = boolValue;
                        else if (key == "Sell_Components") _reuseConfig.Sell_Components = boolValue;
                        else if (key == "Sell_Tools") _reuseConfig.Sell_Tools = boolValue;
                        else if (key == "Sell_Weapons") _reuseConfig.Sell_Weapons = boolValue;
                        else if (key == "Sell_Ammo") _reuseConfig.Sell_Ammo = boolValue;
                        else if (key == "Sell_Food") _reuseConfig.Sell_Food = boolValue;
                        else if (key == "Sell_Seeds") _reuseConfig.Sell_Seeds = boolValue;
                        else if (key == "Sell_Consumables") _reuseConfig.Sell_Consumables = boolValue;
                        else if (key == "Sell_Prototech") _reuseConfig.Sell_Prototech = boolValue;
                    }
                    // Parse Buy_Categories
                    else if (currentSection == "Buy_Categories")
                    {
                        bool boolValue;
                        bool.TryParse(value, out boolValue);

                        if (key == "Buy_Ore") _reuseConfig.Buy_Ore = boolValue;
                        else if (key == "Buy_Ingots") _reuseConfig.Buy_Ingots = boolValue;
                        else if (key == "Buy_Components") _reuseConfig.Buy_Components = boolValue;
                        else if (key == "Buy_Tools") _reuseConfig.Buy_Tools = boolValue;
                        else if (key == "Buy_Weapons") _reuseConfig.Buy_Weapons = boolValue;
                        else if (key == "Buy_Ammo") _reuseConfig.Buy_Ammo = boolValue;
                        else if (key == "Buy_Food") _reuseConfig.Buy_Food = boolValue;
                        else if (key == "Buy_Seeds") _reuseConfig.Buy_Seeds = boolValue;
                        else if (key == "Buy_Consumables") _reuseConfig.Buy_Consumables = boolValue;
                        else if (key == "Buy_Prototech") _reuseConfig.Buy_Prototech = boolValue;
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR parsing CustomData:\n{ex}");
            }

            return _reuseConfig;
        }

        #endregion

        #region Item Syncing

        /// <summary>
        /// Synchronisiert Store Items aus Katalogen
        /// Whitelist-Logik: Items werden gespawnt wenn Kategorie AN oder Item auf Whitelist
        /// Blacklist-Logik: Items werden NIE gespawnt wenn auf Blacklist (überschreibt alles)
        /// </summary>
        private void SyncStoreItems(IMyStoreBlock block, StoreConfig config)
        {
            try
            {
                _reuseCategories.Clear();

                if (config.Sell_Ore)         _reuseCategories.Add("Ore");
                if (config.Sell_Ingots)      _reuseCategories.Add("Ingots");
                if (config.Sell_Components)  _reuseCategories.Add("Components");
                if (config.Sell_Tools)       _reuseCategories.Add("Tools");
                if (config.Sell_Weapons)     _reuseCategories.Add("Weapons");
                if (config.Sell_Ammo)        _reuseCategories.Add("Ammo");
                if (config.Sell_Food)        _reuseCategories.Add("Food");
                if (config.Sell_Seeds)       _reuseCategories.Add("Seeds");
                if (config.Sell_Consumables) _reuseCategories.Add("Consumables");
                if (config.Sell_Prototech)   _reuseCategories.Add("Prototech");

                foreach (var entry in config.Sell_Whitelist)
                {
                    var parts = entry.Split(':');
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                        _reuseCategories.Add(parts[0]);
                }

                // PHASE 1: Sync Sell Items (spawn + orders)
                foreach (var cat in _reuseCategories)
                    SyncCategoryItems(block, cat, config);

                // PHASE 2: Despawn disabled categories (respektiert Whitelist!)
                DespawnDisabledCategories(block, config);

                // PHASE 3: Sync Buy Orders (nur Orders, kein Spawnen)
                SyncBuyOrders(block, config);

                // PHASE 4: Blacklist-Items entfernen (auch bereits gespawnte!)
                DespawnBlacklistedItems(block, config);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR syncing items for '{block.CustomName}':\n{ex}");
            }
        }

        /// <summary>
        /// Hilfsmethode: Ist eine Sell-Kategorie aktiviert?
        /// </summary>
        private bool IsSellCategoryEnabled(string category, StoreConfig config)
        {
            switch (category)
            {
                case "Ore":         return config.Sell_Ore;
                case "Ingots":      return config.Sell_Ingots;
                case "Components":  return config.Sell_Components;
                case "Tools":       return config.Sell_Tools;
                case "Weapons":     return config.Sell_Weapons;
                case "Ammo":        return config.Sell_Ammo;
                case "Food":        return config.Sell_Food;
                case "Seeds":       return config.Sell_Seeds;
                case "Consumables": return config.Sell_Consumables;
                case "Prototech":   return config.Sell_Prototech;
                default:            return false;
            }
        }

        /// <summary>
        /// Hilfsmethode: Ist eine Buy-Kategorie aktiviert?
        /// </summary>
        private bool IsBuyCategoryEnabled(string category, StoreConfig config)
        {
            switch (category)
            {
                case "Ore":         return config.Buy_Ore;
                case "Ingots":      return config.Buy_Ingots;
                case "Components":  return config.Buy_Components;
                case "Tools":       return config.Buy_Tools;
                case "Weapons":     return config.Buy_Weapons;
                case "Ammo":        return config.Buy_Ammo;
                case "Food":        return config.Buy_Food;
                case "Seeds":       return config.Buy_Seeds;
                case "Consumables": return config.Buy_Consumables;
                case "Prototech":   return config.Buy_Prototech;
                default:            return false;
            }
        }

        /// <summary>
        /// Synchronisiert Items einer Category
        /// </summary>
        private void SyncCategoryItems(IMyStoreBlock block, string category, StoreConfig config)
        {
            try
            {
                // DEBUG: Log category sync attempt
                _logger?.Trace(MODULE, $"SyncCategoryItems called for {category}");
                
                string catalogFile = $"RAM_StoreEvent_{category}.ini";
                ReadCatalog(catalogFile);

                if (_reuseCatalog.Count == 0)
                {
                    _logger?.Debug(MODULE, $"Catalog is EMPTY or not found for {category}!");
                    return;
                }

                DateTime nextRefresh = ReadNextRefreshTime(catalogFile);

                if (!config.Maintenance && !_forceMaintenanceOnce)
                {
                    if (DateTime.Now < nextRefresh)
                    {
                        _logger?.Trace(MODULE, 
                            $"NextRefresh not reached for {category} (Next: {nextRefresh:yyyy-MM-dd HH:mm:ss}), skipping spawn"
                        );
                        return;
                    }
                }
                else
                {
                    _logger?.Debug(MODULE, $"Maintenance Mode ACTIVE for {category}, spawning items immediately...");
                }

                _logger?.Debug(MODULE, $"Spawning items for {category}...");

                int spawnedCount = 0;
                int skippedBlacklist = 0;
                int skippedCategory = 0;

                foreach (var item in _reuseCatalog.Values)
                {
                    bool inBlacklist = config.Sell_Blacklist.Contains($"{category}:{item.ItemName}");
                    bool inWhitelist = config.Sell_Whitelist.Contains($"{category}:{item.ItemName}");
                    bool categoryEnabled = IsSellCategoryEnabled(category, config);

                    // Blacklist hat absolute Priorität — niemals spawnen
                    if (inBlacklist)
                    {
                        skippedBlacklist++;
                        continue;
                    }

                    // Kategorie aus UND nicht auf Whitelist → überspringen
                    if (!categoryEnabled && !inWhitelist)
                    {
                        skippedCategory++;
                        continue;
                    }

                    // Item ist erlaubt (Kategorie an ODER Whitelist) → spawnen
                    SpawnItem(block, category, item.TypeId, item.SubtypeId, item.Amount, item.Price);
                    CreateStoreOrder(block, item.TypeId, item.SubtypeId, item.Amount, item.Price);
                    spawnedCount++;
                }
                
                _logger?.Debug(MODULE, $"{category} sync complete - Spawned: {spawnedCount}, Skipped (blacklist): {skippedBlacklist}, Skipped (category off): {skippedCategory}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR syncing {category}:\n{ex}");
            }
        }

        #endregion

        #region Item Spawning

        /// <summary>
        /// Spawnt oder updated Item im Block Inventory
        /// </summary>
        private void SpawnItem(IMyStoreBlock block, string category, string typeId, string subtypeId, int targetAmount, int price)
        {
            try
            {
                IMyInventory inventory = block.GetInventory();
                if (inventory == null)
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR: '{block.CustomName}' has no inventory!");
                    return;
                }

                // Parse TypeId string to MyObjectBuilderType
                MyObjectBuilderType objectBuilderType;
                if (!MyObjectBuilderType.TryParse(typeId, out objectBuilderType))
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR: Invalid TypeId '{typeId}' for {subtypeId}");
                    return;
                }

                // Create Definition ID using TypeId + SubtypeId
                MyDefinitionId itemDefId = new MyDefinitionId(objectBuilderType, subtypeId);

                // Check current amount in inventory
                int currentAmount = GetItemAmount(inventory, itemDefId);

                // Calculate difference
                int difference = targetAmount - currentAmount;

                if (difference == 0)
                {
                    // Perfect amount already
                    return;
                }
                else if (difference > 0)
                {
                    // Need to add items
                    AddItems(inventory, itemDefId, difference);
                }
                else
                {
                    // Need to remove items
                    RemoveItems(inventory, itemDefId, Math.Abs(difference));
                }

                // Verify spawned amount
                int finalAmount = GetItemAmount(inventory, itemDefId);
                
                if (finalAmount != targetAmount)
                {
                    _logger?.Debug(MODULE, 
                        $"'{block.CustomName}' {typeId}/{subtypeId} - Target: {targetAmount}, Actual: {finalAmount}"
                    );
                }
                else
                {
                    _logger?.Debug(MODULE, 
                        $"'{block.CustomName}' {typeId}/{subtypeId} = {finalAmount} @ {price} Credits ✓"
                    );
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR spawning {typeId}/{subtypeId}:\n{ex}");
            }
        }

        /// <summary>
        /// Gibt aktuellen Item-Bestand im Inventory zurück
        /// </summary>
        private int GetItemAmount(IMyInventory inventory, MyDefinitionId itemDefId)
        {
            try
            {
                _reuseInvItems.Clear();
                inventory.GetItems(_reuseInvItems);

                int totalAmount = 0;
                foreach (var item in _reuseInvItems)
                {
                    if (item.Type.TypeId == itemDefId.TypeId.ToString() &&
                        item.Type.SubtypeId.ToString() == itemDefId.SubtypeName)
                    {
                        totalAmount += (int)item.Amount;
                    }
                }
                return totalAmount;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR getting amount:\n{ex}");
                return 0;
            }
        }

        /// <summary>
        /// Fügt Items zum Inventory hinzu
        /// </summary>
        private void AddItems(IMyInventory inventory, MyDefinitionId itemDefId, int amount)
        {
            try
            {
                MyFixedPoint amountToAdd = (MyFixedPoint)amount;
                
                var builder = VRage.ObjectBuilders.MyObjectBuilderSerializer.CreateNewObject(itemDefId);
                var physicalObject = builder as VRage.Game.MyObjectBuilder_PhysicalObject;
                
                if (physicalObject != null)
                {
                    inventory.AddItems(amountToAdd, physicalObject);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR adding items:\n{ex}");
            }
        }

        /// <summary>
        /// Entfernt Items aus Inventory
        /// </summary>
        private void RemoveItems(IMyInventory inventory, MyDefinitionId itemDefId, int amount)
        {
            try
            {
                _reuseInvItems.Clear();
                inventory.GetItems(_reuseInvItems);

                int remainingToRemove = amount;

                for (int i = 0; i < _reuseInvItems.Count && remainingToRemove > 0; i++)
                {
                    var item = _reuseInvItems[i];
                    if (item.Type.TypeId == itemDefId.TypeId.ToString() &&
                        item.Type.SubtypeId.ToString() == itemDefId.SubtypeName)
                    {
                        int itemAmount = (int)item.Amount;
                        int toRemove = Math.Min(itemAmount, remainingToRemove);
                        inventory.RemoveItemsOfType((MyFixedPoint)toRemove, itemDefId);
                        remainingToRemove -= toRemove;
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR removing items:\n{ex}");
            }
        }

        /// <summary>
        /// Gibt ObjectBuilder Type für Category zurück
        /// WICHTIG: Verwendet MyObjectBuilderType.Parse() statt typeof()
        /// damit TypeIds wie "SeedItem" korrekt funktionieren!
        /// </summary>
        /// <summary>
        /// Liest Katalog-File
        /// </summary>
        private void ReadCatalog(string filename)
        {
            _reuseCatalog.Clear();

            try
            {
                _logger?.Trace(MODULE, $"Checking if file exists: {filename}");
                
                if (!FileExists(filename))
                {
                    _logger?.Trace(MODULE, $"FILE NOT FOUND: {filename}");
                    return;
                }
                
                _logger?.Trace(MODULE, $"File exists, reading content...");

                string content = ReadFile(filename);
                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger?.Trace(MODULE, $"File content is EMPTY: {filename}");
                    return;
                }
                
                _logger?.Trace(MODULE, $"File content loaded, length: {content.Length} chars");

                string currentSection = "";
                string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                _logger?.Trace(MODULE, $"Parsing {lines.Length} lines...");

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        continue;
                    }

                    if (currentSection != "Items")
                        continue;

                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = trimmed.Substring(0, equalsIndex).Trim();
                    string value = trimmed.Substring(equalsIndex + 1).Trim();

                    // Parse ItemName_TypeId, ItemName_SubtypeId, ItemName_Amount or ItemName_Price
                    if (key.EndsWith("_TypeId"))
                    {
                        string itemName = key.Substring(0, key.Length - 7);
                        if (!_reuseCatalog.ContainsKey(itemName))
                            _reuseCatalog[itemName] = new CatalogItem { ItemName = itemName };
                        _reuseCatalog[itemName].TypeId = value;
                    }
                    else if (key.EndsWith("_SubtypeId"))
                    {
                        string itemName = key.Substring(0, key.Length - 10);
                        if (!_reuseCatalog.ContainsKey(itemName))
                            _reuseCatalog[itemName] = new CatalogItem { ItemName = itemName };
                        _reuseCatalog[itemName].SubtypeId = value;
                    }
                    else if (key.EndsWith("_Amount"))
                    {
                        string itemName = key.Substring(0, key.Length - 7);
                        int amount;
                        if (int.TryParse(value, out amount))
                        {
                            if (!_reuseCatalog.ContainsKey(itemName))
                                _reuseCatalog[itemName] = new CatalogItem { ItemName = itemName };
                            _reuseCatalog[itemName].Amount = amount;
                        }
                    }
                    else if (key.EndsWith("_Price"))
                    {
                        string itemName = key.Substring(0, key.Length - 6);
                        int price;
                        if (int.TryParse(value, out price))
                        {
                            if (!_reuseCatalog.ContainsKey(itemName))
                                _reuseCatalog[itemName] = new CatalogItem { ItemName = itemName };
                            _reuseCatalog[itemName].Price = price;
                        }
                    }
                }
                
                _logger?.Debug(MODULE, $"Parsed {_reuseCatalog.Count} items from {filename}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR reading catalog {filename}:\n{ex}");
            }

            return;
        }

        /// <summary>
        /// Liest NextRefresh Zeit aus RAM Event File
        /// KRITISCH: Nur bei NextRefresh Zeit Items spawnen!
        /// </summary>
        private DateTime ReadNextRefreshTime(string filename)
        {
            try
            {
                if (!FileExists(filename))
                    return DateTime.MinValue;

                string content = ReadFile(filename);
                if (string.IsNullOrWhiteSpace(content))
                    return DateTime.MinValue;

                string currentSection = "";
                string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        continue;
                    }

                    if (currentSection == "EventInfo" && trimmed.StartsWith("NextRefresh="))
                    {
                        string value = trimmed.Substring(12); // "NextRefresh=".Length = 12
                        DateTime nextRefresh;
                        if (DateTime.TryParse(value, out nextRefresh))
                        {
                            return nextRefresh;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR reading NextRefresh from {filename}:\n{ex}");
            }

            return DateTime.MinValue;
        }

        private bool FileExists(string filename)
        {
            try
            {
                return MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(TraderStoreBlockModule));
            }
            catch
            {
                return false;
            }
        }

        private string ReadFile(string filename)
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(TraderStoreBlockModule)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(TraderStoreBlockModule)))
                    {
                        return reader.ReadToEnd();
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR reading {filename}:\n{ex}");
                return null;
            }
        }

        #endregion

        #region Item Despawning

        /// <summary>
        /// Entfernt Items aus deaktivierten Categories
        /// WICHTIG: Whitelist-Items bleiben auch bei deaktivierter Kategorie!
        /// </summary>
        private void DespawnDisabledCategories(IMyStoreBlock block, StoreConfig config)
        {
            try
            {
                IMyInventory inventory = block.GetInventory();
                if (inventory == null)
                    return;

                string[] allCategories = { "Ore", "Ingots", "Components", "Tools", "Weapons", "Ammo", "Food", "Seeds", "Consumables", "Prototech" };

                foreach (var category in allCategories)
                {
                    if (IsSellCategoryEnabled(category, config))
                        continue; // Kategorie an → nichts despawnen

                    // Kategorie aus → despawn, aber Whitelist-Items behalten
                    RemoveStoreOrdersForCategoryExcludingWhitelist(block, category, config.Sell_Whitelist, StoreItemTypes.Offer);
                    DespawnCategoryItemsExcludingWhitelist(inventory, category, config.Sell_Whitelist);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR despawning disabled categories:\n{ex}");
            }
        }

        /// <summary>
        /// Entfernt Blacklist-Items aus Inventar + Store Orders
        /// Läuft nach jedem Spawn — entfernt auch bereits gespawnte Items
        /// </summary>
        private void DespawnBlacklistedItems(IMyStoreBlock block, StoreConfig config)
        {
            try
            {
                var storeBlock = block as Sandbox.ModAPI.IMyStoreBlock;
                if (storeBlock == null)
                    return;

                string[] allCategories = { "Ore", "Ingots", "Components", "Tools", "Weapons", "Ammo", "Food", "Seeds", "Consumables", "Prototech" };

                // --- SELL: Inventar + Offer Orders bereinigen ---
                if (config.Sell_Blacklist.Count > 0)
                {
                    IMyInventory inventory = block.GetInventory();
                    if (inventory != null)
                    {
                        foreach (var category in allCategories)
                        {
                            var categoryItems = CategoryDefinitions.GetItemsForCategory(category);
                            if (categoryItems == null || categoryItems.Count == 0)
                                continue;

                            _reuseInvItems.Clear();
                            inventory.GetItems(_reuseInvItems);

                            for (int i = _reuseInvItems.Count - 1; i >= 0; i--)
                            {
                                var item = _reuseInvItems[i];
                                string itemKey = $"{item.Type.TypeId}/{item.Type.SubtypeId}";

                                foreach (var itemDef in categoryItems)
                                {
                                    if ($"MyObjectBuilder_{itemDef.TypeId}/{itemDef.SubtypeId}" == itemKey)
                                    {
                                        if (config.Sell_Blacklist.Contains($"{category}:{itemDef.DisplayName}"))
                                        {
                                            inventory.RemoveItemsAt(i, (MyFixedPoint)(int)item.Amount);
                                            _logger?.Debug(MODULE, $"Despawned blacklisted sell item {category}:{itemDef.DisplayName}");
                                        }
                                        break;
                                    }
                                }
                            }

                            _reuseStoreItems.Clear();
                            storeBlock.GetStoreItems(_reuseStoreItems);

                            foreach (var order in _reuseStoreItems)
                            {
                                if (!order.Item.HasValue || order.StoreItemType != StoreItemTypes.Offer)
                                    continue;

                                string itemKey = $"{order.Item.Value.TypeId}/{order.Item.Value.SubtypeName}";

                                foreach (var itemDef in categoryItems)
                                {
                                    if ($"MyObjectBuilder_{itemDef.TypeId}/{itemDef.SubtypeId}" == itemKey)
                                    {
                                        if (config.Sell_Blacklist.Contains($"{category}:{itemDef.DisplayName}"))
                                            storeBlock.RemoveStoreItem(order);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                // --- BUY: Buy Orders bereinigen ---
                if (config.Buy_Blacklist.Count > 0)
                {
                    foreach (var category in allCategories)
                    {
                        var categoryItems = CategoryDefinitions.GetItemsForCategory(category);
                        if (categoryItems == null || categoryItems.Count == 0)
                            continue;

                        _reuseStoreItems.Clear();
                        storeBlock.GetStoreItems(_reuseStoreItems);

                        foreach (var order in _reuseStoreItems)
                        {
                            if (!order.Item.HasValue || order.StoreItemType != StoreItemTypes.Order)
                                continue;

                            string itemKey = $"{order.Item.Value.TypeId}/{order.Item.Value.SubtypeName}";

                            foreach (var itemDef in categoryItems)
                            {
                                if ($"MyObjectBuilder_{itemDef.TypeId}/{itemDef.SubtypeId}" == itemKey)
                                {
                                    if (config.Buy_Blacklist.Contains($"{category}:{itemDef.DisplayName}"))
                                    {
                                        storeBlock.RemoveStoreItem(order);
                                        _logger?.Debug(MODULE, $"Removed blacklisted buy order {category}:{itemDef.DisplayName}");
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR despawning blacklisted items:\n{ex}");
            }
        }

        /// <summary>
        /// Entfernt Items einer Category aus Inventory, AUSSER Whitelist-Items
        /// </summary>
        private void DespawnCategoryItemsExcludingWhitelist(IMyInventory inventory, string category, List<string> whitelist)
        {
            try
            {
                var categoryItems = CategoryDefinitions.GetItemsForCategory(category);
                if (categoryItems == null || categoryItems.Count == 0)
                    return;

                _reuseInvItems.Clear();
                inventory.GetItems(_reuseInvItems);

                int removedCount = 0;

                for (int i = _reuseInvItems.Count - 1; i >= 0; i--)
                {
                    var item = _reuseInvItems[i];
                    string itemKey = $"{item.Type.TypeId}/{item.Type.SubtypeId}";

                    // Prüfe ob dieses Item zur Kategorie gehört
                    bool belongsToCategory = false;
                    string displayName = "";
                    foreach (var itemDef in categoryItems)
                    {
                        if ($"MyObjectBuilder_{itemDef.TypeId}/{itemDef.SubtypeId}" == itemKey)
                        {
                            belongsToCategory = true;
                            displayName = itemDef.DisplayName;
                            break;
                        }
                    }

                    if (!belongsToCategory)
                        continue;

                    // Whitelist-Item? → behalten
                    if (whitelist.Contains($"{category}:{displayName}"))
                        continue;

                    int amount = (int)item.Amount;
                    inventory.RemoveItemsAt(i, (MyFixedPoint)amount);
                    removedCount++;
                }

                if (removedCount > 0)
                    _logger?.Debug(MODULE, $"Removed {removedCount} non-whitelisted {category} items");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR despawning {category}:\n{ex}");
            }
        }

        /// <summary>
        /// Entfernt Store Orders einer Category, AUSSER Whitelist-Items
        /// </summary>
        private void RemoveStoreOrdersForCategoryExcludingWhitelist(IMyStoreBlock block, string category, List<string> whitelist, StoreItemTypes orderType)
        {
            try
            {
                var storeBlock = block as Sandbox.ModAPI.IMyStoreBlock;
                if (storeBlock == null)
                    return;

                var categoryItems = CategoryDefinitions.GetItemsForCategory(category);
                if (categoryItems == null || categoryItems.Count == 0)
                    return;

                _reuseStoreItems.Clear();
                storeBlock.GetStoreItems(_reuseStoreItems);

                int removedCount = 0;

                foreach (var order in _reuseStoreItems)
                {
                    if (!order.Item.HasValue || order.StoreItemType != orderType)
                        continue;

                    string itemKey = $"{order.Item.Value.TypeId}/{order.Item.Value.SubtypeName}";

                    foreach (var itemDef in categoryItems)
                    {
                        if ($"MyObjectBuilder_{itemDef.TypeId}/{itemDef.SubtypeId}" == itemKey)
                        {
                            // Whitelist-Item? → behalten
                            if (whitelist.Contains($"{category}:{itemDef.DisplayName}"))
                                break;

                            storeBlock.RemoveStoreItem(order);
                            removedCount++;
                            break;
                        }
                    }
                }

                if (removedCount > 0)
                    _logger?.Debug(MODULE, $"Removed {removedCount} non-whitelisted {category} orders");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR removing orders for {category}:\n{ex}");
            }
        }

        #endregion

        #region Store Orders

        /// <summary>
        /// Erstellt oder updated Store Order mit TypeId + SubtypeId
        /// KRITISCH: TypeId + SubtypeId müssen EXAKT mit SpawnItem() übereinstimmen!
        /// WICHTIG: Prüft ob Order bereits existiert und erstellt nur wenn nötig!
        /// </summary>
        private void CreateStoreOrder(IMyStoreBlock block, string typeId, string subtypeId, int amount, int price)
        {
            try
            {
                var storeBlock = block as Sandbox.ModAPI.IMyStoreBlock;
                if (storeBlock == null)
                    return;
                
                // Parse TypeId string to MyObjectBuilderType
                // WICHTIG: EXAKT dieselbe Logik wie SpawnItem()!
                // typeId = "Ore" → "MyObjectBuilder_Ore"
                MyObjectBuilderType objectBuilderType = MyObjectBuilderType.Parse("MyObjectBuilder_" + typeId);
                
                // Create DefinitionId with TypeId + SubtypeId
                // WICHTIG: EXAKT dieselbe Logik wie SpawnItem()!
                var itemDefId = new VRage.Game.MyDefinitionId(objectBuilderType, subtypeId);
                
                // Get all existing orders
                _reuseStoreItems.Clear();
                storeBlock.GetStoreItems(_reuseStoreItems);
                
                _logger?.Debug(MODULE, 
                    $"DEBUG: Checking for existing order {typeId}/{subtypeId}, total orders: {_reuseStoreItems.Count}"
                );
                
                // Check if order already exists
                VRage.Game.ModAPI.IMyStoreItem existingOrder = null;
                foreach (var order in _reuseStoreItems)
                {
                    if (order.Item.HasValue &&
                        order.Item.Value.TypeId == itemDefId.TypeId && 
                        order.Item.Value.SubtypeName == itemDefId.SubtypeName &&
                        order.StoreItemType == StoreItemTypes.Offer)
                    {
                        existingOrder = order;
                        _logger?.Debug(MODULE, 
                            $"DEBUG: FOUND existing order for {typeId}/{subtypeId}!"
                        );
                        break;
                    }
                }
                
                if (existingOrder != null)
                {
                    // Order exists - only update if price changed
                    // Vanilla manages amount automatically, we only care about price!
                    if (existingOrder.PricePerUnit != price)
                    {
                        existingOrder.PricePerUnit = price;
                        _logger?.Debug(MODULE, 
                            $"Updated order price {typeId}/{subtypeId}: {price}"
                        );
                    }
                    // Else: Order exists with correct price, nothing to do!
                }
                else
                {
                    // Order does not exist - create new
                    var newItem = storeBlock.CreateStoreItem(itemDefId, amount, price, StoreItemTypes.Offer);
                    storeBlock.InsertStoreItem(newItem);
                    
                    _logger?.Debug(MODULE, 
                        $"Created order {typeId}/{subtypeId}: {amount} @ {price}"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(MODULE, 
                    $"creating order for {typeId}/{subtypeId}:\n{ex}"
                );
            }
        }

        #endregion

        #region Buy System

        /// <summary>
        /// Synchronisiert Buy Orders (Spieler verkauft an Store)
        /// KEIN Item-Spawning — nur Orders erstellen/verwalten
        /// Preis: RAM-Katalog Preis × Buy_Margin (GlobalConfig oder Over_ Override)
        /// </summary>
        private void SyncBuyOrders(IMyStoreBlock block, StoreConfig config)
        {
            try
            {
                _reuseCategories.Clear();

                if (config.Buy_Ore)         _reuseCategories.Add("Ore");
                if (config.Buy_Ingots)      _reuseCategories.Add("Ingots");
                if (config.Buy_Components)  _reuseCategories.Add("Components");
                if (config.Buy_Tools)       _reuseCategories.Add("Tools");
                if (config.Buy_Weapons)     _reuseCategories.Add("Weapons");
                if (config.Buy_Ammo)        _reuseCategories.Add("Ammo");
                if (config.Buy_Food)        _reuseCategories.Add("Food");
                if (config.Buy_Seeds)       _reuseCategories.Add("Seeds");
                if (config.Buy_Consumables) _reuseCategories.Add("Consumables");
                if (config.Buy_Prototech)   _reuseCategories.Add("Prototech");

                foreach (var entry in config.Buy_Whitelist)
                {
                    var parts = entry.Split(':');
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                        _reuseCategories.Add(parts[0]);
                }

                foreach (var cat in _reuseCategories)
                    SyncBuyOrdersForCategory(block, cat, config);

                DespawnDisabledBuyOrders(block, config);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR syncing buy orders for '{block.CustomName}':\n{ex}");
            }
        }

        /// <summary>
        /// Erstellt Buy Orders für eine Kategorie aus dem RAM-Katalog
        /// Preis = RAM-Preis × Buy_Margin
        /// </summary>
        private void SyncBuyOrdersForCategory(IMyStoreBlock block, string category, StoreConfig config)
        {
            try
            {
                string catalogFile = $"RAM_StoreEvent_{category}.ini";
                ReadCatalog(catalogFile);

                if (_reuseCatalog.Count == 0)
                {
                    _logger?.Debug(MODULE, $"No catalog for Buy Orders {category}");
                    return;
                }

                DateTime nextRefresh = ReadNextRefreshTime(catalogFile);
                if (!config.Maintenance && DateTime.Now < nextRefresh)
                    return;

                int createdCount = 0;
                int skippedBlacklist = 0;
                int skippedCategory = 0;

                foreach (var item in _reuseCatalog.Values)
                {
                    bool inBlacklist = config.Buy_Blacklist.Contains($"{category}:{item.ItemName}");
                    bool inWhitelist = config.Buy_Whitelist.Contains($"{category}:{item.ItemName}");
                    bool categoryEnabled = IsBuyCategoryEnabled(category, config);

                    // Blacklist hat absolute Priorität
                    if (inBlacklist)
                    {
                        skippedBlacklist++;
                        continue;
                    }

                    // Kategorie aus UND nicht auf Whitelist → überspringen
                    if (!categoryEnabled && !inWhitelist)
                    {
                        skippedCategory++;
                        continue;
                    }

                    // Buy_Margin berechnen: Over_ wenn Override_enable=true, sonst GlobalConfig
                    float buyMargin = GetBuyMargin(category, item.ItemName);
                    int buyPrice = Math.Max(1, (int)(item.Price * buyMargin));

                    // Buy Order erstellen (StoreItemTypes.Order = Spieler verkauft an Store)
                    CreateBuyOrder(block, item.TypeId, item.SubtypeId, buyPrice);
                    createdCount++;
                }

                _logger?.Debug(MODULE, $"{category} buy orders - Created: {createdCount}, Skipped (blacklist): {skippedBlacklist}, Skipped (category off): {skippedCategory}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR syncing buy orders for {category}:\n{ex}");
            }
        }

        /// <summary>
        /// Gibt Buy_Margin für ein Item zurück
        /// Override_enable=true → Over_Buy_Margin aus Pricelist-File
        /// Override_enable=false → Buy_Margin aus GlobalConfig Rarity-Sektion
        /// </summary>
        private float GetBuyMargin(string category, string itemName)
        {
            try
            {
                // Pricelist direkt lesen
                string pricelistFile = $"Pricelist_{category}.ini";
                if (!FileExists(pricelistFile))
                    return 0.7f;

                string content = ReadFile(pricelistFile);
                if (string.IsNullOrWhiteSpace(content))
                    return 0.7f;

                bool inSection = false;
                bool overrideEnable = false;
                float overBuyMargin = 0f;
                string rarity = "Common";

                foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        inSection = (trimmed == $"[{itemName}]");
                        continue;
                    }

                    if (!inSection)
                        continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = trimmed.Substring(0, eq).Trim();
                    string value = trimmed.Substring(eq + 1).Trim();

                    if (key == "Rarity") rarity = value;
                    else if (key == "Override_enable") bool.TryParse(value, out overrideEnable);
                    else if (key == "Over_Buy_Margin") float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out overBuyMargin);
                }

                if (overrideEnable && overBuyMargin > 0f)
                    return overBuyMargin;

                // GlobalConfig: Buy_Margin aus Rarity-Sektion
                string globalFile = "GlobalConfig.ini";
                if (!FileExists(globalFile))
                    return 0.7f;

                string globalContent = ReadFile(globalFile);
                if (string.IsNullOrWhiteSpace(globalContent))
                    return 0.7f;

                string targetSection = $"Rarity_{rarity}";
                bool inRaritySection = false;

                foreach (var line in globalContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        inRaritySection = (trimmed == $"[{targetSection}]");
                        continue;
                    }

                    if (!inRaritySection)
                        continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = trimmed.Substring(0, eq).Trim();
                    string value = trimmed.Substring(eq + 1).Trim();

                    if (key == "Buy_Margin")
                    {
                        float margin;
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out margin))
                            return margin;
                    }
                }

                return 0.7f; // Fallback
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR getting buy margin for {category}/{itemName}:\n{ex}");
                return 0.7f;
            }
        }

        /// <summary>
        /// Erstellt oder updated eine Buy Order (StoreItemTypes.Order)
        /// Spieler kann Items an Store verkaufen
        /// </summary>
        private void CreateBuyOrder(IMyStoreBlock block, string typeId, string subtypeId, int price)
        {
            try
            {
                var storeBlock = block as Sandbox.ModAPI.IMyStoreBlock;
                if (storeBlock == null)
                    return;

                MyObjectBuilderType objectBuilderType = MyObjectBuilderType.Parse("MyObjectBuilder_" + typeId);
                var itemDefId = new VRage.Game.MyDefinitionId(objectBuilderType, subtypeId);

                // Prüfe ob Buy Order bereits existiert
                _reuseStoreItems.Clear();
                storeBlock.GetStoreItems(_reuseStoreItems);

                VRage.Game.ModAPI.IMyStoreItem existingOrder = null;
                foreach (var order in _reuseStoreItems)
                {
                    if (order.Item.HasValue &&
                        order.Item.Value.TypeId == itemDefId.TypeId &&
                        order.Item.Value.SubtypeName == itemDefId.SubtypeName &&
                        order.StoreItemType == StoreItemTypes.Order)
                    {
                        existingOrder = order;
                        break;
                    }
                }

                if (existingOrder != null)
                {
                    // Order existiert — nur Preis updaten wenn nötig
                    if (existingOrder.PricePerUnit != price)
                    {
                        existingOrder.PricePerUnit = price;
                        _logger?.Debug(MODULE, $"Updated buy order price {typeId}/{subtypeId}: {price}");
                    }
                }
                else
                {
                    // Neue Buy Order erstellen — große Menge damit Store viel kaufen kann
                    int buyAmount = 99999;
                    var newItem = storeBlock.CreateStoreItem(itemDefId, buyAmount, price, StoreItemTypes.Order);
                    storeBlock.InsertStoreItem(newItem);
                    _logger?.Debug(MODULE, $"Created buy order {typeId}/{subtypeId}: {price} Credits");
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR creating buy order for {typeId}/{subtypeId}:\n{ex}");
            }
        }

        /// <summary>
        /// Entfernt Buy Orders für deaktivierte Kategorien (respektiert Buy_Whitelist)
        /// </summary>
        private void DespawnDisabledBuyOrders(IMyStoreBlock block, StoreConfig config)
        {
            try
            {
                string[] allCategories = { "Ore", "Ingots", "Components", "Tools", "Weapons", "Ammo", "Food", "Seeds", "Consumables", "Prototech" };

                foreach (var category in allCategories)
                {
                    if (IsBuyCategoryEnabled(category, config))
                        continue;

                    RemoveStoreOrdersForCategoryExcludingWhitelist(block, category, config.Buy_Whitelist, StoreItemTypes.Order);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderStoreBlock ERROR despawning disabled buy orders:\n{ex}");
            }
        }

        #endregion


        #region Error Handling

        /// <summary>
        /// Versetzt Block in Error Mode
        /// - Block ausschalten
        /// - DangerZone auf Display anzeigen (Surface 0)
        /// - Error loggen
        /// 
        /// ISOLIERT: Betrifft nur diesen einen Block!
        /// </summary>
        private void EnterErrorMode(IMyStoreBlock block, string errorMessage)
        {
            try
            {
                block.Enabled = false;
                _logger?.Debug(MODULE, 
                    $"'{block.CustomName}' ERROR: {errorMessage}"
                );
            }
            catch (Exception ex)
            {
                _logger?.Debug(MODULE, 
                    $"in EnterErrorMode:\n{ex}"
                );
            }
        }

        #endregion
    }
}