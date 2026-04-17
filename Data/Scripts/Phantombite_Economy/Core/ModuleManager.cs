using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Utils;

namespace PhantombiteEconomy.Core
{
    /// <summary>
    /// Manages all PhantombiteEconomy modules with error isolation
    /// </summary>
    public class ModuleManager
    {
        private readonly List<IModule> _modules = new List<IModule>();
        private readonly Dictionary<string, int> _crashCounters = new Dictionary<string, int>();
        private readonly Dictionary<string, bool> _disabledModules = new Dictionary<string, bool>();
        private const int MAX_CRASHES = 3;

        /// <summary>
        /// Register a module for management
        /// </summary>
        public void RegisterModule(IModule module)
        {
            if (module == null)
            {
                MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] ModuleManager: Cannot register null module");
                return;
            }

            _modules.Add(module);
            _crashCounters[module.ModuleName] = 0;
            _disabledModules[module.ModuleName] = false;
            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] ModuleManager: Registered module '{module.ModuleName}'");
        }

        /// <summary>
        /// Initialize all registered modules
        /// </summary>
        public void InitAll()
        {
            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] ModuleManager: Initializing {_modules.Count} modules...");
            
            foreach (var module in _modules)
            {
                if (_disabledModules[module.ModuleName])
                    continue;

                try
                {
                    var startTime = DateTime.UtcNow;
                    module.Init();
                    var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] ModuleManager: '{module.ModuleName}' initialized in {elapsed:F2}ms");
                }
                catch (Exception ex)
                {
                    HandleModuleError(module, "Init", ex);
                }
            }
        }

        /// <summary>
        /// Update all registered modules
        /// </summary>
        public void UpdateAll()
        {
            foreach (var module in _modules)
            {
                if (_disabledModules[module.ModuleName])
                    continue;

                try
                {
                    module.Update();
                }
                catch (Exception ex)
                {
                    HandleModuleError(module, "Update", ex);
                }
            }
        }

        /// <summary>
        /// Save data for all modules
        /// </summary>
        public void SaveAll()
        {
            foreach (var module in _modules)
            {
                if (_disabledModules[module.ModuleName])
                    continue;

                try
                {
                    module.SaveData();
                }
                catch (Exception ex)
                {
                    HandleModuleError(module, "SaveData", ex);
                }
            }
        }

        /// <summary>
        /// Close all modules
        /// </summary>
        public void CloseAll()
        {
            MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] ModuleManager: Closing {_modules.Count} modules...");
            
            foreach (var module in _modules)
            {
                try
                {
                    module.Close();
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] ModuleManager: '{module.ModuleName}' closed successfully");
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLineAndConsole($"[PhantombiteEconomy] ModuleManager: Error closing '{module.ModuleName}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handle module errors with crash counter
        /// </summary>
        private void HandleModuleError(IModule module, string operation, Exception ex)
        {
            _crashCounters[module.ModuleName]++;
            
            MyLog.Default.WriteLineAndConsole(
                $"[PhantombiteEconomy] ModuleManager ERROR: '{module.ModuleName}' crashed in {operation} " +
                $"(Count: {_crashCounters[module.ModuleName]}/{MAX_CRASHES})\n{ex}"
            );

            if (_crashCounters[module.ModuleName] >= MAX_CRASHES)
            {
                _disabledModules[module.ModuleName] = true;
                MyLog.Default.WriteLineAndConsole(
                    $"[PhantombiteEconomy] ModuleManager: '{module.ModuleName}' DISABLED after {MAX_CRASHES} crashes!"
                );
            }
        }

        /// <summary>
        /// Get status of all modules
        /// </summary>
        public string GetStatus()
        {
            var status = $"[PhantombiteEconomy] ModuleManager Status:\n";
            foreach (var module in _modules)
            {
                var disabled = _disabledModules[module.ModuleName] ? "DISABLED" : "ACTIVE";
                var crashes = _crashCounters[module.ModuleName];
                status += $"  - {module.ModuleName}: {disabled} (Crashes: {crashes})\n";
            }
            return status;
        }
    }
}
