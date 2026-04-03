using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using PhantombiteEconomy.Core;

namespace PhantombiteEconomy.Modules
{
    /// <summary>
    /// Economy_Command
    ///
    /// Registriert Economy beim PhantomBite Core über Messaging.
    /// Empfängt Commands vom Core und leitet sie an die entsprechenden Module weiter.
    /// Stellt Log-API und SendMessage für alle Economy-Module bereit.
    ///
    /// Kanal:
    ///   Core empfängt Registrierung: 1995000
    ///   Economy empfängt Commands:   1995004
    ///   Log + CMDRESULT:             1995999
    ///
    /// Commands:
    ///   forcerefresh  [Admin] — Alle Stores sofort initialisieren
    ///   pricelist reload [Admin] — Pricelists neu laden
    ///
    /// AutoTransfer ist ein eigenständiger Mod (Phantombite_AutoTransfer)
    /// und kommuniziert direkt mit Core über Kanal 1995009.
    /// </summary>
    public class EconomyCommandModule : IModule
    {
        public string ModuleName { get { return "Economy_Command"; } }

        // ── Kanäle ───────────────────────────────────────────────────────────
        private const long   CORE_CHANNEL    = 1995000L;
        private const long   ECONOMY_CHANNEL = 1995004L;
        private const long   LOG_CHANNEL     = 1995999L;
        private const string MOD_NAME        = "Phantombite_Economy";

        private bool _initialized = false;

        // ── Log-Level (vom Core gesetzt) ─────────────────────────────────────
        private enum LogLevel { Normal = 0, Debug = 1, Trace = 2 }
        private LogLevel _logLevel = LogLevel.Normal;

        // ── Modul-Referenzen ─────────────────────────────────────────────────
        private FileManagerModule          _fileManager;
        private EventManagerModule         _eventManager;
        private TraderStoreBlockModule     _traderStore;
        private TraderVendingMachineModule _vendingMachine;

        // ── Setup ─────────────────────────────────────────────────────────────

        public void SetFileManager(FileManagerModule fm)  { _fileManager   = fm; }
        public void SetEventManagerModule(EventManagerModule em) { _eventManager  = em; }
        public void SetStoreModules(TraderStoreBlockModule ts, TraderVendingMachineModule vm)
        {
            _traderStore    = ts;
            _vendingMachine = vm;
        }

        // ── IModule ──────────────────────────────────────────────────────────

        public void Init()
        {
            if (_initialized) return;
            MyAPIGateway.Utilities.RegisterMessageHandler(ECONOMY_CHANNEL, OnMessageReceived);
            _initialized = true;
        }

        public void Update()   { }
        public void SaveData() { }

        public void Close()
        {
            if (!_initialized) return;
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.UnregisterMessageHandler(ECONOMY_CHANNEL, OnMessageReceived);
            _initialized = false;
        }

        // ── Nachrichten vom Core ──────────────────────────────────────────────

        private void OnMessageReceived(object data)
        {
            try
            {
                string msg = data as string;
                if (string.IsNullOrEmpty(msg)) return;

                if (msg == "READY")
                {
                    Trace("Economy_Command", "READY empfangen — sende Registrierung...");
                    RegisterWithCore();
                    return;
                }

                if (msg.StartsWith("LOGLEVEL|"))
                {
                    string levelStr = msg.Substring(9).ToLower();
                    _logLevel = levelStr == "trace" ? LogLevel.Trace
                              : levelStr == "debug" ? LogLevel.Debug
                              : LogLevel.Normal;
                    Trace("Economy_Command", "LOGLEVEL gesetzt: " + _logLevel);
                    return;
                }

                if (msg.StartsWith("CMD|"))
                    OnCommandReceived(msg);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Economy_Command: Fehler in OnMessageReceived: " + ex.Message);
            }
        }

        // ── Registrierung beim Core ───────────────────────────────────────────

        private void RegisterWithCore()
        {
            try
            {
                string msg = "REGISTER"
                    + "|economy"
                    + "|PhantomBite Economy"
                    + "|" + ECONOMY_CHANNEL
                    + "|forcerefresh:1:Alle Stores sofort initialisieren"
                    + "|pricelist:1:Pricelists neu laden (!pbc economy pricelist reload)";

                MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, msg);
                Trace("Economy_Command", "Registrierung an Core gesendet");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Economy_Command: Fehler bei Registrierung: " + ex.Message);
            }
        }

        // ── Command Empfang vom Core ──────────────────────────────────────────

