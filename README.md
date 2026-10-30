# 深空AI：可能是DeepSeek

Probably Stolen 原生 Mod，当前版本 0.5.2。针对本机 `VERSION 100-Rev3` / Unity 2021.3.45f2 / 应用版本 0.46D 编译。内部 Mod ID 仍为 `probably_ai`，因此改名不会重置连接配置和立绘设置。

## 当前功能

- 以用户提供的 DeepSeek 拟人形象为参考，生成 1024 × 1536 透明 PNG 全身立绘。当前为双手在身前自然下垂交叠的女仆姿势，带像素画风；使用 Point 采样保留像素边缘。
- 进入当铺后显示于界面左侧；保留完整宽高比，按游戏画布缩放。
- 鼠标悬停人物时显示手形光标和高亮，点击打开聊天框；透明背景仍可穿透点击。
- 主菜单、离店、暂停菜单、设置和射击模式中隐藏。
- F6：本次运行中显示 / 隐藏立绘。F7：打开 / 关闭连接配置窗口（输入框聚焦时也能关闭）。
- 主菜单 → Mods → 深空AI：可能是DeepSeek 的齿轮中，可保存高度、左右位置、底部位置、透明度及快捷键设置。

## 点击立绘聊天

点击人物本身即可打开聊天框；重复点击会聚焦已经打开的窗口。透明背景不响应点击。

- 输入自然语言，按 **Enter** 或点击 **发送**。**Shift+Enter** 换行。
- 例如：“帮我把食物放到柜台上”。助手会按游戏 `FOOD` 类型查找自有物品栏及其子容器中的食品，再逐堆调用移动工具。面包、罐头等名称不含“食物”的物品也能匹配。
- 聊天共用 F7 中保存的服务地址、密钥和模型；需要调整时可点聊天框内的 **连接设置**。F7 配置页也提供 **打开聊天** 入口。
- 支持多轮对话和跟进指令。上下文最多保留最近 8 轮 / 约 10 万字符，并完整保留工具调用及结果；关闭窗口后重新打开仍可继续。聊天记录仅在当前载入的内存中，重启、重新载入存档或清空聊天后移除。更换服务地址、密钥、模型或提示词后，下次发送会清空旧的模型上下文。
- 工具执行结果会显示在聊天中；可以 **停止**。已完成的操作保留，未执行的后续调用不会继续。模型的内部思考内容不会显示在聊天里。

聊天使用统一工具注册表。现有容器工具已经注册，未来经同一接口注册的工具会自动提供给模型和聊天窗口，无需修改聊天发送逻辑。扩展方法见 [docs/hands-api.md](docs/hands-api.md)。

## 客人、预算与需求

点击立绘后，可以直接问：

- “当前客人叫什么，来买东西还是卖东西？”
- “这位客人还剩多少预算，想买什么，不收什么？”
- “物品栏里有哪些他愿意买、预算也够的物品？先告诉我。”
- “这位客人的易货 / 采购 / 枪械委托有什么要求？”

助手通过 `get_current_customer` 读取当前到访对象、实时剩余购买预算、购买标签及指定物品、拒收规则、多样性要求和当前显示的对话。易货返回每条交换条件；采购返回需求说明及报酬；枪械委托区分必需条件与加分条件。不同客人拥有独立的到访 ID，没有客人或尚未到达时不返回上一位信息。未来对话分支和选项不会被执行或提前读取。

`BUY` 表示客人买店主的货，`SELL` 表示客人向店主卖货。预算是客人的实时剩余购买额度，与店主现金、初始额度和委托报酬分开。未启用预算时不填数值，不能理解为零或无限现金。

助手可使用 `check_customer_item` 按游戏原生购买规则核对真实物品，包括嵌套容器里的物品；旧客人 ID 会被拒绝。结果包含当前单件估值、是否在剩余预算内，以及匹配的易货条件索引。估值在上柜台或谈价后可能变化，符合购买偏好也不保证立即成交或完成整份特殊委托。以上两个接口只查询；是否搬上柜台仍取决于你的指令，目前不提供自动成交或选择对话选项。

