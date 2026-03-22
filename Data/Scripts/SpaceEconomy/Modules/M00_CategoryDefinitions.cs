using System.Collections.Generic;

namespace PhantombiteEconomy
{
    /// <summary>
    /// M00 - Category Definitions (Single Source of Truth)
    /// KRITISCH: Definiert ALLE Items mit exaktem TypeId + SubtypeId
    /// Eliminiert alle Konflikte zwischen Categories mit gleichen SubtypeIds
    /// </summary>
    public static class CategoryDefinitions
    {
        /// <summary>
        /// Item Definition mit vollständiger Identifikation
        /// </summary>
        public class ItemDefinition
        {
            public string DisplayName;  // Section Header aus Pricelist (für User Blacklist/Whitelist)
            public string TypeId;       // ObjectBuilder TypeId (z.B. "Ore", "Ingot", "Component")
            public string SubtypeId;    // SubtypeId für Spawning
        }

        /// <summary>
        /// SINGLE SOURCE OF TRUTH für alle Categories
        /// Basiert auf M02 Pricelist Templates
        /// </summary>
        private static Dictionary<string, List<ItemDefinition>> _categoryItems = null;

        /// <summary>
        /// Gibt alle Items für eine Category zurück
        /// </summary>
        public static List<ItemDefinition> GetItemsForCategory(string category)
        {
            if (_categoryItems == null)
                InitializeCategoryItems();

            if (_categoryItems.ContainsKey(category))
                return _categoryItems[category];

            return new List<ItemDefinition>();
        }

        /// <summary>
        /// Gibt alle DisplayNames für eine Category zurück (für Backward Compatibility)
        /// </summary>
        public static List<string> GetDisplayNamesForCategory(string category)
        {
            var items = GetItemsForCategory(category);
            var names = new List<string>();
            
            foreach (var item in items)
                names.Add(item.DisplayName);
            
            return names;
        }

        /// <summary>
        /// Sucht Item Definition nach DisplayName in einer Category
        /// </summary>
        public static ItemDefinition FindItem(string category, string displayName)
        {
            var items = GetItemsForCategory(category);
            
            foreach (var item in items)
            {
                if (item.DisplayName == displayName)
                    return item;
            }
            
            return null;
        }

        /// <summary>
        /// Initialisiert Category Items basierend auf M02 Pricelists
        /// WICHTIG: Diese Liste muss mit M02_FileManager.cs synchron bleiben!
        /// </summary>
        private static void InitializeCategoryItems()
        {
            _categoryItems = new Dictionary<string, List<ItemDefinition>>
            {
                // ============================================================
                // ORE - 13 Items
                // ============================================================
                { "Ore", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "Stone", TypeId = "Ore", SubtypeId = "Stone" },
                        new ItemDefinition { DisplayName = "Ice", TypeId = "Ore", SubtypeId = "Ice" },
                        new ItemDefinition { DisplayName = "Iron", TypeId = "Ore", SubtypeId = "Iron" },
                        new ItemDefinition { DisplayName = "Nickel", TypeId = "Ore", SubtypeId = "Nickel" },
                        new ItemDefinition { DisplayName = "Cobalt", TypeId = "Ore", SubtypeId = "Cobalt" },
                        new ItemDefinition { DisplayName = "Magnesium", TypeId = "Ore", SubtypeId = "Magnesium" },
                        new ItemDefinition { DisplayName = "Silicon", TypeId = "Ore", SubtypeId = "Silicon" },
                        new ItemDefinition { DisplayName = "Silver", TypeId = "Ore", SubtypeId = "Silver" },
                        new ItemDefinition { DisplayName = "Gold", TypeId = "Ore", SubtypeId = "Gold" },
                        new ItemDefinition { DisplayName = "Platinum", TypeId = "Ore", SubtypeId = "Platinum" },
                        new ItemDefinition { DisplayName = "Uranium", TypeId = "Ore", SubtypeId = "Uranium" },
                        new ItemDefinition { DisplayName = "Scrap", TypeId = "Ore", SubtypeId = "Scrap" },
                        new ItemDefinition { DisplayName = "Organic", TypeId = "Ore", SubtypeId = "Organic" }
                    }
                },

