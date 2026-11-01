# 更新日志 / Changelog

## 0.7.7 — 2026-11-01

### 中文

- 新增水质分析 `analyze_water`：读取容量、液量、标签等级和危险标记；只有试纸测试、特性暴露或水质扫描仪扫描过时才返回真实纯度、可饮用判断和成分，未测试时明确标注标签未经验证，不把标签当成真实水质。
- 新增注射器鉴定 `inspect_injector`：读取针体、指示器、序列号、公司和型号，判断是否破损、过期、召回或疑似假货，并返回 `usable`。只使用检查界面可见信息，不读取隐藏的 `ISSUE_*` 标签。
- 通缉犯 / 改造体提醒改在客人进店时由本地代码触发：聊天窗口打开时写入聊天记录，关闭时显示会自动消失的小卡片；只提醒一次，不进入模型上下文。修复模型可能把治安官 / 检查员误认为通缉犯或改造体的问题，当值安保访客会被显式排除；可在 Mods 设置的“客人提醒”中关闭。
- 细化挑货时的固定容器、机器、冰箱与酒架规则：被固定（pin）的物品本身默认不出售；固定储藏（`STORAGE`）内的非固定物品仍可出售；固定机器仅成品区（`output` / `output_disk` / `distillate`）、冰箱内已冰好/冻好的商品，以及酒架内的酒可出售，原料区、模组区、电池区默认不出售；被固定的非储藏非机器容器（如试剂盒、净水药片盒、营养片盒）内的试纸或药片默认不出售。冰箱中尚未冻好/冰镇到位的物品（如未冻好的冰激凌、未冰镇饮料、降温中的化学容器）视为正在冷冻/冷藏（`in_use=true`），默认不出售；未固定的酒架与啤酒箱既可整架/整箱出售，也可单卖里面的酒或啤酒。

### English

- Added `analyze_water`: capacity, liquid volume, label grade and hazard flag; real purity, potability and contents only when the water was strip-tested, exposed or scanned, and an explicit unverified-label result otherwise.
- Added `inspect_injector`: visible body, indicator, serial, company and model, with broken/expired/recalled/counterfeit findings and `usable`. Uses only inspection-screen data and never reads hidden `ISSUE_*` tags.
- Wanted/augmented reminders now fire locally on arrival: added to the chat transcript when open, otherwise shown on a small auto-dismissing card, once per visit and never sent to the model. This also fixes the model mistaking a security officer or inspector for a wanted or augmented visitor; on-duty security staff are explicitly excluded. The notice can be disabled under the mod's **Visitor notices** setting.
- Refined goods-picking rules for pinned containers, machines, fridges and wine racks: any pinned item itself is hidden from default goods search; unpinned items inside pinned `STORAGE` remain sellable; inside a pinned `MACHINE`, only product compartments (`output` / `output_disk` / `distillate`), already chilled/frozen fridge goods, and bottles in a wine rack remain sellable while input, module and power slots are excluded; items inside pinned non-storage, non-machine containers (such as reagent kits, water tablet boxes or nutrient tablet boxes) are excluded. Pre-freeze/pre-chill fridge items (such as un-frozen ice cream, un-chilled drinks or cooling chemical containers) are marked `in_use=true` and excluded by default; unpinned wine racks and beer cases can be sold either whole or by individual bottles/cans.

## 0.7.6 — 2026-11-01

### 中文

- 挑货和批量上柜台更快：助手可以一次摆放多件商品，不必逐件等待；每次移动仍逐件遵守原生所有权、锁定、容量和当前位置规则，部分失败会逐件说明。
- 新增批量核对：一次查询多件货品的报价与合计预算，合计金额和预算结论一起返回。
- 修正多样性客人（`requires_variety`）的处理：客人本次到访每种物品只买一份，已买过的物品标识会被原生拒收；挑货、合计和预算结论都按物品标识去重，不再把多样性当成单纯的价格加成。

### English

- Faster picking and restocking: the assistant can place several goods with one call instead of waiting for each item. Every move still follows native ownership, lock, capacity and current-location rules, and partial results are reported per item.
- Added batch price checks: check several candidate items and their combined budget in one query, returning the total and budget verdict together.
- Fixed handling of variety customers (`requires_variety`): they buy one item per identifier for this visit and natively refuse identifiers already bought; picking, totals and the budget verdict are now deduplicated, and the variety bonus is no longer treated as the rule itself.

## 0.7.5 — 2026-11-01

### 中文

- 修复开启本模组后，其他模组在加载阶段可能崩溃、界面变成英文或存档列表空白的问题：不再在 `Init` 阶段读取游戏语言，而是等游戏本地化初始化完成后再刷新。
- 修复日志中的版本号与 manifest 版本不一致的问题。

### English

- Fixed other mods sometimes crashing during load, the UI switching to English, or the save list appearing empty when this mod was enabled: the game locale is no longer read during `Init`, but after the game's localization initialization completes.
- Fixed the version reported in the log not matching the manifest version.

## 0.7.4 — 2026-11-01

### 中文

- 新增录音机对话：助手可以查看当前客人本次到访已经说过的话，回答“他刚才说了什么”一类问题；只读，不推进对话或编造未显示的发言。
- 物品查询新增赃物标记和热度（0–100 的治安检查发现概率）；给客人挑货时默认避开赃物，需要时用 `include_stolen=true` 明确查看并提醒热度风险。
- 新增肋明治鉴定：读取生产日期、包装厂编号、配料、包装和标志，返回外观判断、可见异常及原生鉴定基准价。
- 新增等离子燃料匣鉴定：读取外壳颜色、裂纹、磨损、未覆盖、加固和封条，返回状态判断、可见异常及原生鉴定基准价。
- 挑货时默认排除已固定且本身不是容器的物品，避免把主人留作他用的物品当作商品。

### English

- Added recorder dialogue: the assistant can read what the current customer has already said during this visit and answer questions about it. Read-only; does not advance dialogue or invent unseen lines.
- Item queries now include a stolen marker and heat (0–100 chance of being found in a security inspection). Stolen goods are excluded by default when picking goods; use `include_stolen=true` to inspect them explicitly and mention the heat risk.
- Added Ribwich appraisal: production date, packing facility, ingredients, wrapper and logos, with a visible assessment, defects and the native appraisal base value.
- Added Plasma Cartridge appraisal: casing color, crack, wear, uncovered, reinforced and seal state, with a condition assessment, defects and the native appraisal base value.
- Pinned items that are not containers are excluded by default when picking goods, so items kept for other purposes are not treated as merchandise.

## 0.7.3 — 2026-10-31

### 中文

- 新增立绘呼吸：待机时以脚底为基准轻微改变身高，脚部不会浮动；可在 Mods 设置中调整开关、幅度和周期。
- 修复挑货时把设备中供电的电池当作商品：设备电池槽和充电中的电池现在标记为 `in_use`，`find_items` 默认不返回，避免被摆上柜台。

### English

- Added portrait breathing: Xiaojing gently changes height from planted feet while idle; the toggle, distance and cycle are adjustable in the mod settings.
- Fixed device batteries being picked as goods: batteries in machine power compartments or charging are marked `in_use` and hidden from `find_items` by default, so they are not placed on the counter.

## 0.7.2 — 2026-10-31

### 中文

- 挑货时默认排除充电器里正在充电的电池。

### English

- Charging batteries in a charger are excluded by default when picking goods for a customer.
