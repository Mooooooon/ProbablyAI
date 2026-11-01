# 深空AI：可能是DeepSeek · 工具接口（v0.7.7）

手动面板和模型工具共用 `HandsService.Execute(toolName, JObject arguments)`，由 `IHandsBackend` 对接游戏。游戏适配器只接受主线程调用，只能在当铺正常操作状态使用。模型不接触任意 C#、文件系统或反射调用。

## 机械手、肋明治、等离子燃料、香烟、食品券、注射器与水质鉴定（只读）

`InspectionService` / `IInspectionBackend` 在统一注册表中提供七个工具，聊天和 F7 助手指令均可调用。先用 `find_items` / `list_container` 获取精确物品 ID，再调用：

| 工具 | 参数 | 返回内容 |
| --- | --- | --- |
| `appraise_mechanical_hand` | `item_id` | `item` 实时身份及位置；`material`、`control_interface`、`interface_marking`、`condition`；`armored`、`bladed`、`tactical_set`、`deluxe_set`；`base_value` 原生单件鉴定基准价 |
| `appraise_ribwich` | `item_id` | `item`；`production_month`、`production_year`、`facility_number`、`barcode_printed`、`facility_printed`；`meat`、`bun`、`pickles`、`wrap`、`opened`、`double_meat`、`deluxe_edition`；`davre_logo`、`ribwich_logo`；`visual_assessment`、`issues`；`base_value` |
| `appraise_plasma_fuel` | `item_id` | `item`；`color`、`crack`、`wear`、`uncovered`、`reinforced`、`seal`；`condition_assessment`、`issues`；`base_value` |
| `inspect_cigarette` | `item_id` | `item` 实时身份及位置；`seal`、`logo`、`lettering`、`box` 外观编号；`visual_assessment` 与 `issues` |
| `inspect_food_stamp` | `item_id` | `item` 实时身份及位置；`seal`、`logo`、`chip`、`lettering`、`face_value`；`visual_assessment` 与 `issues` |
| `inspect_injector` | `item_id` | `item`；`indicator`、`body`、`color`、`serial`、`company`、`model`；`visual_assessment`（`appears_genuine` / `appears_unusable` / `appears_expired` / `appears_counterfeit` / `unknown`）、`usable`（`true` / `false` / `null`）与 `issues` |
| `analyze_water` | `item_id` | `item`；`empty`、`volume_ml`、`capacity_ml`、`fill_percent`；`label_grade`、`label_purity_range`、`label_hazardous`；`tested`、`purity`、`purity_percent`、`grade`、`grade_range`、`potable`、`assessment_source`、`label_matches_test`、`contents_revealed`、`contents`、`last_scan`、`issues` |

```json
{"item_id":"用本轮物品查询结果的 id 替换"}
```

机械手的材质为 `ALUMINUM_ALLOY` / `CARBON_COMPOSITE` / `GOLD_PLATED`，接口为 `EMG` / `EMG_PLUS` / `NEURAL` / `UNSPECIFIED`，标记为 `FULL` / `SYMBOL`，磨损为 `NONE` / `WEAR` / `DAMAGE`。套装判断使用 `AppHandHelper.IsTacticalSet` / `IsDeluxeSet`，价格直接使用 `AppHandHelper.GetCorrectValue`，已含全部套装效果与原生最低价格限制。不自行重算、不乘堆叠数量，不能当作实时报价或鉴定服务报酬。

肋明治（`RIBWICH_TAG`，袋装肋排三明治）仅读取 `RibwichUI.OnInit` 使用的可见标签：生产月份 / 年份、包装厂编号、条码和厂标是否印出、肉饼（普通 / 双份）、面包和酸黄瓜（普通 / 豪华）、包装是否敞开，以及 Davre / 肋明治标志编号。`opened=true` 表示 `wrap` 为 1 或 2，价值按已开封的 40 信用点基准计算。可见异常以 `production_date` / `facility_number` / `davre_logo` / `ribwich_logo` / `production_print` / `facility_print` / `deluxe` 返回；`base_value` 直接使用 `RibwichHelper.GetCorrectValue`，不乘堆叠数量，不能当作实时报价或鉴定报酬。缺失标签或超出可见范围的编号返回 `unknown`；不读取隐藏真伪或 `realCondition`，不提交估价、不质问客人、不改价。

