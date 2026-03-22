using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using PhantombiteEconomy.Core;

namespace PhantombiteEconomy.Modules
{
    /// <summary>
    /// M07 - AutoTransferModule
    ///
    /// Verwaltet Ladezonen für den automatischen Item-Transfer
    /// zwischen Spieler-Inventar und Trader Zone Containern.
    ///
    /// PHASE 1 (aktuell):
    ///   - CustomData Templates in Trader-Blöcke schreiben
    ///   - Beim Serverstart: alle vorhandenen Blöcke prüfen
    ///   - Bei neuem Block: sofort Template schreiben
    ///   - Validierung: fehlende/ungültige CustomData → Template neu schreiben
    ///
    /// PHASE 2 (folgt nach Test):
    ///   - Zonen-Aufbau via IsConnectedTo()
    ///   - Andock-Erkennung + Spieler-Reservierung
    ///
    /// PHASE 3 (folgt nach Test):
    ///   - Transfer IN / OUT Logik
    /// </summary>
    public class AutoTransferModule : IModule
    {
        public string ModuleName => "AutoTransfer";

        // -------------------------------------------------------------------------
        // Block SubtypeIds — alle Trader-Blöcke die ein Template bekommen
        // -------------------------------------------------------------------------

        private static readonly HashSet<string> CONTAINER_SUBTYPES = new HashSet<string>
        {
            "TraderZoneContainer",
            "TraderLargeBlockLargeContainer",
            "TraderLargeBlockLargeIndustrialContainer"
        };

        private static readonly HashSet<string> CONNECTOR_SUBTYPES = new HashSet<string>
        {
            "TraderConnector",
            "TraderConnectorSmall",
            "TraderConnectorMedium",
            "TraderLargeBlockInsetConnector",
            "TraderLargeBlockInsetConnectorSmall",
            "TraderSmallBlockInsetConnector",
            "TraderSmallBlockInsetConnectorMedium"
        };

        private static readonly HashSet<string> LCD_SUBTYPES = new HashSet<string>
        {
            "TraderSmallTextPanel",
            "TraderSmallLCDPanelWide",
            "TraderSmallLCDPanel",
            "TraderLargeBlockCorner_LCD_1",
            "TraderLCDLargeBlockCorner_LCD_2",
            "TraderLargeBlockCorner_LCD_Flat_1",
            "TraderLargeBlockCorner_LCD_Flat_2",
            "TraderSmallBlockCorner_LCD_1",
            "TraderSmallBlockCorner_LCD_2",
            "TraderSmallBlockCorner_LCD_Flat_1",
            "TraderSmallBlockCorner_LCD_Flat_2",
            "TraderLargeTextPanel",
            "TraderLargeLCDPanel",
            "TraderLargeLCDPanelWide"
        };

        private static readonly HashSet<string> SORTER_SUBTYPES = new HashSet<string>
        {
            "TraderLargeBlockConveyorSorter",
            "TraderMediumBlockConveyorSorter",
            "TraderSmallBlockConveyorSorter"
        };

        // -------------------------------------------------------------------------
        // CustomData Templates
        // -------------------------------------------------------------------------

        // ZoneNumber=0 → ungültig, Zone inaktiv bis Admin die richtige Zahl einträgt
        private const string TEMPLATE_CONTAINER =
            "[AutoTransfer]\r\n" +
            "# ZoneNumber=0 ist ungültig — bitte auf gewünschte Zone setzen (z.B. ZoneNumber=1)\r\n" +
            "ZoneNumber=0\r\n" +
            "AutoTransIn=true\r\n" +
            "AutoTransOut=true\r\n" +
            "\r\n" +
            "[ZoneBlocks]\r\n" +
            "# Wird automatisch befüllt nach !sem autotrans scan\r\n" +
            "Connectors=\r\n" +
            "LCDs=\r\n" +
            "SortersIn=\r\n" +
            "SortersOut=\r\n";

        private const string TEMPLATE_SORTER =
            "[AutoTransfer]\r\n" +
            "# ZoneNumber=0 ist ungültig — bitte auf gewünschte Zone setzen (z.B. ZoneNumber=1)\r\n" +
            "ZoneNumber=0\r\n" +
            "# SorterMode: in  = zieht Items aus Spieler-Schiff -> Container\r\n" +
            "#             out = schickt Items aus Container -> Spieler-Schiff\r\n" +
            "SorterMode=in\r\n";

        private const string TEMPLATE_CONNECTOR =
            "[AutoTransfer]\r\n" +
            "# ZoneNumber=0 ist ungültig — bitte auf gewünschte Zone setzen (z.B. ZoneNumber=1)\r\n" +
            "ZoneNumber=0\r\n";

        private const string TEMPLATE_LCD =
            "[AutoTransfer]\r\n" +
            "# ZoneNumber=0 ist ungültig — bitte auf gewünschte Zone setzen (z.B. ZoneNumber=1)\r\n" +
            "# LCDMode: Main = alle Zonen, Zone = Status + Container-Liter, List = Item-Liste\r\n" +
            "ZoneNumber=0\r\n" +
            "LCDMode=Main\r\n";

        // -------------------------------------------------------------------------
        // Required Keys für Validierung
        // -------------------------------------------------------------------------

        private static readonly string[] REQUIRED_CONTAINER = { "[AutoTransfer]", "ZoneNumber=", "AutoTransIn=", "AutoTransOut=", "[ZoneBlocks]", "Connectors=", "LCDs=", "SortersIn=", "SortersOut=" };
        private static readonly string[] REQUIRED_CONNECTOR = { "[AutoTransfer]", "ZoneNumber=" };
        private static readonly string[] REQUIRED_LCD       = { "[AutoTransfer]", "ZoneNumber=", "LCDMode=" };
        private static readonly string[] REQUIRED_SORTER    = { "[AutoTransfer]", "ZoneNumber=", "SorterMode=" };

        // -------------------------------------------------------------------------
        // Inner Classes
        // -------------------------------------------------------------------------

        private class ZoneData
        {
            public int          ZoneNumber;
            public long         ContainerEntityId;
            public List<long>   ConnectorIds  = new List<long>();
            public List<long>   LcdIds        = new List<long>();
            public List<long>   SorterInIds   = new List<long>();
            public List<long>   SorterOutIds  = new List<long>();
            public bool         HasError;
            public string       ErrorMessage = "";
        }

        /// <summary>Dynamischer RAM-State pro Zone — wer ist angedockt</summary>
        private class ZoneState
        {
            public int          ZoneNumber;
            public ulong        OccupiedByPlayerId = 0;
            public string       PlayerName         = "";
            public TransferMode Mode               = TransferMode.None;
            public int          TransferCounter    = 0;
            public string       OutCurrentSubtype  = "";
            public int          LcdScrollOffset    = 0;
            public int          LcdScrollCounter   = 0;
            // Sort IN: Wachstumsprüfung
            public float        LastContainerVolume    = -1f;
            public int          SortInNoGrowthCounter  = 0;
            // Eigener Takt-Counter für Sort-Checks (unabhängig von TransferCounter)
            public int          SortCheckCounter       = 0;
            // Transfer Log
            public StringBuilder                SessionLog      = new StringBuilder();
            public Dictionary<string, float>    ContainerSnapshot = null;
            public string                       SessionStart    = "";
            public bool                         HasTransferError = false;
            // Einmalige Log-Flags — verhindert Spam bei anhaltenden Zuständen
            public bool                         PlayerInvFullLogged  = false;
            public bool                         ContainerItemSkipped = false;
        }

        private enum TransferMode { None, In, Out, SortIn, SortOut }

        // -------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------

        private CommandModule             _commandModule;
        private FileManagerModule         _fileManager;
        private bool                      _initialized    = false;
        private bool                      _zonesLoaded    = false;
        private int                       _updateCounter  = 0;
        private const int                 UPDATE_INTERVAL   = 60;  // ~1 Sekunde
        private const int                 TRANSFER_INTERVAL = 2;   // 2 Sekunden zwischen Stapeln
        private const int                 LCD_SCROLL_INTERVAL = 3; // Sekunden pro Scroll-Schritt
        private const int                 SORT_CHECK_INTERVAL = 5; // Sekunden zwischen Wachstums-Checks
        private const int                 NO_GROWTH_MAX       = 2; // Messungen ohne Wachstum → Stopp

        // Log-Modus: false = nur bei Fehler, true = immer
        private bool _logMode = false;

        // RAM: ZoneNumber → ZoneData (wird per scan befüllt)
        private Dictionary<int, ZoneData>  _zones  = new Dictionary<int, ZoneData>();

        // RAM: ZoneNumber → ZoneState (dynamisch — wer ist angedockt)
        private Dictionary<int, ZoneState> _states = new Dictionary<int, ZoneState>();

        // KeepList: SubtypeId → Mindestmenge
        private Dictionary<string, int> _keepList = new Dictionary<string, int>();

        // Cache: Main-LCD EntityIds (befüllt beim scan)
        private List<long> _mainLcdIds = new List<long>();

