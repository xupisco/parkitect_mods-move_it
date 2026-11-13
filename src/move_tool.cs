namespace move_it
{
    // Reuses the native pipette for raycast, highlight and tooltip.
    internal sealed class move_tool : AbstractPipetteTool
    {
        // Whether the next pick starts a copy. It begins at the default action and Tab switches it before clicking,
        // the same way Tab switches it once the object is in hand.
        public bool copy_mode = move_it_settings.copy_by_default;

        public void toggle_copy_mode()
        {
            copy_mode = !copy_mode;
        }

        public bool picks_as_copy(BuildableObject buildable_object)
        {
            return (copy_mode && move_rules.can_copy(buildable_object)) || move_rules.only_copyable(buildable_object);
        }

        // A ride's platforms, entrance and exit stand for the ride, the same way they do in the native picker. An entrance
        // or exit stands for itself when asked to, or when its ride is of a kind that can't be moved as a whole.
        protected override BuildableObject getBuildableObjectBelowMouse()
        {
            BuildableObject below_mouse = base.getBuildableObjectBelowMouse();
            if (below_mouse is AttractionPlatform platform)
            {
                return platform.attraction;
            }
            if (below_mouse is EntranceExit entrance_exit)
            {
                bool only_this_piece = !(entrance_exit.attraction is FlatRide) || InputManager.getKey(move_it_mod.part_hotkey_id);
                return only_this_piece ? entrance_exit : entrance_exit.attraction;
            }
            return below_mouse;
        }

        // Refused objects stay pickable so that hovering and clicking them can explain why.
        protected override bool isPickableObject(BuildableObject buildable_object)
        {
            return move_rules.is_candidate(buildable_object) || move_rules.is_copy_only(buildable_object);
        }

        protected override string getTooltipText(BuildableObject buildable_object)
        {
            if (move_rules.is_copy_only(buildable_object))
            {
                return move_rules.get_copy_refusal_reason(buildable_object) ?? texts.copy_object(buildable_object.getName());
            }

            string refusal = move_rules.get_refusal_reason(buildable_object, picks_as_copy(buildable_object));
            if (refusal != null)
            {
                return refusal;
            }
            // Says what the click will do.
            return picks_as_copy(buildable_object)
                ? texts.copy_object(buildable_object.getName())
                : texts.move_object(buildable_object.getName());
        }
    }
}
