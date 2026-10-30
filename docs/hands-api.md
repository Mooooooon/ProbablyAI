# 深空AI：可能是DeepSeek · 工具接口（v0.5.2）

手动面板和模型工具共用 `HandsService.Execute(toolName, JObject arguments)`，由 `IHandsBackend` 对接游戏。游戏适配器只接受主线程调用，只能在当铺正常操作状态使用。模型不接触任意 C#、文件系统或反射调用。

## 客人查询（只读）

`CustomerService` / `ICustomerBackend` 在相同注册表中提供两个工具，聊天和 F7 的助手指令均可调用。

| 工具 | 参数 | 作用 |
| --- | --- | --- |
| `get_current_customer` | `{}` | 当前客人、意图、实时预算、购买偏好、拒收规则、当前显示的对话及特殊委托 |
| `check_customer_item` | `customer_id`、`item_id` | 原生普通购买规则、当前估值与预算检查、匹配的易货条件索引 |

`get_current_customer` 的 `data.status` 为 `absent`、`arriving` 或 `present`。只有 `present` 返回 `data.customer`，其中：

- `id` 是本次到访的会话 ID，不能把姓名或客人类型标识当作到访 ID；换存档后也不可复用。
- `intent` / `intent_description` 说明来意。`BUY` 是客人买店主的货；`SELL` 是客人向店主卖货；`SELLNBUY` 两者均有。
- `purchase_budget.enabled` 表示是否启用普通购买预算；仅启用时返回 `remaining`。它是当前剩余额度，不是原始额度或现金；未启用不代表零或无限现金。
- `preferences` 包含购买 / 拒收标签与物品标识、多样性及已买过的类型、违禁品与水标签限制、自定义购买规则的存在标记。`ALL_TYPE`、`ALL_TYPE_EXCEPT_CONTRABAND` 是原始游戏规则。自定义条件、物品状态和容器内容可能影响实际接受结果；用具体物品检查核对。
- `current_dialogue` 仅包含当前已显示对话的说话人和文字，不遍历后续分支。
- `barter.requirements` 保留每条易货条件的零起始 `index`、要求的物品 / 标签及拒收状态，不合并重复条件。每条条件需要游戏认可的独立物品，不能自行把堆叠数量当作条件数。
- `procurement` 返回当前采购阶段的描述、预付与交付报酬；`gun_order` 返回说明、基础报酬及分开的 `mandatory` / `bonus` 条件。报酬与购买预算分开，不估算最终奖励。

示例：先以 `{}` 调用 `get_current_customer`，再查询真实物品，然后检查：

```json
{"customer_id":"用当前 customer.id 替换","item_id":"用物品查询结果 id 替换"}
```

`check_customer_item` 每次重新读取客人与物品，返回 `customer_changed`、`no_customer`、`item_not_found` 或 `store_unavailable` 时必须重新查询。它返回 `matches_regular_purchase_rules`、`current_item_value`、`value_is_estimate=true`、`purchase_budget`、可选的 `within_remaining_budget` 和 `matching_barter_requirement_indexes`。物品估值是当前单件值，不是整个堆叠价格；未启用预算时省略可负担性结论。上柜台、检查与谈价会影响结果。普通购买规则匹配不保证成交，某条易货匹配不代表整份易货完成；此工具不验证采购 / 枪械委托交付，也不执行选择对话、交易或移动。

## 查询

| 工具 | 参数 | 作用 |
| --- | --- | --- |
| `list_containers` | 可选 `query`、`parent_id`、`offset`、`limit` | 列出容器、嵌套文件夹和机器分区 |
| `list_container` | `container_id`；可选分页 | 直接物品及直接子容器 |
| `find_items` | `query` 或 `item_type`；可选 `owned_only`、`container_id`、分页 | 按名称、类型或嵌套路径递归查找物品 |
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

## 操作

| 工具 | 参数 | 作用 |
| --- | --- | --- |
| `can_move_item` | `item_id`、`source_container_id`、`destination_container_id`、`quantity` | 预检，不修改内容 |
| `move_item` | 同上 | 原生堆叠 / 拆堆 / 移动，报告实际移动量 |
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

常见目标：`counter` 上柜台、`inventory` 入主库、`documents` 归档、`trash` 投放垃圾桶。文件夹和机器分区的目标 ID 必须从查询结果取得；例如先查“熔炉”，再选它的 `role=input` 分区。

`quantity=0` 表示所选一个物品堆的全部数量，不能代表所有同名物品。必须显式传数量。源容器用于检查并发变化：物品被玩家 / 游戏移走后，旧源位置会返回 `source_changed`。目标为多分区 `group` 时返回 `choose_compartment`；容器不能移进自身或后代。每次执行重新检查原生取放规则、所有权和容量。

成功返回 `success=true, code=ok`；空间不足时返回 `success=false, code=partial`，`data` 中包含 `requested`、实际 `moved` 和 `remaining`。不要把 `remaining` 当作成功数量。查询状态和移动状态不能跨多个回合视为不变。

目前不提供购买 / 出售、改变所有权、凭空生成、直接销毁、解锁、清空垃圾桶或启动机器的接口。原生容器回调和机器自动加工仍遵循游戏本身的行为。

## 模型工具调用

`HandsProtocol.Tools()` 定义 8 个容器工具，由 `ToolRegistry.ForHands` 注册；`CustomerService.Register` 再注册 2 个客人只读工具，共 10 个。`HandsAgent` 从注册表获取当前可用的 OpenAI Chat Completions function tools，按顺序执行返回的调用，再将结果以 `role=tool` 与对应 `tool_call_id` 传回模型。原始 assistant 消息中的 `reasoning_content` 也会保留，以兼容 DeepSeek 思考模式。参考：[OpenAI Function calling](https://developers.openai.com/api/docs/guides/function-calling)、[DeepSeek Tool Calls](https://api-docs.deepseek.com/guides/tool_calls/)。

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

点击立绘使用 `ConversationMemory` 保留有限历史，包括完整的工具调用和结果。取消途中会给尚未执行的调用补充 `not_executed` 结果，避免下次对话出现悬空的 tool call；并记录已完成动作。历史按完整轮次裁剪，最多 8 轮 / 约 10 万字符，不写存档或磁盘。更换服务地址、密钥、模型或系统提示词后，下次发送会清理旧上下文；同一助手不能并发执行两个工具循环。

## 可编辑系统提示词

`ConnectionOptions.SystemPrompt` 保存完整提示词。`AssistantPrompt.Default` 是游戏“深空当铺：可能是偷的”中的女仆助手“小鲸”设定及工具使用规则，默认禁止颜文字和 emoji，除非主人明确要求。旧配置缺少该字段、字段为 null 或仅含空白时采用此默认值；原样保存的 v0.5.1 默认模板也会自动更新，长度上限为 16000 字。自定义文本保留原始换行和空白，不追加隐藏的旧版提示词；本地工具参数和游戏规则仍由代码校验。

F7 的多行编辑器可直接查看、修改和恢复默认提示词，点击“保存配置”后随连接配置持久保存。“恢复默认”仅改变编辑草稿；“发送测试消息”使用未保存的当前草稿，可预览人设，不提供游戏工具。聊天和助手操作使用已保存值，每轮工具循环固定使用发送时的配置快照；进行中保存新提示词会在下一次发送生效。