## 连接配置（F7）

1. 进入当铺，按 **F7**。默认服务地址为 `https://api.deepseek.com`，默认模型为 `deepseek-flash`。
2. 填写自己的 API Key。输入框以密码形式遮蔽；不要求在聊天中提供密钥。本地免密服务可留空。
3. 点击 **获取模型列表**，从下拉框选择模型，或直接填写模型 ID。获取失败、返回空列表、列表不包含当前模型时，仍可手动填写。
4. 在 **助手提示词** 中查看并编辑完整系统提示词，支持多行、换行和最多 16000 字。默认身份为游戏“深空当铺：可能是偷的”中的女仆助手“小鲸”，称呼用户为“主人”，语气活泼、温柔可爱，默认禁止颜文字和 emoji，除非主人明确要求；同时包含现有物品、客人和工具使用规则。**恢复默认提示词** 会填回完整默认内容；留空后保存也会使用默认值。此前原样保存的 v0.5.1 默认模板会自动更新，自定义提示词保留。
5. 点击 **保存配置**，连接和提示词都会保存到本机，重启游戏后仍可读取。提示词用于之后的聊天与助手操作；保存新提示词后，下次发送会清理旧设定的聊天上下文。旧版连接配置没有提示词字段时，会自动采用新的默认女仆设定，保留原连接。
6. 可在 **测试对话** 中输入消息，再点击 **发送测试消息** 验证 Chat Completions 或预览人设。测试使用窗口中当前的连接和提示词，修改后无需先保存即可预览。每次是独立测试，不会自动附带存档数据或对话历史。获取列表、测试和恢复默认都不会自动保存；关闭窗口会放弃未保存的修改。

服务地址使用 API 基础地址，支持根地址和带版本 / 代理路径的地址，如 `https://api.deepseek.com`、`https://api.openai.com/v1`、`http://127.0.0.1:1234/v1`。代码按填写的前缀追加 `/models` 和 `/chat/completions`，不会自动添加或重复 `/v1`。认证使用 `Authorization: Bearer ...`。

API Key 只用于用户填写的服务地址，不进入日志、模型列表或游戏存档。勾选 **记住 API Key** 时，密钥以明文保存在本机的连接配置文件；取消勾选后保存，会从文件中移除密钥，仅在本次运行内保留。不要分享这个文件。连接配置独立于 Mod 安装目录，更新 Mod 不覆盖它：

```text
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\probably_ai\connection.local.json
```

打开窗口、保存配置均不会发送网络请求；“获取模型列表”“发送测试消息”、聊天发送及“执行助手指令”会联网。模型调用工具后，相应物品、容器或客人信息会作为工具结果发给配置的服务。请求超时为 60 秒，支持取消；关闭窗口、离店或切换场景时也会取消。当前未实现自动买卖。

## 助手的“手”：物品与容器

按 **F7 → 物品与容器 / 助手操作**。

- **手动操作**：刷新 → 选来源范围 → 选具体物品 → 选目标容器 / 机器分区 → 填数量 → 预检或移动。可勾选“包含子容器中的物品”，直接选择深层文件。数量 `0` 只表示所选这一堆全部。刷新后会清除物品选择，防止误操作到下一件同名物品。
- **助手指令**：使用已保存的连接配置，输入自然语言指令后点击执行。模型必须支持 Chat Completions 的 function / tool calling。助手先查询物品，再调用操作接口，结果写入下方记录。每次指令独立处理。
- 示例：“把窗口里的普通矿石拿到柜台”“把物品栏里的文件放进档案架”“把档案架里导师笔记中的某份笔记拿到柜台”“把物品栏里的废金属放进熔炉原料区”。物品和容器名称以当前存档实际内容为准。

