using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Machines
{
    internal sealed class GameMachineBackend
    {
        private readonly GameHandsBackend hands;

        public GameMachineBackend(GameHandsBackend hands) => this.hands = hands;

        public HandsResult Inspect(string itemId) => HandsResult.Ok(Read(Find(itemId)));

        public HandsResult Operate(string itemId, string action, string recipeId, int? target)
        {
            var before = Find(itemId);
            var control = Controls(before).FirstOrDefault(entry => entry.Id == action);
            if (control == null)
                throw new HandsException("machine_control_unavailable", "当前机器没有这个可用控制，请重新查询。");

            // Use the same readings for inspection and verification, excluding control metadata.
            var initial = HandsProtocol.Json(State(before));
            Machine after;
            object state;
            try
            {
                control.Invoke(recipeId, target);
                after = Find(itemId);
                state = State(after);
            }
            catch (HandsException) { throw; }
            catch (Exception)
            {
                throw new HandsException("machine_unverified", "未能核实机器操作结果，请先重新查询机器和分区。");
            }

            var outputIds = after.Compartments.Where(entry => entry.Role == "output").Select(entry => entry.Id).ToHashSet();
            var previous = before.World.Items.Where(entry => outputIds.Contains(entry.ContainerId))
                .ToDictionary(entry => entry.Id, entry => entry.Quantity);
            var output = after.World.Items.Where(entry => outputIds.Contains(entry.ContainerId) && entry.Owned)
                .Select(entry => new { item_id = entry.Id, entry.Identifier, entry.ContainerId,
                    added_quantity = entry.Quantity - (previous.TryGetValue(entry.Id, out int count) ? count : 0) })
                .Where(entry => entry.added_quantity > 0).ToArray();
            var changed = initial != HandsProtocol.Json(state);
            var data = new { item_id = itemId, action, changed, state, added_output = output, compartments = after.Compartments };
            if ((action == "produce" || action == "dispense_box") && !output.Any(entry =>
                entry.Identifier == (action == "produce" ? RecipeId(after.Item) : "ammo_box")))
            {
                if (changed)
                    return new HandsResult
                    {
                        Code = "machine_unverified", Data = data,
                        Message = T("机器状态已变化，但未核实新增成品，请检查后再继续。")
                    };
            }
            if (changed || control.IsSetting)
                return HandsResult.Ok(data, changed ? "已执行机器控制并读取当前状态。" : "机器已处于要求的设置。");
            return new HandsResult
            {
                Code = "machine_no_change", Data = data,
                Message = T("机器没有可核实的变化，请检查原料、供电和成品空间。")
            };
        }

        public WaitReading Observe(MachineWait request, string vesselId)
        {
            var machine = Find(request.ItemId);
            var compartment = machine.Compartments.FirstOrDefault(entry => entry.Id == request.ContainerId);
            if (compartment == null || compartment.Locked)
                throw new HandsException("machine_compartment_unavailable", "目标分区不属于当前机器，或已锁定。");

            if (request.Identifier != null)
            {
                if (compartment.Role != "output" && compartment.Role != "eject")
                    throw new HandsException("machine_compartment_unavailable", "请选择机器的实际成品分区。");
                var items = machine.World.Items.Where(entry => entry.ContainerId == compartment.Id &&
                    entry.Owned && entry.Identifier == request.Identifier).ToArray();
                var count = items.Sum(entry => (long)entry.Quantity);
                return new WaitReading
                {
                    Ready = count >= request.Quantity,
                    Data = new { item_id = request.ItemId, container_id = compartment.Id, quantity = count,
                        required_quantity = request.Quantity, items, state = State(machine) }
                };
            }

            if (compartment.Role != "container" || (machine.Item.identifier != "heating_plate" && machine.Item.identifier != "evaporator"))
                throw new HandsException("machine_compartment_unavailable", "干燥等待需要加热板或蒸发器的原料容器分区。");
            var vessel = machine.World.Items.FirstOrDefault(entry => entry.ContainerId == compartment.Id);
            if (vessel == null || !vessel.Owned || vesselId != null && vesselId != vessel.Id)
                throw new HandsException("machine_vessel_changed", "机器内的原料容器不存在、不是自有物品或已被替换。");
            var nativeVessel = hands.ResolveCurrentItem(vessel.Id);
            if (!GeneralHelper.IsItemEffectivelyOwned(nativeVessel) || !nativeVessel.IsTag("LIQUID_CONTAINER_TAG"))
                throw new HandsException("machine_vessel_changed", "机器内的原料容器不存在、不是自有物品或已被替换。");
            var volume = WaterHelper.GetTotalVolume(nativeVessel);
            if (volume <= 0)
                throw new HandsException("machine_no_material", "原料容器已空，没有可继续加工的内容物。");
            var dry = !WaterHelper.IsWet(nativeVessel);
            return new WaitReading
            {
                Ready = dry, VesselId = vessel.Id,
                Data = new { item_id = request.ItemId, container_id = compartment.Id, vessel_item_id = vessel.Id,
                    dry, volume_ul = volume, state = State(machine) }
            };
        }

        private Machine Find(string itemId)
        {
            var world = hands.Capture();
            if (GameMaster.current != null && GameMaster.current.InCombat())
                throw new HandsException("in_combat", "战斗中不能使用物品，请等战斗结束。");
            var item = hands.ResolveCurrentItem(itemId);
            var info = world.Items.First(entry => entry.Id == itemId);
            var row = item.contentWindow?.childElement as MachineryPanelRow;
            if (row == null || !item.IsGameItemType("MACHINE") && item.identifier != "box_dispenser" &&
                item.identifier != "animal_cage")
                throw new HandsException("wrong_item_type", "请选择带有生产分区的机器。");
            if (!GeneralHelper.IsItemEffectivelyOwned(item))
                throw new HandsException("machine_not_owned", "只能操作玩家拥有的机器。");
            var root = world.Containers.FirstOrDefault(entry => entry.Id == info.ContentContainerId);
            if (WindowLocked(item) || root?.Locked == true)
                throw new HandsException("container_locked", "容器已锁定。");
            if (world.Truncated || root == null)
                throw new HandsException("machine_unverified", "无法完整读取机器和分区，请重新查询。");
            var containers = world.Containers.ToDictionary(entry => entry.Id);
            for (var parentId = info.ContainerId; parentId != null && containers.TryGetValue(parentId, out var parent); parentId = parent.ParentId)
                if (parent.Locked || parent.InsertLocked && parent.RemoveLocked)
                    throw new HandsException("container_locked", "容器已锁定。");
            return new Machine
            {
                Item = item, Info = info, Row = row, World = world,
                Compartments = world.Containers.Where(entry => entry.OwnerItemId == itemId && entry.Kind != "group").ToArray()
            };
        }

        private object Read(Machine machine) => new
        {
            machine.Info.Id, machine.Info.Identifier, machine.Info.Name,
            compartments = machine.Compartments,
            controls = Controls(machine).Select(control => new { action = control.Id, control.Options }).ToArray(),
            tool = machine.Item.identifier == "bottle_printer" ? "print_bottle" : null,
            state = State(machine)
        };

        private object State(Machine machine)
        {
            var item = machine.Item;
            var parts = new Dictionary<string, object>();
            foreach (var compartment in machine.Compartments.Where(entry => !entry.Locked))
            {
                parts.Add(compartment.Id, machine.World.Items.Where(entry => entry.ContainerId == compartment.Id)
                    .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                    .Select(entry => DescribeContents(entry)).ToArray());
            }
            var state = new Dictionary<string, object> { ["contents"] = parts };
            if (Slot(machine, "power") is GameSlotInventory power)
            {
                state["battery_installed"] = power.childItem != null;
                if (power.childItem != null && GeneralHelper.IsItemEffectivelyOwned(power.childItem) &&
                    power.childItem.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG))
                {
                    state["battery_energy"] = power.childItem.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_ENERGY).GetInt();
                    state["battery_capacity"] = power.childItem.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY).GetInt();
                }
            }
            if (item.IsTag(MachineProgressHelper.PROGRESS_TYPE_MACHINE_TAG))
            {
                var (current, target) = MachineProgressHelper.GetProgress(item);
                state["machine_state"] = MachineProgressHelper.GetMachineState(item);
                if (item.identifier == "desequencer")
                    state["machine_state"] = item.GetTagReadonly("DESEQUENCER_STATE_TAG").GetString();
                state["progress"] = new { current, target };
            }
            switch (item.identifier)
            {
                case "heating_plate":
                    var modes = new[] { "off", "low", "high" };
                    int mode = item.GetTagReadonly(MachineHeatingPlate.HEATING_PLATE_STATE).GetInt();
                    if (mode >= 0 && mode < modes.Length) state["heat_mode"] = modes[mode];
                    state["target_temperature_c"] = item.GetTagReadonly(MachineHeatingPlate.CURRENT_TARGET_TEMP).GetInt();
                    break;
                case "chem_finisher":
                    state["recipe_id"] = RecipeId(item);
                    var input = Slot(machine, "container");
                    if (input != null && !input.IsRemoveLocked())
                        state["recipe_ready"] = MachineChemFinisher.CanOutput(item, input);
                    break;
                case "livestock_feed_dispenser":
                    state["feed"] = FeedState(item);
                    break;
                case "ras":
                case "animal_cage":
                    var dispenser = HostSlot(machine, "dispenser");
                    if (dispenser?.childItem is GameItem feed && GeneralHelper.IsItemEffectivelyOwned(feed))
                        state["feed"] = FeedState(feed);
                    break;
                case "auto_butcher":
                    state["last_harvest_count"] = item.GetTagReadonly("AUTO_BUTCHER_LAST_COUNT_INT").GetInt();
                    state["last_roe_count"] = item.GetTagReadonly("AUTO_BUTCHER_LAST_ROE_INT").GetInt();
                    state["kill_and_store"] = MachineAutoButcher.IsKillAndStoreOnly(item);
                    break;
                case "plant_dna_manipulator":
                    state["eject_disk"] = MachineDnaManipulator.IsEjectEnabled(item);
                    break;
            }
            return state;
        }

        private object DescribeContents(ItemInfo entry)
        {
            var item = hands.ResolveCurrentItem(entry.Id);
            var liquid = GeneralHelper.IsItemEffectivelyOwned(item) && item.IsTag("LIQUID_CONTAINER_TAG");
            var volume = liquid ? WaterHelper.GetTotalVolume(item) : (int?)null;
            return new
            {
                item_id = entry.Id, entry.Identifier, entry.Name, entry.Quantity, entry.Owned,
                volume_ul = volume,
                dry = liquid && volume > 0 ? !WaterHelper.IsWet(item) : (bool?)null,
                temperature_c = liquid && item.IsTag("CONTAINER_TEMP_TAG") ?
                    item.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble() : (double?)null
            };
        }

        private static object FeedState(GameItem item) => new
        {
            stored_kcal = item.GetTagReadonly("STORED_KCAL_TAG").GetInt(),
            auto_blend = MachineFeedDispenser.IsAutoBlendOn(item),
            target_percent = MachineFeedDispenser.GetTargetPercent(item)
        };

        private static IEnumerable<Control> Controls(Machine machine)
        {
            var item = machine.Item;
            var panel = Panel(machine, "control");
            switch (item.identifier)
            {
                case "blender":
                    if (Available(panel as MachineryButton) is MachineryButton blend)
                        yield return new Control("blend", () =>
                        {
                            RequireSource(Slot(machine, "container"), requireItem: true);
                            RequireSource(Inventory(machine, "input"));
                            Click(blend);
                        });
                    break;
                case "heating_plate":
                    if (panel is MachineryRadioList list && list.group.buttons.Count == 3)
                    {
                        var modes = new[] { "heat_off", "heat_low", "heat_high" };
                        for (int index = 0; index < modes.Length; index++)
                        {
                            var mode = index;
                            if (Available(list.Get(mode)) != null)
                                yield return new Control(modes[mode], () =>
                                {
                                    if (mode != 0) RequireSource(Slot(machine, "container"));
                                    if (item.GetTagReadonly(MachineHeatingPlate.HEATING_PLATE_STATE).GetInt() == mode) return;
                                    list.group.Select(mode, notify: true);
                                    VerifySetting(item.GetTagReadonly(MachineHeatingPlate.HEATING_PLATE_STATE).GetInt() == mode);
                                }, setting: true);
                        }
                    }
                    if (MachineBlender.GetUpgradeCount(item) >= 2 && Panel(machine, "target") is MachineryStack heat &&
                        heat.elements.Count == 3 && heat.elements[1] is MachineryText)
                    {
                        if (Available(heat.elements[0] as MachineryButton) is MachineryButton minus)
                            yield return new Control("decrease_temperature", () => Click(minus));
                        if (Available(heat.elements[2] as MachineryButton) is MachineryButton plus)
                            yield return new Control("increase_temperature", () => Click(plus));
                    }
                    break;
                case "chem_finisher":
                    if (panel is MachineryStack chem && chem.elements.Count == 4 &&
                        chem.elements[1] is MachineryText && chem.elements[2] is MachineryText)
                    {
                        if (Available(chem.elements[0] as MachineryButton) is MachineryButton configure)
                            yield return new Control("set_recipe",
                                (recipe, _) => SelectRecipe(machine, configure, recipe), true,
                                ChemFinisherRecipe.chemFinisherRecipes.Select(entry => new
                                {
                                    value = entry.identifier, name = entry.displayName, volume_ul = entry.requiredVolume,
                                    requires_dry = entry.isRequireDry, ingredient_identifier = entry.activeIngredientId
                                }).ToArray());
                        if (Available(chem.elements[3] as MachineryButton) is MachineryButton produce)
                            yield return new Control("produce", () => Produce(machine, produce));
                    }
                    break;
                case "box_dispenser":
                    if (Available(panel as MachineryButton) is MachineryButton dispense)
                        yield return new Control("dispense_box", () =>
                        {
                            var battery = Slot(machine, "power");
                            RequireSource(battery, requireItem: true);
                            if (!PowerHelper.CanDrawPowerSource(battery.childItem, 1))
                                throw new HandsException("machine_no_power", "机器没有足够的电量。");
                            RequireOutput(Inventory(machine, "output"));
                            Click(dispense);
                        });
                    break;
                case "livestock_feed_dispenser":
                    if (panel is MachineryStack feed && feed.elements.Count == 4)
                    {
                        if (Available(feed.elements[0] as MachineryButton) is MachineryButton blendFeed)
                            yield return new Control("blend_feed", () =>
                            {
                                RequireSource(Inventory(machine, "storage"));
                                Click(blendFeed);
                            });
                        if (feed.elements[3] is MachinerySlider slider && !slider.isDisabled)
                            yield return new Control("set_feed_target", (_, value) =>
                            {
                                slider.SetInt(value.Value, notify: true);
                                VerifySetting(MachineFeedDispenser.GetTargetPercent(item) == value.Value);
                            }, true, new { minimum = 0, maximum = 100, step = 5 });
                    }
                    if (item.MayToggleSlotItem() && item.CanToggleSlotItem())
                    {
                        yield return Toggle(item, "auto_blend_on", () => MachineFeedDispenser.IsAutoBlendOn(item), true,
                            () => RequireSource(Inventory(machine, "storage")));
                        yield return Toggle(item, "auto_blend_off", () => MachineFeedDispenser.IsAutoBlendOn(item), false);
                    }
                    break;
                case "ras":
                case "animal_cage":
                    if (Panel(machine, "dispenser") is GridPixelElement grid &&
                        Available(grid.GetElement(0, 1) as MachineryButton) is MachineryButton hostBlend)
                        yield return new Control("blend_feed", () =>
                        {
                            var slot = HostSlot(machine, "dispenser");
                            RequireSource(slot, requireItem: true);
                            var dispenser = slot.childItem;
                            if (dispenser.identifier != "livestock_feed_dispenser" || dispenser.contentWindow == null ||
                                WindowLocked(dispenser))
                                throw new HandsException("machine_input_unavailable", "机器的原料、容器或电池不可用，或所在分区已锁定。");
                            RequireSource(MachineFeedDispenser.GetStorageGrid(dispenser));
                            Click(hostBlend);
                        });
                    break;
                case "auto_butcher":
                    if (Panel(machine, "output") is MachineryStack tray && tray.elements.Count == 7 &&
                        tray.elements[0] is MachineryIntake { content: GameGridInventory output })
                    {
                        if (Available(tray.elements[1] as MachineryButton) is MachineryButton harvest &&
                            MachineAutoButcher.GetHost(item) is GameItem host)
                            yield return new Control("butcher_now", () =>
                            {
                                RequireSource(output);
                                RequireOutput(output);
                                var source = host.identifier == "animal_cage" ? AnimalCage.GetCageInventory(host) :
                                    host.identifier == "ras" ? MachineRAS.GetTankGrid(host) : null;
                                RequireSource(source);
                                if (host.identifier == "ras" &&
                                    item.GetTagReadonly("AUTO_BUTCHER_TANK_CUTS_TO_DISPENSER_BOOL").GetBool() &&
                                    MachineRAS.GetDispenserSlot(host)?.childItem is GameItem destination &&
                                    (!GeneralHelper.IsItemEffectivelyOwned(destination) || WindowLocked(destination)))
                                    throw new HandsException("machine_input_unavailable", "机器的原料、容器或电池不可用，或所在分区已锁定。");
                                Click(harvest);
                            });
                        if (Available(tray.elements[2] as MachineryButton) is MachineryButton process)
                            yield return new Control("butcher_contents", () =>
                            {
                                RequireSource(output);
                                RequireOutput(output);
                                Click(process);
                            });
                    }
                    break;
                case "plant_dna_manipulator":
                    if (item.MayToggleSlotItem() && item.CanToggleSlotItem())
                    {
                        yield return Toggle(item, "eject_disk", () => MachineDnaManipulator.IsEjectEnabled(item), true);
                        yield return Toggle(item, "keep_disk", () => MachineDnaManipulator.IsEjectEnabled(item), false);
                    }
                    break;
            }
        }

        private static Control Toggle(GameItem item, string action, Func<bool> read, bool desired, Action check = null)
            => new Control(action, () =>
            {
                if (read() == desired) return;
                check?.Invoke();
                if (!item.MayToggleSlotItem() || !item.CanToggleSlotItem())
                    throw new HandsException("machine_control_unavailable", "当前机器没有这个可用控制，请重新查询。");
                item.ToggleSlotItem();
                VerifySetting(read() == desired);
            }, setting: true);

        private static void SelectRecipe(Machine machine, MachineryButton configure, string recipeId)
        {
            if (ChemFinisherRecipe.GetRecipeById(recipeId) == null)
                throw new HandsException("machine_invalid_recipe", "配方 ID 不在这台机器的配方列表中。");
            if (RecipeId(machine.Item) == recipeId) return;
            var ui = ChemFinisherUIManager.Instance;
            if (ui == null || ui.UIPanel == null || ui.UIPanel.activeSelf)
                throw new HandsException("machine_recipe_ui_busy", "配方窗口当前不可用或正在使用。");
            try
            {
                Click(configure);
                if (!ui.UIPanel.activeSelf || ui.currentItem != machine.Item)
                    throw new HandsException("machine_recipe_ui_busy", "配方窗口当前不可用或正在使用。");
                ui.OnChemRecipeSelected(recipeId);
                VerifySetting(RecipeId(machine.Item) == recipeId);
            }
            finally
            {
                if (ui != null && ui.UIPanel != null && ui.UIPanel.activeSelf && ui.currentItem == machine.Item)
                    ui.HideUI();
            }
        }

        private static void Produce(Machine machine, MachineryButton produce)
        {
            var input = Slot(machine, "container");
            var output = Inventory(machine, "output");
            RequireSource(input, requireItem: true);
            RequireOutput(output);
            var recipeId = RecipeId(machine.Item);
            if (recipeId == null || !MachineChemFinisher.CanOutput(machine.Item, input))
                throw new HandsException("machine_recipe_not_ready", "请检查已选配方、原料体积和干燥要求。");

            // The game consumes reagent before placing the product. Use its placement check first.
            var sample = DirectoryMaster.Item(recipeId);
            if (sample == null)
                throw new HandsException("machine_invalid_recipe", "配方 ID 不在这台机器的配方列表中。");
            try
            {
                ChemicalHelper.RemoveAllLiquid(sample);
                var placement = output.TryFindOneValidInventorySlot(sample);
                if (placement == null || placement.inventory != output || placement.targetItem != null)
                    throw new HandsException("machine_output_full", "机器成品区没有足够空间。");
            }
            finally { sample.Destroy(); }
            Click(produce);
        }

        private static string RecipeId(GameItem item) => item.IsTag(MachineChemFinisher.SELECTED_CHEM_ID_TAG) ?
            item.GetTagReadonly(MachineChemFinisher.SELECTED_CHEM_ID_TAG).GetString() : null;

        private static PixelElement Panel(Machine machine, string role)
        {
            var panel = machine.Row.GetPanel(role);
            return panel == null || panel.isHidden ? null : panel.content;
        }

        private static GameInventory Inventory(Machine machine, string role)
        {
            var content = Panel(machine, role);
            return (content is MachineryIntake intake ? intake.content : content) as GameInventory;
        }

        private static GameSlotInventory Slot(Machine machine, string role) => Inventory(machine, role) as GameSlotInventory;

        private static GameSlotInventory HostSlot(Machine machine, string role)
            => ((Panel(machine, role) as GridPixelElement)?.GetElement(0, 0) as MachineryIntake)?.content as GameSlotInventory;

        private static bool WindowLocked(GameItem item) => item.contentWindow?.isLocked == true ||
            item.IsTag("LOCKED_CONTAINER_TAG") && item.GetTagReadonly("LOCK_STATUS").GetBool();

        private static TElement Available<TElement>(TElement element) where TElement : SelectableElement
            => element != null && !element.isDisabled && !element.isHidden && !element.isSelected && !element.requiresHold ?
                element : null;

        private static void Click(MachineryButton button)
        {
            if (Available(button) == null)
                throw new HandsException("machine_control_unavailable", "当前机器没有这个可用控制，请重新查询。");
            try { button.Select(); }
            finally { button.Deselect(); }
        }

        private static void RequireSource(GameInventory inventory, bool requireItem = false)
        {
            if (inventory == null || inventory.IsRemoveLocked() || requireItem && inventory.childItems.Count == 0 ||
                inventory.childItems.Any(item => !GeneralHelper.IsItemEffectivelyOwned(item)))
                throw new HandsException("machine_input_unavailable", "机器的原料、容器或电池不可用，或所在分区已锁定。");
        }

        private static void RequireOutput(GameInventory inventory)
        {
            if (inventory == null || inventory.IsInsertLocked() || !GeneralHelper.MayPlayerInsertInto(inventory))
                throw new HandsException("container_locked", "目标容器已锁定或禁止放入。");
        }

        private static void VerifySetting(bool matches)
        {
            if (!matches)
                throw new HandsException("machine_unverified", "未能核实机器操作结果，请先重新查询机器和分区。");
        }

        private sealed class Machine
        {
            public GameItem Item;
            public ItemInfo Info;
            public MachineryPanelRow Row;
            public WorldSnapshot World;
            public ContainerInfo[] Compartments;
        }

        private sealed class Control
        {
            public string Id { get; }
            public bool IsSetting { get; }
            public object Options { get; }
            public Action<string, int?> Invoke { get; }

            public Control(string id, Action invoke, bool setting = false) : this(id, (_, _) => invoke(), setting) { }

            public Control(string id, Action<string, int?> invoke, bool setting, object options = null)
            {
                Id = id;
                Invoke = invoke;
                IsSetting = setting;
                Options = options;
            }
        }
    }
}