等离子燃料匣（`PLASMA_CELL_TAG`，Plasma Cartridge）仅读取 `FuelUI.OnInit` 使用的可见标签：颜色 `STANDARD` / `HIGH_QUALITY` / `POOR1` / `POOR2`，裂纹 `NONE` / `CRACK`，磨损 `NONE` / `WEAR` / `WEAR_HEAVY`，未覆盖 `NONE` / `UNCOVERED`，加固 `NONE` / `REINFORCED`，封条 `LL` / `ZHONGHUA` / `REPUBLIC` / `IGNITION_LAB` / `NONE`。可见异常以 `crack` / `wear` / `wear_heavy` / `uncovered` / `poor_color` 返回；`condition_assessment` 为 `appears_intact`、`appears_damaged` 或 `unknown`。该物品没有真伪判定，`base_value` 直接使用 `PlasmaHelper.GetCorrectValue`，不乘堆叠数量，不能当作实时报价或鉴定报酬。缺失标签或状态超出上述范围返回 `unknown` 且不计算基准价；不读取隐藏状态、不提交估价、不质问客人、不改价。

香烟仅读取 `CigaretteUI.OnInit` 使用的四个外观标签。`0` 是标准样本；封条、标志或文字为 `-1` 表示缺失，其他有效编号表示变体，包装 `1–4` 为颜色变体。可见异常以 `seal` / `logo` / `lettering` / `color` 返回；`visual_assessment` 为 `appears_genuine`、`appears_counterfeit` 或 `unknown`。缺失标签或超出原生范围的编号返回 `unknown`。不读取隐藏的 `*_ISSUE` 或 `realCondition`；外观符合不保证隐藏真伪。

食品券仅读取 `StampUI.OnInit` 使用的五个可见标签：`STAMP_SEAL`、`STAMP_LOGO`、`STAMP_CHIP`、`STAMP_LETTERING`、`STAMP_AMOUNT`。`0` 是标准样本，`-1` 通常表示缺失，其他有效编号表示变体；`face_value` 是票面印制的信用点。游戏只发行 40 / 80 / 120 / 160 / 200 / 240 信用点面值，面值达到 160 时应有防伪标（`seal=0`），低于 160 时不应有（`seal=-1`）；芯片缺失或损坏时无法在配给站兑换。可见异常以 `seal` / `logo` / `chip` / `lettering` / `amount` 返回，`amount` 表示票面面值不属于上述发行面值。缺失标签、非正面值或超出原生范围的编号返回 `unknown`。不读取隐藏的 `STAMP_*_ISSUE` 或 `realCondition`。

注射器仅支持带 `INS_INJECTOR_TAG` 的可目视检查类型（大支 Immunivax），读取 `InjectorUI.OnInit` 使用的六个可见字段：`INJECTOR_INDICATOR`、`INJECTOR_BODY`、`INJECTOR_COLOR`、`INJECTOR_SERIAL`、`INJECTOR_COMPANY`、`INJECTOR_MODEL` 和检查界面上的针体显示。`indicator=1` 表示过期指示，`indicator=3` 或 `body=1–3` 表示指示器/针体异常；序列号应为 7 位数字、前缀在 125–455 内且不属于召回批次（250 / 251 / 301 / 355 / 385 / 386 / 421）。可见异常以 `body_broken` / `indicator_unusable` / `indicator_expired` / `recalled_serial` / `serial_format` / `label_company` / `label_model` / `color` 返回；破损、指示器异常或召回批次返回 `appears_unusable` 且 `usable=false`，过期返回 `appears_expired` 且 `usable=false`，仅标签/序列号/颜色异常返回 `appears_counterfeit` 且 `usable=null`。不读取隐藏的 `ISSUE_*` 或 `realCondition`；外观符合不保证隐藏真伪。