        private void OnCommandReceived(string msg)
        {
            try
            {
                string[] parts = msg.Split('|');
                if (parts.Length < 2) return;

                string command = parts[1].ToLower();

                // STEAM:steamId aus letztem Argument extrahieren
                ulong steamId = 0;
                int   argEnd  = parts.Length;
                if (parts[parts.Length - 1].StartsWith("STEAM:"))
                {
                    ulong.TryParse(parts[parts.Length - 1].Substring(6), out steamId);
                    argEnd = parts.Length - 1;
                }

                string[] args = new string[argEnd - 2];
                Array.Copy(parts, 2, args, 0, args.Length);
                string argsJoined = string.Join("|", args);

                Debug("Economy_Command", "Command empfangen: " + command
                    + (argsJoined.Length > 0 ? " " + argsJoined : "") + " — SteamId: " + steamId);

                IMyPlayer player = FindPlayer(steamId);

                bool   executed  = false;
                string resultMsg = "";

                switch (command)
                {
                    case "forcerefresh":
                        if (_eventManager == null)
                        {
                            resultMsg = "EventManager Modul nicht verfügbar.";
                            break;
                        }
                        _eventManager.ForceRefreshAll();
                        _traderStore?.ForceRefresh();
                        _vendingMachine?.ForceRefresh();
                        executed  = true;
                        resultMsg = "Economy: Alle Stores aktualisiert.";
                        Debug("Economy_Command", "ForceRefresh ausgeführt");
                        break;

                    case "pricelist":
                        if (_eventManager == null)
                        {
                            resultMsg = "EventManager Modul nicht verfügbar.";
                            break;
                        }
                        if (args.Length == 0 || args[0].ToLower() != "reload")
                        {
                            resultMsg = "Usage: !pbc economy pricelist reload";
                            break;
                        }
                        _eventManager.ReloadPricelists();
                        executed  = true;
                        resultMsg = "Economy: Pricelists neu geladen.";
                        Debug("Economy_Command", "Pricelist reload ausgeführt");
                        break;

                    default:
                        resultMsg = "Economy: Unbekannter Command: " + command;
                        break;
                }

                // CMDRESULT zurück an Core — immer "economy" als modName
                string status = executed ? "ok" : "fail";
                string result = "CMDRESULT|economy|" + command + "|" + argsJoined + "|" + steamId + "|" + status + "|" + resultMsg;
                MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, result);
                Trace("Economy_Command", "CMDRESULT gesendet: status=" + status + ", msg='" + resultMsg + "'");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Economy_Command: Fehler in OnCommandReceived: " + ex.Message);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private IMyPlayer FindPlayer(ulong steamId)
        {
            try
            {
                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);
                foreach (var p in players)
                    if (p.SteamUserId == steamId) return p;
            }
            catch { }
            return null;
        }

        public bool IsAdmin(IMyPlayer player)
        {
            if (player == null) return false;
            if (MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE) return true;
            return player.PromoteLevel >= MyPromoteLevel.Admin;
        }

        // ── Chat-Feedback für laufende Economy-Meldungen ─────────────────────

        public void SendMessage(IMyPlayer player, string message)
        {
            if (MyAPIGateway.Utilities == null) return;
            try { MyAPIGateway.Utilities.ShowMessage("[Economy]", message); }
            catch { }
        }

        public void SendToPlayer(ulong steamId, string message)
        {
            try
            {
                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);
                foreach (var p in players)
                {
                    if (p.SteamUserId == steamId)
                    {
                        MyAPIGateway.Utilities.ShowMessage("[Economy]", message);
                        return;
                    }
                }
            }
            catch { }
        }

        // ── Log API für alle Economy-Module ──────────────────────────────────

        public void Warn(string module, string message)  { SendLog("WARN",  module, message); }
        public void Error(string module, string message) { SendLog("ERROR", module, message); }

        public void Info(string module, string message)
        {
            if (_logLevel < LogLevel.Debug) return;
            SendLog("INFO", module, message);
        }

        public void Debug(string module, string message)
        {
            if (_logLevel < LogLevel.Debug) return;
            SendLog("DEBUG", module, message);
        }

        public void Trace(string module, string message)
        {
            if (_logLevel < LogLevel.Trace) return;
            SendLog("TRACE", module, message);
        }

        private void SendLog(string level, string module, string message)
        {
            try
            {
                MyAPIGateway.Utilities.SendModMessage(LOG_CHANNEL,
                    "LOG|" + MOD_NAME + "|" + level + "|" + module + "|" + message);
            }
            catch { }
        }
    }
}