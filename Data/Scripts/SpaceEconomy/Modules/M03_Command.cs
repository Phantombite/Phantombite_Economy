using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.Utils;
using PhantombiteEconomy.Core;

namespace PhantombiteEconomy.Modules
{
    /// <summary>
    /// M04 - CommandModule
    /// Chat-basiertes Command-System mit Admin-Check
    /// 
    /// Prefix: !sem
    /// Beispiel: !sem storeitems ore refresh
    /// 
    /// WICHTIG:
    /// - Commands laufen überall (Singleplayer + Multiplayer Client + Server)
    /// - Singleplayer: Keine Admin-Abfrage (jeder ist Admin)
    /// - Multiplayer: Admin-Check via PromoteLevel
    /// 
    /// Framework für später: Auto-Transfer, Store-Management, etc.
    /// </summary>
    public class CommandModule : IModule
    {
        public string ModuleName => "Command";

        private const string COMMAND_PREFIX = "!sem";
        private bool _initialized = false;
        private AutoTransferModule _autoTransfer;
        private EventManagerModule _eventManager;
        private TraderStoreBlockModule _traderStore;
        private TraderVendingMachineModule _vendingMachine;

        // Command Registry: name -> (adminOnly, handler)
        private Dictionary<string, CommandInfo> _commands = new Dictionary<string, CommandInfo>();

        // Command Info Structure
        private class CommandInfo
        {
            public bool AdminOnly;
            public Action<IMyPlayer, string[]> Handler;
            public string Description;

            public CommandInfo(bool adminOnly, Action<IMyPlayer, string[]> handler, string description)
            {
                AdminOnly = adminOnly;
                Handler = handler;
                Description = description;
            }
        }

        public void Init()
        {
            if (_initialized)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Command: Already initialized");
                return;
            }

            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Command: Initializing...");

            // Chat-Listener registrieren
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;

            // Commands registrieren (später erweiterbar)
            RegisterCommands();

            _initialized = true;
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Command: Initialized successfully");
        }

        public void Update()
        {
            // Keine Update-Logik nötig
        }

        public void SaveData()
        {
            // Keine Daten zu speichern
        }

        public void Close()
        {
            if (!_initialized)
                return;

            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Command: Closing...");

            // Chat-Listener abmelden
            if (MyAPIGateway.Utilities != null)
            {
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            }

            _initialized = false;
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Command: Closed");
        }

        /// <summary>
        /// Wird von Session nach Init beider Module aufgerufen
        /// </summary>
        public void SetAutoTransferModule(AutoTransferModule autoTransfer)
        {
            _autoTransfer = autoTransfer;
        }

        /// <summary>
        /// Wird von Session nach Init beider Module aufgerufen
        /// </summary>
        public void SetEventManagerModule(EventManagerModule eventManager)
        {
            _eventManager = eventManager;
        }

        public void SetStoreModules(TraderStoreBlockModule traderStore, TraderVendingMachineModule vendingMachine)
        {
            _traderStore = traderStore;
            _vendingMachine = vendingMachine;
        }

        /// <summary>
        /// Registriert alle Commands (später erweiterbar)
        /// </summary>
        private void RegisterCommands()
        {
            RegisterCommand("help",          false, CmdHelp,          "Zeigt alle verfügbaren Commands");
            RegisterCommand("autotrans",     false, CmdAutoTrans,     "AutoTransfer steuern [in|out|sort in|sort out|stop|scan|report|help]");
            RegisterCommand("forcerefresh",  true,  CmdForceRefresh,  "[Admin] Alle Stores sofort initialisieren");
            RegisterCommand("pricelist",     true,  CmdPricelist,     "[Admin] Pricelists neu laden [reload]");

            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Command: 4 commands registered");
        }

        /// <summary>
        /// Handler für !sem help
        /// Admins sehen alle Commands, normale Spieler nur ihre
        /// </summary>
        private void CmdHelp(IMyPlayer player, string[] args)
        {
            bool isAdmin = IsAdmin(player);
            SendMessage(player, "=== PhantombiteEconomy Commands ===");

            foreach (var kvp in _commands)
            {
                if (kvp.Value.AdminOnly && !isAdmin)
                    continue;

                SendMessage(player, $"  !sem {kvp.Key} — {kvp.Value.Description}");
            }
        }

        /// <summary>
        /// Handler für !sem forcerefresh
        /// </summary>
        private void CmdForceRefresh(IMyPlayer player, string[] args)
        {
            if (_eventManager == null)
            {
                SendMessage(player, "EventManager module not available.");
                return;
            }

            SendMessage(player, "ForceRefresh: Running...");
            _eventManager.ForceRefreshAll();
            _traderStore?.ForceRefresh();
            _vendingMachine?.ForceRefresh();
            SendMessage(player, "ForceRefresh: Done.");
        }

