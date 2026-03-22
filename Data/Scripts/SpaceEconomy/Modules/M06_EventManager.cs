using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRage.Utils;
using PhantombiteEconomy.Core;

namespace PhantombiteEconomy.Modules
{
    /// <summary>
    /// M06 - EventManager
    /// Globales Event-System für Category-Refreshes
    /// 
    /// Features:
    /// - Persistent Event Files (RAM_StoreEvent_[Category].ini)
    /// - Timer Management (LastRefresh, NextRefresh)
    /// - Roll New Amounts & Prices
    /// - Write Global Catalogs
    /// - Handle Overdue Events
    /// 
    /// Categories: Ore, Ingots, Components, Tools, Weapons, Ammo, Food, Seeds, Consumables, Prototech
    /// 
    /// STANDALONE: Läuft komplett unabhängig, schreibt nur Files!
    /// </summary>
    public class EventManagerModule : IModule
    {
        public string ModuleName => "EventManager";

        // Categories to manage
        private readonly string[] CATEGORIES = {
            "Ore",
            "Ingots", 
            "Components",
            "Tools",
            "Weapons",
            "Ammo",
            "Food",
            "Seeds",
            "Consumables",
            "Prototech"
        };

        // Event Data per Category
        private Dictionary<string, CategoryEvent> _categoryEvents = new Dictionary<string, CategoryEvent>();

        // Global Configuration
        private bool _dynamicPrice = true;
        private Random _rand = new Random();

        // State
        private bool _initialized = false;
        private const int UPDATE_INTERVAL = 300; // Check every 5 seconds
        private int _updateCounter = 0;

        // Event Data Structure
        private class CategoryEvent
        {
            public string Category;
            public DateTime LastRefresh;
            public DateTime NextRefresh;
            public int RefreshTime; // in seconds
            public Dictionary<string, ItemCatalogEntry> Items;

            public CategoryEvent(string category)
            {
                Category = category;
                Items = new Dictionary<string, ItemCatalogEntry>();
            }
        }

        private class ItemCatalogEntry
        {
            public string ItemName;     // Section Header (für Pricelist Lookup)
            public string TypeId;       // ObjectBuilder TypeId (Ore, Ingot, Component, etc.)
            public string SubtypeId;    // ECHTE SubtypeId (fürs Spawnen!)
            public int Amount;
            public int Price;
        }

        // Pricelist Data from M02
        private class PricelistItemData
        {
            public string SubtypeId;  // ECHTE SubtypeId für Spawning!
            public string Rarity;     // Link to GlobalConfig Rarity section
            public int BasePrice;
            public int MinPrice;
            public int MaxPrice;
            public bool OverrideEnable;
            public float Over_BuyMargin;
            public int Over_DynamicPriceStep;
            public float Over_DynamicPriceFactor;
        }

        // Rarity Data from GlobalConfig
        private class RarityData
        {
            public int BaseSpawnAmount;
            public int MinSpawnAmount;
            public int MaxSpawnAmount;
            public int RefreshTime;
            public float BuyMargin;
            public int DynamicPriceStep;
            public float DynamicPriceFactor;
        }

        public void Init()
        {
            try
            {
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] EventManager: Initializing...");

                if (!MyAPIGateway.Multiplayer.IsServer)
                {
                    MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] EventManager: Client detected - Module disabled");
                    return;
                }

                // Load global configuration
                LoadGlobalConfig();

                // Load all category events
                LoadAllCategoryEvents();

                // Check for overdue events
                CheckOverdueEvents();

