using System;
using System.Collections.Generic;
using System.IO;
using MiniJSON;
using UnityEngine;

namespace move_it
{
    // The player's choices for the mod. Kept next to the game's settings rather than in the mod folder, which Workshop
    // updates can replace.
    internal static class move_it_settings
    {
        private static string file_path => FilePaths.getFolderPath("move_it.settings.json");

        private static bool loaded;
        private static bool copy_by_default_value;

        // Whether picking a single object starts in copy mode instead of moving it. Tab still switches.
        public static bool copy_by_default
        {
            get
            {
                load();
                return copy_by_default_value;
            }
            set
            {
                load();
                copy_by_default_value = value;
            }
        }

        public static void load()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;

            try
            {
                if (File.Exists(file_path) && Json.Deserialize(File.ReadAllText(file_path)) is Dictionary<string, object> values
                    && values.TryGetValue("copy_by_default", out object copy) && copy is bool copy_value)
                {
                    copy_by_default_value = copy_value;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[move_it] Couldn't read " + file_path + ": " + exception);
            }
        }

        public static void save()
        {
            try
            {
                Dictionary<string, object> values = new Dictionary<string, object>
                {
                    { "copy_by_default", copy_by_default_value }
                };
                File.WriteAllText(file_path, Json.Serialize(values));
            }
            catch (Exception exception)
            {
                Debug.LogError("[move_it] Couldn't write " + file_path + ": " + exception);
            }
        }
    }
}