                // ============================================================
                // INGOTS - 11 Items
                // ============================================================
                { "Ingots", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "Stone", TypeId = "Ingot", SubtypeId = "Stone" },  // Display: Gravel
                        new ItemDefinition { DisplayName = "Iron", TypeId = "Ingot", SubtypeId = "Iron" },
                        new ItemDefinition { DisplayName = "Nickel", TypeId = "Ingot", SubtypeId = "Nickel" },
                        new ItemDefinition { DisplayName = "Cobalt", TypeId = "Ingot", SubtypeId = "Cobalt" },
                        new ItemDefinition { DisplayName = "Magnesium", TypeId = "Ingot", SubtypeId = "Magnesium" },
                        new ItemDefinition { DisplayName = "Silicon", TypeId = "Ingot", SubtypeId = "Silicon" },
                        new ItemDefinition { DisplayName = "Silver", TypeId = "Ingot", SubtypeId = "Silver" },
                        new ItemDefinition { DisplayName = "Gold", TypeId = "Ingot", SubtypeId = "Gold" },
                        new ItemDefinition { DisplayName = "Platinum", TypeId = "Ingot", SubtypeId = "Platinum" },
                        new ItemDefinition { DisplayName = "Uranium", TypeId = "Ingot", SubtypeId = "Uranium" },
                        new ItemDefinition { DisplayName = "Scrap", TypeId = "Ingot", SubtypeId = "Scrap" }
                    }
                },

                // ============================================================
                // COMPONENTS - 21 Items
                // ============================================================
                { "Components", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "Construction", TypeId = "Component", SubtypeId = "Construction" },
                        new ItemDefinition { DisplayName = "MetalGrid", TypeId = "Component", SubtypeId = "MetalGrid" },
                        new ItemDefinition { DisplayName = "InteriorPlate", TypeId = "Component", SubtypeId = "InteriorPlate" },
                        new ItemDefinition { DisplayName = "SteelPlate", TypeId = "Component", SubtypeId = "SteelPlate" },
                        new ItemDefinition { DisplayName = "Girder", TypeId = "Component", SubtypeId = "Girder" },
                        new ItemDefinition { DisplayName = "SmallTube", TypeId = "Component", SubtypeId = "SmallTube" },
                        new ItemDefinition { DisplayName = "LargeTube", TypeId = "Component", SubtypeId = "LargeTube" },
                        new ItemDefinition { DisplayName = "Motor", TypeId = "Component", SubtypeId = "Motor" },
                        new ItemDefinition { DisplayName = "Display", TypeId = "Component", SubtypeId = "Display" },
                        new ItemDefinition { DisplayName = "BulletproofGlass", TypeId = "Component", SubtypeId = "BulletproofGlass" },
                        new ItemDefinition { DisplayName = "Superconductor", TypeId = "Component", SubtypeId = "Superconductor" },
                        new ItemDefinition { DisplayName = "Computer", TypeId = "Component", SubtypeId = "Computer" },
                        new ItemDefinition { DisplayName = "Reactor", TypeId = "Component", SubtypeId = "Reactor" },
                        new ItemDefinition { DisplayName = "Thrust", TypeId = "Component", SubtypeId = "Thrust" },
                        new ItemDefinition { DisplayName = "GravityGenerator", TypeId = "Component", SubtypeId = "GravityGenerator" },
                        new ItemDefinition { DisplayName = "Medical", TypeId = "Component", SubtypeId = "Medical" },
                        new ItemDefinition { DisplayName = "RadioCommunication", TypeId = "Component", SubtypeId = "RadioCommunication" },
                        new ItemDefinition { DisplayName = "Detector", TypeId = "Component", SubtypeId = "Detector" },
                        new ItemDefinition { DisplayName = "Explosives", TypeId = "Component", SubtypeId = "Explosives" },
                        new ItemDefinition { DisplayName = "SolarCell", TypeId = "Component", SubtypeId = "SolarCell" },
                        new ItemDefinition { DisplayName = "PowerCell", TypeId = "Component", SubtypeId = "PowerCell" }
                    }
                },

                // ============================================================
                // TOOLS - 13 Items
                // ============================================================
                { "Tools", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "Welder", TypeId = "PhysicalGunObject", SubtypeId = "WelderItem" },
                        new ItemDefinition { DisplayName = "Welder2", TypeId = "PhysicalGunObject", SubtypeId = "Welder2Item" },
                        new ItemDefinition { DisplayName = "Welder3", TypeId = "PhysicalGunObject", SubtypeId = "Welder3Item" },
                        new ItemDefinition { DisplayName = "Welder4", TypeId = "PhysicalGunObject", SubtypeId = "Welder4Item" },
                        new ItemDefinition { DisplayName = "AngleGrinder", TypeId = "PhysicalGunObject", SubtypeId = "AngleGrinderItem" },
                        new ItemDefinition { DisplayName = "AngleGrinder2", TypeId = "PhysicalGunObject", SubtypeId = "AngleGrinder2Item" },
                        new ItemDefinition { DisplayName = "AngleGrinder3", TypeId = "PhysicalGunObject", SubtypeId = "AngleGrinder3Item" },
                        new ItemDefinition { DisplayName = "AngleGrinder4", TypeId = "PhysicalGunObject", SubtypeId = "AngleGrinder4Item" },
                        new ItemDefinition { DisplayName = "HandDrill", TypeId = "PhysicalGunObject", SubtypeId = "HandDrillItem" },
                        new ItemDefinition { DisplayName = "HandDrill2", TypeId = "PhysicalGunObject", SubtypeId = "HandDrill2Item" },
                        new ItemDefinition { DisplayName = "HandDrill3", TypeId = "PhysicalGunObject", SubtypeId = "HandDrill3Item" },
                        new ItemDefinition { DisplayName = "HandDrill4", TypeId = "PhysicalGunObject", SubtypeId = "HandDrill4Item" },
                        new ItemDefinition { DisplayName = "FlareGun", TypeId = "PhysicalGunObject", SubtypeId = "FlareGunItem" }
                    }
                },

                // ============================================================
                // WEAPONS - 10 Items
                // ============================================================
                { "Weapons", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "SemiAutoPistol", TypeId = "PhysicalGunObject", SubtypeId = "SemiAutoPistolItem" },
                        new ItemDefinition { DisplayName = "FullAutoPistol", TypeId = "PhysicalGunObject", SubtypeId = "FullAutoPistolItem" },
                        new ItemDefinition { DisplayName = "ElitePistol", TypeId = "PhysicalGunObject", SubtypeId = "ElitePistolItem" },
                        new ItemDefinition { DisplayName = "AutomaticRifle", TypeId = "PhysicalGunObject", SubtypeId = "AutomaticRifleItem" },
                        new ItemDefinition { DisplayName = "PreciseAutomaticRifle", TypeId = "PhysicalGunObject", SubtypeId = "PreciseAutomaticRifleItem" },
                        new ItemDefinition { DisplayName = "RapidFireAutomaticRifle", TypeId = "PhysicalGunObject", SubtypeId = "RapidFireAutomaticRifleItem" },
                        new ItemDefinition { DisplayName = "UltimateAutomaticRifle", TypeId = "PhysicalGunObject", SubtypeId = "UltimateAutomaticRifleItem" },
                        new ItemDefinition { DisplayName = "BasicHandHeldLauncher", TypeId = "PhysicalGunObject", SubtypeId = "BasicHandHeldLauncherItem" },
                        new ItemDefinition { DisplayName = "AdvancedHandHeldLauncher", TypeId = "PhysicalGunObject", SubtypeId = "AdvancedHandHeldLauncherItem" },
                        new ItemDefinition { DisplayName = "CubePlacer", TypeId = "PhysicalGunObject", SubtypeId = "CubePlacerItem" }
                    }
                },

                // ============================================================
                // AMMO - 21 Items
                // ============================================================
                { "Ammo", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "NATO_5p56x45mm", TypeId = "AmmoMagazine", SubtypeId = "NATO_5p56x45mm" },
                        new ItemDefinition { DisplayName = "NATO_25x184mm", TypeId = "AmmoMagazine", SubtypeId = "NATO_25x184mm" },
                        new ItemDefinition { DisplayName = "Missile200mm", TypeId = "AmmoMagazine", SubtypeId = "Missile200mm" },
                        new ItemDefinition { DisplayName = "SemiAutoPistolMagazine", TypeId = "AmmoMagazine", SubtypeId = "SemiAutoPistolMagazine" },
                        new ItemDefinition { DisplayName = "FullAutoPistolMagazine", TypeId = "AmmoMagazine", SubtypeId = "FullAutoPistolMagazine" },
                        new ItemDefinition { DisplayName = "ElitePistolMagazine", TypeId = "AmmoMagazine", SubtypeId = "ElitePistolMagazine" },
                        new ItemDefinition { DisplayName = "AutomaticRifleGun_Mag_20rd", TypeId = "AmmoMagazine", SubtypeId = "AutomaticRifleGun_Mag_20rd" },
                        new ItemDefinition { DisplayName = "PreciseAutomaticRifleGun_Mag_5rd", TypeId = "AmmoMagazine", SubtypeId = "PreciseAutomaticRifleGun_Mag_5rd" },
                        new ItemDefinition { DisplayName = "RapidFireAutomaticRifleGun_Mag_50rd", TypeId = "AmmoMagazine", SubtypeId = "RapidFireAutomaticRifleGun_Mag_50rd" },
                        new ItemDefinition { DisplayName = "UltimateAutomaticRifleGun_Mag_30rd", TypeId = "AmmoMagazine", SubtypeId = "UltimateAutomaticRifleGun_Mag_30rd" },
                        new ItemDefinition { DisplayName = "InteriorTurret_Mag_100rd", TypeId = "AmmoMagazine", SubtypeId = "InteriorTurret_Mag_100rd" },
                        new ItemDefinition { DisplayName = "MediumCalibreAmmo", TypeId = "AmmoMagazine", SubtypeId = "MediumCalibreAmmo" },
                        new ItemDefinition { DisplayName = "LargeCalibreAmmo", TypeId = "AmmoMagazine", SubtypeId = "LargeCalibreAmmo" },
                        new ItemDefinition { DisplayName = "AutocannonClip", TypeId = "AmmoMagazine", SubtypeId = "AutocannonClip" },
                        new ItemDefinition { DisplayName = "SmallRailgunAmmo", TypeId = "AmmoMagazine", SubtypeId = "SmallRailgunAmmo" },
                        new ItemDefinition { DisplayName = "LargeRailgunAmmo", TypeId = "AmmoMagazine", SubtypeId = "LargeRailgunAmmo" },
                        new ItemDefinition { DisplayName = "FlareClip", TypeId = "AmmoMagazine", SubtypeId = "FlareClip" },
                        new ItemDefinition { DisplayName = "FireworksBoxRed", TypeId = "AmmoMagazine", SubtypeId = "FireworksBoxRed" },
                        new ItemDefinition { DisplayName = "FireworksBoxGreen", TypeId = "AmmoMagazine", SubtypeId = "FireworksBoxGreen" },
                        new ItemDefinition { DisplayName = "FireworksBoxBlue", TypeId = "AmmoMagazine", SubtypeId = "FireworksBoxBlue" },
                        new ItemDefinition { DisplayName = "FireworksBoxRainbow", TypeId = "AmmoMagazine", SubtypeId = "FireworksBoxRainbow" },
                        new ItemDefinition { DisplayName = "FireworksBoxPink", TypeId = "AmmoMagazine", SubtypeId = "FireworksBoxPink" },
                        new ItemDefinition { DisplayName = "FireworksBoxYellow", TypeId = "AmmoMagazine", SubtypeId = "FireworksBoxYellow" }
                    }
                },

                // ============================================================
                // FOOD - 29 Items
                // WICHTIG: Grain hat TypeId="PhysicalObject", Rest="ConsumableItem"!
                // ============================================================
                { "Food", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "ClangCola", TypeId = "ConsumableItem", SubtypeId = "ClangCola" },
                        new ItemDefinition { DisplayName = "CosmicCoffee", TypeId = "ConsumableItem", SubtypeId = "CosmicCoffee" },
                        new ItemDefinition { DisplayName = "Fruit", TypeId = "ConsumableItem", SubtypeId = "Fruit" },
                        new ItemDefinition { DisplayName = "Mushrooms", TypeId = "ConsumableItem", SubtypeId = "Mushrooms" },
                        new ItemDefinition { DisplayName = "Vegetables", TypeId = "ConsumableItem", SubtypeId = "Vegetables" },
                        new ItemDefinition { DisplayName = "Grain", TypeId = "PhysicalObject", SubtypeId = "Grain" },
                        new ItemDefinition { DisplayName = "MammalMeatRaw", TypeId = "ConsumableItem", SubtypeId = "MammalMeatRaw" },
                        new ItemDefinition { DisplayName = "MammalMeatCooked", TypeId = "ConsumableItem", SubtypeId = "MammalMeatCooked" },
                        new ItemDefinition { DisplayName = "InsectMeatRaw", TypeId = "ConsumableItem", SubtypeId = "InsectMeatRaw" },
                        new ItemDefinition { DisplayName = "InsectMeatCooked", TypeId = "ConsumableItem", SubtypeId = "InsectMeatCooked" },
                        new ItemDefinition { DisplayName = "MealPack_KelpCrisp", TypeId = "ConsumableItem", SubtypeId = "MealPack_KelpCrisp" },
                        new ItemDefinition { DisplayName = "MealPack_FruitBar", TypeId = "ConsumableItem", SubtypeId = "MealPack_FruitBar" },
                        new ItemDefinition { DisplayName = "MealPack_GardenSlaw", TypeId = "ConsumableItem", SubtypeId = "MealPack_GardenSlaw" },
                        new ItemDefinition { DisplayName = "MealPack_RedPellets", TypeId = "ConsumableItem", SubtypeId = "MealPack_RedPellets" },
                        new ItemDefinition { DisplayName = "MealPack_GreenPellets", TypeId = "ConsumableItem", SubtypeId = "MealPack_GreenPellets" },
                        new ItemDefinition { DisplayName = "MealPack_Chili", TypeId = "ConsumableItem", SubtypeId = "MealPack_Chili" },
                        new ItemDefinition { DisplayName = "MealPack_Ramen", TypeId = "ConsumableItem", SubtypeId = "MealPack_Ramen" },
                        new ItemDefinition { DisplayName = "MealPack_Flatbread", TypeId = "ConsumableItem", SubtypeId = "MealPack_Flatbread" },
                        new ItemDefinition { DisplayName = "MealPack_FruitPastry", TypeId = "ConsumableItem", SubtypeId = "MealPack_FruitPastry" },
                        new ItemDefinition { DisplayName = "MealPack_VeggieBurger", TypeId = "ConsumableItem", SubtypeId = "MealPack_VeggieBurger" },
                        new ItemDefinition { DisplayName = "MealPack_Curry", TypeId = "ConsumableItem", SubtypeId = "MealPack_Curry" },
                        new ItemDefinition { DisplayName = "MealPack_Dumplings", TypeId = "ConsumableItem", SubtypeId = "MealPack_Dumplings" },
                        new ItemDefinition { DisplayName = "MealPack_Spaghetti", TypeId = "ConsumableItem", SubtypeId = "MealPack_Spaghetti" },
                        new ItemDefinition { DisplayName = "MealPack_Lasagna", TypeId = "ConsumableItem", SubtypeId = "MealPack_Lasagna" },
                        new ItemDefinition { DisplayName = "MealPack_Burrito", TypeId = "ConsumableItem", SubtypeId = "MealPack_Burrito" },
                        new ItemDefinition { DisplayName = "MealPack_FrontierStew", TypeId = "ConsumableItem", SubtypeId = "MealPack_FrontierStew" },
                        new ItemDefinition { DisplayName = "MealPack_SearedSabiroid", TypeId = "ConsumableItem", SubtypeId = "MealPack_SearedSabiroid" },
                        new ItemDefinition { DisplayName = "MealPack_SteakDinner", TypeId = "ConsumableItem", SubtypeId = "MealPack_SteakDinner" },
                        new ItemDefinition { DisplayName = "MealPack_ExpiredSlop", TypeId = "ConsumableItem", SubtypeId = "MealPack_ExpiredSlop" }
                    }
                },

                // ============================================================
                // SEEDS - 4 Items
                // ============================================================
                { "Seeds", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "Vegetables", TypeId = "SeedItem", SubtypeId = "Vegetables" },
                        new ItemDefinition { DisplayName = "Mushrooms", TypeId = "SeedItem", SubtypeId = "Mushrooms" },
                        new ItemDefinition { DisplayName = "Grain", TypeId = "SeedItem", SubtypeId = "Grain" },
                        new ItemDefinition { DisplayName = "Fruit", TypeId = "SeedItem", SubtypeId = "Fruit" }
                    }
                },

                // ============================================================
                // CONSUMABLES - 3 Items
                // ============================================================
                { "Consumables", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "Medkit", TypeId = "ConsumableItem", SubtypeId = "Medkit" },
                        new ItemDefinition { DisplayName = "Powerkit", TypeId = "ConsumableItem", SubtypeId = "Powerkit" },
                        new ItemDefinition { DisplayName = "RadiationKit", TypeId = "ConsumableItem", SubtypeId = "RadiationKit" }
                    }
                },

                // ============================================================
                // PROTOTECH - 8 Items
                // WICHTIG: PrototechScrap hat TypeId="Ingot", Rest="Component"!
                // ============================================================
                { "Prototech", new List<ItemDefinition>
                    {
                        new ItemDefinition { DisplayName = "PrototechScrap", TypeId = "Ingot", SubtypeId = "PrototechScrap" },
                        new ItemDefinition { DisplayName = "PrototechFrame", TypeId = "Component", SubtypeId = "PrototechFrame" },
                        new ItemDefinition { DisplayName = "PrototechPanel", TypeId = "Component", SubtypeId = "PrototechPanel" },
                        new ItemDefinition { DisplayName = "PrototechCapacitor", TypeId = "Component", SubtypeId = "PrototechCapacitor" },
                        new ItemDefinition { DisplayName = "PrototechPropulsionUnit", TypeId = "Component", SubtypeId = "PrototechPropulsionUnit" },
                        new ItemDefinition { DisplayName = "PrototechMachinery", TypeId = "Component", SubtypeId = "PrototechMachinery" },
                        new ItemDefinition { DisplayName = "PrototechCircuitry", TypeId = "Component", SubtypeId = "PrototechCircuitry" },
                        new ItemDefinition { DisplayName = "PrototechCoolingUnit", TypeId = "Component", SubtypeId = "PrototechCoolingUnit" }
                    }
                }
            };
        }
    }
}