        /// <summary>
        /// Handler für !sem pricelist reload
        /// </summary>
        private void CmdPricelist(IMyPlayer player, string[] args)
        {
            if (_eventManager == null)
            {
                SendMessage(player, "EventManager module not available.");
                return;
            }

            if (args.Length == 0 || args[0].ToLower() != "reload")
            {
                SendMessage(player, "Usage: !sem pricelist reload");
                return;
            }

            SendMessage(player, "Pricelist: Reloading config and re-rolling all categories...");
            _eventManager.ReloadPricelists();
            SendMessage(player, "Pricelist: Done.");
        }

        /// <summary>
        /// Handler für !sem autotrans [in|out|stop|scan]
        /// </summary>
        private void CmdAutoTrans(IMyPlayer player, string[] args)
        {
            if (_autoTransfer == null)
            {
                SendMessage(player, "AutoTransfer module not available.");
                return;
            }

            _autoTransfer.HandleCommand(player, args);
        }

        /// <summary>
        /// Registriert einen Command
        /// </summary>
        /// <param name="name">Command-Name (ohne Prefix)</param>
        /// <param name="adminOnly">Nur für Admins?</param>
        /// <param name="handler">Handler-Funktion</param>
        public void RegisterCommand(string name, bool adminOnly, Action<IMyPlayer, string[]> handler, string description = "")
        {
            if (_commands.ContainsKey(name))
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] Command: WARNING - Command '{name}' already registered!");
                return;
            }

            _commands[name] = new CommandInfo(adminOnly, handler, description);
            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] Command: Registered '{name}' (Admin: {adminOnly})");
        }

        /// <summary>
        /// Chat-Message Event Handler
        /// </summary>
        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            try
            {
                // Prüfen ob Message mit !sem beginnt
                if (!messageText.StartsWith(COMMAND_PREFIX, StringComparison.OrdinalIgnoreCase))
                    return;

                // Command von anderen Spielern verstecken
                sendToOthers = false;

                // Spieler holen
                IMyPlayer player = MyAPIGateway.Session?.Player;
                if (player == null)
                {
                    MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Command: No player found for message");
                    return;
                }

                // Command parsen und ausführen
                ParseAndExecute(player, messageText);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] Command ERROR in OnMessageEntered:\n{ex}");
            }
        }

        /// <summary>
        /// Parst und führt Command aus
        /// </summary>
        private void ParseAndExecute(IMyPlayer player, string message)
        {
            try
            {
                // Prefix entfernen und in Tokens splitten
                string commandText = message.Substring(COMMAND_PREFIX.Length).Trim();
                
                if (string.IsNullOrWhiteSpace(commandText))
                {
                    SendMessage(player, "Usage: !sem <command> [args]");
                    return;
                }

                // In Tokens splitten
                string[] tokens = commandText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string commandName = tokens[0].ToLower();
                string[] args = new string[tokens.Length - 1];
                Array.Copy(tokens, 1, args, 0, args.Length);

                // Command suchen
                if (!_commands.ContainsKey(commandName))
                {
                    SendMessage(player, $"Unknown command: {commandName}");
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] Command: Player '{player.DisplayName}' tried unknown command: {commandName}");
                    return;
                }

                CommandInfo cmdInfo = _commands[commandName];

                // Admin-Check
                if (cmdInfo.AdminOnly && !IsAdmin(player))
                {
                    SendMessage(player, "Error: This command requires admin privileges!");
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] Command: Player '{player.DisplayName}' tried admin command without permissions: {commandName}");
                    return;
                }

                // Command ausführen
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] Command: Player '{player.DisplayName}' executed: {commandName}");
                cmdInfo.Handler(player, args);
            }
            catch (Exception ex)
            {
                SendMessage(player, "Error executing command!");
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] Command ERROR in ParseAndExecute:\n{ex}");
            }
        }

        /// <summary>
        /// Prüft ob Spieler Admin ist
        /// Singleplayer: Immer true (keine Admin-Abfrage nötig)
        /// Multiplayer: Echte PromoteLevel-Prüfung
        /// </summary>
        public bool IsAdmin(IMyPlayer player)
        {
            if (player == null)
                return false;

            // Singleplayer: Jeder ist automatisch Admin
            if (MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE)
                return true;

            // Multiplayer: Admin oder höher
            return player.PromoteLevel >= MyPromoteLevel.Admin;
        }

        /// <summary>
        /// Sendet Chat-Message an Spieler
        /// </summary>
        public void SendMessage(IMyPlayer player, string message)
        {
            if (player == null || MyAPIGateway.Utilities == null)
                return;

            try
            {
                MyAPIGateway.Utilities.ShowMessage("[PhantombiteEconomy]", message);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] Command ERROR sending message:\n{ex}");
            }
        }
    }
}