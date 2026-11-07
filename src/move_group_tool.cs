using System.Collections.Generic;
using UnityEngine;

namespace move_it
{
    // Reuses the selection the bulldozer and the blueprint tool are built on: clicking, dragging and the selection box.
    internal sealed class move_group_tool : ObjectSelectionTool
    {
        private const string highlight_tag = "MoveItSelected";

        private Material material;
        private Material wind_sway_material;
        private float pulse;
        private bool box_mode;

        public move_group_tool()
        {
            objectFilter = is_selectable;
            OnAddedSelectedObject += on_added;
            OnRemovedSelectedObject += on_removed;
            selectionType = SelectionType.Blueprints;
        }

        // The filter gets whatever is under the mouse, but what gets selected is its root, like a platform's ride.
        private bool is_selectable(SerializedMonoBehaviour selected_object)
        {
            if (selected_object == null)
            {
                return false;
            }
            return selected_object.getRootObject(selectionType) is BuildableObject root && move_rules.get_group_refusal_reason(root) == null;
        }

        public void toggle_box_mode()
        {
            box_mode = !box_mode;
            setMode(box_mode ? Mode.SELECTION_BOX : Mode.SELECTION_INDIVIDUAL);
        }

        public override void onMouseToolEnable()
        {
            base.onMouseToolEnable();
            if (material == null)
            {
                material = Object.Instantiate(ScriptableSingleton<AssetManager>.Instance.selectedObjectMaterial);
            }
            if (wind_sway_material == null)
            {
                wind_sway_material = Object.Instantiate(ScriptableSingleton<AssetManager>.Instance.selectedObjectMaterialWindSway);
            }
        }

        public override void onMouseToolRemove()
        {
            base.onMouseToolRemove();
            Object.Destroy(material);
            Object.Destroy(wind_sway_material);
        }

        public override void tick()
        {
            base.tick();
            pulse += Time.unscaledDeltaTime * 5f;
            Color color = new Color(0f, 0.65f, 0.9f) * (0.5f + (1f + Mathf.Sin(pulse)) * 0.125f);
            color.a = 1f;
            material.color = color;
            wind_sway_material.color = color;
        }

        private void on_added(SerializedMonoBehaviour selected_object)
        {
            Material highlight = selected_object is TreeEntity ? wind_sway_material : material;
            foreach (SerializedMonoBehaviour item in get_belonging(selected_object))
            {
                Utility.attachMaterialManagerByObjectForSelection(item, highlight_tag, highlight);
            }
        }

        private void on_removed(SerializedMonoBehaviour selected_object)
        {
            if (selected_object == null)
            {
                return;
            }
            foreach (SerializedMonoBehaviour item in get_belonging(selected_object))
            {
                Utility.destroyMaterialManagerByObject(item, highlight_tag);
            }
        }

        private static List<SerializedMonoBehaviour> get_belonging(SerializedMonoBehaviour selected_object)
        {
            List<SerializedMonoBehaviour> belonging = new List<SerializedMonoBehaviour>();
            selected_object.retrieveObjectsBelongingToThis(belonging);
            return belonging;
        }
    }
}