水容器仅读取玩家可见的数据：容量与液量、`CATEGORY_WATER_PURITY` 的瓶身标签等级、已试纸测试/已暴露/已扫描时的真实纯度，以及水质扫描仪显示过的成分。`label_grade` 为 `pure` / `high_quality` / `base` / `ghostwater` / `rustwater` / `gutterflow`；`potable` 为 `safe` / `caution` / `unsafe` / `toxic` / `unknown`。未测试时 `assessment_source=label`，只代表标签，不能当作真实水质；只有 `LAST_SCAN_RESULT_TAG` 存在时才返回 `contents` 与 `contents_revealed=true`。空容器返回 `empty=true` 且不推断水质；酒类容器（`WINE_CONTAINER_TAG`）和化学产品不属于该工具。安全等级根据游戏水质阈值判断：纯净水/优质水为 `safe`，基础水为 `caution`，幽灵水/锈水为 `unsafe`，下水道水为 `toxic`。

原生适配每次通过共享 `GameHandsBackend.Capture()` 刷新，沿用主线程、场景、拖动、容器锁定和会话 ID 限制。目视检查未解锁返回 `inspection_locked`；物品已不可访问返回 `item_not_found`；类型不符返回 `wrong_item_type`。注射器沿用目视检查解锁；水瓶、水壶等含水的 `LIQUID_CONTAINER_TAG` 只要标签可读即可分析，不要求目视检查教学；酒类和化学产品不属于该工具。仅接受 `item_id`，拒绝多余参数。七个工具不改变特性、所有权、价格或委托状态，也不调用提交估价、质问、兑换及对话接口。

## 阅读笔记、书籍与指南（只读）

`DocumentService` / `IDocumentBackend` 注册一个只读工具 `read_document`，用于读取真实物品上的文字。先用 `find_items` / `list_container` 取得物品 ID：

| 工具 | 参数 | 返回内容 |
| --- | --- | --- |
| `read_document` | `item_id` | `item`；`kind`（`mentor_note` / `book` / `slip` / `guide`）；`title`、`body`、`signature`；书籍的 `pages`（`number`、`subtitle`、`body`）或指南的 `text` |

读取来源：

- 导师笔记 / 情报单：`TUTORIAL_PAGE_TITLE`、`TUTORIAL_PAGE_BODY`、`TUTORIAL_PAGE_SIGNATURE`、`SLIP_TITLE`、`SLIP_BODY`。
- 书籍：`BOOK_TAG` + `BOOK_ID`，通过 `StoreBookData` 返回全部页；缺少页数据时回退到对应指南面板。
- 指南 / 纸面界面：按物品标识读取 `PaperUIManager` 或 `MultiPageUIManager` 中的 TMP 文本，包括香烟、食品券、注射器、机械手、通缉令、香烟色卡等。界面文本可能包含标题、图注和按钮标签，且依赖当前本地化状态。
- 不读取或编造不存在的内容；文盲状态返回 `cannot_read`，无法识别的文档返回 `not_readable`。该工具不打开窗口、不翻页、不改变物品。

## 客人查询（只读）

`CustomerService` / `ICustomerBackend` 在相同注册表中提供三个工具，聊天和 F7 的助手指令均可调用。

| 工具 | 参数 | 作用 |
| --- | --- | --- |
| `get_current_customer` | `{}` | 当前客人、意图、实时预算、购买偏好、拒收规则、当前显示的对话、录音机中本次到访的对话及特殊委托 |
| `check_customer_item` | `customer_id`、`item_id` | 原生普通购买规则、当前估值与预算检查、匹配的易货条件索引 |
| `check_customer_items` | `customer_id`、`item_ids`（1–20 个） | 一次核对多件物品的偏好与报价，并返回合计金额及合计预算结论 |

