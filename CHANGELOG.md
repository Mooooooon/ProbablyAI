# 更新日志 / Changelog

## 0.7.11 — 2026-11-03

### 中文

- 开放全部非战斗物品操作：`use_item_on_item` 不再限制净水药片 / 水配给等白名单，任何非战斗物品都可以对另一个真实物品或角色使用；新增 `activate_item`，相当于玩家在物品栏里双击使用物品。试纸、扫描仪、书籍、医疗、手术、动物、食物、机器和工具等都可以尝试。
- 所有操作继续走游戏原生 `MayTarget` / `CanTarget` / `Target` 或 `MayActivateSlotItem` / `CanActivateSlotItem` / `ActivateSlotItem`，是否允许、消耗、打开原生窗口和产生效果由游戏决定；不绕过所有权、锁定、容量或界面状态。
- 按仓库规则仍不开放战斗：武器、弹药、爆炸物、战斗能力和敌人目标会被拒绝，战斗中的物品操作也会被拒绝；不自动买卖、提交报价、选择对话或改存档。试纸、扫描仪、书籍等可能打开的原生窗口需要玩家按界面提示继续，助手不会代替点选。

### English

- Opened all non-combat item actions: `use_item_on_item` no longer whitelists purification tablets or water rations; any non-combat item can be used on another real item or character. Added `activate_item`, equivalent to double-clicking an item in the inventory. Test strips, scanners, books, medical items, surgery, animals, food, machines and tools can be attempted.
- Every action still uses the native `MayTarget` / `CanTarget` / `Target` or `MayActivateSlotItem` / `CanActivateSlotItem` / `ActivateSlotItem` path; the game decides whether it is allowed, what is consumed, which native windows open and what effect occurs. Ownership, locks, capacity and screen state are never bypassed.
- Combat remains excluded by the repository policy: weapons, ammunition, explosives, combat abilities and enemy targets are rejected, as are item actions during combat. The assistant still does not trade, submit quotes, choose dialogue or edit saves. Native windows opened by test strips, scanners or books require the player to continue; the assistant does not click them.

## 0.7.10 — 2026-11-03

### 中文

- 新增 `use_item_on_item`：可以对水瓶、水壶等水容器使用净水药片、营养药片、UV 过滤器或水配给。使用游戏原生的物品对物品操作，由游戏判定所有权、锁定、目标容器、容量和消耗；药片与水配给通常被消耗，UV 过滤器保留。目标必须是水容器，酒类、化学产品容器或没有水的药片 / UV 目标会被拒绝。不会打开试纸 / 扫描仪界面，也不读取未测试的纯度或成分；净化反应可能需要时间。
- 聊天记录改为持久存储：完整对话按存档槽保存在本机 `ModData/probably_ai`，重启游戏或重新载入存档后恢复；只有手动“清空聊天”或更换连接 / 提示词才会删除。F7 的对话轮次（1–50）与历史 token 上限（1,000–200,000，估算）现在只决定发送给模型的历史，超限时从最旧完整轮次开始省略，不再删除聊天记录或本机文件。
- 聊天记录文件不保存 API Key，只保存公开连接哈希与聊天消息；旧配置和损坏文件会安全回退到新对话。

### English

- Added `use_item_on_item`: use a purification tablet, nutrient tablet, UV filter or water ration on a water bottle, pitcher or tank. It uses the game's native item-on-item action, with ownership, locks, target container, capacity and consumption decided by the game; tablets and rations are normally consumed, while the UV filter remains. The target must be a water container; wine, chemical-product and dry tablet/UV targets are rejected. It does not open test-strip or scanner UI and never reads untested purity or contents; purification may take time.
- Chat history is now persistent: the complete transcript is stored per save slot under local `ModData/probably_ai` and restored after a restart or reload. Only an explicit **Clear chat** or a connection/prompt change removes it. The F7 turn limit (1–50) and history token limit (1,000–200,000, estimated) now only choose what is sent to the model; excess older complete turns are omitted from that request instead of being deleted from the transcript or local file.
- The history file stores no API key, only a public connection hash and chat messages; old configurations and corrupt files fall back to a new conversation.

## 0.7.9 — 2026-11-03

### 中文

- 新增液体操作：可以直接说“把这瓶水倒进水壶”“把这瓶脏水倒掉”或“拿这个空瓶去水龙头接水”。小鲸会使用游戏原生的物品对物品操作，遵守所有权、锁定、容量、过滤器、温度和水龙头解锁规则；一次倒出能倒入的全部液体，不能指定毫升数，结果报告实际倒出 / 接收量和两边余量。目标过滤器可能截留杂质，空滴管对容器使用时会按原生行为改为吸取；不会凭空生成液体或容器、不销毁物品、不改变所有权或价格。
- F7 新增聊天上下文设置：可自由设置对话轮次上限（1–50）和历史 token 上限（1,000–200,000，估算）。超过任一上限时从最旧的完整轮次开始丢弃，不会拆断工具调用与结果配对；只修改上限不会清空当前聊天。旧配置自动采用默认值。
- 修正日志中的版本号与 manifest 版本不一致的问题。

### English

- Added liquid handling: ask Xiaojing to pour water or wine into another container, dump a liquid container into the pawnshop drain, or fill a bottle at the faucet. She uses the game's native item-on-item action, respecting ownership, locks, capacity, filters, temperature and faucet unlock rules. One call transfers everything the source can pour; millilitres cannot be chosen, and the result reports the actual amount moved, the amount received and both remaining levels. A target filter may retain contaminants, and an empty dropper draws liquid natively. It never creates liquid or containers, destroys items, or changes ownership or prices.
- F7 now has chat context settings: set the maximum conversation turns (1–50) and the history token limit (1,000–200,000, estimated). When either limit is exceeded, the oldest complete turns are dropped without splitting tool calls from their results, and changing only the limits keeps the current conversation. Old configurations use the defaults automatically.
- Fixed the version reported in the log not matching the manifest version.

## 0.7.8 — 2026-11-01

### 中文

- 修复通缉犯与改造体到店提醒一直滞留在聊天框底部、不随对话消失的问题：提醒按真实时序融入聊天记录流，后续对话正常排在提醒下方，并随多轮对话自然推移与淘汰，清空聊天时一并清理；发送给模型的消息上下文继续严格隔离该本地提醒。
- 精简创意工坊商店页中英文简介与功能列表，精炼核心功能，避免长文本字数超限。

### English

- Fixed an issue where wanted and augmented arrival reminders remained pinned to the bottom of the chat window: arrival notices are now integrated chronologically into the chat transcript so new dialogue appears below them and they expire naturally with conversation turns or chat clearing, while remaining strictly excluded from model context.
- Streamlined Steam Workshop store descriptions in both English and Chinese to prevent description character limit overflow.

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
