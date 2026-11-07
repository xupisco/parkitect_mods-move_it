using HarmonyLib;
using UnityEngine;

namespace move_it
{
    // Entry point. The game discovers this type through GetExportedTypes, so it is the only public type in the assembly.
    public class move_it_mod : AbstractMod, IModSettings
    {
        internal const string identifier = "xupisco@MoveIt";
        internal const string hotkey_id = identifier + "/move";
        internal const string part_hotkey_id = identifier + "/move_part";
        internal const string group_box_hotkey_id = identifier + "/group_box";

        private Harmony harmony;
        private KeyGroup key_group;
        private KeyMapping key_mapping;
        private KeyMapping part_key_mapping;
        private KeyMapping group_box_key_mapping;
        private GameObject behaviour_object;

        public override string getName()
        {
            return "Move It";
        }

        public override string getDescription()
        {
            return texts.mod_description;
        }

        public override string getVersionNumber()
        {
            return "0.11.0";
        }

        public override string getIdentifier()
        {
            return identifier;
        }

        // The command is already synchronized, but this stays off until it has been tested with two clients.
        // A client without the handler would drop the command silently and desync, so all players must have the mod.
        public override bool isMultiplayerModeCompatible()
        {
            return false;
        }

        public override bool isRequiredByAllPlayersInMultiplayerMode()
        {
            return true;
        }

        public override void onEnabled()
        {
            harmony = new Harmony(identifier);
            try
            {
                harmony.PatchAll(typeof(move_it_mod).Assembly);
            }
            catch
            {
                harmony.UnpatchAll(identifier);
                harmony = null;
                throw;
            }

            register_hotkey();
            CommandController.Instance.registerCustomCommand(move_command.command_id, move_command.on_received);

            behaviour_object = new GameObject("move_it");
            behaviour_object.AddComponent<move_behaviour>();
        }

        public override void onDisabled()
        {
            if (behaviour_object != null)
            {
                // Immediate, so the tool and the session are gone before the patches are removed.
                Object.DestroyImmediate(behaviour_object);
                behaviour_object = null;
            }

            CommandController.Instance.unregisterCustomCommand(move_command.command_id, move_command.on_received);
            unregister_hotkey();

            harmony?.UnpatchAll(identifier);
            harmony = null;
        }

        // The group is registered explicitly: registering a group that already exists makes
        // InputManager.registerKeyGroup throw, which the implicit per-mod group would hit when the mod is re-enabled.
        private void register_hotkey()
        {
            InputManager input = ScriptableSingleton<InputManager>.Instance;

            key_group = new KeyGroup(identifier);
            key_group.keyGroupName = getName();
            input.registerKeyGroup(key_group);

            key_mapping = new KeyMapping(hotkey_id, KeyCode.BackQuote, KeyCode.None);
            // The controls settings show these as given, in the language the game had when the mod was enabled.
            key_mapping.keyName = texts.move_key_name;
            key_mapping.keyDescription = texts.move_key_description;
            key_mapping.keyGroupIdentifier = identifier;
            input.registerKeyMapping(key_mapping);

            part_key_mapping = new KeyMapping(part_hotkey_id, KeyCode.LeftAlt, KeyCode.None);
            part_key_mapping.keyName = texts.part_key_name;
            part_key_mapping.keyDescription = texts.part_key_description;
            part_key_mapping.keyGroupIdentifier = identifier;
            input.registerKeyMapping(part_key_mapping);

            group_box_key_mapping = new KeyMapping(group_box_hotkey_id, KeyCode.Tab, KeyCode.None);
            group_box_key_mapping.keyName = texts.group_box_key_name;
            group_box_key_mapping.keyDescription = texts.group_box_key_description;
            group_box_key_mapping.keyGroupIdentifier = identifier;
            input.registerKeyMapping(group_box_key_mapping);
        }

        private void unregister_hotkey()
        {
            InputManager input = ScriptableSingleton<InputManager>.Instance;
            input.unregisterKeyMapping(key_mapping);
            input.unregisterKeyMapping(part_key_mapping);
            input.unregisterKeyMapping(group_box_key_mapping);
            input.unregisterKeyGroup(key_group);
            key_mapping = null;
            part_key_mapping = null;
            group_box_key_mapping = null;
            key_group = null;
        }

        public void onSettingsOpened()
        {
            move_it_settings.load();
        }

        public void onSettingsClosed()
        {
            move_it_settings.save();
        }

        // The default action as a pair of toggles that work like radio buttons.
        public void onDrawSettingsUI()
        {
            GUILayout.BeginVertical();
            GUILayout.Label(texts.default_action_label);
            bool copy = move_it_settings.copy_by_default;
            if (GUILayout.Toggle(!copy, " " + texts.move_action) && copy)
            {
                move_it_settings.copy_by_default = false;
            }
            if (GUILayout.Toggle(copy, " " + texts.copy_action) && !copy)
            {
                move_it_settings.copy_by_default = true;
            }
            GUILayout.Label(texts.default_action_hint);
            GUILayout.EndVertical();
        }
    }
}
