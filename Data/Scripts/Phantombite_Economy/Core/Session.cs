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
    /// Haupt-Session für PhantomBite Economy.
    ///
    /// Ladereihenfolge:
    /// - Economy_Command      — Registrierung beim Core + Command-Empfang
    /// - Economy_FileManager  — eigene Config + Pricelists
    /// - Economy_EventManager — Refresh-Timer + Preise würfeln
    /// - Economy_TraderStore  — TraderStore Blöcke verwalten
    /// - Economy_VendingMachine — VendingMachine Blöcke verwalten
    ///
    /// AutoTransfer ist ein eigenständiger Mod (Phantombite_AutoTransfer)
    /// und läuft komplett unabhängig von Economy.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class PhantombiteEconomySession : MySessionComponentBase
    {
        private ModuleManager _moduleManager;

        private EconomyCommandModule       _commandModule;
        private FileManagerModule          _fileManager;
        private TraderStoreBlockModule     _traderStore;
        private TraderVendingMachineModule _vendingMachine;
        private EventManagerModule         _eventManager;

        private bool _isInitialized = false;
        private const string MOD_NAME = "PhantombiteEconomy";

        public override void LoadData()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] Session LoadData gestartet...");

                _moduleManager  = new ModuleManager();

                _commandModule  = new EconomyCommandModule();
                _fileManager    = new FileManagerModule();
                _eventManager   = new EventManagerModule();
                _traderStore    = new TraderStoreBlockModule();
                _vendingMachine = new TraderVendingMachineModule();

                // Module verknüpfen
                _commandModule.SetFileManager(_fileManager);
                _commandModule.SetEventManagerModule(_eventManager);
                _commandModule.SetStoreModules(_traderStore, _vendingMachine);

                // Logger an alle Module übergeben
                _traderStore.SetLogger(_commandModule);
                _vendingMachine.SetLogger(_commandModule);
                _eventManager.SetLogger(_commandModule);

                // Reihenfolge wichtig: Command zuerst damit Logger bereit ist
                _moduleManager.RegisterModule(_commandModule);
                _moduleManager.RegisterModule(_fileManager);
                _moduleManager.RegisterModule(_eventManager);
                _moduleManager.RegisterModule(_traderStore);
                _moduleManager.RegisterModule(_vendingMachine);

                _moduleManager.InitAll();

                _isInitialized = true;
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] Session LoadData abgeschlossen.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] KRITISCHER FEHLER in LoadData:\n" + ex);
            }
        }

        public override void BeforeStart()
        {
            if (!_isInitialized) return;

            try
            {
                // Core anschreiben — READY wird von Core gesendet
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] FEHLER in BeforeStart:\n" + ex);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_isInitialized) return;

            try { _moduleManager.UpdateAll(); }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] FEHLER in UpdateBeforeSimulation:\n" + ex);
            }
        }

        public override void SaveData()
        {
            if (!_isInitialized) return;

            try { _moduleManager.SaveAll(); }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] FEHLER in SaveData:\n" + ex);
            }
        }

        protected override void UnloadData()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] Session UnloadData gestartet...");
                _moduleManager?.CloseAll();
                _isInitialized = false;
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] Session UnloadData abgeschlossen.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] FEHLER in UnloadData:\n" + ex);
            }
        }
    }
}