根容器：`inventory` = 物品栏 / 主储藏区；`counter` = 交易柜台；`customer` = 窗口 / 客人储存区；`documents` = 档案架；`trash` = 垃圾桶；`showcase` = 展示柜；`hidden` = 隐藏储藏区。储藏箱、背包、文件夹等物品的内部库存自动递归加入；熔炉等机器分别暴露 `input`、`output`、`power`、`module`、`note`，多页储藏箱分别暴露每个分页。已锁定容器只显示锁定状态，不读取内部物品。

所有嵌套对象均返回独立的会话 ID、父级和完整可读路径，例如“档案架 → 导师笔记 → 关于食品的笔记”。同名物品不会合并；物品移动后仍按实际引用识别，拆堆 / 合堆或切换存档后必须重新查询 ID。关闭容器窗口不影响查询正常可访问的内部存储。

操作使用游戏原生 `MayRemove`、槽位搜索及 `SlotMarker.TryAcceptOnce`，不改所有权、材料规则、锁定或机器工作状态。目标不接受、放不下或源位置变化都会返回具体原因。机器存在多个分区时，必须选择具体分区；投料不会主动启动机器，但游戏自身可能按其正常逻辑加工材料。移动到垃圾桶只执行投放，不调用销毁或清空垃圾桶。

停止 / 关闭窗口只取消后续动作，已经完成的移动保留。记录会显示实际 `moved` / `remaining`，部分成功不等于全部成功。每条助手指令最多 16 轮 / 48 个工具调用，按顺序执行；物品访问始终在 Unity 主线程，并在每次工具调用前刷新当前状态。

接口定义与可直接调用的 JSON 示例见 [docs/hands-api.md](docs/hands-api.md)。当前版本的游戏内布局和真实存档行为仍需要玩家实测；自动测试使用模拟游戏状态，不修改真实存档。

## 构建与安装

需要 .NET SDK 及游戏自身 Managed 目录中的 DLL，无需 Unity 编辑器、BepInEx 或 MelonLoader。

```powershell
# 在项目根目录执行，生成 dist\ProbablyAI
.\scripts\Build.ps1

# 关闭游戏后，编译并安装到游戏 Mods\ProbablyAI
.\scripts\Build.ps1 -Install

# 游戏安装在其他目录时
.\scripts\Build.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Probably Stolen' -Install
```

启动游戏 → Mods → 选择“深空AI：可能是DeepSeek” → 激活 → 重启游戏 → 载入当铺。关闭游戏后重新安装更新。停用请在 Mods 中取消激活并重启。

从 0.1.x 更新到 0.2.0 时，因为新增 `network` 能力，游戏可能暂不加载此 Mod。请在 Mods 中选择 **重新审核并启用 / Review and enable**，确认联网能力后重启。无需修改开发者模式或信任记录。

## 扩展入口