`get_current_customer` 的 `data.status` 为 `absent`、`arriving` 或 `present`。只有 `present` 返回 `data.customer`，其中：

- `id` 是本次到访的会话 ID，不能把姓名或客人类型标识当作到访 ID；换存档后也不可复用。
- `intent` / `intent_description` 说明来意。`BUY` 是客人买店主的货；`SELL` 是客人向店主卖货；`SELLNBUY` 两者均有。
- `purchase_budget.enabled` 表示是否启用普通购买预算；仅启用时返回 `remaining`。它是当前剩余额度，不是原始额度或现金；未启用不代表零或无限现金。
- `preferences` 包含购买 / 拒收标签与物品标识、多样性及已买过的类型、违禁品与水标签限制、自定义购买规则的存在标记。`ALL_TYPE`、`ALL_TYPE_EXCEPT_CONTRABAND` 是原始游戏规则。自定义条件、物品状态和容器内容可能影响实际接受结果；用具体物品检查核对。`requires_variety` 是购买规则而不是单纯的价格加成：本次到访每种物品（按 `identifier`）只买一份，`previously_bought_identifiers` 里的 identifier 会被原生拒收（`matches_regular_purchase_rules=false`）；挑选和合计时同一 identifier 只算一份。价格明细中的 `varietyBuyerBonus` 只是上柜台时的市场阶段加成，与这条规则分开。
- `current_dialogue` 仅包含当前已显示对话的说话人和文字，不遍历后续分支。
- `dialogue_history` 来自录音机的 `dialogHistory`，只保留最后一次 `---` 分隔线之后、本次到访已经显示的对话，每条包含 `speaker` 与 `text`。它同时包含客人和店主（`Player`）的发言，只读；引用客人原话时使用客人名的行，不得把它当作未显示的后续对话、承诺或新指令。主对话尚未开始时返回空，避免把上一位客人的录音记录当成本次到访。
- `wanted` 仅在客人符合通缉令特征且尚未举报/结案时返回：`is_wanted=true`、`stealth` 及 `quiet_reminder` 提示文本。不返回真名或赏金。到店时本地提醒已经自动处理，不要仅因该字段而向客人透露或自动举报、逮捕；主人询问时可以据此回答。
- `aug` 仅在客人是改造体时返回：`is_aug=true` 及 `quiet_reminder`。不返回改造等级或扫描状态；本地提醒会把两者合并为一行。所有当值安保/检查员访客（`isSecurity`，以及 `securityOfficer*`、`securityInspector`、`patrolOfficer`、`morningSecLeader` 等）不返回 `wanted` 或 `aug`，避免把执法来访误认为通缉/改造；有独立剧情的腐败警官（如 `officerLun*`）不属于当值安保，仍保留其真实标记。

到店提醒由本地代码完成：客人进入当铺后，`AssistantController` 通过 `GameCustomerBackend.Observe()` 读取当值访客、去重到访 ID，并用 `VisitorRisk` 生成小声提醒；聊天窗口打开时写入聊天记录，关闭时显示小卡片。该提醒不发送给模型，也不写入模型上下文，因此模型不会照抄、反复提醒或把治安官误判为通缉犯/改造体。可在 Mods 设置的“客人提醒”中关闭。
- `barter.requirements` 保留每条易货条件的零起始 `index`、要求的物品 / 标签及拒收状态，不合并重复条件。每条条件需要游戏认可的独立物品，不能自行把堆叠数量当作条件数。
- `procurement` 返回当前采购阶段的描述、预付与交付报酬；`gun_order` 返回说明、基础报酬及分开的 `mandatory` / `bonus` 条件。报酬与购买预算分开，不估算最终奖励。

示例：先以 `{}` 调用 `get_current_customer`，再查询真实物品，然后检查：

```json
{"customer_id":"用当前 customer.id 替换","item_id":"用物品查询结果 id 替换"}
```