        // Wiederverwendbare Objekte — GC-Druck im Update-Pfad vermeiden
        private StringBuilder         _sbLcd        = new StringBuilder();
        private List<IMyPlayer>       _reusePlayers  = new List<IMyPlayer>();
        private List<VRage.Game.ModAPI.Ingame.MyInventoryItem> _reuseItems = new List<VRage.Game.ModAPI.Ingame.MyInventoryItem>();

        // -------------------------------------------------------------------------
        // Konstruktor
        // -------------------------------------------------------------------------

        public AutoTransferModule(CommandModule commandModule, FileManagerModule fileManager)
        {
            _commandModule = commandModule;
            _fileManager   = fileManager;
        }

        // -------------------------------------------------------------------------
        // IModule
        // -------------------------------------------------------------------------

        public void Init()
        {
            if (_initialized) return;

            if (LoggerModule.DebugMode)
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] AutoTransfer: Initializing...");

            try
            {
                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    // KeepList aus GlobalConfig laden
                    LoadKeepList();

                    // Alle vorhandenen Trader-Blöcke prüfen und Template schreiben wenn nötig
                    ScanAndDeployTemplates();

                    // Zonen werden beim ersten Update geladen (Entities noch nicht bereit in Init)

                    // Event: neues Grid → GridBlockAdded Event registrieren
                    MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
                }