                _initialized = true;
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] EventManager: Initialized successfully");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR in Init:\n{ex}");
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
                    CheckEventTriggers();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR in Update:\n{ex}");
            }
        }

        public void SaveData()
        {
            // Events are saved on trigger
        }

        public void Close()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] EventManager: Closing...");

                _categoryEvents.Clear();
                _initialized = false;

                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] EventManager: Closed");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR in Close:\n{ex}");
            }
        }

        #region Global Configuration

        /// <summary>
        /// Lädt GlobalConfig.ini
        /// </summary>
        private void LoadGlobalConfig()
        {
            try
            {
                string filename = "GlobalConfig.ini";
                
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(EventManagerModule)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(EventManagerModule)))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            line = line.Trim();
                            
                            // Skip comments and empty lines
                            if (line.StartsWith("#") || line.StartsWith(";") || string.IsNullOrWhiteSpace(line))
                                continue;
                            
                            // Parse key=value
                            if (line.Contains("="))
                            {
                                string[] parts = line.Split('=');
                                if (parts.Length >= 2)
                                {
                                    string key = parts[0].Trim();
                                    string value = parts[1].Trim();
                                    
                                    if (key == "DynamicPrice")
                                    {
                                        bool boolValue;
                                        if (bool.TryParse(value, out boolValue))
                                        {
                                            _dynamicPrice = boolValue;
                                            if (LoggerModule.DebugMode)
                                            MyLog.Default.WriteLineAndConsole(
                                                $"[PhantombiteEconomy] EventManager: DynamicPrice = {_dynamicPrice}"
                                            );
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole(
                        "[PhantombiteEconomy] EventManager: GlobalConfig.ini not found, using defaults (DynamicPrice=true)"
                    );
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR loading GlobalConfig:\n{ex}");
            }
        }

        #endregion

        #region Event Loading

        /// <summary>
        /// Lädt alle Category Events (oder erstellt neue)
        /// </summary>
        private void LoadAllCategoryEvents()
        {
            try
            {
                foreach (var category in CATEGORIES)
                {
                    string filename = GetEventFilename(category);

                    if (FileExists(filename))
                    {
                        LoadCategoryEvent(category);
                    }
                    else
                    {
                        CreateNewCategoryEvent(category);
                    }
                }

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager: Loaded {_categoryEvents.Count} category events");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR loading events:\n{ex}");
            }
        }

        /// <summary>
        /// Lädt ein Category Event aus File
        /// </summary>
        private void LoadCategoryEvent(string category)
        {
            try
            {
                string filename = GetEventFilename(category);
                string content = ReadFile(filename);

                if (string.IsNullOrWhiteSpace(content))
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager: {filename} empty, creating new");
                    CreateNewCategoryEvent(category);
                    return;
                }

                var categoryEvent = ParseEventFile(content, category);
                _categoryEvents[category] = categoryEvent;

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole(
                    $"[PhantombiteEconomy] EventManager: Loaded {category} - Next refresh: {categoryEvent.NextRefresh}"
                );
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR loading {category}:\n{ex}");
                CreateNewCategoryEvent(category);
            }
        }

        /// <summary>
        /// Parst Event File Content
        /// </summary>
        private CategoryEvent ParseEventFile(string content, string category)
        {
            var categoryEvent = new CategoryEvent(category);
            string currentSection = "";

            try
            {
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

                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = trimmed.Substring(0, equalsIndex).Trim();
                    string value = trimmed.Substring(equalsIndex + 1).Trim();

                    if (currentSection == "EventInfo")
                    {
                        if (key == "LastRefresh")
                            DateTime.TryParse(value, out categoryEvent.LastRefresh);
                        else if (key == "NextRefresh")
                            DateTime.TryParse(value, out categoryEvent.NextRefresh);
                        else if (key == "RefreshTime")
                            int.TryParse(value, out categoryEvent.RefreshTime);
                    }
                    else if (currentSection == "Items")
                    {
                        // Parse item entries: ItemName_Amount=value or ItemName_Price=value
                        if (key.EndsWith("_Amount"))
                        {
                            string itemName = key.Substring(0, key.Length - 7); // Remove "_Amount"
                            int amount;
                            if (int.TryParse(value, out amount))
                            {
                                if (!categoryEvent.Items.ContainsKey(itemName))
                                    categoryEvent.Items[itemName] = new ItemCatalogEntry { ItemName = itemName };
                                categoryEvent.Items[itemName].Amount = amount;
                            }
                        }
                        else if (key.EndsWith("_Price"))
                        {
                            string itemName = key.Substring(0, key.Length - 6); // Remove "_Price"
                            int price;
                            if (int.TryParse(value, out price))
                            {
                                if (!categoryEvent.Items.ContainsKey(itemName))
                                    categoryEvent.Items[itemName] = new ItemCatalogEntry { ItemName = itemName };
                                categoryEvent.Items[itemName].Price = price;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR parsing {category}:\n{ex}");
            }

            return categoryEvent;
        }

        #endregion

        #region Event Creation & Refresh

        /// <summary>
        /// Erstellt neues Category Event (erste Initialisierung)
        /// </summary>
        private void CreateNewCategoryEvent(string category)
        {
            try
            {
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager: Creating new event for {category}");

                var categoryEvent = new CategoryEvent(category);
                categoryEvent.LastRefresh = DateTime.Now;

                // Get refresh time from GlobalConfig (Rarity_Common as default for new events)
                var defaultRarityData = GetRarityData("Common");
                categoryEvent.RefreshTime = (defaultRarityData != null && defaultRarityData.RefreshTime > 0)
                    ? defaultRarityData.RefreshTime
                    : 21600; // Fallback: 6 Stunden
                categoryEvent.NextRefresh = DateTime.Now.AddSeconds(categoryEvent.RefreshTime);

                // Roll initial data
                RollCategoryData(categoryEvent);

                // Save to file
                SaveEventFile(categoryEvent);

                _categoryEvents[category] = categoryEvent;

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole(
                    $"[PhantombiteEconomy] EventManager: Created {category} event - {categoryEvent.Items.Count} items"
                );
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR creating event for {category}:\n{ex}");
            }
        }

        /// <summary>
        /// Würfelt neue Daten für Category
        /// Respektiert GlobalConfig.DynamicPrice:
        /// - true: Würfelt zwischen Min/Max (Dynamic Pricing)
        /// - false: Nutzt MinAmount und MinPrice (Static/Base Pricing)
        /// </summary>
        private void RollCategoryData(CategoryEvent categoryEvent)
        {
            try
            {
                categoryEvent.Items.Clear();

                // Get all items for this category from M00 (Single Source of Truth)
                var items = CategoryDefinitions.GetDisplayNamesForCategory(categoryEvent.Category);

                foreach (var itemName in items)
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager DEBUG: Processing item '{itemName}' in category '{categoryEvent.Category}'");
                    
                    // STEP 1: Get pricelist data (BasePrice, MinPrice, MaxPrice, Rarity)
                    var pricelistData = GetPricelistData(categoryEvent.Category, itemName);
                    if (pricelistData == null)
                    {
                        MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager WARNING: No pricelist data for {categoryEvent.Category}/{itemName}, skipping!");
                        continue;
                    }
                    
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole(
                        $"[PhantombiteEconomy] EventManager DEBUG: Found pricelist data for {itemName}: " +
                        $"Rarity={pricelistData.Rarity}, BasePrice={pricelistData.BasePrice}, MinPrice={pricelistData.MinPrice}, MaxPrice={pricelistData.MaxPrice}"
                    );
                    
                    // STEP 2: Get rarity data (BaseSpawnAmount, MinSpawnAmount, MaxSpawnAmount)
                    RarityData rarityData = null;
                    if (!string.IsNullOrEmpty(pricelistData.Rarity))
                    {
                        rarityData = GetRarityData(pricelistData.Rarity);
                    }
                    
                    if (rarityData == null)
                    {
                        MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager WARNING: No rarity data for '{pricelistData.Rarity}', skipping {itemName}!");
                        continue;
                    }
                    
                    // STEP 3: Calculate amount and price based on DynamicPrice setting
                    // Resolve Override: if Override_enable=true, use Over_ values, else use GlobalConfig Rarity values
                    int dynamicPriceStep = pricelistData.OverrideEnable ? pricelistData.Over_DynamicPriceStep : rarityData.DynamicPriceStep;
                    float dynamicPriceFactor = pricelistData.OverrideEnable ? pricelistData.Over_DynamicPriceFactor : rarityData.DynamicPriceFactor;
                    
                    int amount, price;
                    
                    if (_dynamicPrice)
                    {
                        amount = _rand.Next(rarityData.MinSpawnAmount, rarityData.MaxSpawnAmount + 1);
                        price  = _rand.Next(pricelistData.MinPrice, pricelistData.MaxPrice + 1);
                        
                        if (LoggerModule.DebugMode)
                        MyLog.Default.WriteLineAndConsole(
                            $"[PhantombiteEconomy] EventManager: DYNAMIC {itemName} ({pricelistData.Rarity}): " +
                            $"Amount={amount} (rolled {rarityData.MinSpawnAmount}-{rarityData.MaxSpawnAmount}), " +
                            $"Price={price} (rolled {pricelistData.MinPrice}-{pricelistData.MaxPrice}), " +
                            $"PriceStep={dynamicPriceStep}, PriceFactor={dynamicPriceFactor}" +
                            (pricelistData.OverrideEnable ? " [OVERRIDE]" : "")
                        );
                    }
                    else
                    {
                        // STATIC PRICING: Use BasePrice and BaseSpawnAmount
                        amount = rarityData.BaseSpawnAmount;
                        price = pricelistData.BasePrice;
                        
                        if (LoggerModule.DebugMode)
                        MyLog.Default.WriteLineAndConsole(
                            $"[PhantombiteEconomy] EventManager: STATIC {itemName} ({pricelistData.Rarity}): " +
                            $"Amount={amount} (BaseSpawnAmount), Price={price} (BasePrice)"
                        );
                    }

                    // STEP 4: Get Item Definition from M00
                    var itemDef = CategoryDefinitions.FindItem(categoryEvent.Category, itemName);
                    if (itemDef == null)
                    {
                        MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR: Item '{itemName}' not found in M00 for category '{categoryEvent.Category}'");
                        continue;
                    }

                    // STEP 5: Create catalog entry using M00 TypeId + SubtypeId
                    categoryEvent.Items[itemName] = new ItemCatalogEntry
                    {
                        ItemName = itemName,  // DisplayName (Section Header)
                        TypeId = itemDef.TypeId,
                        SubtypeId = itemDef.SubtypeId,
                        Amount = amount,
                        Price = price
                    };
                    
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole(
                        $"[PhantombiteEconomy] EventManager DEBUG: Created catalog entry for {itemName}: " +
                        $"TypeId={itemDef.TypeId}, SubtypeId={itemDef.SubtypeId}, Amount={amount}, Price={price}"
                    );
                }

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole(
                    $"[PhantombiteEconomy] EventManager: Rolled {categoryEvent.Items.Count} items for {categoryEvent.Category} (DynamicPrice={_dynamicPrice})"
                );
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR rolling data:\n{ex}");
            }
        }

        #endregion

        #region Public Command API

        /// <summary>
        /// Triggert sofortigen Refresh aller Categories (für !sem forcerefresh)
        /// </summary>
        public void ForceRefreshAll()
        {
            try
            {
                if (!_initialized)
                    return;

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] EventManager: ForceRefreshAll triggered by command");

                foreach (var category in CATEGORIES)
                {
                    if (!_categoryEvents.ContainsKey(category))
                        continue;

                    var categoryEvent = _categoryEvents[category];

                    // Neue Daten würfeln
                    RollCategoryData(categoryEvent);

                    // NextRefresh auf jetzt setzen → M04/M05 spawnen beim nächsten Tick sofort
                    categoryEvent.LastRefresh = DateTime.Now;
                    categoryEvent.NextRefresh = DateTime.Now;

                    // Datei speichern
                    SaveEventFile(categoryEvent);

                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager: ForceRefresh {category} — NextRefresh set to now");
                }

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] EventManager: ForceRefreshAll complete");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR in ForceRefreshAll:\n{ex}");
            }
        }

        /// <summary>
        /// Liest GlobalConfig + Pricelists neu und rollt alle Categories neu (für !sem pricelist reload)
        /// </summary>
        public void ReloadPricelists()
        {
            try
            {
                if (!_initialized)
                    return;

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] EventManager: ReloadPricelists triggered by command");

                // GlobalConfig neu laden (z.B. DynamicPrice Änderung)
                LoadGlobalConfig();

                // Alle Categories neu rollen mit aktuellen Pricelist-Dateien
                foreach (var category in CATEGORIES)
                    TriggerCategoryRefresh(category);

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] EventManager: ReloadPricelists complete");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR in ReloadPricelists:\n{ex}");
            }
        }

        #endregion

        #region Event Triggering

        /// <summary>
        /// Prüft überfällige Events beim Start
        /// </summary>
        private void CheckOverdueEvents()
        {
            try
            {
                DateTime now = DateTime.Now;
                int overdueCount = 0;

                foreach (var kvp in _categoryEvents)
                {
                    var categoryEvent = kvp.Value;

                    if (categoryEvent.NextRefresh <= now)
                    {
                        if (LoggerModule.DebugMode)
                        MyLog.Default.WriteLineAndConsole(
                            $"[PhantombiteEconomy] EventManager: {categoryEvent.Category} overdue by {(now - categoryEvent.NextRefresh).TotalMinutes:F1} minutes, triggering now"
                        );

                        TriggerCategoryRefresh(categoryEvent.Category);
                        overdueCount++;
                    }
                }

                if (overdueCount > 0)
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager: Triggered {overdueCount} overdue events");
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR checking overdue:\n{ex}");
            }
        }

        /// <summary>
        /// Prüft ob Events fällig sind
        /// </summary>
        private void CheckEventTriggers()
        {
            try
            {
                DateTime now = DateTime.Now;

                foreach (var kvp in _categoryEvents)
                {
                    var categoryEvent = kvp.Value;

                    if (categoryEvent.NextRefresh <= now)
                    {
                        TriggerCategoryRefresh(categoryEvent.Category);
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR checking triggers:\n{ex}");
            }
        }

        /// <summary>
        /// Triggert Category Refresh
        /// </summary>
        private void TriggerCategoryRefresh(string category)
        {
            try
            {
                if (!_categoryEvents.ContainsKey(category))
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager: Category {category} not found");
                    return;
                }

                var categoryEvent = _categoryEvents[category];
                DateTime now = DateTime.Now;

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager: Triggering refresh for {category}");

                // Update timestamps
                categoryEvent.LastRefresh = now;
                categoryEvent.NextRefresh = now.AddSeconds(categoryEvent.RefreshTime);

                // Roll new data
                RollCategoryData(categoryEvent);

                // Save to file
                SaveEventFile(categoryEvent);

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole(
                    $"[PhantombiteEconomy] EventManager: {category} refreshed - Next refresh: {categoryEvent.NextRefresh}"
                );
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR triggering {category}:\n{ex}");
            }
        }

        #endregion

        #region File I/O

        /// <summary>
        /// Speichert Event File
        /// </summary>
        private void SaveEventFile(CategoryEvent categoryEvent)
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                sb.AppendLine("# ==============================================================================");
                sb.AppendLine($"# CATEGORY EVENT - {categoryEvent.Category}");
                sb.AppendLine("# Global Product Catalog - All stores read from this file");
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine();

                sb.AppendLine("[EventInfo]");
                sb.AppendLine($"LastRefresh={categoryEvent.LastRefresh:O}");
                sb.AppendLine($"NextRefresh={categoryEvent.NextRefresh:O}");
                sb.AppendLine($"RefreshTime={categoryEvent.RefreshTime}");
                sb.AppendLine();

                sb.AppendLine("[Items]");
                foreach (var item in categoryEvent.Items.Values)
                {
                    // KRITISCH: Key muss SubtypeId sein (nicht DisplayName)!
                    // Grund: M04/M05 suchen mit item.Type.SubtypeId.ToString()
                    // Beispiel: Tools haben DisplayName="Welder" aber SubtypeId="WelderItem"
                    string key = item.SubtypeId;
                    
                    sb.AppendLine($"{key}_TypeId={item.TypeId}");
                    sb.AppendLine($"{key}_SubtypeId={item.SubtypeId}");
                    sb.AppendLine($"{key}_Amount={item.Amount}");
                    sb.AppendLine($"{key}_Price={item.Price}");
                }

                string filename = GetEventFilename(categoryEvent.Category);
                WriteFile(filename, sb.ToString());

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager: Saved {filename}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR saving event:\n{ex}");
            }
        }

        private string GetEventFilename(string category)
        {
            return $"RAM_StoreEvent_{category}.ini";
        }

        private bool FileExists(string filename)
        {
            try
            {
                return MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(EventManagerModule));
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
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(EventManagerModule)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(EventManagerModule)))
                    {
                        return reader.ReadToEnd();
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR reading {filename}:\n{ex}");
                return null;
            }
        }

        private void WriteFile(string filename, string content)
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, typeof(EventManagerModule)))
                {
                    writer.Write(content);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR writing {filename}:\n{ex}");
            }
        }

        #endregion

        #region Item Mapping

        /// <summary>
        /// Gibt Items für Category zurück
        /// Basierend auf M02 Pricelists mit SubtypeIds
        /// </summary>

        /// <summary>
        /// Gibt ObjectBuilder TypeId für Category zurück
        /// KRITISCH: TypeId + SubtypeId zusammen identifizieren Items eindeutig!
        /// </summary>

        /// <summary>
        /// Liest Pricelist Datei und extrahiert BasePrice/MinAmount/Max Values für ein Item
        /// </summary>
        private PricelistItemData GetPricelistData(string category, string itemName)
        {
            try
            {
                string filename = $"Pricelist_{category}.ini";
                
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(EventManagerModule)))
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager WARNING: Pricelist not found: {filename}");
                    return null;
                }

                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(EventManagerModule)))
                {
                    string line;
                    string currentSection = "";
                    
                    while ((line = reader.ReadLine()) != null)
                    {
                        string trimmed = line.Trim();
                        
                        // Skip empty lines and comments
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                            continue;
                        
                        // Section header
                        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                        {
                            currentSection = trimmed.Substring(1, trimmed.Length - 2);
                            continue;
                        }
                        
                        // Found our item section
                        if (currentSection == itemName)
                        {
                            if (LoggerModule.DebugMode)
                            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager DEBUG: Found section [{itemName}] in {filename}");
                            
                            // Parse all values for this item
                            var data = new PricelistItemData();
                            
                            // Read ALL lines until next section or EOF
                            while ((line = reader.ReadLine()) != null)
                            {
                                string itemLine = line.Trim();
                                
                                // Stop at next section
                                if (itemLine.StartsWith("["))
                                    break;
                                
                                // Skip empty lines and comments
                                if (string.IsNullOrWhiteSpace(itemLine) || itemLine.StartsWith("#"))
                                    continue;
                                
                                // Parse key=value pairs
                                if (!itemLine.Contains("="))
                                    continue;
                                
                                string[] parts = itemLine.Split(new[] { '=' }, 2);
                                if (parts.Length != 2)
                                    continue;
                                
                                string key = parts[0].Trim();
                                string value = parts[1].Trim();
                                
                                // Parse based on key
                                switch (key)
                                {
                                    case "SubtypeId":
                                        data.SubtypeId = value;
                                        break;
                                    case "Rarity":
                                        data.Rarity = value;
                                        break;
                                    case "Sell_BasePrice":
                                        int.TryParse(value, out data.BasePrice);
                                        break;
                                    case "Sell_MinPrice":
                                        int.TryParse(value, out data.MinPrice);
                                        break;
                                    case "Sell_MaxPrice":
                                        int.TryParse(value, out data.MaxPrice);
                                        break;
                                    case "Override_enable":
                                        bool.TryParse(value, out data.OverrideEnable);
                                        break;
                                    case "Over_Buy_Margin":
                                        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out data.Over_BuyMargin);
                                        break;
                                    case "Over_DynamicPriceStep":
                                        int.TryParse(value, out data.Over_DynamicPriceStep);
                                        break;
                                    case "Over_DynamicPriceFactor":
                                        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out data.Over_DynamicPriceFactor);
                                        break;
                                    // Ignore other fields
                                }
                            }
                            
                            if (LoggerModule.DebugMode)
                            MyLog.Default.WriteLineAndConsole(
                                $"[PhantombiteEconomy] EventManager DEBUG: Parsed {itemName}: " +
                                $"Rarity={data.Rarity}, BasePrice={data.BasePrice}, MinPrice={data.MinPrice}, MaxPrice={data.MaxPrice}"
                            );
                            
                            return data;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR reading pricelist for {category}/{itemName}:\n{ex}");
            }
            
            return null;
        }

        /// <summary>
        /// Liest GlobalConfig und extrahiert Rarity Data (BaseSpawnAmount, MinSpawnAmount, MaxSpawnAmount, etc.)
        /// </summary>
        private RarityData GetRarityData(string rarity)
        {
            try
            {
                string content = ReadFile("GlobalConfig.ini");  // Use local ReadFile method
                if (string.IsNullOrEmpty(content))
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR: GlobalConfig.ini is empty!");
                    return null;
                }

                string sectionHeader = $"[Rarity_{rarity}]";
                bool inSection = false;
                
                RarityData data = new RarityData();
                
                foreach (var line in content.Split('\n'))
                {
                    string trimmed = line.Trim();
                    
                    // Check if we entered the target section
                    if (trimmed.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        inSection = true;
                        continue;
                    }
                    
                    // Check if we left the section
                    if (inSection && trimmed.StartsWith("["))
                    {
                        // Entered another section, stop
                        break;
                    }
                    
                    // Parse key=value pairs in our section
                    if (inSection && trimmed.Contains("="))
                    {
                        var parts = trimmed.Split(new char[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string value = parts[1].Trim();
                            
                            if (key == "BaseSpawnAmount")
                                int.TryParse(value, out data.BaseSpawnAmount);
                            else if (key == "MinSpawnAmount")
                                int.TryParse(value, out data.MinSpawnAmount);
                            else if (key == "MaxSpawnAmount")
                                int.TryParse(value, out data.MaxSpawnAmount);
                            else if (key == "RefreshTime")
                                int.TryParse(value, out data.RefreshTime);
                            else if (key == "Buy_Margin")
                                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out data.BuyMargin);
                            else if (key == "DynamicPriceStep")
                                int.TryParse(value, out data.DynamicPriceStep);
                            else if (key == "DynamicPriceFactor")
                                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out data.DynamicPriceFactor);
                        }
                    }
                }
                
                if (inSection)
                {
                    if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole(
                        $"[PhantombiteEconomy] EventManager DEBUG: Parsed Rarity '{rarity}': " +
                        $"BaseSpawnAmount={data.BaseSpawnAmount}, MinSpawnAmount={data.MinSpawnAmount}, MaxSpawnAmount={data.MaxSpawnAmount}"
                    );
                    return data;
                }
                else
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager WARNING: Rarity section '{rarity}' not found in GlobalConfig!");
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] EventManager ERROR reading rarity data for '{rarity}':\n{ex}");
            }
            
            return null;
        }

        #endregion
    }
}