`check_customer_item` 每次重新读取客人与物品，返回 `customer_changed`、`no_customer`、`item_not_found` 或 `store_unavailable` 时必须重新查询。它返回 `identifier`、`matches_regular_purchase_rules`、`current_item_value`、`value_is_estimate`、`pricing`、`purchase_budget`、可选的 `within_remaining_budget` 和 `matching_barter_requirement_indexes`。`matches_regular_purchase_rules=false` 表示游戏会拒收，包括多样性客人已买过的相同 `identifier`。物品报价沿用原生单个物品对象的计价方式，不自行乘堆叠数量；仅客人购买自有物品且启用预算时返回可负担性结论。

`check_customer_items` 只读取一次客人和一次容器树，对 `item_ids` 中 1–20 件物品逐件返回与 `check_customer_item` 相同的字段，并给出 `total_current_item_value`、`priced_count`、`total_is_estimate`、`within_combined_budget`、`duplicate_identifiers`，以及多样性客人专用的 `one_per_identifier_total` / `one_per_identifier_count`。合计只累加客人按普通购买规则愿意买的自有物品的当前报价，按每个物品对象计价、不乘堆叠数量；`item_ids` 不能重复，否则合计会重复计算；单件失效只在对应条目返回 `code=item_not_found`，其余物品仍会返回。`duplicate_identifiers` 列出本次请求中重复出现的 identifier；客人 `requires_variety=true` 时应改用 `one_per_identifier_total`（每种只算一份），`within_combined_budget` 也按去重后的合计判断，不要把重复物品摆上柜台。客人为空、到访已变化或当铺不可用时，整次调用失败。挑货核对多件时应优先使用它，把多次模型往返合并为一次查询。

`pricing.source=counter_offer` 表示柜台顶层的原生 `GetNegociatedValue()` 实时报价，`value_is_estimate=false`。`projected_counter_offer` 表示自有物品上柜台前的只读预估，包含多样性价格加成、实时价格事件、当前零售加价、香水、已知客人效果等；不执行上柜台回调、不改动真实特性、容器或所有权。其他情况使用 `current_value`，不把客人物品的买入价与客人购买预算比较。未知自定义回调、检查或谈价可能影响最终价格，上柜台后必须重新查询。

价格明细包含 `base_value`（含复杂物品附加价值）、`market_scaled_value`（水等按市场区缩放的价值）、`misc_value`（包含 `contents_value`）、`innate_percent`、`market_percent`、`final_multiplier` 和 `modifiers`。每条效果包含 `stage`、`percent`、`projected`、`inherits_to_contents` 及可见名称。仅使用当前公开/已揭露状态的数值，不读取未揭露的真实状态来估价。计算顺序与游戏一致：

```text
属性值 = max(0, base_value × (1 + innate_percent / 100))
报价 = max(0, Round(((属性值 + market_scaled_value) × max(0, 1 + market_percent / 100) + misc_value) × final_multiplier))
```

属性区、市场区各自相加；最终谈价的每个因子分别相乘，最后按原生方式四舍六入五取偶。容器内容按原生展开方式只计一次，可继承的客人市场加成传入内容物；零售、事件和最终谈价不作为继承百分比重复传入。`current_item_value` 已含全部明细效果，模型不得再次加价。例如基础价 100，多样性价格加成 +15%、零售 +15%、价格事件 +50%，报价为 180，预算 170 时不够。多件货品需要合计各报价并读取最新剩余预算，不能把单件分别够用当作合计够用；多样性客人还要按 `one_per_identifier_total` 去重。

普通购买规则匹配不保证成交，某条易货匹配不代表整份易货完成；此工具不验证采购 / 枪械委托交付，也不执行选择对话、交易或移动。

## 查询