                _initialized = true;
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] AutoTransfer: Initialized successfully.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in Init:\n{ex}");
            }
        }

        public void Update()
        {
            if (!_initialized || !MyAPIGateway.Multiplayer.IsServer) return;

            try
            {
                // Zonen beim ersten Update laden — Entities sind jetzt bereit
                if (!_zonesLoaded)
                {
                    LoadZonesFromCustomData();
                    _zonesLoaded = true;
                    return;
                }

                _updateCounter++;
                if (_updateCounter < UPDATE_INTERVAL) return;
                _updateCounter = 0;

                CheckConnections();
                RunTransfers();
                UpdateLCDs();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in Update:\n{ex}");
            }
        }

        public void SaveData()
        {
            // Phase 1: nichts zu speichern
        }

        public void Close()
        {
            if (!_initialized) return;

            try
            {
                if (MyAPIGateway.Entities != null)
                    MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in Close:\n{ex}");
            }

            _initialized = false;
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] AutoTransfer: Closed.");
        }

        // -------------------------------------------------------------------------
        // Command Handler (von M03 aufgerufen)
        // -------------------------------------------------------------------------

        public void HandleCommand(IMyPlayer player, string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    _commandModule.SendMessage(player, "Usage: !sem autotrans <in|out|stop|scan|report>");
                    return;
                }

                string cmd = args[0].ToLower();

                switch (cmd)
                {
                    case "help":
                        _commandModule.SendMessage(player, "=== !sem autotrans ===");
                        _commandModule.SendMessage(player, "  in        — Spieler-Inventar in Container einlagern");
                        _commandModule.SendMessage(player, "  out       — Container in Spieler-Inventar entnehmen");
                        _commandModule.SendMessage(player, "  sort in   — Sortierer: Schiff -> Container einschalten");
                        _commandModule.SendMessage(player, "  sort out  — Sortierer: Container -> Schiff einschalten");
                        _commandModule.SendMessage(player, "  stop      — Alles stoppen (Transfer + Sortierer)");
                        if (_commandModule.IsAdmin(player))
                        {
                            _commandModule.SendMessage(player, "  scan      — [Admin] Zonen neu einlesen");
                            _commandModule.SendMessage(player, "  report    — [Admin] Zonenstatus anzeigen");
                            _commandModule.SendMessage(player, "  log on/off — [Admin] Transfer-Log immer speichern");
                        }
                        break;
                    case "scan":
                        if (!_commandModule.IsAdmin(player))
                        {
                            _commandModule.SendMessage(player, "Fehler: Nur Admins können scan ausführen.");
                            return;
                        }
                        ExecuteZoneScan(player);
                        break;

                    case "report":
                        if (!_commandModule.IsAdmin(player))
                        {
                            _commandModule.SendMessage(player, "Fehler: Nur Admins können report ausführen.");
                            return;
                        }
                        ExecuteReport(player);
                        break;

                    case "in":
                        ExecuteSetMode(player, TransferMode.In);
                        break;

                    case "out":
                        ExecuteSetMode(player, TransferMode.Out);
                        break;

                    case "sort":
                        if (args.Length < 2)
                        {
                            _commandModule.SendMessage(player, "Usage: !sem autotrans sort <in|out>");
                            return;
                        }
                        switch (args[1].ToLower())
                        {
                            case "in":  ExecuteSetMode(player, TransferMode.SortIn);  break;
                            case "out": ExecuteSetMode(player, TransferMode.SortOut); break;
                            default:
                                _commandModule.SendMessage(player, "Usage: !sem autotrans sort <in|out>");
                                break;
                        }
                        break;

                    case "stop":
                        ExecuteSetMode(player, TransferMode.None);
                        break;

                    case "log":
                        if (!_commandModule.IsAdmin(player))
                        {
                            _commandModule.SendMessage(player, "Fehler: Nur Admins können den Log-Modus ändern.");
                            return;
                        }
                        if (args.Length < 2)
                        {
                            _commandModule.SendMessage(player, $"Log-Modus: {(_logMode ? "ON" : "OFF")}  |  Usage: !sem autotrans log <on|off>");
                            return;
                        }
                        switch (args[1].ToLower())
                        {
                            case "on":
                                _logMode = true;
                                _commandModule.SendMessage(player, "Transfer-Log: ON — jede Session wird gespeichert.");
                                break;
                            case "off":
                                _logMode = false;
                                _commandModule.SendMessage(player, "Transfer-Log: OFF — nur bei Fehlern speichern.");
                                break;
                            default:
                                _commandModule.SendMessage(player, "Usage: !sem autotrans log <on|off>");
                                break;
                        }
                        break;

                    default:
                        _commandModule.SendMessage(player, $"Unbekannter Sub-Command: '{cmd}'  |  Usage: !sem autotrans <in|out|stop|scan|report|list>");
                        break;
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in HandleCommand:\n{ex}");
            }
        }

        // -------------------------------------------------------------------------
        // Transfer Commands
        // -------------------------------------------------------------------------

        private void ExecuteSetMode(IMyPlayer player, TransferMode mode)
        {
            // Spieler in einer Zone suchen
            ZoneState state = FindPlayerState(player.SteamUserId);

            if (state == null)
            {
                _commandModule.SendMessage(player, "Fehler: Nicht an einer Ladezone angedockt.");
                return;
            }

            // Zone holen damit Sortierer gesteuert werden können
            ZoneData zone;
            _zones.TryGetValue(state.ZoneNumber, out zone);

            // Zuerst alles stoppen: Sortierer aus, Script-Transfer zurücksetzen
            if (zone != null)
                SetSortersForMode(zone, TransferMode.None);

            state.Mode            = mode;
            state.TransferCounter = TRANSFER_INTERVAL;
            state.OutCurrentSubtype = "";
            state.LastContainerVolume   = -1f;
            state.SortInNoGrowthCounter = 0;
            state.SortCheckCounter      = 0;
            state.PlayerInvFullLogged   = false;
            state.ContainerItemSkipped  = false;

            // Sortierer nur für Sort-Modi einschalten
            if ((mode == TransferMode.SortIn || mode == TransferMode.SortOut) && zone != null)
            {
                SetSortersForMode(zone, mode);

                // Sort-Start: Container-Stand loggen
                string tsSort = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string sortLabel = (mode == TransferMode.SortIn) ? "SORT IN" : "SORT OUT";
                if (_logMode)
                {
                    state.SessionLog.AppendLine($"[{tsSort}] {sortLabel} gestartet:");
                    var sortSnapshot = TakeContainerSnapshot(zone);
                    if (sortSnapshot != null)
                        LogSnapshotToBuilder(state.SessionLog, sortSnapshot);
                }
                else
                {
                    state.SessionLog.AppendLine($"[{tsSort}] {sortLabel} gestartet.");
                }
            }

            string modeMsg;
            switch (mode)
            {
                case TransferMode.In:
                    modeMsg = "AutoTransfer IN gestartet — Spieler-Inventar -> Container.";
                    break;
                case TransferMode.Out:
                    modeMsg = "AutoTransfer OUT gestartet — Container -> Spieler-Inventar.";
                    break;
                case TransferMode.SortIn:
                    modeMsg = "AutoTransfer SORT IN gestartet — Schiff -> Container (Sortierer aktiv).";
                    break;
                case TransferMode.SortOut:
                    modeMsg = "AutoTransfer SORT OUT gestartet — Container -> Schiff (Sortierer aktiv).";
                    break;
                default:
                    modeMsg = "AutoTransfer gestoppt.";
                    break;
            }

            _commandModule.SendMessage(player, modeMsg);
        }

        /// <summary>
        /// Schaltet Sortierer der Zone je nach Modus ein oder aus.
        /// TransferMode.None  = alle Sortierer aus
        /// TransferMode.SortIn  = SorterIn ein, SorterOut aus
        /// TransferMode.SortOut = SorterOut ein, SorterIn aus
        /// </summary>
        private void SetSortersForMode(ZoneData zone, TransferMode mode)
        {
            try
            {
                foreach (long id in zone.SorterInIds)
                {
                    var block = MyAPIGateway.Entities.GetEntityById(id) as IMyFunctionalBlock;
                    if (block != null && !block.Closed)
                        block.Enabled = (mode == TransferMode.SortIn);
                }
                foreach (long id in zone.SorterOutIds)
                {
                    var block = MyAPIGateway.Entities.GetEntityById(id) as IMyFunctionalBlock;
                    if (block != null && !block.Closed)
                        block.Enabled = (mode == TransferMode.SortOut);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in SetSortersForMode Zone {zone.ZoneNumber}:\n{ex}");
            }
        }

        private ZoneState FindPlayerState(ulong steamId)
        {
            foreach (var kv in _states)
            {
                if (kv.Value.OccupiedByPlayerId == steamId)
                    return kv.Value;
            }
            return null;
        }

        // -------------------------------------------------------------------------
        // Transfer Logik
        // -------------------------------------------------------------------------

        private void RunTransfers()
        {
            foreach (var kv in _states)
            {
                ZoneState state = kv.Value;
                if (state.Mode == TransferMode.None || state.OccupiedByPlayerId == 0) continue;

                ZoneData zone;
                if (!_zones.TryGetValue(state.ZoneNumber, out zone)) continue;
                if (zone.HasError) continue;

                state.TransferCounter++;
                if (state.TransferCounter < TRANSFER_INTERVAL) continue;
                state.TransferCounter = 0;

                try
                {
                    if (state.Mode == TransferMode.In)
                        RunTransferIn(zone, state);
                    else if (state.Mode == TransferMode.Out)
                        RunTransferOut(zone, state);
                    else if (state.Mode == TransferMode.SortIn)
                        CheckSortIn(zone, state);
                    else if (state.Mode == TransferMode.SortOut)
                        CheckSortOut(zone, state);
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in RunTransfers Zone {state.ZoneNumber}:\n{ex}");
                }
            }
        }

        /// <summary>
        /// Transferiert einen Stapel vom Spieler-Inventar in den Container.
        /// Respektiert KeepList — Items unter Mindestmenge bleiben beim Spieler.
        /// </summary>
        private void RunTransferIn(ZoneData zone, ZoneState state)
        {
            _reusePlayers.Clear();
            MyAPIGateway.Players.GetPlayers(_reusePlayers, p => p.SteamUserId == state.OccupiedByPlayerId);
            if (_reusePlayers.Count == 0) return;

            IMyCharacter character = _reusePlayers[0].Character;
            if (character == null) return;

            IMyInventory playerInv = character.GetInventory(0);
            if (playerInv == null) return;

            IMyInventory containerInv = GetContainerInventory(zone);
            if (containerInv == null) return;

            if ((float)containerInv.CurrentVolume >= (float)containerInv.MaxVolume * 0.999f)
            {
                state.Mode = TransferMode.None;
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                state.SessionLog.AppendLine($"[{ts}] Transfer IN gestoppt — Container voll. Item blieb im Spieler-Inventar.");
                state.HasTransferError = true;
                SendToPlayer(state.OccupiedByPlayerId, "AutoTransfer IN gestoppt — Container voll.");
                return;
            }

            // Snapshot für .Type — Transfer sofort nach Snapshot (Index stabil)
            _reuseItems.Clear();
            playerInv.GetItems(_reuseItems);

            for (int i = 0; i < _reuseItems.Count; i++)
            {
                var item     = _reuseItems[i];
                int current  = (int)(float)item.Amount;
                int keep     = GetKeepAmount(item.Type.SubtypeId);
                int transfer = current - keep;

                if (transfer <= 0) continue;

                VRage.MyFixedPoint fp = (VRage.MyFixedPoint)transfer;

                // Prüfen ob Container das Item aufnehmen kann
                if (!containerInv.CanItemsBeAdded(fp, item.Type))
                {
                    if (!state.ContainerItemSkipped)
                    {
                        string tsSkip = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        string skipKey = item.Type.TypeId + "/" + item.Type.SubtypeId;
                        state.SessionLog.AppendLine($"[{tsSkip}] Transfer IN übersprungen — Container verweigert Item: {item.Type.SubtypeId} ({skipKey}) x{transfer}");
                        state.ContainerItemSkipped = true;
                    }
                    continue;
                }
                state.ContainerItemSkipped = false;

                playerInv.TransferItemTo(containerInv, i, null, true, fp, false);

                string tsItem = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string itemKey = item.Type.TypeId + "/" + item.Type.SubtypeId;
                float usedL = (float)containerInv.CurrentVolume * 1000f;
                if (_logMode)
                    state.SessionLog.AppendLine($"[{tsItem}] Transfer IN: {item.Type.SubtypeId} ({itemKey}) x{transfer} | Container: {usedL:F0}L");
                SendToPlayer(state.OccupiedByPlayerId, $"Transfer IN: {item.Type.SubtypeId} ({itemKey}) x{transfer}");
                SendToPlayer(state.OccupiedByPlayerId, $"Container: {usedL:F0}L");
                return;
            }

            // Spieler-Inventar leer oder alles auf KeepList → still warten, kein Abbruch
        }

        /// <summary>
        /// Transferiert Items vom Container ins Spieler-Inventar.
        /// Bevorzugt aktuelles Item (OutCurrentSubtype) bis es leer ist,
        /// dann erst zum nächsten Stapel wechseln.
        /// </summary>
        private void RunTransferOut(ZoneData zone, ZoneState state)
        {
            _reusePlayers.Clear();
            MyAPIGateway.Players.GetPlayers(_reusePlayers, p => p.SteamUserId == state.OccupiedByPlayerId);
            if (_reusePlayers.Count == 0) return;

            IMyCharacter character = _reusePlayers[0].Character;
            if (character == null) return;

            IMyInventory playerInv = character.GetInventory(0);
            if (playerInv == null) return;

            IMyInventory containerInv = GetContainerInventory(zone);
            if (containerInv == null) return;

            if ((float)playerInv.CurrentVolume >= (float)playerInv.MaxVolume * 0.999f)
            {
                if (!state.PlayerInvFullLogged)
                {
                    string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    state.SessionLog.AppendLine($"[{ts}] Transfer OUT pausiert — Spieler-Inventar voll. Verbleibend im Container: {(float)containerInv.CurrentVolume * 1000f:F0}L");
                    state.PlayerInvFullLogged = true;
                    SendToPlayer(state.OccupiedByPlayerId, "Transfer OUT pausiert — Spieler-Inventar voll.");
                }
                return;
            }
            state.PlayerInvFullLogged = false; // Reset sobald Platz da ist

            _reuseItems.Clear();
            containerInv.GetItems(_reuseItems);

            if (_reuseItems.Count == 0)
            {
                state.Mode              = TransferMode.None;
                state.OutCurrentSubtype = "";
                SendToPlayer(state.OccupiedByPlayerId, "AutoTransfer OUT abgeschlossen — Container leer.");
                return;
            }

            if (string.IsNullOrEmpty(state.OutCurrentSubtype))
                state.OutCurrentSubtype = _reuseItems[0].Type.SubtypeId;

            int targetIndex = -1;
            for (int i = 0; i < _reuseItems.Count; i++)
            {
                if (_reuseItems[i].Type.SubtypeId == state.OutCurrentSubtype)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex < 0)
            {
                state.OutCurrentSubtype = _reuseItems[0].Type.SubtypeId;
                targetIndex = 0;
            }

            var targetItem = _reuseItems[targetIndex];
            VRage.MyFixedPoint amount = targetItem.Amount;

            // Prüfen ob Spieler-Inventar das Item aufnehmen kann — verhindert Despawn
            if (!playerInv.CanItemsBeAdded(amount, targetItem.Type)) return;

            containerInv.TransferItemTo(playerInv, targetIndex, null, true, amount, false);

            string tsOut = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string itemKeyOut = targetItem.Type.TypeId + "/" + targetItem.Type.SubtypeId;
            float usedLOut = (float)containerInv.CurrentVolume * 1000f;
            if (_logMode)
                state.SessionLog.AppendLine($"[{tsOut}] Transfer OUT: {targetItem.Type.SubtypeId} ({itemKeyOut}) x{(int)(float)amount} | Container: {usedLOut:F0}L");
            SendToPlayer(state.OccupiedByPlayerId, $"Transfer OUT: {targetItem.Type.SubtypeId} ({itemKeyOut}) x{(int)(float)amount}");
            SendToPlayer(state.OccupiedByPlayerId, $"Container: {usedLOut:F0}L");
        }

        /// <summary>
        /// Sort IN: prüft alle SORT_CHECK_INTERVAL Sekunden ob Container-Volumen gewachsen ist.
        /// Kein Wachstum nach NO_GROWTH_MAX Messungen → Sortierer aus, Meldung.
        /// </summary>
        private void CheckSortIn(ZoneData zone, ZoneState state)
        {
            state.SortCheckCounter++;
            if (state.SortCheckCounter < SORT_CHECK_INTERVAL) return;
            state.SortCheckCounter = 0;

            IMyInventory containerInv = GetContainerInventory(zone);
            if (containerInv == null) return;

            float currentVolume = (float)containerInv.CurrentVolume;

            if (state.LastContainerVolume < 0f)
            {
                // Erste Messung — Basiswert setzen
                state.LastContainerVolume   = currentVolume;
                state.SortInNoGrowthCounter = 0;
                return;
            }

            if (currentVolume > state.LastContainerVolume + 0.001f)
            {
                // Wachstum erkannt → Counter zurücksetzen
                state.LastContainerVolume   = currentVolume;
                state.SortInNoGrowthCounter = 0;
            }
            else
            {
                state.SortInNoGrowthCounter++;

                if (state.SortInNoGrowthCounter >= NO_GROWTH_MAX)
                {
                    // Kein Wachstum — Schiff wohl leer
                    SetSortersForMode(zone, TransferMode.None);
                    state.Mode                  = TransferMode.None;
                    state.LastContainerVolume   = -1f;
                    state.SortInNoGrowthCounter = 0;

                    string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    if (_logMode)
                    {
                        state.SessionLog.AppendLine($"[{ts}] Sort IN gestoppt — Schiff scheint leer. Container-Stand:");
                        var snap = TakeContainerSnapshot(zone);
                        if (snap != null) LogSnapshotToBuilder(state.SessionLog, snap);
                    }
                    else
                    {
                        state.SessionLog.AppendLine($"[{ts}] Sort IN gestoppt — Schiff scheint leer.");
                    }
                    SendToPlayer(state.OccupiedByPlayerId, "Sort IN gestoppt — Schiff scheint leer.");
                }
            }
        }

        /// <summary>
        /// Sort OUT: prüft ob Container leer ist → Sortierer aus, Meldung.
        /// </summary>
        private void CheckSortOut(ZoneData zone, ZoneState state)
        {
            state.SortCheckCounter++;
            if (state.SortCheckCounter < SORT_CHECK_INTERVAL) return;
            state.SortCheckCounter = 0;

            IMyInventory containerInv = GetContainerInventory(zone);
            if (containerInv == null) return;

            if ((float)containerInv.CurrentVolume < 0.001f)
            {
                SetSortersForMode(zone, TransferMode.None);
                state.Mode = TransferMode.None;

                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                state.SessionLog.AppendLine($"[{ts}] Sort OUT gestoppt — Container leer.");

                SendToPlayer(state.OccupiedByPlayerId, "Sort OUT gestoppt — Container leer.");
            }
        }

        private IMyInventory GetContainerInventory(ZoneData zone)
        {
            var block = MyAPIGateway.Entities.GetEntityById(zone.ContainerEntityId) as IMyTerminalBlock;
            if (block == null || block.Closed)
            {
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: Container EntityId {zone.ContainerEntityId} nicht gefunden oder geschlossen.");
                return null;
            }
            var inv = block.GetInventory(0);
            if (inv == null)
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: Container {block.DisplayName} hat kein Inventar.");
            return inv;
        }

        private int GetKeepAmount(string subtypeId)
        {
            int keep;
            return _keepList.TryGetValue(subtypeId, out keep) ? keep : 0;
        }

        // -------------------------------------------------------------------------
        // KeepList laden
        // -------------------------------------------------------------------------

        private void LoadKeepList()
        {
            try
            {
                _keepList.Clear();

                // Alle Zeilen aus [AutoTransfer_KeepList] in GlobalConfig lesen
                bool inSection = false;
                string content = _fileManager.GetGlobalConfigRaw();

                if (string.IsNullOrWhiteSpace(content)) return;

                foreach (var rawLine in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = rawLine.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed)) continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        inSection = (trimmed == "[AutoTransfer_KeepList]");
                        continue;
                    }

                    if (!inSection) continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = trimmed.Substring(0, eq).Trim();
                    string val = trimmed.Substring(eq + 1).Trim();

                    int amount;
                    if (int.TryParse(val, out amount) && amount > 0)
                        _keepList[key] = amount;
                }

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: KeepList geladen — {_keepList.Count} Einträge.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in LoadKeepList:\n{ex}");
            }
        }

        // -------------------------------------------------------------------------
        // Andock-Erkennung
        // -------------------------------------------------------------------------

        /// <summary>
        /// Prüft alle bekannten Zonen ob ein Schiff angedockt/getrennt hat.
        /// Läuft alle UPDATE_INTERVAL Frames.
        /// Crash-isoliert: Fehler in Zone X beeinflusst Zone Y nicht.
        /// </summary>
        private void CheckConnections()
        {
            foreach (var kv in _zones)
            {
                try
                {
                    ZoneData zone = kv.Value;

                    // Fehlerhafte Zonen still überspringen
                    if (zone.HasError) continue;

                    // State für diese Zone holen oder neu erstellen
                    ZoneState state;
                    if (!_states.TryGetValue(zone.ZoneNumber, out state))
                    {
                        state = new ZoneState { ZoneNumber = zone.ZoneNumber };
                        _states[zone.ZoneNumber] = state;
                    }

                    // Prüfen ob irgendein Connector dieser Zone verbunden ist
                    IMyCubeGrid dockedGrid = FindDockedGrid(zone);

                    bool isConnected = (dockedGrid != null);
                    bool isOccupied  = (state.OccupiedByPlayerId != 0);

                    if (isConnected && !isOccupied)
                    {
                        // Neu angedockt → Besitzer ermitteln
                        ulong  playerId;
                        string playerName;
                        GetGridOwner(dockedGrid, out playerId, out playerName);

                        if (playerId != 0)
                            OnPlayerDocked(zone, state, playerId, playerName);
                    }
                    else if (!isConnected && isOccupied)
                    {
                        // Getrennt → Zone freigeben
                        OnPlayerUndocked(zone, state);
                    }
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in CheckConnections Zone {kv.Key}:\n{ex}");
                }
            }
        }

        /// <summary>
        /// Gibt das erste angedockte fremde Grid zurück das an einem Zone-Connector hängt.
        /// </summary>
        private IMyCubeGrid FindDockedGrid(ZoneData zone)
        {
            foreach (long connId in zone.ConnectorIds)
            {
                var connector = MyAPIGateway.Entities.GetEntityById(connId) as IMyShipConnector;
                if (connector == null || connector.Closed) continue;
                if (connector.Status != Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected) continue;

                var other = connector.OtherConnector;
                if (other == null) continue;

                return other.CubeGrid;
            }
            return null;
        }

        /// <summary>
        /// Ermittelt den Besitzer des Grids (BigOwners[0]).
        /// Fallback: erster Spieler dessen Character auf dem Grid sitzt.
        /// </summary>
        private void GetGridOwner(IMyCubeGrid grid, out ulong playerId, out string playerName)
        {
            playerId   = 0;
            playerName = "";

            // Primär: Grid-Besitzer
            var owners = grid.BigOwners;
            if (owners != null && owners.Count > 0)
            {
                long ownerId = owners[0];
                _reusePlayers.Clear();
                MyAPIGateway.Players.GetPlayers(_reusePlayers, p => p.IdentityId == ownerId);

                if (_reusePlayers.Count > 0)
                {
                    playerId   = _reusePlayers[0].SteamUserId;
                    playerName = _reusePlayers[0].DisplayName;
                    return;
                }
            }

            // Fallback: Spieler der im Cockpit des Grids sitzt
            _reusePlayers.Clear();
            MyAPIGateway.Players.GetPlayers(_reusePlayers);

            var cockpits = new List<IMySlimBlock>();
            grid.GetBlocks(cockpits, b => b.FatBlock is IMyCockpit);

            foreach (var slim in cockpits)
            {
                var cockpit = slim.FatBlock as IMyCockpit;
                if (cockpit == null || !cockpit.IsUnderControl) continue;

                foreach (var p in _reusePlayers)
                {
                    if (p.Character != null && cockpit.Pilot == p.Character)
                    {
                        playerId   = p.SteamUserId;
                        playerName = p.DisplayName;
                        return;
                    }
                }
            }
        }

        private void OnPlayerDocked(ZoneData zone, ZoneState state, ulong playerId, string playerName)
        {
            state.OccupiedByPlayerId = playerId;
            state.PlayerName         = playerName;

            // Session Log starten
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            state.SessionStart       = timestamp;
            state.HasTransferError   = false;
            state.SessionLog.Clear();
            state.SessionLog.AppendLine($"[{timestamp}] Zone {zone.ZoneNumber} - Spieler: {playerName} - Angedockt");

            // Container-Snapshot
            state.ContainerSnapshot = TakeContainerSnapshot(zone);
            if (_logMode && state.ContainerSnapshot != null)
            {
                state.SessionLog.AppendLine($"[{timestamp}] Container-Stand beim Andocken:");
                LogSnapshotToBuilder(state.SessionLog, state.ContainerSnapshot);
            }

            // Alle nicht-verbundenen Connectors der Zone deaktivieren
            foreach (long connId in zone.ConnectorIds)
            {
                var c = MyAPIGateway.Entities.GetEntityById(connId) as IMyShipConnector;
                if (c != null && c.Status != Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected)
                    c.Enabled = false;
            }

            SendToPlayer(playerId, $"Ladezone {zone.ZoneNumber} reserviert. Befehle: !sem autotrans in/out/sort in/sort out/stop");
            if (LoggerModule.DebugMode)
            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: '{playerName}' angedockt an Zone {zone.ZoneNumber}.");
        }

        private void OnPlayerUndocked(ZoneData zone, ZoneState state)
        {
            ulong  oldPlayerId = state.OccupiedByPlayerId;
            string oldName     = state.PlayerName;

            // Sortierer sicherheitshalber ausschalten
            SetSortersForMode(zone, TransferMode.None);

            // Session Log abschließen
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            state.SessionLog.AppendLine($"[{timestamp}] Zone {zone.ZoneNumber} - Spieler: {oldName} - Abgedockt");

            // Container-Stand beim Abdocken loggen
            var finalSnapshot = TakeContainerSnapshot(zone);
            if (finalSnapshot != null)
            {
                if (_logMode)
                {
                    state.SessionLog.AppendLine($"[{timestamp}] Container-Stand beim Abdocken:");
                    LogSnapshotToBuilder(state.SessionLog, finalSnapshot);
                }

                // Script-Transfer: Fehlerprüfung immer — unabhängig von log mode
                if (state.ContainerSnapshot != null &&
                    (state.Mode == TransferMode.In || state.Mode == TransferMode.Out ||
                     state.Mode == TransferMode.None))
                {
                    CheckSnapshotErrors(zone, state, finalSnapshot);
                }
            }

            // Log speichern?
            bool saveLog = _logMode || state.HasTransferError;
            if (saveLog)
                SaveSessionLog(zone, state);

            // State zurücksetzen
            state.OccupiedByPlayerId  = 0;
            state.PlayerName          = "";
            state.Mode                = TransferMode.None;
            state.OutCurrentSubtype   = "";
            state.ContainerSnapshot   = null;
            state.SessionLog.Clear();
            state.HasTransferError    = false;
            state.PlayerInvFullLogged  = false;
            state.ContainerItemSkipped = false;
            state.LastContainerVolume = -1f;
            state.SortInNoGrowthCounter = 0;
            state.SortCheckCounter    = 0;

            // Alle Connectors der Zone wieder aktivieren
            foreach (long connId in zone.ConnectorIds)
            {
                var c = MyAPIGateway.Entities.GetEntityById(connId) as IMyShipConnector;
                if (c != null) c.Enabled = true;
            }

            SendToPlayer(oldPlayerId, $"Ladezone {zone.ZoneNumber} freigegeben.");
            if (LoggerModule.DebugMode)
            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: '{oldName}' abgedockt von Zone {zone.ZoneNumber}.");
        }

        // -------------------------------------------------------------------------
        // Transfer Log Hilfsmethoden
        // -------------------------------------------------------------------------

        /// <summary>
        /// Liest Container-Inventar als Dictionary: "TypeId/SubtypeId" → Menge
        /// </summary>
        private Dictionary<string, float> TakeContainerSnapshot(ZoneData zone)
        {
            try
            {
                IMyInventory inv = GetContainerInventory(zone);
                if (inv == null) return null;

                var snapshot = new Dictionary<string, float>();
                _reuseItems.Clear();
                inv.GetItems(_reuseItems);

                foreach (var item in _reuseItems)
                {
                    string key = item.Type.TypeId + "/" + item.Type.SubtypeId;
                    float existing;
                    if (snapshot.TryGetValue(key, out existing))
                        snapshot[key] = existing + (float)item.Amount;
                    else
                        snapshot[key] = (float)item.Amount;
                }

                return snapshot;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in TakeContainerSnapshot:\n{ex}");
                return null;
            }
        }

        /// <summary>
        /// Schreibt Snapshot-Inhalt in einen StringBuilder
        /// </summary>
        private void LogSnapshotToBuilder(StringBuilder sb, Dictionary<string, float> snapshot)
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                sb.AppendLine("  (leer)");
                return;
            }

            foreach (var kv in snapshot)
            {
                // Key = "TypeId/SubtypeId" → SubtypeId für Anzeige extrahieren
                string[] parts = kv.Key.Split('/');
                string displayName = (parts.Length > 1) ? parts[1] : kv.Key;
                sb.AppendLine(string.Format("  {0,-28} ({1,-40}) x {2}", displayName, kv.Key, (int)kv.Value));
            }
        }

        /// <summary>
        /// Vergleicht Snapshot vor Transfer mit aktuellem Container-Stand.
        /// Trägt Fehler ins SessionLog ein und setzt HasTransferError.
        /// </summary>
        private void CheckSnapshotErrors(ZoneData zone, ZoneState state, Dictionary<string, float> finalSnapshot)
        {
            bool errorFound = false;
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var sb = new StringBuilder();
            sb.AppendLine($"[{ts}] Fehlerprüfung:");

            // Alle Items aus finalSnapshot prüfen ob sie im Snapshot vorhanden und korrekt sind
            foreach (var kv in finalSnapshot)
            {
                float before;
                state.ContainerSnapshot.TryGetValue(kv.Key, out before);
                float after = kv.Value;

                if (after < before - 0.5f) // Weniger als vorher — Item verschwunden
                {
                    string[] parts = kv.Key.Split('/');
                    string displayName = (parts.Length > 1) ? parts[1] : kv.Key;
                    sb.AppendLine($"  FEHLER: {displayName} ({kv.Key}) — vorher {(int)before}, nachher {(int)after} (Differenz: {(int)(before - after)})");
                    errorFound = true;
                }
            }

            if (!errorFound)
                sb.AppendLine("  Kein Fehler — alle Items korrekt.");

            state.SessionLog.Append(sb);
            if (errorFound)
                state.HasTransferError = true;
        }

        /// <summary>
        /// Speichert SessionLog als Datei im Storage.
        /// Dateiname: TransferLog_Zone{N}_{Datum}_{Uhrzeit}.txt
        /// </summary>
        private void SaveSessionLog(ZoneData zone, ZoneState state)
        {
            try
            {
                string safeName = state.SessionStart.Replace(":", "-").Replace(" ", "_");
                string fileName = string.Format("TransferLog_Zone{0}_{1}.txt", zone.ZoneNumber, safeName);

                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(fileName, typeof(AutoTransferModule)))
                {
                    writer.Write(state.SessionLog.ToString());
                }

                if (LoggerModule.DebugMode)
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: Log gespeichert → {fileName}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in SaveSessionLog:\n{ex}");
            }
        }

        // -------------------------------------------------------------------------
        // -------------------------------------------------------------------------
        // LCD Updates
        // -------------------------------------------------------------------------

        private void UpdateLCDs()
        {
            // Alle Zonen-LCDs aktualisieren
            foreach (var kv in _zones)
            {
                try
                {
                    ZoneData zone = kv.Value;
                    if (zone.LcdIds.Count == 0) continue;

                    ZoneState state;
                    _states.TryGetValue(zone.ZoneNumber, out state);

                    // Inventar nur lesen wenn Zone besetzt — spart GetItems Aufruf
                    float containerLiter = 0f;

                    bool zoneOccupied = (state != null && state.OccupiedByPlayerId != 0);

                    if (zoneOccupied)
                    {
                        IMyInventory containerInv = GetContainerInventory(zone);
                        if (containerInv != null)
                        {
                            _reuseItems.Clear();
                            containerInv.GetItems(_reuseItems);
                            containerLiter = (float)containerInv.CurrentVolume * 1000f;

                            state.LcdScrollCounter++;
                            if (state.LcdScrollCounter >= LCD_SCROLL_INTERVAL)
                            {
                                state.LcdScrollCounter = 0;
                                if (_reuseItems.Count > 0)
                                    state.LcdScrollOffset = (state.LcdScrollOffset + 1) % _reuseItems.Count;
                                else
                                    state.LcdScrollOffset = 0;
                            }
                        }
                    }

                    foreach (long lcdId in zone.LcdIds)
                    {
                        try
                        {
                            var lcdBlock = MyAPIGateway.Entities.GetEntityById(lcdId) as IMyTextPanel;
                            if (lcdBlock == null || lcdBlock.Closed) continue;

                            string mode = ParseLcdMode(lcdBlock.CustomData);
                            string text = "";

                            if (mode == "zone")
                                text = BuildZoneLcdText(zone, state, containerLiter);
                            else if (mode == "list")
                                text = BuildListLcdText(zone, state, _reuseItems);
                            else
                                continue;

                            lcdBlock.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                            lcdBlock.WriteText(text, false);
                        }
                        catch (Exception ex)
                        {
                            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in UpdateLCDs LCD {lcdId}:\n{ex}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in UpdateLCDs Zone {kv.Key}:\n{ex}");
                }
            }

            // Main-LCDs separat — alle Zonen auf einmal
            UpdateMainLCDs();
        }

        private void UpdateMainLCDs()
        {
            try
            {
                if (_mainLcdIds.Count == 0) return;

                string mainText = BuildMainLcdText();

                foreach (long lcdId in _mainLcdIds)
                {
                    try
                    {
                        var lcd = MyAPIGateway.Entities.GetEntityById(lcdId) as IMyTextPanel;
                        if (lcd == null || lcd.Closed) continue;

                        lcd.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                        lcd.WriteText(mainText, false);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in UpdateMainLCDs:\n{ex}");
            }
        }

        private string BuildZoneLcdText(ZoneData zone, ZoneState state, float containerLiter)
        {
            _sbLcd.Clear();
            _sbLcd.AppendLine($"=== LADEZONE {zone.ZoneNumber} ===");

            if (state == null || state.OccupiedByPlayerId == 0)
            {
                _sbLcd.AppendLine("Status: FREI");
            }
            else
            {
                _sbLcd.AppendLine("Status: BELEGT");
                _sbLcd.AppendLine($"Spieler: {state.PlayerName}");

                string modeStr = state.Mode == TransferMode.In      ? "TRANSFER IN"
                               : state.Mode == TransferMode.Out     ? "TRANSFER OUT"
                               : state.Mode == TransferMode.SortIn  ? "SORT IN (Sortierer)"
                               : state.Mode == TransferMode.SortOut ? "SORT OUT (Sortierer)"
                               : "GESTOPPT";
                _sbLcd.AppendLine($"Modus: {modeStr}");
                _sbLcd.AppendLine($"Container: {containerLiter:F0}L");
            }

            return _sbLcd.ToString();
        }

        private string BuildListLcdText(ZoneData zone, ZoneState state, List<VRage.Game.ModAPI.Ingame.MyInventoryItem> items)
        {
            _sbLcd.Clear();
            _sbLcd.AppendLine($"=== INHALT ZONE {zone.ZoneNumber} ===");

            if (items == null || items.Count == 0)
            {
                _sbLcd.AppendLine("- leer -");
                return _sbLcd.ToString();
            }

            const int PAGE_SIZE = 8;

            if (items.Count <= PAGE_SIZE)
            {
                foreach (var it in items)
                    _sbLcd.AppendLine($"{it.Type.SubtypeId,-24} x{(int)(float)it.Amount}");
            }
            else
            {
                int scrollOffset = (state != null) ? state.LcdScrollOffset : 0;

                for (int i = 0; i < PAGE_SIZE; i++)
                {
                    int idx = (scrollOffset + i) % items.Count;
                    var item = items[idx];
                    _sbLcd.AppendLine($"{item.Type.SubtypeId,-24} x{(int)(float)item.Amount}");
                }
            }

            return _sbLcd.ToString();
        }

        private string BuildMainLcdText()
        {
            _sbLcd.Clear();
            _sbLcd.AppendLine("=== LADEZONEN UEBERSICHT ===");

            if (_zones.Count == 0)
            {
                _sbLcd.AppendLine("Keine Zonen konfiguriert.");
                return _sbLcd.ToString();
            }

            var sorted = new List<int>(_zones.Keys);
            sorted.Sort();

            foreach (int zoneNumber in sorted)
            {
                ZoneData zone = _zones[zoneNumber];

                if (zone.HasError)
                {
                    _sbLcd.AppendLine($"Zone {zoneNumber}: [FEHLER]");
                    continue;
                }

                ZoneState state;
                if (!_states.TryGetValue(zoneNumber, out state) || state.OccupiedByPlayerId == 0)
                {
                    _sbLcd.AppendLine($"Zone {zoneNumber}: FREI");
                }
                else
                {
                    string modeStr = state.Mode == TransferMode.In      ? "IN"
                                   : state.Mode == TransferMode.Out     ? "OUT"
                                   : state.Mode == TransferMode.SortIn  ? "SORT-IN"
                                   : state.Mode == TransferMode.SortOut ? "SORT-OUT"
                                   : "STOP";
                    _sbLcd.AppendLine($"Zone {zoneNumber}: BELEGT - {state.PlayerName} ({modeStr})");
                }
            }

            return _sbLcd.ToString();
        }

        // -------------------------------------------------------------------------
        // List Command
        // -------------------------------------------------------------------------

        private void ExecuteList(IMyPlayer player, string[] args)
        {
            try
            {
                int zoneNumber = -1;

                // Admin kann Zone-Nummer angeben
                if (args.Length > 1)
                {
                    if (!_commandModule.IsAdmin(player))
                    {
                        _commandModule.SendMessage(player, "Fehler: Nur Admins koennen eine Zone angeben.");
                        return;
                    }
                    if (!int.TryParse(args[1], out zoneNumber))
                    {
                        _commandModule.SendMessage(player, "Fehler: Ungueltige Zone-Nummer.");
                        return;
                    }
                }
                else
                {
                    // Angedockter Spieler — eigene Zone
                    ZoneState playerState = FindPlayerState(player.SteamUserId);
                    if (playerState == null)
                    {
                        _commandModule.SendMessage(player, "Fehler: Nicht an einer Ladezone angedockt.");
                        return;
                    }
                    zoneNumber = playerState.ZoneNumber;
                }

                ZoneData zone;
                if (!_zones.TryGetValue(zoneNumber, out zone))
                {
                    _commandModule.SendMessage(player, $"Fehler: Zone {zoneNumber} nicht gefunden.");
                    return;
                }

                IMyInventory containerInv = GetContainerInventory(zone);
                if (containerInv == null)
                {
                    _commandModule.SendMessage(player, "Fehler: Container nicht erreichbar.");
                    return;
                }

                var items = new List<VRage.Game.ModAPI.Ingame.MyInventoryItem>();
                containerInv.GetItems(items);

                float usedL = (float)containerInv.CurrentVolume * 1000f;
                _commandModule.SendMessage(player, $"=== Zone {zoneNumber} Container ({usedL:F0}L) ===");

                if (items.Count == 0)
                {
                    _commandModule.SendMessage(player, "- leer -");
                    return;
                }

                foreach (var item in items)
                {
                    int amount = (int)(float)item.Amount;
                    _commandModule.SendMessage(player, $"{item.Type.SubtypeId,-28} x{amount}");
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in ExecuteList:\n{ex}");
            }
        }

        // -------------------------------------------------------------------------

        private void SendToPlayer(ulong steamId, string message)
        {
            try
            {
                _reusePlayers.Clear();
                MyAPIGateway.Players.GetPlayers(_reusePlayers, p => p.SteamUserId == steamId);
                if (_reusePlayers.Count == 0) return;

                _commandModule.SendMessage(_reusePlayers[0], message);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in SendToPlayer:\n{ex}");
            }
        }

        // -------------------------------------------------------------------------
        // Zone Laden beim Start
        // -------------------------------------------------------------------------

        /// <summary>
        /// Liest alle Container-CustomDatas beim Serverstart aus.
        /// Baut Zonen im RAM auf anhand der bereits gespeicherten [ZoneBlocks] EntityIds.
        /// Kein IsConnectedTo() nötig — EntityIds wurden beim letzten Scan eingetragen.
        /// </summary>
        private void LoadZonesFromCustomData()
        {
            try
            {
                _zones.Clear();
                _states.Clear();

                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null) continue;

                    var blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks);

                    foreach (var slim in blocks)
                    {
                        var block = slim.FatBlock as IMyTerminalBlock;
                        if (block == null) continue;

                        string subtype = block.BlockDefinition.SubtypeId;
                        if (!CONTAINER_SUBTYPES.Contains(subtype)) continue;

                        string cd = block.CustomData ?? "";

                        int zoneNumber = ParseZoneNumber(cd);
                        if (zoneNumber <= 0) continue;

                        // Doppelte ZoneNumber → als Fehler markieren
                        if (_zones.ContainsKey(zoneNumber))
                        {
                            _zones[zoneNumber].HasError     = true;
                            _zones[zoneNumber].ErrorMessage = $"Zone {zoneNumber}: Doppelte ZoneNumber beim Start gefunden.";
                            if (LoggerModule.DebugMode)
                            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: Zone {zoneNumber} doppelt — übersprungen.");
                            continue;
                        }

                        var zone = new ZoneData
                        {
                            ZoneNumber        = zoneNumber,
                            ContainerEntityId = block.EntityId,
                            ConnectorIds      = ParseZoneBlockIds(cd, "Connectors"),
                            LcdIds            = ParseZoneBlockIds(cd, "LCDs"),
                            SorterInIds       = ParseZoneBlockIds(cd, "SortersIn"),
                            SorterOutIds      = ParseZoneBlockIds(cd, "SortersOut")
                        };

                        // Keine Connectors → Fehler aber trotzdem laden (report zeigt es)
                        if (zone.ConnectorIds.Count == 0)
                        {
                            zone.HasError     = true;
                            zone.ErrorMessage = $"Zone {zoneNumber}: Keine Connectors — bitte !sem autotrans scan ausführen.";
                            if (LoggerModule.DebugMode)
                            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: Zone {zoneNumber} ohne Connectors geladen.");
                        }

                        _zones[zoneNumber] = zone;
                    }
                }

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: {_zones.Count} Zone(n) beim Start geladen.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in LoadZonesFromCustomData:\n{ex}");
            }
        }

        /// <summary>
        /// Liest eine kommagetrennte EntityId-Liste aus [ZoneBlocks] der CustomData.
        /// key = "Connectors" oder "LCDs"
        /// </summary>
        private List<long> ParseZoneBlockIds(string customData, string key)
        {
            var result = new List<long>();
            if (string.IsNullOrWhiteSpace(customData)) return result;

            bool inZoneBlocks = false;

            foreach (var rawLine in customData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("#")) continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inZoneBlocks = (trimmed == "[ZoneBlocks]");
                    continue;
                }

                if (!inZoneBlocks) continue;

                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                if (trimmed.Substring(0, eq).Trim() != key) continue;

                string value = trimmed.Substring(eq + 1).Trim();
                if (string.IsNullOrWhiteSpace(value)) return result;

                foreach (var part in value.Split(','))
                {
                    long id;
                    if (long.TryParse(part.Trim(), out id))
                        result.Add(id);
                }

                return result;
            }

            return result;
        }

        // -------------------------------------------------------------------------
        // Zone Scan
        // -------------------------------------------------------------------------

        /// <summary>
        /// Scannt alle Container mit gültiger ZoneNumber.
        /// Prüft via IsConnectedTo() welche Connectors zur Zone gehören.
        /// Schreibt Ergebnis in [ZoneBlocks] der Container-CustomData.
        /// Speichert Zonen im RAM.
        /// </summary>
        private void ExecuteZoneScan(IMyPlayer player)
        {
            try
            {
                _commandModule.SendMessage(player, "AutoTransfer: Scan gestartet...");
                _zones.Clear();
                _states.Clear();
                _mainLcdIds.Clear();

                // Alle Container-, Connector-, LCD- und Sorter-Blöcke sammeln
                var allContainers = new List<IMyTerminalBlock>();
                var allConnectors = new List<IMyTerminalBlock>();
                var allZoneLcds   = new List<IMyTerminalBlock>(); // nur LCDMode=Zone
                var allSorters    = new List<IMyTerminalBlock>();

                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null) continue;

                    var blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks);

                    foreach (var slim in blocks)
                    {
                        var block = slim.FatBlock as IMyTerminalBlock;
                        if (block == null) continue;

                        string subtype = block.BlockDefinition.SubtypeId;

                        if (CONTAINER_SUBTYPES.Contains(subtype))
                            allContainers.Add(block);
                        else if (CONNECTOR_SUBTYPES.Contains(subtype))
                            allConnectors.Add(block);
                        else if (SORTER_SUBTYPES.Contains(subtype))
                            allSorters.Add(block);
                        else if (LCD_SUBTYPES.Contains(subtype))
                        {
                            string lcdMode = ParseLcdMode(block.CustomData);
                            if (lcdMode == "zone" || lcdMode == "list")
                                allZoneLcds.Add(block);
                            else if (lcdMode == "main")
                                _mainLcdIds.Add(block.EntityId);
                        }
                    }
                }

                _commandModule.SendMessage(player, $"  Gefunden: {allContainers.Count} Container, {allConnectors.Count} Connectors, {allZoneLcds.Count} Zone-LCDs, {allSorters.Count} Sortierer");

                // Jeden Container auswerten
                int zonesOk    = 0;
                int zonesError = 0;

                foreach (var container in allContainers)
                {
                    int zoneNumber = ParseZoneNumber(container.CustomData);

                    // ZoneNumber=0 → ungültig, überspringen
                    if (zoneNumber <= 0) continue;

                    var zone = new ZoneData
                    {
                        ZoneNumber        = zoneNumber,
                        ContainerEntityId = container.EntityId
                    };

                    // Doppelte ZoneNumber → Fehler
                    if (_zones.ContainsKey(zoneNumber))
                    {
                        zone.HasError     = true;
                        zone.ErrorMessage = $"Zone {zoneNumber}: Doppelte ZoneNumber — mehrere Container gefunden!";
                        _zones[zoneNumber] = zone;
                        zonesError++;
                        _commandModule.SendMessage(player, $"  [ERROR] Zone {zoneNumber}: Doppelte ZoneNumber!");
                        continue;
                    }

                    // Connectors via IsConnectedTo() finden
                    IMyInventory containerInv = ((IMyEntity)container).GetInventory(0);
                    if (containerInv == null)
                    {
                        zone.HasError     = true;
                        zone.ErrorMessage = $"Zone {zoneNumber}: Container hat kein Inventar.";
                        _zones[zoneNumber] = zone;
                        zonesError++;
                        _commandModule.SendMessage(player, $"  [ERROR] Zone {zoneNumber}: Container kein Inventar.");
                        continue;
                    }

                    foreach (var connector in allConnectors)
                    {
                        IMyInventory connInv = ((IMyEntity)connector).GetInventory(0);
                        if (connInv == null) continue;

                        if (containerInv.IsConnectedTo(connInv))
                            zone.ConnectorIds.Add(connector.EntityId);
                    }

                    // Kein Connector gefunden → Zone Error
                    if (zone.ConnectorIds.Count == 0)
                    {
                        zone.HasError     = true;
                        zone.ErrorMessage = $"Zone {zoneNumber}: Kein Connector per Förderleitung verbunden.";
                        _zones[zoneNumber] = zone;
                        zonesError++;
                        _commandModule.SendMessage(player, $"  [ERROR] Zone {zoneNumber}: Kein Connector gefunden.");
                        continue;
                    }

                    // LCDs mit passender ZoneNumber zuordnen
                    foreach (var lcd in allZoneLcds)
                    {
                        int lcdZone = ParseZoneNumber(lcd.CustomData);
                        if (lcdZone == zoneNumber)
                            zone.LcdIds.Add(lcd.EntityId);
                    }

                    // Sortierer mit passender ZoneNumber zuordnen (per CustomData ZoneNumber + SorterMode)
                    foreach (var sorter in allSorters)
                    {
                        int sorterZone = ParseZoneNumber(sorter.CustomData);
                        if (sorterZone != zoneNumber) continue;

                        string sorterMode = ParseSorterMode(sorter.CustomData);
                        if (sorterMode == "in")
                            zone.SorterInIds.Add(sorter.EntityId);
                        else if (sorterMode == "out")
                            zone.SorterOutIds.Add(sorter.EntityId);
                    }

                    // Zone OK → Connectors, LCDs und Sortierer in [ZoneBlocks] schreiben
                    WriteZoneBlocksToCustomData(container, zone.ConnectorIds, zone.LcdIds, zone.SorterInIds, zone.SorterOutIds);
                    _zones[zoneNumber] = zone;
                    zonesOk++;
                    _commandModule.SendMessage(player, $"  [OK] Zone {zoneNumber}: {zone.ConnectorIds.Count} Connector(s), {zone.LcdIds.Count} LCD(s), {zone.SorterInIds.Count} Sortierer-IN, {zone.SorterOutIds.Count} Sortierer-OUT gefunden.");
                }

                // Zusammenfassung
                int scanOk    = zonesOk;
                int scanError = zonesError;

                // RAM neu aufbauen aus frisch geschriebenen CustomDatas
                LoadZonesFromCustomData();
                _zonesLoaded = true;

                if (_zones.Count == 0)
                    _commandModule.SendMessage(player, "AutoTransfer: Keine Zonen gefunden. Bitte ZoneNumber in Container CustomData setzen.");
                else
                    _commandModule.SendMessage(player, $"AutoTransfer: Scan abgeschlossen — {scanOk} OK, {scanError} Fehler.");

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: ZoneScan — {scanOk} OK, {scanError} Fehler.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in ExecuteZoneScan:\n{ex}");
                _commandModule.SendMessage(player, "AutoTransfer: Scan fehlgeschlagen — siehe Log.");
            }
        }

        /// <summary>
        /// Schreibt Connector- und LCD-EntityIds in den [ZoneBlocks] Abschnitt der Container-CustomData.
        /// </summary>
        private void WriteZoneBlocksToCustomData(IMyTerminalBlock container, List<long> connectorIds, List<long> lcdIds, List<long> sorterInIds, List<long> sorterOutIds)
        {
            try
            {
                string cd = container.CustomData ?? "";
                string connectorLine  = "Connectors="  + string.Join(",", connectorIds);
                string lcdLine        = "LCDs="        + string.Join(",", lcdIds);
                string sorterInLine   = "SortersIn="   + string.Join(",", sorterInIds);
                string sorterOutLine  = "SortersOut="  + string.Join(",", sorterOutIds);

                var lines   = cd.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var result  = new StringBuilder();
                bool inZoneBlocks      = false;
                bool connectorWritten  = false;
                bool lcdWritten        = false;
                bool sorterInWritten   = false;
                bool sorterOutWritten  = false;

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        inZoneBlocks = (trimmed == "[ZoneBlocks]");
                        result.AppendLine(line);
                        continue;
                    }

                    if (inZoneBlocks && trimmed.StartsWith("Connectors="))
                    {
                        result.AppendLine(connectorLine);
                        connectorWritten = true;
                        continue;
                    }

                    if (inZoneBlocks && trimmed.StartsWith("LCDs="))
                    {
                        result.AppendLine(lcdLine);
                        lcdWritten = true;
                        continue;
                    }

                    if (inZoneBlocks && trimmed.StartsWith("SortersIn="))
                    {
                        result.AppendLine(sorterInLine);
                        sorterInWritten = true;
                        continue;
                    }

                    if (inZoneBlocks && trimmed.StartsWith("SortersOut="))
                    {
                        result.AppendLine(sorterOutLine);
                        sorterOutWritten = true;
                        continue;
                    }

                    result.AppendLine(line);
                }

                if (!connectorWritten)  result.AppendLine(connectorLine);
                if (!lcdWritten)        result.AppendLine(lcdLine);
                if (!sorterInWritten)   result.AppendLine(sorterInLine);
                if (!sorterOutWritten)  result.AppendLine(sorterOutLine);

                container.CustomData = result.ToString();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in WriteZoneBlocksToCustomData:\n{ex}");
            }
        }

        // -------------------------------------------------------------------------
        // Report
        // -------------------------------------------------------------------------

        /// <summary>
        /// Gibt alle bekannten Zonen mit Status und Fehlerdetails aus.
        /// </summary>
        private void ExecuteReport(IMyPlayer player)
        {
            try
            {
                if (_zones.Count == 0)
                {
                    _commandModule.SendMessage(player, "AutoTransfer Report: Keine Zonen im RAM — bitte !sem autotrans scan ausführen.");
                    return;
                }

                _commandModule.SendMessage(player, $"AutoTransfer Report — {_zones.Count} Zone(n):");

                var sorted = new List<int>(_zones.Keys);
                sorted.Sort();

                foreach (int zoneNumber in sorted)
                {
                    ZoneData zone = _zones[zoneNumber];

                    if (zone.HasError)
                        _commandModule.SendMessage(player, $"  Zone {zoneNumber}: [ERROR] {zone.ErrorMessage}");
                    else
                        _commandModule.SendMessage(player, $"  Zone {zoneNumber}: [OK] {zone.ConnectorIds.Count} Connector(s), {zone.LcdIds.Count} LCD(s)");
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in ExecuteReport:\n{ex}");
            }
        }

        // -------------------------------------------------------------------------
        // CustomData Parsing Hilfsmethoden
        // -------------------------------------------------------------------------

        /// <summary>
        /// Liest ZoneNumber aus CustomData. Gibt -1 zurück wenn nicht gefunden oder ungültig.
        /// </summary>
        private int ParseZoneNumber(string customData)
        {
            if (string.IsNullOrWhiteSpace(customData)) return -1;

            bool inAutoTransfer = false;

            foreach (var rawLine in customData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("#")) continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inAutoTransfer = (trimmed == "[AutoTransfer]");
                    continue;
                }

                if (!inAutoTransfer) continue;

                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                if (trimmed.Substring(0, eq).Trim() == "ZoneNumber")
                {
                    int val;
                    if (int.TryParse(trimmed.Substring(eq + 1).Trim(), out val))
                        return val;
                }
            }

            return -1;
        }

        /// <summary>
        /// Liest LCDMode aus CustomData. Gibt "main" oder "zone" zurück (lowercase).
        /// </summary>
        private string ParseLcdMode(string customData)
        {
            if (string.IsNullOrWhiteSpace(customData)) return "main";

            bool inAutoTransfer = false;

            foreach (var rawLine in customData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("#")) continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inAutoTransfer = (trimmed == "[AutoTransfer]");
                    continue;
                }

                if (!inAutoTransfer) continue;

                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                if (trimmed.Substring(0, eq).Trim() == "LCDMode")
                    return trimmed.Substring(eq + 1).Trim().ToLower();
            }

            return "main";
        }

        /// <summary>
        /// Liest SorterMode aus CustomData. Gibt "in" oder "out" zurück (lowercase). Default: "in".
        /// </summary>
        private string ParseSorterMode(string customData)
        {
            if (string.IsNullOrWhiteSpace(customData)) return "in";

            bool inAutoTransfer = false;

            foreach (var rawLine in customData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("#")) continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inAutoTransfer = (trimmed == "[AutoTransfer]");
                    continue;
                }

                if (!inAutoTransfer) continue;

                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                if (trimmed.Substring(0, eq).Trim() == "SorterMode")
                {
                    string val = trimmed.Substring(eq + 1).Trim().ToLower();
                    return (val == "out") ? "out" : "in";
                }
            }

            return "in";
        }

        // -------------------------------------------------------------------------
        // Template Deployment
        // -------------------------------------------------------------------------

        /// <summary>
        /// Scannt alle Entities nach Trader-Blöcken und schreibt Templates wo nötig.
        /// Wird beim Init und per !sem autotrans scan aufgerufen.
        /// </summary>
        private void ScanAndDeployTemplates()
        {
            try
            {
                int count = 0;

                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null) continue;

                    var blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks);

                    foreach (var slim in blocks)
                    {
                        if (TryDeployTemplate(slim.FatBlock))
                            count++;
                    }
                }

                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: Templates in {count} Blöcke geschrieben (Scan).");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in ScanAndDeployTemplates:\n{ex}");
            }
        }

        /// <summary>
        /// Neues Entity hinzugefügt → Grid-Event registrieren.
        /// </summary>
        private void OnEntityAdd(IMyEntity entity)
        {
            try
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null) return;

                grid.OnBlockAdded += OnBlockAdded;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in OnEntityAdd:\n{ex}");
            }
        }

        /// <summary>
        /// Neuer Block gesetzt → sofort Template schreiben wenn nötig.
        /// </summary>
        private void OnBlockAdded(IMySlimBlock slim)
        {
            try
            {
                TryDeployTemplate(slim.FatBlock);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer ERROR in OnBlockAdded:\n{ex}");
            }
        }

        /// <summary>
        /// Prüft ob Block ein AutoTransfer-Trader-Block ist.
        /// Schreibt Template wenn CustomData fehlt oder ungültig ist.
        /// Gibt true zurück wenn Template geschrieben wurde.
        /// </summary>
        private bool TryDeployTemplate(IMyCubeBlock block)
        {
            if (block == null) return false;

            var termBlock = block as IMyTerminalBlock;
            if (termBlock == null) return false;

            string subtype = block.BlockDefinition.SubtypeId;

            string template;
            string[] requiredKeys;
            string blockType;

            if (CONTAINER_SUBTYPES.Contains(subtype))
            {
                template     = TEMPLATE_CONTAINER;
                requiredKeys = REQUIRED_CONTAINER;
                blockType    = "Container";
            }
            else if (CONNECTOR_SUBTYPES.Contains(subtype))
            {
                template     = TEMPLATE_CONNECTOR;
                requiredKeys = REQUIRED_CONNECTOR;
                blockType    = "Connector";
            }
            else if (LCD_SUBTYPES.Contains(subtype))
            {
                template     = TEMPLATE_LCD;
                requiredKeys = REQUIRED_LCD;
                blockType    = "LCD";
            }
            else if (SORTER_SUBTYPES.Contains(subtype))
            {
                template     = TEMPLATE_SORTER;
                requiredKeys = REQUIRED_SORTER;
                blockType    = "Sorter";
            }
            else
            {
                return false; // Kein AutoTransfer-Block
            }

            string customData = termBlock.CustomData ?? "";

            // Leer → Template schreiben
            if (string.IsNullOrWhiteSpace(customData))
            {
                termBlock.CustomData = template;
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: Template geschrieben ({blockType}) → {subtype} ({block.EntityId})");
                return true;
            }

            // Validieren — alle required Keys vorhanden?
            if (!IsValidTemplate(customData, requiredKeys))
            {
                // Ungültig → Template anhängen damit vorhandene Admin-Daten nicht verloren gehen
                termBlock.CustomData = customData.TrimEnd() + "\r\n\r\n" + template;
                if (LoggerModule.DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] AutoTransfer: Template ergänzt ({blockType}) → {subtype} ({block.EntityId})");
                return true;
            }

            return false; // Template bereits korrekt vorhanden
        }

        /// <summary>
        /// Prüft ob alle required Keys in der CustomData vorhanden sind.
        /// </summary>
        private bool IsValidTemplate(string customData, string[] requiredKeys)
        {
            foreach (var key in requiredKeys)
            {
                if (!customData.Contains(key))
                    return false;
            }
            return true;
        }
    }
}