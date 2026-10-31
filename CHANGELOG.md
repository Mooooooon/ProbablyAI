# 更新日志 / Changelog

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