| 工具 | 参数 | 作用 |
| --- | --- | --- |
| `list_containers` | 可选 `query`、`parent_id`、`offset`、`limit` | 列出容器、嵌套文件夹和机器分区 |
| `list_container` | `container_id`；可选分页 | 直接物品及直接子容器 |
| `find_items` | `query` 或 `item_type`；可选 `owned_only`、`container_id`、`include_in_use`、`include_pinned`、`include_stolen`、分页 | 按名称、类型或嵌套路径递归查找物品；默认隐藏在用物品（设备供电/充电电池、冰箱内未冻好物品）、受固定限制的物品，以及赃物 |
| `inspect_item` | `item_id` | 名称、描述、数量、所有权、实际位置、内容容器 ID |

每页默认 40 条、最多 100 条。结果包含 `total`、`has_more`；全局遍历达 10000 个物品堆、2000 个容器或 64 层时返回 `truncated`。物品 ID 和嵌套容器 ID 仅当前载入有效，不能存为跨存档引用；根容器别名固定。

```json
{"query":"导师笔记"}
```

将这个对象传给 `find_items`，根据真实返回的 `content_container_id` 调用 `list_container`；或搜索完整路径，定位具体文件。不要用名称代替 ID，也不要在多条同名结果中盲选第一条。

找所有自有食品可传下面的对象。`item_type` 匹配游戏真实类型，忽略大小写；返回物品的 `types` 可供进一步分类。食品匹配不要求面包、罐头的名称含有“食物”。

```json
{"item_type":"FOOD","owned_only":true,"container_id":"inventory"}
```

`query` 与 `item_type` 至少提供一个，同时提供时按两者筛选；`query` 为“食物 / 食品 / food”时会按 FOOD 类型处理。`owned_only` 默认 false，以保留查询客人物品的能力。

物品结果还包含 `in_use`：电池装在生产设备的电池槽中供电、位于充电器的电池分区充电，或冰箱中的物品尚未完成冰镇 / 冷冻（如还没冻好的冰激凌、未冰镇饮品、正在降温的化学品容器）时为 `true`，表示正在使用或等待升值而非待售库存；冰箱里已经冻好或冰镇完成的物品则可正常出售。`find_items` 默认不返回 `in_use=true` 的物品；主人明确要求查看或更换时传 `include_in_use=true`。`charging` 仍单独表示充电器中的电池确实正在充电。

`pinned` 表示主人用固定模式标记了该物品（整理或卸载时保持原位），`container` 表示该物品本身是可打开的容器。挑货时，固定物品（`pinned=true`）本身默认不作为商品出售；若被固定的是储藏容器（`STORAGE` 仓库），里面的未固定物品可以出售；若被固定的是机器（`MACHINE`），其成品区（`output`）、酒架酒槽（`wine`）或冰箱中已冻好 / 冰镇好的物品可以出售，而原料区、模组区、电池区不出售；若被固定的是非储藏、非机器容器（如试剂盒、净水药片盒、啤酒箱），容器本身及里面的物品默认都不出售。未固定的酒架或啤酒箱既可以整件出售，也可以单卖里面的酒水。主人明确要求查看这些受固定限制的物品时传 `include_pinned=true`。

`stolen` 表示物品是赃物，`heat` 是 0–100 的热度（治安检查时被发现的概率百分比，越高越容易被发现）。挑货时默认不使用赃物：`find_items` 默认不返回 `stolen=true` 的物品；主人明确要求查看或挑选赃物时传 `include_stolen=true`，并提醒热度风险。`list_container` 和 `inspect_item` 始终返回赃物标记与热度，方便核对；助手不得自动举报、没收或改变所有权。

## 操作

| 工具 | 参数 | 作用 |
| --- | --- | --- |
| `can_move_item` | `item_id`、`source_container_id`、`destination_container_id`、`quantity` | 预检，不修改内容 |
| `move_item` | 同上 | 移动一个物品堆，报告实际移动量 |
| `move_items` | `destination_container_id`、`moves`（1–20 项，每项 `item_id`、`source_container_id`、可选 `quantity`） | 一次按顺序移动多件；逐件返回结果及合计 |
| `open_container` | `container_id` | 显示容器所在的原生窗口 |
| `close_container` | `container_id` | 隐藏该原生窗口 |

