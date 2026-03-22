using System;
using VRage.Utils;
using PhantombiteEconomy.Core;

namespace PhantombiteEconomy.Modules
{
    /// <summary>
    /// M01 - Logging module for PhantombiteEconomy mod
    /// DebugMode: false = nur Warnings/Errors, true = alle Logs
    /// Wird von M02 aus GlobalConfig.ini gesetzt (DebugMode=true)
    /// </summary>
    public class LoggerModule : IModule
    {
        public string ModuleName => "Logger";

        /// <summary>
        /// Statisches Flag — alle Module prüfen direkt ohne Referenz
        /// </summary>
        public static bool DebugMode = false;

        public void Init()
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Logger initialized");
        }

        public void Update() { }

        public void SaveData() { }

        public void Close()
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteEconomy] Logger closed");
        }

        public void Warning(string message)
        {
            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] WARNING: {message}");
        }

        public void Error(string message, Exception ex = null)
        {
            if (ex != null)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] ERROR: {message}\n{ex}");
            else
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] ERROR: {message}");
        }

        public void Debug(string message)
        {
            if (DebugMode)
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] DEBUG: {message}");
        }
    }
}
