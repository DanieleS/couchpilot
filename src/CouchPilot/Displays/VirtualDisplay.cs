using System;

namespace CouchPilot.Displays
{
    // Recognises the virtual display Vibepollo creates for a stream, with the same rules
    // Vibepollo itself uses (src/platform/windows/virtual_display_identity.cpp).
    public static class VirtualDisplay
    {
        public static bool IsVibepollo(string devicePath, string friendlyName)
        {
            if (Contains(friendlyName, "Sunshine Virtual Display")) return true;
            if (string.IsNullOrEmpty(devicePath)) return false;
            if (Contains(devicePath, "SunshineVirtualDisplay") || Contains(devicePath, "Sunshine Virtual Display")
                || Contains(devicePath, "SUDOVDA") || Contains(devicePath, "SUDOMAKER"))
            {
                return true;
            }

            // \\?\DISPLAY#<hardware id>#...: SDD4/SDD5 are Vibepollo's synthetic EDIDs, SMK is SudoVDA's.
            var start = devicePath.IndexOf("DISPLAY#", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return false;
            start += "DISPLAY#".Length;
            var end = devicePath.IndexOf('#', start);
            var hardwareId = end < 0 ? devicePath.Substring(start) : devicePath.Substring(start, end - start);
            return hardwareId.StartsWith("SMK", StringComparison.OrdinalIgnoreCase)
                || hardwareId.StartsWith("SDD4", StringComparison.OrdinalIgnoreCase)
                || hardwareId.StartsWith("SDD5", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(string value, string part) =>
            value != null && value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