```json
{
  "item_id": "以查询返回值替换",
  "source_container_id": "inventory",
  "destination_container_id": "counter",
  "quantity": 1
}
```

挑货或批量上柜台时改用 `move_items`，一次调用摆好整批商品，不必每件各调用一次：

```json
{
  "destination_container_id": "counter",
  "moves": [
    {"item_id": "以查询返回值替换", "source_container_id": "inventory"},
    {"item_id": "以查询返回值替换", "source_container_id": "inventory", "quantity": 1}
  ]
}
```

`move_items` 的全部移动共用一个目标容器；`quantity` 省略或为 0 表示这一堆全部。执行前先整批校验参数结构，结构错误（缺少 `moves`、超过 20 项、未知参数、类型不符）不会移动任何物品；执行中某一堆失败（位置变化、容量不足、原生拒绝）只跳过这一堆，其余继续。返回逐件 `results`、`total`、`succeeded`、`failed`、`moved` 和 `requested`，整体 `code` 为 `ok` / `partial` / `not_moved`。目标为多分区 `group` 或禁止放入时整批失败。已完成的移动不会回滚。

常见目标：`counter` 上柜台、`inventory` 入主库、`documents` 归档、`trash` 投放垃圾桶。文件夹和机器分区的目标 ID 必须从查询结果取得；例如先查“熔炉”，再选它的 `role=input` 分区。

`quantity=0` 表示所选一个物品堆的全部数量，不能代表所有同名物品。必须显式传数量。源容器用于检查并发变化：物品被玩家 / 游戏移走后，旧源位置会返回 `source_changed`；执行时还会用原生 `parentInventory` 再核对一次源位置，所以同一批 `move_items` 中前面的移动改变了物品位置时，后面的对应条目会失败而不是从错误位置取走物品。目标为多分区 `group` 时返回 `choose_compartment`；容器不能移进自身或后代。每次执行重新检查原生取放规则、所有权和容量。

成功返回 `success=true, code=ok`；空间不足时返回 `success=false, code=partial`，`data` 中包含 `requested`、实际 `moved` 和 `remaining`。不要把 `remaining` 当作成功数量。查询状态和移动状态不能跨多个回合视为不变。

目前不提供购买 / 出售、改变所有权、凭空生成、直接销毁、解锁、清空垃圾桶或启动机器的接口。原生容器回调和机器自动加工仍遵循游戏本身的行为。

## 模型工具调用

