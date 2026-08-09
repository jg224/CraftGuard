using System;
using System.Collections.Generic;

namespace InventoryUX.Core
{
    internal static class ToolViewPreferences
    {
        internal const string DefaultValue =
            "_HammerPieceTable=Mod;_HoePieceTable=Default;_CultivatorPieceTable=Default";

        internal static bool IsModView(string serialized, string toolKey)
        {
            string normalizedKey = NormalizeToolKey(toolKey);
            List<Entry> entries = Parse(serialized);
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Key, normalizedKey, StringComparison.OrdinalIgnoreCase))
                {
                    return !string.Equals(entries[i].Value, "Default", StringComparison.OrdinalIgnoreCase);
                }
            }

            // Keep the non-Hammer building tools unobtrusive on first use. Unknown and
            // modded piece tables retain CraftGuard's established Mod View default.
            return normalizedKey.IndexOf("HoePieceTable", StringComparison.OrdinalIgnoreCase) < 0
                && normalizedKey.IndexOf("CultivatorPieceTable", StringComparison.OrdinalIgnoreCase) < 0;
        }

        internal static string Set(string serialized, string toolKey, bool useModView)
        {
            string normalizedKey = NormalizeToolKey(toolKey);
            if (normalizedKey.Length == 0) return serialized ?? string.Empty;

            string value = useModView ? "Mod" : "Default";
            List<Entry> entries = Parse(serialized);
            bool replaced = false;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.Equals(entries[i].Key, normalizedKey, StringComparison.OrdinalIgnoreCase)) continue;

                entries[i] = new Entry(normalizedKey, value);
                replaced = true;
                break;
            }

            if (!replaced) entries.Add(new Entry(normalizedKey, value));

            var values = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                values[i] = entries[i].Key + "=" + entries[i].Value;
            }
            return string.Join(";", values);
        }

        private static List<Entry> Parse(string serialized)
        {
            var entries = new List<Entry>();
            string[] values = (serialized ?? string.Empty).Split(
                new[] { ';' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < values.Length; i++)
            {
                int separator = values[i].IndexOf('=');
                if (separator <= 0) continue;

                string key = NormalizeToolKey(values[i].Substring(0, separator));
                string value = values[i].Substring(separator + 1).Trim();
                if (key.Length == 0 || value.Length == 0) continue;
                entries.Add(new Entry(key, value));
            }
            return entries;
        }

        private static string NormalizeToolKey(string toolKey)
        {
            string key = (toolKey ?? string.Empty).Trim();
            const string CloneSuffix = "(Clone)";
            if (key.EndsWith(CloneSuffix, StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring(0, key.Length - CloneSuffix.Length).TrimEnd();
            }
            return key.Replace(";", string.Empty).Replace("=", string.Empty);
        }

        private struct Entry
        {
            internal Entry(string key, string value)
            {
                Key = key;
                Value = value;
            }

            internal string Key { get; }
            internal string Value { get; }
        }
    }
}
