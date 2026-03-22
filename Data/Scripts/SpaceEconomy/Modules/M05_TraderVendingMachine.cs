using System;
using System.Collections.Generic;
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
    /// M05 - TraderVendingMachine
    /// Verwaltet VendingMachine Blocks mit Sell-Only Trading
    /// 
    /// Features (Phase 1 - Custom Data Only):
    /// - Auto-Discovery von VendingMachine Blocks
    /// - Custom Data Template Deployment (OHNE Buy Categories!)
    /// - Custom Data Validation
    /// - Error Mode: Block OFF + DangerZone Display
    /// - Regular Polling (alle 5 Sekunden)
    /// 
    /// STANDALONE: Läuft komplett unabhängig von M04!
    /// ERROR ISOLATION: Jeder Block komplett isoliert - ein Fehler betrifft nur EINEN Block!
    /// UNTERSCHIED zu M04: KEINE Buy Orders, nur Sell!
    /// </summary>
    public class TraderVendingMachineModule : IModule
    {
        public string ModuleName => "TraderVendingMachine";

        // Block Detection
        private const string BLOCK_SUBTYPE = "VendingMachine";
        private List<IMyStoreBlock> _vendingMachines = new List<IMyStoreBlock>();

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
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] TraderVendingMachine: Initializing...");

                if (!MyAPIGateway.Multiplayer.IsServer)
                {
                    MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] TraderVendingMachine: Client detected - Module disabled");
                    return;
                }

                // Cache initial befüllen
                FindVendingMachines();

                // Events für automatische Cache-Pflege
                MyAPIGateway.Entities.OnEntityAdd    += OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;

                _initialized = true;
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine: Initialized — {_vendingMachines.Count} blocks cached");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR in Init:\n{ex}");
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
                    PollVendingMachines();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR in Update:\n{ex}");
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
            PollVendingMachines();
            if (LoggerModule.DebugMode)
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] TraderVendingMachine: ForceRefresh executed");
        }

        public void Close()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] TraderVendingMachine: Closing...");

                if (MyAPIGateway.Entities != null)
                {
                    MyAPIGateway.Entities.OnEntityAdd    -= OnEntityAdd;
                    MyAPIGateway.Entities.OnEntityRemove -= OnEntityRemove;
                }

                _vendingMachines.Clear();
                _initialized = false;

                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] TraderVendingMachine: Closed");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR in Close:\n{ex}");
            }
        }

        #region Block Detection & Polling

        /// <summary>
        /// Pollt alle VendingMachine Blocks und prüft Custom Data
        /// </summary>
        private void PollVendingMachines()
        {
            try
            {
                // Cache nutzen — kein World-Scan mehr
                foreach (var block in _vendingMachines)
                {
                    if (block == null || block.Closed)
                        continue;

                    ProcessBlock(block);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR in PollVendingMachines:\n{ex}");
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
                SyncStoreItems(block, config);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR processing '{block.CustomName}':\n{ex}");
                EnterErrorMode(block, "Internal processing error");
            }
        }

        /// <summary>
        /// Findet alle VendingMachine Blocks im Spiel
        /// </summary>
        private void FindVendingMachines()
        {
            try
            {
                _vendingMachines.Clear();

                HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities)
                {
                    IMyCubeGrid grid = entity as IMyCubeGrid;
                    if (grid == null)
                        continue;

                    List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks, b => IsVendingMachineBlock(b));

                    foreach (var slimBlock in blocks)
                    {
                        IMyStoreBlock vendingBlock = slimBlock.FatBlock as IMyStoreBlock;
                        if (vendingBlock != null && !_vendingMachines.Contains(vendingBlock))
                        {
                            _vendingMachines.Add(vendingBlock);
                        }
                    }
                }

                if (_vendingMachines.Count > 0)
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine: Found {_vendingMachines.Count} VendingMachine blocks");
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR in FindVendingMachines:\n{ex}");
            }
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            try
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null) return;

                grid.OnBlockAdded += OnBlockAdded;

                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks, b => IsVendingMachineBlock(b));
                foreach (var slim in blocks)
                {
                    var vendingBlock = slim.FatBlock as IMyStoreBlock;
                    if (vendingBlock != null && !_vendingMachines.Contains(vendingBlock))
                        _vendingMachines.Add(vendingBlock);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR in OnEntityAdd:\n{ex}");
            }
        }

        private void OnEntityRemove(IMyEntity entity)
        {
            try
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null) return;

                grid.OnBlockAdded -= OnBlockAdded;
                _vendingMachines.RemoveAll(b => b.CubeGrid == grid);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR in OnEntityRemove:\n{ex}");
            }
        }

        private void OnBlockAdded(IMySlimBlock slim)
        {
            try
            {
                if (!IsVendingMachineBlock(slim)) return;
                var vendingBlock = slim.FatBlock as IMyStoreBlock;
                if (vendingBlock != null && !_vendingMachines.Contains(vendingBlock))
                    _vendingMachines.Add(vendingBlock);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR in OnBlockAdded:\n{ex}");
            }
        }

        /// <summary>
        /// Prüft ob ein Block eine VendingMachine ist
        /// </summary>
        private bool IsVendingMachineBlock(IMySlimBlock block)
        {
            if (block?.FatBlock == null)
                return false;

            return block.FatBlock is IMyStoreBlock && 
                   block.BlockDefinition.Id.SubtypeName.Contains(BLOCK_SUBTYPE);
        }

        #endregion

        #region Custom Data Management

        /// <summary>
        /// Deployed Custom Data Template in Block (OHNE Buy Categories!)
        /// </summary>
        private void DeployCustomDataTemplate(IMyStoreBlock block)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine("# VENDING MACHINE - Configuration");
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine("# HINWEIS: VendingMachine verkauft NUR - keine Buy Categories!");
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
                
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine: Deployed CustomData template to '{block.CustomName}'");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR in DeployCustomDataTemplate:\n{ex}");
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
        /// Store Configuration from CustomData (Sell-Only!)
        /// </summary>
        private class StoreConfig
        {
            public bool Maintenance = false;

            public bool Sell_Ore, Sell_Ingots, Sell_Components, Sell_Tools;
            public bool Sell_Weapons, Sell_Ammo, Sell_Food, Sell_Seeds;
            public bool Sell_Consumables, Sell_Prototech;

            public List<string> Sell_Whitelist = new List<string>();
            public List<string> Sell_Blacklist = new List<string>();

            public void Reset()
            {
                Maintenance = false;
                Sell_Ore = Sell_Ingots = Sell_Components = Sell_Tools = false;
                Sell_Weapons = Sell_Ammo = Sell_Food = Sell_Seeds = false;
                Sell_Consumables = Sell_Prototech = false;
                Sell_Whitelist.Clear();
                Sell_Blacklist.Clear();
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
        /// Validiert Custom Data (OHNE Buy Sections!)
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

                // VendingMachine darf KEINE Buy Sections haben!
                // (Optional: Warnung wenn doch vorhanden, aber kein Error)

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

                    // Only validate in Sell_Categories section
                    if (currentSection != "Sell_Categories")
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
        /// Parst CustomData in StoreConfig (Sell-Only!)
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
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR parsing CustomData:\n{ex}");
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
                // Bestimme welche Kategorien verarbeitet werden müssen
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

                // Whitelist-Einträge hinzufügen (auch bei disabled Kategorien!)
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

                // PHASE 3: Blacklist-Items entfernen (auch bereits gespawnte!)
                DespawnBlacklistedItems(block, config);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR syncing items for '{block.CustomName}':\n{ex}");
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
        /// Synchronisiert Items einer Category
        /// </summary>
        private void SyncCategoryItems(IMyStoreBlock block, string category, StoreConfig config)
        {
            try
            {
                string catalogFile = $"RAM_StoreEvent_{category}.ini";
                ReadCatalog(catalogFile);

                if (_reuseCatalog.Count == 0)
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine: No catalog for {category}");
                    return;
                }

                DateTime nextRefresh = ReadNextRefreshTime(catalogFile);

                if (!config.Maintenance && !_forceMaintenanceOnce)
                {
                    if (DateTime.Now < nextRefresh)
                        return;
                }
                else
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine: Maintenance Mode ACTIVE for {category}");
                }

                int spawnedCount = 0;
                int skippedBlacklist = 0;
                int skippedCategory = 0;

                foreach (var item in _reuseCatalog.Values)
                {
                    bool inBlacklist = config.Sell_Blacklist.Contains($"{category}:{item.ItemName}");
                    bool inWhitelist = config.Sell_Whitelist.Contains($"{category}:{item.ItemName}");
                    bool categoryEnabled = IsSellCategoryEnabled(category, config);

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

                    // Item ist erlaubt → spawnen
                    SpawnItem(block, item.TypeId, item.SubtypeId, item.Amount, item.Price);
                    CreateStoreOrder(block, item.TypeId, item.SubtypeId, item.Amount, item.Price);
                    spawnedCount++;
                }

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine: {category} sync - Spawned: {spawnedCount}, Skipped (blacklist): {skippedBlacklist}, Skipped (category off): {skippedCategory}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR syncing {category}:\n{ex}");
            }
        }

        #endregion

        #region Item Spawning (DUPLICATE from M04 - STAHLBETON!)

        /// <summary>
        /// Spawnt oder updated Item im Block Inventory
        /// KRITISCH: TypeId + SubtypeId MÜSSEN von M00 kommen! (Single Source of Truth)
        /// WICHTIG: EXAKT dieselbe Parsing-Logik wie CreateStoreOrder()!
        /// </summary>
        private void SpawnItem(IMyStoreBlock block, string typeId, string subtypeId, int targetAmount, int price)
        {
            try
            {
                IMyInventory inventory = block.GetInventory();
                if (inventory == null)
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR: '{block.CustomName}' has no inventory!");
                    return;
                }

                // Parse TypeId string to MyObjectBuilderType
                // WICHTIG: EXAKT dieselbe Logik wie CreateStoreOrder()!
                // typeId = "Ore" → "MyObjectBuilder_Ore"
                MyObjectBuilderType objectBuilderType = MyObjectBuilderType.Parse("MyObjectBuilder_" + typeId);

                // Create Definition ID with TypeId + SubtypeId
                // WICHTIG: EXAKT dieselbe Logik wie CreateStoreOrder()!
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
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole(
                        $"[PhantombiteEconomy] TraderVendingMachine WARNING: '{block.CustomName}' {typeId}/{subtypeId} - Target: {targetAmount}, Actual: {finalAmount}"
                    );
                }
                else
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole(
                        $"[PhantombiteEconomy] TraderVendingMachine: '{block.CustomName}' {typeId}/{subtypeId} = {finalAmount} @ {price} Credits ✓"
                    );
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR spawning {typeId}/{subtypeId}:\n{ex}");
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
                    // Compare TypeId and SubtypeId
                    if (item.Type.TypeId == itemDefId.TypeId.ToString() && 
                        item.Type.SubtypeId == itemDefId.SubtypeName)
                    {
                        totalAmount += (int)item.Amount;
                    }
                }

                return totalAmount;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR getting amount:\n{ex}");
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
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR adding items:\n{ex}");
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
                    // Compare TypeId and SubtypeId
                    if (item.Type.TypeId == itemDefId.TypeId.ToString() && 
                        item.Type.SubtypeId == itemDefId.SubtypeName)
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
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR removing items:\n{ex}");
            }
        }

        /// <summary>
        /// Erstellt oder updated Store Order (DUPLICATE from M04 - STAHLBETON!)
        /// </summary>
        private void CreateStoreOrder(IMyStoreBlock block, string typeId, string subtypeId, int amount, int price)
        {
            try
            {
                var storeBlock = block as Sandbox.ModAPI.IMyStoreBlock;
                if (storeBlock == null)
                    return;

                MyObjectBuilderType objectBuilderType = MyObjectBuilderType.Parse("MyObjectBuilder_" + typeId);
                var itemDefId = new VRage.Game.MyDefinitionId(objectBuilderType, subtypeId);

                _reuseStoreItems.Clear();
                storeBlock.GetStoreItems(_reuseStoreItems);

                VRage.Game.ModAPI.IMyStoreItem existingOrder = null;
                foreach (var order in _reuseStoreItems)
                {
                    if (order.Item.HasValue &&
                        order.Item.Value.TypeId == itemDefId.TypeId &&
                        order.Item.Value.SubtypeName == itemDefId.SubtypeName &&
                        order.StoreItemType == StoreItemTypes.Offer)
                    {
                        existingOrder = order;
                        break;
                    }
                }

                if (existingOrder != null)
                {
                    if (existingOrder.PricePerUnit != price)
                        existingOrder.PricePerUnit = price;
                }
                else
                {
                    var newItem = storeBlock.CreateStoreItem(itemDefId, amount, price, StoreItemTypes.Offer);
                    storeBlock.InsertStoreItem(newItem);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR creating order {typeId}/{subtypeId}:\n{ex}");
            }
        }

        /// <summary>
        /// Liest Katalog-File (DUPLICATE from M04 - STAHLBETON!)
        /// </summary>
        private void ReadCatalog(string filename)
        {
            _reuseCatalog.Clear();

            try
            {
                if (!FileExists(filename))
                    return;

                string content = ReadFile(filename);
                if (string.IsNullOrWhiteSpace(content))
                    return;

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

                    if (currentSection != "Items")
                        continue;

                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = trimmed.Substring(0, equalsIndex).Trim();
                    string value = trimmed.Substring(equalsIndex + 1).Trim();

                    // Parse ItemName_Amount or ItemName_Price
                    if (key.EndsWith("_Amount"))
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
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR reading catalog {filename}:\n{ex}");
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
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR reading NextRefresh from {filename}:\n{ex}");
            }

            return DateTime.MinValue;
        }

        private bool FileExists(string filename)
        {
            try
            {
                return MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(TraderVendingMachineModule));
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
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(TraderVendingMachineModule)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(TraderVendingMachineModule)))
                    {
                        return reader.ReadToEnd();
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR reading {filename}:\n{ex}");
                return null;
            }
        }

        #endregion

        #region Item Despawning

        /// <summary>
        /// Entfernt Items aus deaktivierten Categories
        /// KRITISCH: Items die auf false stehen müssen gelöscht werden!
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
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR despawning disabled categories:\n{ex}");
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
                if (config.Sell_Blacklist.Count == 0)
                    return;

                IMyInventory inventory = block.GetInventory();
                if (inventory == null)
                    return;

                var storeBlock = block as Sandbox.ModAPI.IMyStoreBlock;
                if (storeBlock == null)
                    return;

                string[] allCategories = { "Ore", "Ingots", "Components", "Tools", "Weapons", "Ammo", "Food", "Seeds", "Consumables", "Prototech" };

                foreach (var category in allCategories)
                {
                    var categoryItems = CategoryDefinitions.GetItemsForCategory(category);
                    if (categoryItems == null || categoryItems.Count == 0)
                        continue;

                    // Inventory bereinigen
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
                                    if (LoggerModule.DebugMode)
                                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine: Despawned blacklisted item {category}:{itemDef.DisplayName}");
                                }
                                break;
                            }
                        }
                    }

                    // Store Orders bereinigen
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
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR despawning blacklisted items:\n{ex}");
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

                    if (whitelist.Contains($"{category}:{displayName}"))
                        continue;

                    int amount = (int)item.Amount;
                    inventory.RemoveItemsAt(i, (MyFixedPoint)amount);
                    removedCount++;
                }

                if (removedCount > 0)
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine: Removed {removedCount} non-whitelisted {category} items");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR despawning {category}:\n{ex}");
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
                            if (whitelist.Contains($"{category}:{itemDef.DisplayName}"))
                                break;

                            storeBlock.RemoveStoreItem(order);
                            removedCount++;
                            break;
                        }
                    }
                }

                if (removedCount > 0)
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine: Removed {removedCount} non-whitelisted {category} orders");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] TraderVendingMachine ERROR removing orders for {category}:\n{ex}");
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
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole(
                    $"[PhantombiteEconomy] TraderVendingMachine '{block.CustomName}' ERROR: {errorMessage}"
                );
            }
            catch (Exception ex)
            {
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole(
                    $"[PhantombiteEconomy] TraderVendingMachine ERROR in EnterErrorMode:\n{ex}"
                );
            }
        }

        #endregion
    }
}