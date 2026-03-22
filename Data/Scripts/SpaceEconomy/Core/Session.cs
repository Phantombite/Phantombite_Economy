using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using PhantombiteEconomy.Core;
using PhantombiteEconomy.Modules;

namespace PhantombiteEconomy
{
    /// <summary>
    /// Main session component for PhantombiteEconomy mod
    /// 
    /// Registered Modules:
    /// - M01: Logger
    /// - M02: FileManager
    /// - M03: Command
    /// - M04: TraderStoreBlock
    /// - M05: TraderVendingMachine
    /// - M06: EventManager
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class PhantombiteEconomySession : MySessionComponentBase
    {
        private ModuleManager _moduleManager;
        
        // Phase 1 Modules
        private LoggerModule _logger;
        private FileManagerModule _fileManager;
        private CommandModule _commandModule;
        
        // Store Modules
        private TraderStoreBlockModule _traderStore;
        private TraderVendingMachineModule _vendingMachine;
        private EventManagerModule _eventManager;

        // AutoTransfer
        private AutoTransferModule _autoTransfer;
        
        private bool _isInitialized = false;
        private const string MOD_NAME = "PhantombiteEconomy";

        public override void LoadData()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole($"[{MOD_NAME}] Session LoadData started...");

                // Initialize module manager
                _moduleManager = new ModuleManager();

                // Initialize Phase 1 modules
                _logger = new LoggerModule();
                _fileManager = new FileManagerModule();
                _commandModule = new CommandModule();
                
                // Initialize Store modules
                _eventManager = new EventManagerModule();
                _traderStore = new TraderStoreBlockModule();
                _vendingMachine = new TraderVendingMachineModule();

                // Initialize AutoTransfer
                _autoTransfer = new AutoTransferModule(_commandModule, _fileManager);
                _commandModule.SetAutoTransferModule(_autoTransfer);
                _commandModule.SetEventManagerModule(_eventManager);
                _commandModule.SetStoreModules(_traderStore, _vendingMachine);

                // Register Phase 1 modules
                _moduleManager.RegisterModule(_logger);
                _moduleManager.RegisterModule(_fileManager);
                _moduleManager.RegisterModule(_commandModule);

                // Register Store modules
                _moduleManager.RegisterModule(_eventManager);
                _moduleManager.RegisterModule(_traderStore);
                _moduleManager.RegisterModule(_vendingMachine);

                // Register AutoTransfer
                _moduleManager.RegisterModule(_autoTransfer);

                // Initialize all modules
                _moduleManager.InitAll();

                _isInitialized = true;
                MyLog.Default.WriteLineAndConsole($"[{MOD_NAME}] Session LoadData completed successfully!");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[{MOD_NAME}] CRITICAL ERROR in LoadData:\n{ex}");
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_isInitialized)
                return;

            try
            {
                _moduleManager?.UpdateAll();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[{MOD_NAME}] ERROR in UpdateBeforeSimulation:\n{ex}");
            }
        }

        public override void SaveData()
        {
            if (!_isInitialized)
                return;

            try
            {
                _moduleManager?.SaveAll();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[{MOD_NAME}] ERROR in SaveData:\n{ex}");
            }
        }

        protected override void UnloadData()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole($"[{MOD_NAME}] Session UnloadData started...");
                
                _moduleManager?.CloseAll();
                
                _isInitialized = false;
                MyLog.Default.WriteLineAndConsole($"[{MOD_NAME}] Session UnloadData completed!");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[{MOD_NAME}] ERROR in UnloadData:\n{ex}");
            }
        }
    }
}