# 更新日志 / Changelog

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
