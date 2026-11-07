using System.Collections.Generic;
using Parkitect.UI;
using UnityEngine;

namespace move_it
{
    // Reads the hotkey and drives the pick tool, which the game does not tick by itself.
    internal sealed class move_behaviour : MonoBehaviour
    {
        private move_tool tool;
        private move_group_tool group_tool;

        private void Update()
        {
            GameController game = GameController.Instance;
            if (game == null || game.park == null)
            {
                return;
            }

            if (InputManager.getKeyDown(move_it_mod.hotkey_id) && !UIUtility.isInputFieldFocused())
            {
                toggle(game);
            }

            move_session session = move_session.current;
            if (session != null)
            {
                if (InputManager.getKeyDown(move_it_mod.group_box_hotkey_id) && !UIUtility.isInputFieldFocused())
                {
                    session.toggle_copy_mode();
                }
                session.tick_action_tooltip();
            }

            if (tool != null && game.isActiveMouseTool(tool))
            {
                if (InputManager.getKeyDown(move_it_mod.group_box_hotkey_id) && !UIUtility.isInputFieldFocused())
                {
                    tool.toggle_copy_mode();
                }
                tool.tick();
            }

            if (group_tool != null && game.isActiveMouseTool(group_tool))
            {
                if (InputManager.getKeyDown(move_it_mod.group_box_hotkey_id) && !UIUtility.isInputFieldFocused())
                {
                    group_tool.toggle_box_mode();
                }
                group_tool.tick();
            }
        }

        private void toggle(GameController game)
        {
            if (move_session.current != null)
            {
                move_session.current.cancel();
            }
            else if (group_tool != null)
            {
                confirm_group();
            }
            else if (tool != null)
            {
                stop_tool();
            }
            else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                group_tool = new move_group_tool();
                group_tool.OnRemoved += on_group_tool_removed;
                game.enableMouseTool(group_tool);
            }
            else
            {
                tool = new move_tool();
                tool.OnObjectSelected += on_object_selected;
                tool.OnRemoved += on_tool_removed;
                game.enableMouseTool(tool);
            }
        }

        private void on_object_selected(BuildableObject selected_object)
        {
            if (selected_object is Path path)
            {
                copy_path(path);
                return;
            }

            string refusal = move_rules.get_refusal_reason(selected_object);
            if (refusal != null)
            {
                ErrorMessageController.showErrorMessage(refusal);
                return;
            }

            // The builder goes on top of the tool stack first, the same order the native picker uses.
            move_session.begin(selected_object, tool != null && tool.copy_mode);
            stop_tool();
        }

        // The same steps the native picker takes for a path. The window is reused when it's already open.
        private void copy_path(Path path)
        {
            string refusal = move_rules.get_copy_refusal_reason(path);
            if (refusal != null)
            {
                ErrorMessageController.showErrorMessage(refusal);
                return;
            }

            PathBuilder path_builder = UIWindowsController.Instance.spawnWindowFromPrefab(ScriptableSingleton<UIAssetManager>.Instance.pathBuilderWindowGO).GetComponent<PathBuilder>();
            path_builder.copySettingsFrom(path);
            path_builder.setFixedGhostHeightIfRaised(path.transform.position);
            stop_tool();
        }

        private void confirm_group()
        {
            List<BuildableObject> roots = new List<BuildableObject>();
            foreach (SerializedMonoBehaviour selected_object in group_tool.getSelectedObjects())
            {
                if (selected_object is BuildableObject root && SerializedMonoBehaviour.exists(root) && move_rules.get_group_refusal_reason(root) == null)
                {
                    roots.Add(root);
                }
            }

            // Pressing the key with nothing selected just leaves the selection. The builder goes on top of the tool
            // stack first, the same order the native picker uses, and removing the tool clears the highlights.
            if (roots.Count > 0)
            {
                move_session.begin_group(roots);
            }
            stop_group_tool();
        }

        private void on_group_tool_removed()
        {
            // Covers Esc, which removes the tool without going through stop_group_tool.
            group_tool = null;
        }

        private void stop_group_tool()
        {
            if (group_tool != null)
            {
                GameController.Instance?.removeMouseTool(group_tool);
                group_tool = null;
            }
        }

        private void on_tool_removed()
        {
            // Covers Esc, which removes the tool without going through stop_tool.
            tool = null;
        }

        private void stop_tool()
        {
            if (tool != null)
            {
                GameController.Instance?.removeMouseTool(tool);
                tool = null;
            }
        }

        private void OnDestroy()
        {
            move_session.current?.cancel();
            stop_tool();
            stop_group_tool();
        }
    }
}
