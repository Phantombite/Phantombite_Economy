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
    /// M01 - FileManager
    /// Komplettes Storage-Management mit Self-Healing
    /// 
    /// - GlobalConfig mit Rarity-Defaults
    /// - 9 Pricelists (Ore, Ingots, Components, Tools, Weapons, Ammo, Food, Seeds, Prototech)
    /// - Section-basiertes INI Parsing
    /// - Item-Validierung
    /// 
    /// Storage: Storage\PhantombiteEconomy\ (FLAT, keine Unterordner)
    /// Server-only: Alle File-Operationen
    /// </summary>
    public class FileManagerModule : IModule
    {
        public string ModuleName => "FileManager";

        // Config Files
        private const string GLOBAL_CONFIG_FILE = "GlobalConfig.ini";
        private readonly string[] PRICELIST_FILES = {
            "Pricelist_Ore.ini",
            "Pricelist_Ingots.ini",
            "Pricelist_Components.ini",
            "Pricelist_Tools.ini",
            "Pricelist_Weapons.ini",
            "Pricelist_Ammo.ini",
            "Pricelist_Food.ini",
            "Pricelist_Seeds.ini",
            "Pricelist_Consumables.ini",            
            "Pricelist_Prototech.ini",
            "Pricelist_Blacklist.ini"
        };

        // Data Structures
        private Dictionary<string, string> _globalConfig = new Dictionary<string, string>();
        private Dictionary<string, Dictionary<string, ItemData>> _pricelists = new Dictionary<string, Dictionary<string, ItemData>>();
        private bool _configsLoaded = false;

        // Item Data Class
        public class ItemData
        {
            public string Category;
            public string Rarity;
            public int Sell_BasePrice;
            public int Sell_MinPrice;
            public int Sell_MaxPrice;
            public bool Override_enable;
            public float Over_Buy_Margin;
            public int Over_DynamicPriceStep;
            public float Over_DynamicPriceFactor;
        }

        public void Init()
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: Initializing...");

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                EnsureConfigsExist();
                LoadAllConfigs();
            }

            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: Initialized");
        }

        public void Update()
        {
            // No update logic needed
        }

        public void SaveData()
        {
            // Configs saved on-demand
        }

        public void Close()
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: Closing...");
            _globalConfig.Clear();
            _pricelists.Clear();
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: Closed");
        }

        #region Self-Healing

        private void EnsureConfigsExist()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: Self-Healing check...");

                int created = 0;

                if (!FileExists(GLOBAL_CONFIG_FILE))
                {
                    DeployGlobalConfig();
                    created++;
                }

                foreach (var pricelistFile in PRICELIST_FILES)
                {
                    if (!FileExists(pricelistFile))
                    {
                        DeployPricelist(pricelistFile);
                        created++;
                    }
                }

                if (created > 0)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Created {created} missing configs");
                else
                    MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: All configs OK");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager ERROR in EnsureConfigsExist:\n{ex}");
            }
        }

        #endregion

        #region Config Deployment

        private void DeployGlobalConfig()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine("# GLOBAL CONFIG - PhantombiteEconomy");
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine();
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine("# GLOBAL CONFIG - ShopSystem");
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("[General]");
                sb.AppendLine("ShopSystem_enable=true");
                sb.AppendLine("DynamicPrice_enable=true");
                sb.AppendLine("TransferSystem_enable=true");
                sb.AppendLine("DebugMode=false");
                sb.AppendLine();

                sb.AppendLine("[StationRefill]");
                sb.AppendLine("Enabled=true");
                sb.AppendLine("IntervalHours=5");
                sb.AppendLine("AmmoSubtype=RapidFireAutomaticRifleGun_Mag_50rd");
                sb.AppendLine("FactionTags=SPT");
                sb.AppendLine();

                sb.AppendLine("[Rarity_Common]");
                sb.AppendLine("BaseSpawnAmount=4000");
                sb.AppendLine("MinSpawnAmount=3000");
                sb.AppendLine("MaxSpawnAmount=5000");
                sb.AppendLine("RefreshTime=21600");
                sb.AppendLine("Buy_Margin=0.7");
                sb.AppendLine("DynamicPriceStep=50");
                sb.AppendLine("DynamicPriceFactor=1.05");
                sb.AppendLine();

                sb.AppendLine("[Rarity_Uncommon]");
                sb.AppendLine("BaseSpawnAmount=2000");
                sb.AppendLine("MinSpawnAmount=1500");
                sb.AppendLine("MaxSpawnAmount=3000");
                sb.AppendLine("RefreshTime=43200");
                sb.AppendLine("Buy_Margin=0.65");
                sb.AppendLine("DynamicPriceStep=30");
                sb.AppendLine("DynamicPriceFactor=1.08");
                sb.AppendLine();

                sb.AppendLine("[Rarity_Rare]");
                sb.AppendLine("BaseSpawnAmount=1000");
                sb.AppendLine("MinSpawnAmount=500");
                sb.AppendLine("MaxSpawnAmount=1500");
                sb.AppendLine("RefreshTime=86400");
                sb.AppendLine("Buy_Margin=0.6");
                sb.AppendLine("DynamicPriceStep=20");
                sb.AppendLine("DynamicPriceFactor=1.12");
                sb.AppendLine();

                sb.AppendLine("[Rarity_Epic]");
                sb.AppendLine("BaseSpawnAmount=300");
                sb.AppendLine("MinSpawnAmount=100");
                sb.AppendLine("MaxSpawnAmount=500");
                sb.AppendLine("RefreshTime=172800");
                sb.AppendLine("Buy_Margin=0.5");
                sb.AppendLine("DynamicPriceStep=10");
                sb.AppendLine("DynamicPriceFactor=1.15");
                sb.AppendLine();

                sb.AppendLine("[Rarity_Legendary]");
                sb.AppendLine("BaseSpawnAmount=50");
                sb.AppendLine("MinSpawnAmount=10");
                sb.AppendLine("MaxSpawnAmount=100");
                sb.AppendLine("RefreshTime=259200");
                sb.AppendLine("Buy_Margin=0.4");
                sb.AppendLine("DynamicPriceStep=5");
                sb.AppendLine("DynamicPriceFactor=1.20");
                sb.AppendLine();

                sb.AppendLine("[Rarity_Special]");
                sb.AppendLine("BaseSpawnAmount=1500");
                sb.AppendLine("MinSpawnAmount=1000");
                sb.AppendLine("MaxSpawnAmount=2000");
                sb.AppendLine("RefreshTime=43200");
                sb.AppendLine("Buy_Margin=0.6");
                sb.AppendLine("DynamicPriceStep=30");
                sb.AppendLine("DynamicPriceFactor=1.08");
                sb.AppendLine();

                sb.AppendLine("[Rarity_Blacklist]");
                sb.AppendLine("BaseSpawnAmount=0");
                sb.AppendLine("MinSpawnAmount=0");
                sb.AppendLine("MaxSpawnAmount=0");
                sb.AppendLine("RefreshTime=0");
                sb.AppendLine("Buy_Margin=0.0");
                sb.AppendLine("DynamicPriceStep=0");
                sb.AppendLine("DynamicPriceFactor=0");
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine();

                sb.AppendLine("# ==============================================================================");
                sb.AppendLine("# GLOBAL CONFIG - AutoTransferSystem");
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine();
                sb.AppendLine("[AutoTransfer_KeepList]");
                sb.AppendLine("# Items die IMMER im Spieler-Inventar bleiben beim AutoTransfer IN");
                sb.AppendLine("# Format: SubtypeId=Mindestmenge");
                sb.AppendLine();
                sb.AppendLine("# --- Werkzeuge ---");
                sb.AppendLine("WelderItem=1");
                sb.AppendLine("Welder2Item=1");
                sb.AppendLine("Welder3Item=1");
                sb.AppendLine("Welder4Item=1");
                sb.AppendLine("AngleGrinderItem=1");
                sb.AppendLine("AngleGrinder2Item=1");
                sb.AppendLine("AngleGrinder3Item=1");
                sb.AppendLine("AngleGrinder4Item=1");
                sb.AppendLine("HandDrillItem=1");
                sb.AppendLine("HandDrill2Item=1");
                sb.AppendLine("HandDrill3Item=1");
                sb.AppendLine("HandDrill4Item=1");
                sb.AppendLine("FlareGunItem=1");
                sb.AppendLine();
                sb.AppendLine("# --- Handfeuerwaffen ---");
                sb.AppendLine("SemiAutoPistolItem=1");
                sb.AppendLine("FullAutoPistolItem=1");
                sb.AppendLine("ElitePistolItem=1");
                sb.AppendLine("AutomaticRifleItem=1");
                sb.AppendLine("PreciseAutomaticRifleItem=1");
                sb.AppendLine("RapidFireAutomaticRifleItem=1");
                sb.AppendLine("UltimateAutomaticRifleItem=1");
                sb.AppendLine("BasicHandHeldLauncherItem=1");
                sb.AppendLine("AdvancedHandHeldLauncherItem=1");
                sb.AppendLine();
                sb.AppendLine("# --- Munition (10x) ---");
                sb.AppendLine("SemiAutoPistolMagazine=10");
                sb.AppendLine("FullAutoPistolMagazine=10");
                sb.AppendLine("ElitePistolMagazine=10");
                sb.AppendLine("AutomaticRifleGun_Mag_20rd=10");
                sb.AppendLine("PreciseAutomaticRifleGun_Mag_5rd=10");
                sb.AppendLine("RapidFireAutomaticRifleGun_Mag_50rd=10");
                sb.AppendLine("UltimateAutomaticRifleGun_Mag_30rd=10");
                sb.AppendLine("FlareClip=10");
                sb.AppendLine();
                sb.AppendLine("# --- Raketen (5x) ---");
                sb.AppendLine("Missile200mm=5");
                sb.AppendLine();
                sb.AppendLine("# --- Consumables ---");
                sb.AppendLine("Medkit=2");
                sb.AppendLine("Powerkit=1");
                sb.AppendLine("RadiationKit=1");
                sb.AppendLine();

                WriteFile(GLOBAL_CONFIG_FILE, sb.ToString());
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Created {GLOBAL_CONFIG_FILE}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager ERROR deploying GlobalConfig:\n{ex}");
            }
        }

        private void DeployPricelist(string filename)
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                // ============================================================
                // PRICELIST_ORE.INI - KOMPLETT (11 Items)
                // ============================================================
                if (filename == "Pricelist_Ore.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - ORE");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Stone]");
                    sb.AppendLine("SubtypeId=Stone");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=10");
                    sb.AppendLine("Sell_MinPrice=9");
                    sb.AppendLine("Sell_MaxPrice=11");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Ice]");
                    sb.AppendLine("SubtypeId=Ice");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=50");
                    sb.AppendLine("Sell_MinPrice=35");
                    sb.AppendLine("Sell_MaxPrice=70");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Iron]");
                    sb.AppendLine("SubtypeId=Iron");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=80");
                    sb.AppendLine("Sell_MaxPrice=125");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Nickel]");
                    sb.AppendLine("SubtypeId=Nickel");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=80");
                    sb.AppendLine("Sell_MaxPrice=125");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Silicon]");
                    sb.AppendLine("SubtypeId=Silicon");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=80");
                    sb.AppendLine("Sell_MaxPrice=125");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Cobalt]");
                    sb.AppendLine("SubtypeId=Cobalt");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=300");
                    sb.AppendLine("Sell_MinPrice=210");
                    sb.AppendLine("Sell_MaxPrice=420");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Magnesium]");
                    sb.AppendLine("SubtypeId=Magnesium");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=200");
                    sb.AppendLine("Sell_MinPrice=140");
                    sb.AppendLine("Sell_MaxPrice=280");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Silver]");
                    sb.AppendLine("SubtypeId=Silver");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=200");
                    sb.AppendLine("Sell_MinPrice=140");
                    sb.AppendLine("Sell_MaxPrice=280");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Gold]");
                    sb.AppendLine("SubtypeId=Gold");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=200");
                    sb.AppendLine("Sell_MinPrice=120");
                    sb.AppendLine("Sell_MaxPrice=320");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Platinum]");
                    sb.AppendLine("SubtypeId=Platinum");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=400");
                    sb.AppendLine("Sell_MinPrice=240");
                    sb.AppendLine("Sell_MaxPrice=640");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Uranium]");
                    sb.AppendLine("SubtypeId=Uranium");
                    sb.AppendLine("Category=Ore");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=500");
                    sb.AppendLine("Sell_MinPrice=300");
                    sb.AppendLine("Sell_MaxPrice=800");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                }
                // ============================================================
                // PRICELIST_INGOTS.INI - KOMPLETT (10 Items)
                // ============================================================
                else if (filename == "Pricelist_Ingots.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - INGOTS");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Gravel]");
                    sb.AppendLine("SubtypeId=Stone");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=1");
                    sb.AppendLine("Sell_MinPrice=1");
                    sb.AppendLine("Sell_MaxPrice=1");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Iron]");
                    sb.AppendLine("SubtypeId=Iron");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=10");
                    sb.AppendLine("Sell_MinPrice=8");
                    sb.AppendLine("Sell_MaxPrice=13");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Nickel]");
                    sb.AppendLine("SubtypeId=Nickel");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=40");
                    sb.AppendLine("Sell_MinPrice=32");
                    sb.AppendLine("Sell_MaxPrice=50");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Silicon]");
                    sb.AppendLine("SubtypeId=Silicon");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=8");
                    sb.AppendLine("Sell_MinPrice=6");
                    sb.AppendLine("Sell_MaxPrice=10");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Cobalt]");
                    sb.AppendLine("SubtypeId=Cobalt");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=120");
                    sb.AppendLine("Sell_MinPrice=84");
                    sb.AppendLine("Sell_MaxPrice=168");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Magnesium]");
                    sb.AppendLine("SubtypeId=Magnesium");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=80");
                    sb.AppendLine("Sell_MinPrice=56");
                    sb.AppendLine("Sell_MaxPrice=112");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Silver]");
                    sb.AppendLine("SubtypeId=Silver");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=70");
                    sb.AppendLine("Sell_MaxPrice=140");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Gold]");
                    sb.AppendLine("SubtypeId=Gold");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=240");
                    sb.AppendLine("Sell_MinPrice=144");
                    sb.AppendLine("Sell_MaxPrice=384");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Platinum]");
                    sb.AppendLine("SubtypeId=Platinum");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=1600");
                    sb.AppendLine("Sell_MinPrice=960");
                    sb.AppendLine("Sell_MaxPrice=2560");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Uranium]");
                    sb.AppendLine("SubtypeId=Uranium");
                    sb.AppendLine("Category=Ingots");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=2000");
                    sb.AppendLine("Sell_MinPrice=1200");
                    sb.AppendLine("Sell_MaxPrice=3200");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                }
                // ============================================================
                // PRICELIST_COMPONENTS.INI - Komplett
                // ============================================================
                else if (filename == "Pricelist_Components.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - COMPONENTS");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Construction]");
                    sb.AppendLine("SubtypeId=Construction");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=10");
                    sb.AppendLine("Sell_MinPrice=9");
                    sb.AppendLine("Sell_MaxPrice=11");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[InteriorPlate]");
                    sb.AppendLine("SubtypeId=InteriorPlate");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=3");
                    sb.AppendLine("Sell_MinPrice=3");
                    sb.AppendLine("Sell_MaxPrice=3");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[SteelPlate]");
                    sb.AppendLine("SubtypeId=SteelPlate");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=21");
                    sb.AppendLine("Sell_MinPrice=19");
                    sb.AppendLine("Sell_MaxPrice=23");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Girder]");
                    sb.AppendLine("SubtypeId=Girder");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=6");
                    sb.AppendLine("Sell_MinPrice=5");
                    sb.AppendLine("Sell_MaxPrice=7");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MetalGrid]");
                    sb.AppendLine("SubtypeId=MetalGrid");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=66");
                    sb.AppendLine("Sell_MinPrice=53");
                    sb.AppendLine("Sell_MaxPrice=83");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[SmallTube]");
                    sb.AppendLine("SubtypeId=SmallTube");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=5");
                    sb.AppendLine("Sell_MinPrice=4");
                    sb.AppendLine("Sell_MaxPrice=6");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[LargeTube]");
                    sb.AppendLine("SubtypeId=LargeTube");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=30");
                    sb.AppendLine("Sell_MinPrice=24");
                    sb.AppendLine("Sell_MaxPrice=38");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Motor]");
                    sb.AppendLine("SubtypeId=Motor");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=220");
                    sb.AppendLine("Sell_MinPrice=176");
                    sb.AppendLine("Sell_MaxPrice=275");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[BulletproofGlass]");
                    sb.AppendLine("SubtypeId=BulletproofGlass");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=60");
                    sb.AppendLine("Sell_MinPrice=48");
                    sb.AppendLine("Sell_MaxPrice=75");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();

                    sb.AppendLine("[Display]");
                    sb.AppendLine("SubtypeId=Display");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=80");
                    sb.AppendLine("Sell_MinPrice=56");
                    sb.AppendLine("Sell_MaxPrice=112");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Computer]");
                    sb.AppendLine("SubtypeId=Computer");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=180");
                    sb.AppendLine("Sell_MinPrice=126");
                    sb.AppendLine("Sell_MaxPrice=252");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[RadioCommunication]");
                    sb.AppendLine("SubtypeId=RadioCommunication");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=70");
                    sb.AppendLine("Sell_MinPrice=49");
                    sb.AppendLine("Sell_MaxPrice=98");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Detector]");
                    sb.AppendLine("SubtypeId=Detector");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=400");
                    sb.AppendLine("Sell_MinPrice=280");
                    sb.AppendLine("Sell_MaxPrice=560");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[SolarCell]");
                    sb.AppendLine("SubtypeId=SolarCell");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=80");
                    sb.AppendLine("Sell_MinPrice=56");
                    sb.AppendLine("Sell_MaxPrice=112");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[PowerCell]");
                    sb.AppendLine("SubtypeId=PowerCell");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=160");
                    sb.AppendLine("Sell_MinPrice=112");
                    sb.AppendLine("Sell_MaxPrice=224");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Explosives]");
                    sb.AppendLine("SubtypeId=Explosives");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=70");
                    sb.AppendLine("Sell_MaxPrice=140");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Reactor]");
                    sb.AppendLine("SubtypeId=Reactor");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=10000");
                    sb.AppendLine("Sell_MinPrice=6000");
                    sb.AppendLine("Sell_MaxPrice=16000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Thrust]");
                    sb.AppendLine("SubtypeId=Thrust");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=4000");
                    sb.AppendLine("Sell_MinPrice=2400");
                    sb.AppendLine("Sell_MaxPrice=6400");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[GravityGenerator]");
                    sb.AppendLine("SubtypeId=GravityGenerator");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=800");
                    sb.AppendLine("Sell_MinPrice=480");
                    sb.AppendLine("Sell_MaxPrice=1280");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Medical]");
                    sb.AppendLine("SubtypeId=Medical");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=150");
                    sb.AppendLine("Sell_MinPrice=90");
                    sb.AppendLine("Sell_MaxPrice=240");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Superconductor]");
                    sb.AppendLine("SubtypeId=Superconductor");
                    sb.AppendLine("Category=Components");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=3000");
                    sb.AppendLine("Sell_MinPrice=1800");
                    sb.AppendLine("Sell_MaxPrice=4800");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                }
                // ============================================================
                // PRICELIST_TOOLS.INI - Komplett
                // ============================================================
                else if (filename == "Pricelist_Tools.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - TOOLS");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Welder]");
                    sb.AppendLine("SubtypeId=WelderItem");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=60");
                    sb.AppendLine("Sell_MinPrice=54");
                    sb.AppendLine("Sell_MaxPrice=66");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Welder2]");
                    sb.AppendLine("SubtypeId=Welder2Item");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=240");
                    sb.AppendLine("Sell_MinPrice=192");
                    sb.AppendLine("Sell_MaxPrice=300");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();

                    sb.AppendLine("[Welder3]");
                    sb.AppendLine("SubtypeId=Welder3Item");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=480");
                    sb.AppendLine("Sell_MinPrice=336");
                    sb.AppendLine("Sell_MaxPrice=672");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();

                    sb.AppendLine("[Welder4]");
                    sb.AppendLine("SubtypeId=Welder4Item");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=2080");
                    sb.AppendLine("Sell_MinPrice=1248");
                    sb.AppendLine("Sell_MaxPrice=3328");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[AngleGrinder]");
                    sb.AppendLine("SubtypeId=AngleGrinderItem");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=80");
                    sb.AppendLine("Sell_MinPrice=72");
                    sb.AppendLine("Sell_MaxPrice=88");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[AngleGrinder2]");
                    sb.AppendLine("SubtypeId=AngleGrinder2Item");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=200");
                    sb.AppendLine("Sell_MinPrice=160");
                    sb.AppendLine("Sell_MaxPrice=250");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();

                    sb.AppendLine("[AngleGrinder3]");
                    sb.AppendLine("SubtypeId=AngleGrinder3Item");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=300");
                    sb.AppendLine("Sell_MinPrice=210");
                    sb.AppendLine("Sell_MaxPrice=420");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();

                    sb.AppendLine("[AngleGrinder4]");
                    sb.AppendLine("SubtypeId=AngleGrinder4Item");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=1900");
                    sb.AppendLine("Sell_MinPrice=1140");
                    sb.AppendLine("Sell_MaxPrice=3040");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[HandDrill]");
                    sb.AppendLine("SubtypeId=HandDrillItem");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=120");
                    sb.AppendLine("Sell_MinPrice=108");
                    sb.AppendLine("Sell_MaxPrice=132");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[HandDrill2]");
                    sb.AppendLine("SubtypeId=HandDrill2Item");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=360");
                    sb.AppendLine("Sell_MinPrice=288");
                    sb.AppendLine("Sell_MaxPrice=450");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();

                    sb.AppendLine("[HandDrill3]");
                    sb.AppendLine("SubtypeId=HandDrill3Item");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=560");
                    sb.AppendLine("Sell_MinPrice=392");
                    sb.AppendLine("Sell_MaxPrice=784");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();

                    sb.AppendLine("[HandDrill4]");
                    sb.AppendLine("SubtypeId=HandDrill4Item");
                    sb.AppendLine("Category=Tools");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=2160");
                    sb.AppendLine("Sell_MinPrice=1296");
                    sb.AppendLine("Sell_MaxPrice=3456");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                }
                // ============================================================
                // PRICELIST_WEAPONS.INI - Komplett
                // ============================================================
                else if (filename == "Pricelist_Weapons.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - WEAPONS");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[SemiAutoPistol]");
                    sb.AppendLine("SubtypeId=SemiAutoPistolItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=90");
                    sb.AppendLine("Sell_MaxPrice=110");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[FullAutoPistol]");
                    sb.AppendLine("SubtypeId=FullAutoPistolItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=300");
                    sb.AppendLine("Sell_MinPrice=240");
                    sb.AppendLine("Sell_MaxPrice=375");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[AutomaticRifle]");
                    sb.AppendLine("SubtypeId=AutomaticRifleItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=400");
                    sb.AppendLine("Sell_MinPrice=320");
                    sb.AppendLine("Sell_MaxPrice=500");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[ElitePistol]");
                    sb.AppendLine("SubtypeId=ElitePistolItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=800");
                    sb.AppendLine("Sell_MinPrice=560");
                    sb.AppendLine("Sell_MaxPrice=1120");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[RapidFireAutomaticRifle]");
                    sb.AppendLine("SubtypeId=RapidFireAutomaticRifleItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=1200");
                    sb.AppendLine("Sell_MinPrice=840");
                    sb.AppendLine("Sell_MaxPrice=1680");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();

                    sb.AppendLine("[PreciseAutomaticRifle]");
                    sb.AppendLine("SubtypeId=PreciseAutomaticRifleItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=1500");
                    sb.AppendLine("Sell_MinPrice=1050");
                    sb.AppendLine("Sell_MaxPrice=2100");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[UltimateAutomaticRifle]");
                    sb.AppendLine("SubtypeId=UltimateAutomaticRifleItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=3000");
                    sb.AppendLine("Sell_MinPrice=1800");
                    sb.AppendLine("Sell_MaxPrice=4800");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[BasicHandHeldLauncher]");
                    sb.AppendLine("SubtypeId=BasicHandHeldLauncherItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=5000");
                    sb.AppendLine("Sell_MinPrice=3000");
                    sb.AppendLine("Sell_MaxPrice=8000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[AdvancedHandHeldLauncher]");
                    sb.AppendLine("SubtypeId=AdvancedHandHeldLauncherItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=8000");
                    sb.AppendLine("Sell_MinPrice=4800");
                    sb.AppendLine("Sell_MaxPrice=12800");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[FlareGun]");
                    sb.AppendLine("SubtypeId=FlareGunItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=150");
                    sb.AppendLine("Sell_MinPrice=120");
                    sb.AppendLine("Sell_MaxPrice=188");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[CubePlacer]");
                    sb.AppendLine("SubtypeId=CubePlacerItem");
                    sb.AppendLine("Category=Weapons");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=50");
                    sb.AppendLine("Sell_MinPrice=45");
                    sb.AppendLine("Sell_MaxPrice=55");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                }
                // ============================================================
                // PRICELIST_AMMO.INI - Komplett
                // ============================================================
                else if (filename == "Pricelist_Ammo.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - AMMO");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[SemiAutoPistolMagazine]");
                    sb.AppendLine("SubtypeId=SemiAutoPistolMagazine");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=8");
                    sb.AppendLine("Sell_MinPrice=7");
                    sb.AppendLine("Sell_MaxPrice=9");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[FullAutoPistolMagazine]");
                    sb.AppendLine("SubtypeId=FullAutoPistolMagazine");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=20");
                    sb.AppendLine("Sell_MinPrice=16");
                    sb.AppendLine("Sell_MaxPrice=25");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[AutomaticRifleGun_Mag_20rd]");
                    sb.AppendLine("SubtypeId=AutomaticRifleGun_Mag_20rd");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=30");
                    sb.AppendLine("Sell_MinPrice=24");
                    sb.AppendLine("Sell_MaxPrice=38");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[FlareClip]");
                    sb.AppendLine("SubtypeId=FlareClip");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=10");
                    sb.AppendLine("Sell_MinPrice=8");
                    sb.AppendLine("Sell_MaxPrice=13");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[ElitePistolMagazine]");
                    sb.AppendLine("SubtypeId=ElitePistolMagazine");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=60");
                    sb.AppendLine("Sell_MinPrice=42");
                    sb.AppendLine("Sell_MaxPrice=48");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[RapidFireAutomaticRifleGun_Mag_50rd]");
                    sb.AppendLine("SubtypeId=RapidFireAutomaticRifleGun_Mag_50rd");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=70");
                    sb.AppendLine("Sell_MaxPrice=140");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[PreciseAutomaticRifleGun_Mag_5rd]");
                    sb.AppendLine("SubtypeId=PreciseAutomaticRifleGun_Mag_5rd");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=150");
                    sb.AppendLine("Sell_MinPrice=105");
                    sb.AppendLine("Sell_MaxPrice=210");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[UltimateAutomaticRifleGun_Mag_30rd]");
                    sb.AppendLine("SubtypeId=UltimateAutomaticRifleGun_Mag_30rd");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=300");
                    sb.AppendLine("Sell_MinPrice=180");
                    sb.AppendLine("Sell_MaxPrice=480");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[NATO_25x184mm]");
                    sb.AppendLine("SubtypeId=NATO_25x184mm");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=400");
                    sb.AppendLine("Sell_MinPrice=240");
                    sb.AppendLine("Sell_MaxPrice=640");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[AutocannonClip]");
                    sb.AppendLine("SubtypeId=AutocannonClip");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=500");
                    sb.AppendLine("Sell_MinPrice=300");
                    sb.AppendLine("Sell_MaxPrice=800");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Missile200mm]");
                    sb.AppendLine("SubtypeId=Missile200mm");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=800");
                    sb.AppendLine("Sell_MinPrice=480");
                    sb.AppendLine("Sell_MaxPrice=1280");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MediumCalibreAmmo]");
                    sb.AppendLine("SubtypeId=MediumCalibreAmmo");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=600");
                    sb.AppendLine("Sell_MinPrice=360");
                    sb.AppendLine("Sell_MaxPrice=960");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[LargeCalibreAmmo]");
                    sb.AppendLine("SubtypeId=LargeCalibreAmmo");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=1000");
                    sb.AppendLine("Sell_MinPrice=600");
                    sb.AppendLine("Sell_MaxPrice=1600");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[SmallRailgunAmmo]");
                    sb.AppendLine("SubtypeId=SmallRailgunAmmo");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=700");
                    sb.AppendLine("Sell_MinPrice=420");
                    sb.AppendLine("Sell_MaxPrice=1120");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();
                    
                    sb.AppendLine("[LargeRailgunAmmo]");
                    sb.AppendLine("SubtypeId=LargeRailgunAmmo");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=1200");
                    sb.AppendLine("Sell_MinPrice=720");
                    sb.AppendLine("Sell_MaxPrice=1920");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[NATO_5p56x45mm]");
                    sb.AppendLine("SubtypeId=NATO_5p56x45mm");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=25");
                    sb.AppendLine("Sell_MinPrice=20");
                    sb.AppendLine("Sell_MaxPrice=31");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();

                    sb.AppendLine("[InteriorTurret_Mag_100rd]");
                    sb.AppendLine("SubtypeId=InteriorTurret_Mag_100rd");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=80");
                    sb.AppendLine("Sell_MinPrice=56");
                    sb.AppendLine("Sell_MaxPrice=112");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();

                    sb.AppendLine("[FireworksBoxRed]");
                    sb.AppendLine("SubtypeId=FireworksBoxRed");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=15");
                    sb.AppendLine("Sell_MinPrice=12");
                    sb.AppendLine("Sell_MaxPrice=19");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();

                    sb.AppendLine("[FireworksBoxGreen]");
                    sb.AppendLine("SubtypeId=FireworksBoxGreen");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=15");
                    sb.AppendLine("Sell_MinPrice=12");
                    sb.AppendLine("Sell_MaxPrice=19");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();

                    sb.AppendLine("[FireworksBoxBlue]");
                    sb.AppendLine("SubtypeId=FireworksBoxBlue");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=15");
                    sb.AppendLine("Sell_MinPrice=12");
                    sb.AppendLine("Sell_MaxPrice=19");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();

                    sb.AppendLine("[FireworksBoxRainbow]");
                    sb.AppendLine("SubtypeId=FireworksBoxRainbow");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=15");
                    sb.AppendLine("Sell_MinPrice=12");
                    sb.AppendLine("Sell_MaxPrice=19");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();

                    sb.AppendLine("[FireworksBoxPink]");
                    sb.AppendLine("SubtypeId=FireworksBoxPink");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=15");
                    sb.AppendLine("Sell_MinPrice=12");
                    sb.AppendLine("Sell_MaxPrice=19");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();

                    sb.AppendLine("[FireworksBoxYellow]");
                    sb.AppendLine("SubtypeId=FireworksBoxYellow");
                    sb.AppendLine("Category=Ammo");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=15");
                    sb.AppendLine("Sell_MinPrice=12");
                    sb.AppendLine("Sell_MaxPrice=19");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                }
                // ============================================================
                // PRICELIST_FOOD.INI - Komplett
                // ============================================================
                else if (filename == "Pricelist_Food.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - FOOD");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[ClangCola]");
                    sb.AppendLine("SubtypeId=ClangCola");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=90");
                    sb.AppendLine("Sell_MaxPrice=110");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[CosmicCoffee]");
                    sb.AppendLine("SubtypeId=CosmicCoffee");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=90");
                    sb.AppendLine("Sell_MaxPrice=110");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Fruit]");
                    sb.AppendLine("SubtypeId=Fruit");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=1240");
                    sb.AppendLine("Sell_MinPrice=992");
                    sb.AppendLine("Sell_MaxPrice=1550");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Mushrooms]");
                    sb.AppendLine("SubtypeId=Mushrooms");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=1164");
                    sb.AppendLine("Sell_MinPrice=931");
                    sb.AppendLine("Sell_MaxPrice=1456");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Vegetables]");
                    sb.AppendLine("SubtypeId=Vegetables");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=1189");
                    sb.AppendLine("Sell_MinPrice=951");
                    sb.AppendLine("Sell_MaxPrice=1486");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Grain]");
                    sb.AppendLine("SubtypeId=Grain");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=1264");
                    sb.AppendLine("Sell_MinPrice=1011");
                    sb.AppendLine("Sell_MaxPrice=1580");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MammalMeatRaw]");
                    sb.AppendLine("SubtypeId=MammalMeatRaw");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=50");
                    sb.AppendLine("Sell_MinPrice=40");
                    sb.AppendLine("Sell_MaxPrice=63");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MammalMeatCooked]");
                    sb.AppendLine("SubtypeId=MammalMeatCooked");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=150");
                    sb.AppendLine("Sell_MinPrice=120");
                    sb.AppendLine("Sell_MaxPrice=188");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[InsectMeatRaw]");
                    sb.AppendLine("SubtypeId=InsectMeatRaw");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=40");
                    sb.AppendLine("Sell_MinPrice=32");
                    sb.AppendLine("Sell_MaxPrice=50");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[InsectMeatCooked]");
                    sb.AppendLine("SubtypeId=InsectMeatCooked");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=120");
                    sb.AppendLine("Sell_MinPrice=96");
                    sb.AppendLine("Sell_MaxPrice=150");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MealPack_KelpCrisp]");
                    sb.AppendLine("SubtypeId=MealPack_KelpCrisp");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=200");
                    sb.AppendLine("Sell_MinPrice=140");
                    sb.AppendLine("Sell_MaxPrice=280");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MealPack_FruitBar]");
                    sb.AppendLine("SubtypeId=MealPack_FruitBar");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=250");
                    sb.AppendLine("Sell_MinPrice=175");
                    sb.AppendLine("Sell_MaxPrice=350");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MealPack_GardenSlaw]");
                    sb.AppendLine("SubtypeId=MealPack_GardenSlaw");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=300");
                    sb.AppendLine("Sell_MinPrice=210");
                    sb.AppendLine("Sell_MaxPrice=420");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MealPack_RedPellets]");
                    sb.AppendLine("SubtypeId=MealPack_RedPellets");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=180");
                    sb.AppendLine("Sell_MinPrice=126");
                    sb.AppendLine("Sell_MaxPrice=252");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MealPack_GreenPellets]");
                    sb.AppendLine("SubtypeId=MealPack_GreenPellets");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=180");
                    sb.AppendLine("Sell_MinPrice=126");
                    sb.AppendLine("Sell_MaxPrice=252");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                    
                    sb.AppendLine("[MealPack_Chili]");
                    sb.AppendLine("SubtypeId=MealPack_Chili");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=400");
                    sb.AppendLine("Sell_MinPrice=240");
                    sb.AppendLine("Sell_MaxPrice=640");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_Ramen]");
                    sb.AppendLine("SubtypeId=MealPack_Ramen");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=450");
                    sb.AppendLine("Sell_MinPrice=270");
                    sb.AppendLine("Sell_MaxPrice=720");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_Flatbread]");
                    sb.AppendLine("SubtypeId=MealPack_Flatbread");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=350");
                    sb.AppendLine("Sell_MinPrice=210");
                    sb.AppendLine("Sell_MaxPrice=560");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_FruitPastry]");
                    sb.AppendLine("SubtypeId=MealPack_FruitPastry");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=380");
                    sb.AppendLine("Sell_MinPrice=228");
                    sb.AppendLine("Sell_MaxPrice=608");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_VeggieBurger]");
                    sb.AppendLine("SubtypeId=MealPack_VeggieBurger");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=420");
                    sb.AppendLine("Sell_MinPrice=252");
                    sb.AppendLine("Sell_MaxPrice=672");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_Curry]");
                    sb.AppendLine("SubtypeId=MealPack_Curry");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=500");
                    sb.AppendLine("Sell_MinPrice=300");
                    sb.AppendLine("Sell_MaxPrice=800");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_Dumplings]");
                    sb.AppendLine("SubtypeId=MealPack_Dumplings");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=480");
                    sb.AppendLine("Sell_MinPrice=288");
                    sb.AppendLine("Sell_MaxPrice=768");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_Spaghetti]");
                    sb.AppendLine("SubtypeId=MealPack_Spaghetti");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=550");
                    sb.AppendLine("Sell_MinPrice=330");
                    sb.AppendLine("Sell_MaxPrice=880");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_Lasagna]");
                    sb.AppendLine("SubtypeId=MealPack_Lasagna");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=600");
                    sb.AppendLine("Sell_MinPrice=360");
                    sb.AppendLine("Sell_MaxPrice=960");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_Burrito]");
                    sb.AppendLine("SubtypeId=MealPack_Burrito");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Epic");
                    sb.AppendLine("Sell_BasePrice=520");
                    sb.AppendLine("Sell_MinPrice=312");
                    sb.AppendLine("Sell_MaxPrice=832");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.5");
                    sb.AppendLine("Over_DynamicPriceStep=10");
                    sb.AppendLine("Over_DynamicPriceFactor=1.15");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_FrontierStew]");
                    sb.AppendLine("SubtypeId=MealPack_FrontierStew");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=800");
                    sb.AppendLine("Sell_MinPrice=400");
                    sb.AppendLine("Sell_MaxPrice=1600");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_SearedSabiroid]");
                    sb.AppendLine("SubtypeId=MealPack_SearedSabiroid");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=1000");
                    sb.AppendLine("Sell_MinPrice=500");
                    sb.AppendLine("Sell_MaxPrice=2000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_SteakDinner]");
                    sb.AppendLine("SubtypeId=MealPack_SteakDinner");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=1200");
                    sb.AppendLine("Sell_MinPrice=600");
                    sb.AppendLine("Sell_MaxPrice=2400");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");
                    sb.AppendLine();

                    sb.AppendLine("[MealPack_ExpiredSlop]");
                    sb.AppendLine("SubtypeId=MealPack_ExpiredSlop");
                    sb.AppendLine("Category=Food");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=90");
                    sb.AppendLine("Sell_MaxPrice=110");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                }
                // ============================================================
                // PRICELIST_SEEDS.INI - Komplett
                // ============================================================
                else if (filename == "Pricelist_Seeds.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - SEEDS");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[Vegetables]");
                    sb.AppendLine("SubtypeId=Vegetables");
                    sb.AppendLine("Category=Seeds");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=90");
                    sb.AppendLine("Sell_MaxPrice=110");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();
                
                    sb.AppendLine("[Mushrooms]");
                    sb.AppendLine("SubtypeId=Mushrooms");
                    sb.AppendLine("Category=Seeds");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=100");
                    sb.AppendLine("Sell_MinPrice=90");
                    sb.AppendLine("Sell_MaxPrice=110");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();

                    sb.AppendLine("[Grain]");
                    sb.AppendLine("SubtypeId=Grain");
                    sb.AppendLine("Category=Seeds");
                    sb.AppendLine("Rarity=Uncommon");
                    sb.AppendLine("Sell_BasePrice=150");
                    sb.AppendLine("Sell_MinPrice=120");
                    sb.AppendLine("Sell_MaxPrice=188");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.65");
                    sb.AppendLine("Over_DynamicPriceStep=30");
                    sb.AppendLine("Over_DynamicPriceFactor=1.08");
                    sb.AppendLine();

                    sb.AppendLine("[Fruit]");
                    sb.AppendLine("SubtypeId=Fruit");
                    sb.AppendLine("Category=Seeds");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=200");
                    sb.AppendLine("Sell_MinPrice=140");
                    sb.AppendLine("Sell_MaxPrice=280");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                }
                // ============================================================
                // PRICELIST_Consumables.INI - Komplett
                // ============================================================
                else if (filename == "Pricelist_Consumables.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - Consumables");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();

                    sb.AppendLine("[Medkit]");
                    sb.AppendLine("SubtypeId=Medkit");
                    sb.AppendLine("Category=Consumables");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=500");
                    sb.AppendLine("Sell_MinPrice=400");
                    sb.AppendLine("Sell_MaxPrice=625");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();

                    sb.AppendLine("[Powerkit]");
                    sb.AppendLine("SubtypeId=Powerkit");
                    sb.AppendLine("Category=Consumables");
                    sb.AppendLine("Rarity=Common");
                    sb.AppendLine("Sell_BasePrice=400");
                    sb.AppendLine("Sell_MinPrice=320");
                    sb.AppendLine("Sell_MaxPrice=500");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.7");
                    sb.AppendLine("Over_DynamicPriceStep=50");
                    sb.AppendLine("Over_DynamicPriceFactor=1.05");
                    sb.AppendLine();

                    sb.AppendLine("[RadiationKit]");
                    sb.AppendLine("SubtypeId=RadiationKit");
                    sb.AppendLine("Category=Consumables");
                    sb.AppendLine("Rarity=Rare");
                    sb.AppendLine("Sell_BasePrice=800");
                    sb.AppendLine("Sell_MinPrice=560");
                    sb.AppendLine("Sell_MaxPrice=1120");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.6");
                    sb.AppendLine("Over_DynamicPriceStep=20");
                    sb.AppendLine("Over_DynamicPriceFactor=1.12");
                    sb.AppendLine();
                }                
                // ============================================================
                // PRICELIST_PROTOTECH.INI - Komplett
                // ============================================================
                else if (filename == "Pricelist_Prototech.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - PROTOTECH");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[PrototechScrap]");
                    sb.AppendLine("SubtypeId=PrototechScrap");
                    sb.AppendLine("Category=Prototech");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=50000");
                    sb.AppendLine("Sell_MinPrice=25000");
                    sb.AppendLine("Sell_MaxPrice=100000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");

                    sb.AppendLine("[PrototechFrame]");
                    sb.AppendLine("SubtypeId=PrototechFrame");
                    sb.AppendLine("Category=Prototech");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=100000");
                    sb.AppendLine("Sell_MinPrice=50000");
                    sb.AppendLine("Sell_MaxPrice=200000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");

                    sb.AppendLine("[PrototechPanel]");
                    sb.AppendLine("SubtypeId=PrototechPanel");
                    sb.AppendLine("Category=Prototech");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=15000");
                    sb.AppendLine("Sell_MinPrice=7500");
                    sb.AppendLine("Sell_MaxPrice=30000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");

                    sb.AppendLine("[PrototechCapacitor]");
                    sb.AppendLine("SubtypeId=PrototechCapacitor");
                    sb.AppendLine("Category=Prototech");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=12000");
                    sb.AppendLine("Sell_MinPrice=6000");
                    sb.AppendLine("Sell_MaxPrice=24000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");

                    sb.AppendLine("[PrototechPropulsionUnit]");
                    sb.AppendLine("SubtypeId=PrototechPropulsionUnit");
                    sb.AppendLine("Category=Prototech");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=18000");
                    sb.AppendLine("Sell_MinPrice=9000");
                    sb.AppendLine("Sell_MaxPrice=36000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");

                    sb.AppendLine("[PrototechMachinery]");
                    sb.AppendLine("SubtypeId=PrototechMachinery");
                    sb.AppendLine("Category=Prototech");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=16000");
                    sb.AppendLine("Sell_MinPrice=8000");
                    sb.AppendLine("Sell_MaxPrice=32000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");

                    sb.AppendLine("[PrototechCircuitry]");
                    sb.AppendLine("SubtypeId=PrototechCircuitry");
                    sb.AppendLine("Category=Prototech");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=20000");
                    sb.AppendLine("Sell_MinPrice=10000");
                    sb.AppendLine("Sell_MaxPrice=40000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");

                    sb.AppendLine("[PrototechCoolingUnit]");
                    sb.AppendLine("SubtypeId=PrototechCoolingUnit");
                    sb.AppendLine("Category=Prototech");
                    sb.AppendLine("Rarity=Legendary");
                    sb.AppendLine("Sell_BasePrice=25000");
                    sb.AppendLine("Sell_MinPrice=12500");
                    sb.AppendLine("Sell_MaxPrice=50000");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.4");
                    sb.AppendLine("Over_DynamicPriceStep=5");
                    sb.AppendLine("Over_DynamicPriceFactor=1.20");
                }
                // ============================================================
                // PRICELIST_Blacklist
                // ============================================================
                else if (filename == "Pricelist_Blacklist.ini")
                {
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine("# PRICELIST - Blacklist");
                    sb.AppendLine("# ==============================================================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("[AdminChip]");
                    sb.AppendLine("SubtypeId=AdminChip");
                    sb.AppendLine("Category=Blacklist");
                    sb.AppendLine("Rarity=Blacklist");
                    sb.AppendLine("Sell_BasePrice=999999999");
                    sb.AppendLine("Sell_MinPrice=999999999");
                    sb.AppendLine("Sell_MaxPrice=99999999");
                    sb.AppendLine("Override_enable=false");
                    sb.AppendLine("Over_Buy_Margin=0.0");
                    sb.AppendLine("Over_DynamicPriceStep=0");
                    sb.AppendLine("Over_DynamicPriceFactor=0");
                }
                else
                {
                    // Fallback
                    sb.AppendLine($"# Unbekannte Pricelist: {filename}");
                }

                WriteFile(filename, sb.ToString());
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Created {filename}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager ERROR deploying {filename}:\n{ex}");
            }
        }

        #endregion

        #region Config Loading

        private void LoadAllConfigs()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: Loading configs...");

                LoadGlobalConfig();
                LoadPricelists();

                _configsLoaded = true;
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: Configs loaded");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager ERROR loading configs:\n{ex}");
            }
        }

        private void LoadGlobalConfig()
        {
            try
            {
                string content = ReadFile(GLOBAL_CONFIG_FILE);
                if (content == null)
                {
                    MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: GlobalConfig not found");
                    return;
                }

                _globalConfig = ParseFlatINI(content);
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: GlobalConfig loaded ({_globalConfig.Count} entries)");

                // Migration: fehlende Keys automatisch nachpatchen
                PatchGlobalConfigIfNeeded(content);

                // Debug-Level wird vom PhantomBite Core über LOGLEVEL gesetzt — nicht mehr hier
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager ERROR loading GlobalConfig:\n{ex}");
            }
        }


        /// <summary>
        /// Prüft ob Keys in der GlobalConfig fehlen und patcht sie nach (Migration für bestehende Server).
        /// </summary>
        private void PatchGlobalConfigIfNeeded(string existingContent)
        {
            try
            {
                bool changed = false;
                var sb = new System.Text.StringBuilder(existingContent);

                // StationRefill.FactionTags — neu in v1.1
                if (!_globalConfig.ContainsKey("StationRefill.FactionTags"))
                {
                    // Zeile nach AmmoSubtype einfügen
                    string needle = "AmmoSubtype=RapidFireAutomaticRifleGun_Mag_50rd";
                    int idx = existingContent.IndexOf(needle);
                    if (idx >= 0)
                    {
                        int lineEnd = existingContent.IndexOf('\n', idx);
                        if (lineEnd < 0) lineEnd = existingContent.Length;
                        sb.Insert(lineEnd + 1, "FactionTags=SPT\n");
                    }
                    else
                    {
                        // [StationRefill] Block nicht gefunden — ans Ende hängen
                        sb.AppendLine();
                        sb.AppendLine("[StationRefill]");
                        sb.AppendLine("FactionTags=SPT");
                    }
                    _globalConfig["StationRefill.FactionTags"] = "SPT";
                    changed = true;
                    MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: Migration — StationRefill.FactionTags hinzugefügt");
                }

                if (changed)
                    WriteFile(GLOBAL_CONFIG_FILE, sb.ToString());
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager ERROR in PatchGlobalConfigIfNeeded: " + ex.Message);
            }
        }

        private void LoadPricelists()
        {
            try
            {
                foreach (var pricelistFile in PRICELIST_FILES)
                {
                    string content = ReadFile(pricelistFile);
                    if (content == null)
                        continue;

                    var items = ParsePricelistINI(content);
                    _pricelists[pricelistFile] = items;

                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: {pricelistFile} loaded ({items.Count} items)");
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager ERROR loading Pricelists:\n{ex}");
            }
        }

        #endregion

        #region INI Parsing

        private Dictionary<string, string> ParseFlatINI(string content)
        {
            var result = new Dictionary<string, string>();
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
                    if (equalsIndex > 0)
                    {
                        string key = trimmed.Substring(0, equalsIndex).Trim();
                        string value = trimmed.Substring(equalsIndex + 1).Trim();
                        
                        string fullKey = string.IsNullOrEmpty(currentSection) ? key : currentSection + "." + key;
                        result[fullKey] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager ERROR parsing flat INI:\n{ex}");
            }

            return result;
        }

        private Dictionary<string, ItemData> ParsePricelistINI(string content)
        {
            var result = new Dictionary<string, ItemData>();
            ItemData currentItem = null;
            string currentItemName = "";

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
                        if (currentItem != null && !string.IsNullOrEmpty(currentItemName))
                        {
                            result[currentItemName] = currentItem;
                        }

                        currentItemName = trimmed.Substring(1, trimmed.Length - 2);
                        currentItem = new ItemData();
                        continue;
                    }

                    if (currentItem != null)
                    {
                        int equalsIndex = trimmed.IndexOf('=');
                        if (equalsIndex > 0)
                        {
                            string key = trimmed.Substring(0, equalsIndex).Trim();
                            string value = trimmed.Substring(equalsIndex + 1).Trim();

                            switch (key)
                            {
                                case "Category":
                                    currentItem.Category = value;
                                    break;
                                case "Rarity":
                                    currentItem.Rarity = value;
                                    break;
                                case "Sell_BasePrice":
                                    int.TryParse(value, out currentItem.Sell_BasePrice);
                                    break;
                                case "Sell_MinPrice":
                                    int.TryParse(value, out currentItem.Sell_MinPrice);
                                    break;
                                case "Sell_MaxPrice":
                                    int.TryParse(value, out currentItem.Sell_MaxPrice);
                                    break;
                                case "Override_enable":
                                    bool.TryParse(value, out currentItem.Override_enable);
                                    break;
                                case "Over_Buy_Margin":
                                    float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out currentItem.Over_Buy_Margin);
                                    break;
                                case "Over_DynamicPriceStep":
                                    int.TryParse(value, out currentItem.Over_DynamicPriceStep);
                                    break;
                                case "Over_DynamicPriceFactor":
                                    float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out currentItem.Over_DynamicPriceFactor);
                                    break;
                            }
                        }
                    }
                }

                if (currentItem != null && !string.IsNullOrEmpty(currentItemName))
                {
                    result[currentItemName] = currentItem;
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager ERROR parsing pricelist INI:\n{ex}");
            }

            return result;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Gibt den rohen Inhalt der GlobalConfig zurück.
        /// Wird von M07 für die KeepList-Sektion benötigt.
        /// </summary>
        public string GetGlobalConfigRaw()
        {
            try
            {
                if (!FileExists(GLOBAL_CONFIG_FILE))
                {
                    DeployGlobalConfig();
                }
                return ReadFile(GLOBAL_CONFIG_FILE);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager ERROR in GetGlobalConfigRaw:\n{ex}");
                return "";
            }
        }

        public string GetGlobalConfig(string key, string defaultValue = "")
        {
            if (!_configsLoaded || _globalConfig.Count == 0)
            {
                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] FileManager: GlobalConfig missing - Self-Healing...");
                    DeployGlobalConfig();
                    LoadGlobalConfig();
                    _configsLoaded = true;
                }
            }

            return _globalConfig.ContainsKey(key) ? _globalConfig[key] : defaultValue;
        }

        public int GetGlobalConfigInt(string key, int defaultValue = 0)
        {
            string value = GetGlobalConfig(key, defaultValue.ToString());
            int result;
            return int.TryParse(value, out result) ? result : defaultValue;
        }

        public float GetGlobalConfigFloat(string key, float defaultValue = 0f)
        {
            string value = GetGlobalConfig(key, defaultValue.ToString());
            float result;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : defaultValue;
        }

        public bool GetGlobalConfigBool(string key, bool defaultValue = false)
        {
            string value = GetGlobalConfig(key, defaultValue.ToString());
            bool result;
            return bool.TryParse(value, out result) ? result : defaultValue;
        }

        public bool IsValidItem(string itemName, string category)
        {
            string pricelistFile = $"Pricelist_{category}.ini";
            
            if (!_configsLoaded || !_pricelists.ContainsKey(pricelistFile))
            {
                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: {pricelistFile} missing - Self-Healing...");
                    DeployPricelist(pricelistFile);
                    
                    string content = ReadFile(pricelistFile);
                    if (content != null)
                    {
                        _pricelists[pricelistFile] = ParsePricelistINI(content);
                        _configsLoaded = true;
                    }
                }
            }

            if (!_pricelists.ContainsKey(pricelistFile))
                return false;

            return _pricelists[pricelistFile].ContainsKey(itemName);
        }

        public ItemData GetItemData(string itemName, string category)
        {
            string pricelistFile = $"Pricelist_{category}.ini";

            if (!_configsLoaded || !_pricelists.ContainsKey(pricelistFile))
            {
                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: {pricelistFile} missing - Self-Healing...");
                    DeployPricelist(pricelistFile);
                    
                    string content = ReadFile(pricelistFile);
                    if (content != null)
                    {
                        _pricelists[pricelistFile] = ParsePricelistINI(content);
                        _configsLoaded = true;
                    }
                }
            }

            if (!_pricelists.ContainsKey(pricelistFile))
                return null;

            var pricelist = _pricelists[pricelistFile];
            return pricelist.ContainsKey(itemName) ? pricelist[itemName] : null;
        }

        #endregion

        #region Low-Level File I/O

        public string ReadFile(string filename)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Client tried to read '{filename}' - BLOCKED");
                return null;
            }

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(FileManagerModule)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(FileManagerModule)))
                    {
                        return reader.ReadToEnd();
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Error reading '{filename}': {ex.Message}");
                return null;
            }
        }

        public bool WriteFile(string filename, string content)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Client tried to write '{filename}' - BLOCKED");
                return false;
            }

            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, typeof(FileManagerModule)))
                {
                    writer.Write(content);
                }
                return true;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Error writing '{filename}': {ex.Message}");
                return false;
            }
        }

        public bool FileExists(string filename)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return false;

            try
            {
                return MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(FileManagerModule));
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Error checking '{filename}': {ex.Message}");
                return false;
            }
        }

        public bool DeleteFile(string filename)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Client tried to delete '{filename}' - BLOCKED");
                return false;
            }

            try
            {
                MyAPIGateway.Utilities.DeleteFileInWorldStorage(filename, typeof(FileManagerModule));
                return true;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] FileManager: Error deleting '{filename}': {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}