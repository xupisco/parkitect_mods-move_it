namespace move_it
{
    // The mod's own texts in English and Portuguese, picked by the language chosen in the game's settings. The game's
    // translations only cover its own texts, so these are kept here. Portugal's Portuguese gets the Brazilian texts.
    internal static class texts
    {
        public static bool portuguese
        {
            get
            {
                Settings settings = Settings.Instance;
                string locale = settings != null ? settings.localeIsoCode : null;
                return locale != null && locale.StartsWith("pt", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string pick(string english, string portuguese_text)
        {
            return portuguese ? portuguese_text : english;
        }

        public static string mod_description
        {
            get
            {
                return pick(
                    "Move placed deco, path attachments, shops, flat rides and ride entrances and exits without paying to rebuild them, or place copies of them. Picking a path opens the path builder with its settings.",
                    "Move decorações, itens de caminho, lojas, atrações planas e entradas e saídas de atrações já construídas sem pagar para reconstruí-las, ou coloque cópias delas. Escolher um caminho abre o construtor de caminhos com as configurações dele.");
            }
        }

        // Key names and descriptions in the controls settings.
        public static string move_key_name { get { return pick("Move object", "Mover objeto"); } }

        public static string move_key_description
        {
            get
            {
                return pick(
                    "Pick a placed object and move it. Picking a path opens the path builder with that path's settings.",
                    "Escolha um objeto construído e mova-o. Escolher um caminho abre o construtor de caminhos com as configurações dele.");
            }
        }

        public static string part_key_name { get { return pick("Move only entrance or exit", "Mover só a entrada ou saída"); } }

        public static string part_key_description
        {
            get
            {
                return pick(
                    "Hold while picking a flat ride's entrance or exit to move only that piece.",
                    "Segure ao escolher a entrada ou a saída de uma atração plana para mover só essa peça.");
            }
        }

        public static string group_box_key_name { get { return pick("Group selection box / Copy mode", "Caixa de seleção em grupo / Modo de cópia"); } }

        public static string group_box_key_description
        {
            get
            {
                return pick(
                    "While selecting a group (Shift + the move key), switch between clicking objects and dragging a box. While moving an object, switch between moving it and placing new copies at full price, until Esc. Copied shops and rides start fresh, and rides need a new entrance and exit.",
                    "Ao selecionar um grupo (Shift + a tecla de mover), alterna entre clicar nos objetos e arrastar uma caixa. Ao mover um objeto, alterna entre movê-lo e colocar cópias novas pelo preço cheio, até o Esc. Lojas e atrações copiadas começam do zero, e as atrações precisam de entrada e saída novas.");
            }
        }

        // The mod's settings.
        public static string default_action_label { get { return pick("Default action when picking an object:", "Ação padrão ao escolher um objeto:"); } }

        public static string default_action_hint
        {
            get
            {
                return pick(
                    "Tab still switches while an object is in hand. Groups, entrances and exits are always moved.",
                    "O Tab continua alternando enquanto o objeto está na mão. Grupos, entradas e saídas sempre são movidos.");
            }
        }

        // The tooltips next to the cursor.
        public static string move_action { get { return pick("Move", "Mover"); } }

        public static string copy_action { get { return pick("Copy", "Copiar"); } }

        public static string move_object(string name)
        {
            return pick("Move ", "Mover ") + name;
        }

        public static string copy_object(string name)
        {
            return pick("Copy ", "Copiar ") + name;
        }

        // Why an object can't be moved or copied, shown on hover and as an error when picked.
        public static string path_not_available { get { return pick("This path isn't available in this park", "Este caminho não está disponível neste parque"); } }

        public static string type_not_supported { get { return pick("Move It can't move this type of object yet", "O Move It ainda não consegue mover este tipo de objeto"); } }

        public static string cannot_be_changed { get { return pick("This object can't be changed here", "Este objeto não pode ser alterado aqui"); } }

        public static string linked_to_effect_box { get { return pick("This object is linked to an effect box: unlink it before moving", "Este objeto está ligado a uma caixa de efeitos: desligue antes de mover"); } }

        public static string running_campaign { get { return pick("This object has a running advertising campaign", "Este objeto tem uma campanha de publicidade em andamento"); } }

        public static string not_in_group { get { return pick("Move It can't move this as part of a group", "O Move It não consegue mover isto como parte de um grupo"); } }

        public static string not_connected { get { return pick("This isn't connected to a ride's station", "Isto não está ligado à estação de uma atração"); } }

        public static string close_ride_first { get { return pick("Close the ride before moving its entrance or exit", "Feche a atração antes de mover a entrada ou a saída"); } }

        public static string ride_broken { get { return pick("This ride is broken: repair it before moving", "Esta atração está quebrada: conserte antes de mover"); } }

        public static string ride_synchronized { get { return pick("This ride is synchronized with another ride: unlink it before moving", "Esta atração está sincronizada com outra: desfaça a ligação antes de mover"); } }

        public static string ride_triggers_effect_box { get { return pick("This ride triggers an effect box: unlink it before moving", "Esta atração aciona uma caixa de efeitos: desligue antes de mover"); } }
    }
}