| 文件 | 用途 |
| --- | --- |
| `src/ModEntry.cs` | 原生 IMod 生命周期、持久配置与客户端初始化 |
| `src/AssistantController.cs` | 游戏 Hook、场景清理、快捷键及助手面板 |
| `src/AssistantSettings.cs` | 使用游戏原生 ModConfig 保存设置 |
| `src/UI/PortraitView.cs` | 原生前景画布中的立绘和透明背景穿透 |
| `src/UI/PortraitInteraction.cs` | 人物像素命中、悬停提示、手形光标及点击 |
| `src/UI/ChatWindow.cs` | 多轮自然语言聊天、快捷发送、工具执行结果 |
| `src/UI/ConnectionWindow.cs` | F7 配置、模型选择与测试窗口 |
| `src/UI/HandsWindow.cs` | 容器手动操作和自然语言指令窗口 |
| `src/Hands/GameHandsBackend.cs` | 游戏对象树、机器分区及原生移动适配 |
| `src/Hands/HandsService.cs` | 手动 / 模型共用的查询和操作校验层 |
| `src/Hands/HandsProtocol.cs` | 8 个工具的 JSON Schema 与结果序列化 |
| `src/Hands/HandsAgent.cs` | OpenAI 兼容多轮工具调用、执行记录与取消 |
| `src/Customers/GameCustomerBackend.cs` | 当前客人、实时预算、购买规则及特殊委托的原生只读适配 |
| `src/Customers/CustomerService.cs` | 客人工具注册、参数校验及错误处理 |
| `src/Customers/CustomerProtocol.cs` | 客人查询和具体物品适配检查的 JSON Schema |
| `src/Tools/ToolRegistry.cs` | 当前和后续工具的统一注册、Schema 与分发 |
| `src/Services/ConversationMemory.cs` | 有限聊天历史、完整工具配对与连接变更清理 |
| `src/Services/IAssistantService.cs` | AI 异步接口、只读上下文、OpenAI 兼容服务 |
| `src/Services/OpenAiApiClient.cs` | 模型列表、Chat Completions、超时取消与错误处理 |
| `src/Services/ConnectionSettingsStore.cs` | 本机连接配置和可选密钥保存 |
| `src/Services/ConnectionOptions.cs` | 默认值、验证与 API 地址拼接 |
| `src/Services/AssistantPrompt.cs` | 完整默认女仆提示词、长度限制及空值回退 |
| `assets/deepseek-npc.png` | 可替换的立绘文件；打包时复制至 Textures |
| `docs/portrait-prompt.txt` | 立绘生成提示词与工具记录 |
| `docs/portrait-pixel-v2-prompt.txt` | 像素风女仆站姿版本的编辑提示词与工具记录 |

原版动漫风立绘保留在 `assets/deepseek-npc-anime-v1.png`，当前像素风版本另存为 `assets/deepseek-npc-pixel-v2.png`。实际打包素材仍使用 `assets/deepseek-npc.png`。

已实现 OpenAI 兼容格式的 `GET /models` 和非流式 `POST /chat/completions`；JSON 序列化使用游戏自带的 Newtonsoft.Json，不分发额外运行库。默认配置对应 [DeepSeek 官方入门](https://api-docs.deepseek.com/en/)。协议参考：[模型列表](https://developers.openai.com/api/reference/resources/models/methods/list)、[Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)。

游戏数据仅在 Unity 主线程读取为 `AssistantContext`；异步服务使用这个快照。取消令牌用于切换场景 / 关闭面板。当前不写入游戏存档或改变经济数值。

## 自动验证

```powershell
# 使用 .NET 10 SDK，所有 HTTP 响应均为模拟，不使用真实密钥或外部服务
dotnet run --project .\tests\ProbablyAI.Tests.csproj -c Release
```

67 项测试覆盖连接配置与密钥、模型列表、认证隔离、Chat Completions、工具调用、嵌套容器、同名物品、移动、锁定限制、食品类别、透明像素命中、未来工具注册、多轮历史、取消后工具配对、客人及预算查询，以及提示词旧配置兼容、多行保存、长度限制、模型请求传递和修改后历史清理。游戏状态和 HTTP 均为模拟；原生适配已对本机游戏 DLL 编译，游戏内行为与真实服务连通性由玩家实测。

## 版本管理

项目已初始化本地 Git 仓库，分支为 `main`。源码、文档和立绘纳入版本管理；`.cache`、构建产物、游戏 DLL、日志、`.env` 和 `*.local.json` 连接配置均忽略。不包含 Git 远程仓库或自动上传设置。

## 调试

原生日志带“深空AI：可能是DeepSeek”前缀。游戏日志：`%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log`。

如需 F8 控制台，可按官方文档在游戏根目录创建 `dev.txt`，再运行 `mod-scan probably_ai`。本项目不修改游戏加载器、信任记录或开发者模式。

参考：[官方入门](https://github.com/dragonpan2/probably-stolen-modding)、[ModdingGuide](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md)。游戏 DLL 仅作本地编译引用，不包含在分发包内。`.cache` 中的本地研究材料不属于 Mod 分发内容。
