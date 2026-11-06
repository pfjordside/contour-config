using Microsoft.Win32;

namespace ContourConfig.App
{
    // Per-user autostart through the HKCU Run key; no administrator rights needed.
    internal static class StartupRegistration
    {
        internal const string StartupArgument = "--startup";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ContourConfig";

        internal static string Command => $"\"{Environment.ProcessPath}\" {StartupArgument}";

        internal static bool IsEnabled()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }

        internal static void Set(bool enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