`HandsProtocol.Tools()` 定义 9 个容器工具，由 `ToolRegistry.ForHands` 注册；`CustomerService.Register` 再注册 3 个客人只读工具，`InspectionService.Register` 注册 7 个鉴定只读工具，`DocumentService.Register` 注册 1 个文档阅读工具，共 20 个。`HandsAgent` 从注册表获取当前可用的 OpenAI Chat Completions function tools，按顺序执行返回的调用，再将结果以 `role=tool` 与对应 `tool_call_id` 传回模型。批量工具（`move_items`、`check_customer_items`）让一次调用完成多件操作，减少模型往返次数；单件工具仍可用于单独操作。原始 assistant 消息中的 `reasoning_content` 也会保留，以兼容 DeepSeek 思考模式。参考：[OpenAI Function calling](https://developers.openai.com/api/docs/guides/function-calling)、[DeepSeek Tool Calls](https://api-docs.deepseek.com/guides/tool_calls/)。

模型返回的参数仍由本地代码严格校验。重复调用 ID、超长参数、未知操作和多余参数不会执行；最多 16 轮 / 48 次调用。取消令牌会在每次调用前检查，已完成的移动不会自动回滚。

## 注册未来工具

`ModEntry.Tools` 返回当前载入的注册表。新场景会创建新注册表并触发 `ModEntry.ToolsReady`，可在该事件中注册扩展工具。聊天每次请求自动读取当前注册表，无需把新工具名称添加到聊天窗口的 switch 中。

```csharp
using Newtonsoft.Json.Linq;
using ProbablyAI;
using ProbablyAI.Hands;

// 在扩展 Mod 的 OnEnable 中订阅，在 OnDisable 中取消订阅同一个处理函数。
void RegisterTools(ProbablyAI.Tools.ToolRegistry registry)
{
    registry.Register(new JObject
    {
        ["type"] = "function",
        ["function"] = new JObject
        {
            ["name"] = "get_store_day",
            ["description"] = "读取当前当铺的天数。",
            ["parameters"] = new JObject
            {
                ["type"] = "object", ["properties"] = new JObject(),
                ["required"] = new JArray(), ["additionalProperties"] = false
            }
        }
    }, args => HandsResult.Ok(new { day = StoreStation.Instance.dayCounter }), changesState: false);
}
```

订阅方式：`ModEntry.ToolsReady += RegisterTools`；如果注册时存档已经载入，还可以对非空的 `ModEntry.Tools` 注册一次。名称必须唯一。工具负责校验自己的业务参数和游戏规则，禁止把参数作为任意 C# 或文件路径执行。改变状态的工具设置 `changesState: true`，聊天会显示其实际操作记录。调用发生在 Unity 主线程；跨场景时进行中的聊天会取消。

## 聊天会话

点击立绘使用 `ConversationMemory` 保留有限历史，包括完整的工具调用和结果。`Messages()` 用于原始界面记录，`MessagesForModel()` 在副本中省略历史只读查询数据，并给过去的操作结果加上 `historical=true`；保持调用配对和原有对话。取消途中会给尚未执行的调用补充 `not_executed` 结果，避免下次对话出现悬空的 tool call；并记录已完成动作。历史按完整轮次裁剪，最多 8 轮 / 约 10 万字符，不写存档或磁盘。更换服务地址、密钥、模型或系统提示词后，下次发送会清理旧上下文；同一助手不能并发执行两个工具循环。

游戏中的 `HandsAgent` 启用 `refreshShopContext`：每条消息先通过现有 `get_current_customer` 和 `list_container(counter, limit=100)` 工具同步读取实况，作为临时 `shop_observation` 数据发给模型。它不修改自定义系统提示词，不伪造模型的思考或工具调用，不加入可见聊天或历史，也不会自动执行移动或未来扩展工具。刷新失败保留明确的失败结果，分页保留 `total` / `has_more`，不把未知部分解释为空或用旧记录补齐。成交可能只修改所有权；`owned=false` 的物品不属于待售自有货物。普通金额回复无需例行重复解释来源，只有影响判断或用户询问时再说明。

## 可编辑系统提示词

`ConnectionOptions.SystemPrompt` 保存完整提示词。`AssistantPrompt.Default` 是游戏“深空当铺：可能是偷的”中的女仆助手“小鲸”设定及工具使用规则，默认禁止颜文字和 emoji，除非主人明确要求。旧配置缺少该字段、字段为 null 或仅含空白时采用此默认值；原样保存的 v0.5.1 默认模板也会自动更新，长度上限为 16000 字。自定义文本保留原始换行和空白，不追加隐藏的旧版提示词；本地工具参数和游戏规则仍由代码校验。

F7 的多行编辑器可直接查看、修改和恢复默认提示词，点击“保存配置”后随连接配置持久保存。“恢复默认”仅改变编辑草稿；“发送测试消息”使用未保存的当前草稿，可预览人设，不提供游戏工具。聊天和助手操作使用已保存值，每轮工具循环固定使用发送时的配置快照；进行中保存新提示词会在下一次发送生效。

`ChineseDefault` 和 `EnglishDefault` 分别定义内置模板，`Default` 随游戏语言选择。两种内置模板及旧版原样模板会自动适配当前语言；其他文本原样保留。`Text.UseLocale` 仅在 Unity 主线程读取游戏语言后更新缓存；后台 HTTP 错误处理只读取缓存。注册表保留原始 Schema，每次 `Definitions()` 克隆后翻译说明，不改变工具名、ID、参数名或自定义用户数据。
