# 开发导航

这份文档用于快速定位代码、运行路径和验证入口。开发约束与发布步骤以 [AGENTS.md](../AGENTS.md) 为准；工具参数和结果见 [hands-api.md](hands-api.md)，上传操作见 [workshop-publishing.md](workshop-publishing.md)。这里的仓库路径均相对于仓库根目录，不依赖具体盘符。

## 第一次接手

1. 读 `AGENTS.md`，确认本次任务是改代码、安装还是发布，再运行 `git status --short` 确认已有改动。
2. 从下方“按任务找代码”定位领域；通常先看对应 `*Protocol.cs`、`*Contracts.cs`、`*Service.cs` 和测试，再按需进入 `Game*Backend.cs`。
3. 只有涉及生命周期、工具注册、场景切换时，才需要通读 `ModEntry.cs` 与 `AssistantController.cs`。
4. 用文末命令编译和运行相关验证。安装与上传单独执行，不是编译的附带动作。

这是引用本机游戏程序集的 .NET Mod 工程，不是完整 Unity 工程，也不需要用 Unity Editor 打开。主项目目标是 `netstandard2.1`；测试是 `net10.0` 控制台程序。两者都需要本机游戏提供的 DLL，测试过程不连接真实模型服务或读取玩家存档。

## 目录与职责

| 路径 | 职责 / 优先入口 |
| --- | --- |
| [ProbablyAI.csproj](../ProbablyAI.csproj)、[manifest.xml](../manifest.xml) | 编译目标、游戏引用路径、Mod 标识、版本与能力声明 |
| [src/ModEntry.cs](../src/ModEntry.cs) | `IMod` 生命周期、创建运行对象、连接配置存储、对外工具扩展事件 |
| [src/AssistantController.cs](../src/AssistantController.cs) | 进店 / 场景生命周期、组装工具服务、聊天记忆、快捷键、语言初始化与客人提醒 |
| [src/AssistantSettings.cs](../src/AssistantSettings.cs) | 游戏 Mods 齿轮设置：立绘、呼吸、快捷键、客人提醒；使用原生 `ModConfig` |
| [src/UI](../src/UI) | `ChatWindow` 聊天；`ConnectionWindow` F7 配置；`HandsWindow` 手动操作；立绘与到店提醒 |
| [src/Services](../src/Services) | HTTP、连接选项与持久化、提示词升级、聊天历史存储与发送上下文裁剪 |
| [src/Tools/ToolRegistry.cs](../src/Tools/ToolRegistry.cs) | 工具注册、Schema 生成与翻译、工具分发、是否改变状态的标记 |
| [src/Hands](../src/Hands) | 容器树、物品查询、移动与开关窗口；`HandsAgent` 工具循环；`GameItemDurability` 通用可见耐久读取 |
| [src/Customers](../src/Customers) | 当前客人、预算、需求与价格；`GameCustomerPricing` 原生报价适配、`CustomerPriceCalculator` 价格计算、`RecorderHistory` 对话记录、`VisitorRisk` 提醒判断 |
| [src/Inspection](../src/Inspection) | 目视检查与水质分析；原生读取和纯判断逻辑分离，如 `WaterAssessment`、`InjectorAssessment` |
| [src/Liquids](../src/Liquids) | 倒取、倒掉液体、接水；`LiquidRules` 纯规则，`GameLiquidBackend` 执行原生操作 |
| [src/Items](../src/Items) | 通用物品对物品使用、使用 / 激活；`ItemUseRules` 与 `GameItemUseBackend` |
| [src/Documents](../src/Documents) | 笔记、书籍、指南等原生可见文字的只读读取 |
| [src/Labels](../src/Labels) | 普通标签机的选项查询 / 标签编辑、蓝色标签机改名 / 清除名称；通过原生窗口回调执行 |
| [src/Localization](../src/Localization) | `Text` 语言选择与 `T` / `F`；`EnglishTranslations` 英文文本与工具说明 |
| [tests](../tests) | 控制台测试、模拟 HTTP / 游戏状态、原生接口桩及发布包校验测试 |
| [scripts](../scripts) | `Build.ps1` 编译 / 安装；`Prepare-Workshop.ps1` 打包；`Verify-WorkshopPackage.ps1` 校验 |
| [assets](../assets) | 立绘、封面、预览和历史素材；实际立绘打包源为 `deepseek-npc.png` |
| [workshop](../workshop) | 商店页中英文文案、玩家指南、更新备注、公开条目 ID、各版本发布清单 |
| `.cache/` | 被 Git 忽略的本机研究材料与可选上传工具；不是项目依赖，换机器可能没有 |
| `bin/`、`obj/`、`dist/` | 被 Git 忽略的构建 / 发布产物，不在这些目录里修源码 |

## 运行调用链

```text
ModEntry.Init                 定义原生设置，不提前读取游戏本地化
  → ModEntry.OnEnable        创建客户端、连接配置存储与 AssistantController
  → OnGameLoaded             创建原生适配器、注册工具、恢复当前存档槽的聊天历史
  → ChatWindow / HandsWindow 接收指令
  → HandsAgent.RunAsync      配置快照、实况刷新、历史裁剪、多轮模型请求与取消
  → OpenAiApiClient          解析 Chat Completions 的文本和 tool_calls
  → ToolRegistry.Execute     按工具名分发到对应 Service
  → Service → Game*Backend   参数 / 规则检查后读取或操作原生对象
  → HandsResult              结果交回模型；聊天窗口保存完整轮次
```

`HandsAgent` 位于 `Hands/`，但负责全部已注册工具，不只负责搬运。`AssistantController.OnGameLoaded` 是内置工具的组装入口；对外扩展入口是 `ModEntry.ToolsReady`。`changesState` 决定操作记录与历史投影，新增修改游戏状态的工具需要正确标记。

立绘聊天传入 `ConversationMemory` 保留多轮历史；F7 手动操作页的助手指令不传记忆，是独立请求。F7 的测试对话经 `IAssistantService` / `OpenAiAssistantService` 调用普通聊天接口，不走游戏工具循环。不要把这三个入口混为一处修改。

原生物品 / 容器 / 客人访问在 Unity 主线程执行。`HandsAgent` 保留 Unity 同步上下文；不要把工具分发放进 `Task.Run`。场景卸载时控制器取消请求、关闭窗口并使旧适配器失效，物品 ID 只在当前载入中有效。

## 按任务找代码与测试

| 任务 | 先看 / 通常需要一起修改 | 验证入口 |
| --- | --- | --- |
| F7 设置、新配置或工具调用上限 | `UI/ConnectionWindow.cs`、`Services/ConnectionOptions.cs`、`ConnectionSettingsStore.cs`；循环限制在 `Hands/HandsAgent.cs` | `Program.cs`、`ContextTests.cs`、`HandsTests.cs` |
| 立绘设置或快捷键 | `AssistantSettings.cs`、`AssistantController.cs`、`UI/Portrait*.cs` | `PortraitTests.cs`；原生 UI 编译与游戏实测 |
| 物品字段、耐久、容器树 | `Hands/HandsContracts.cs`、`GameHandsBackend.cs`、`GameItemDurability.cs`、`HandsProtocol.cs`；F7 详情在 `UI/HandsWindow.cs` | `HandsTests.cs`、`DurabilityTests.cs` |
| 查询 / 移动物品、批量移动 | `Hands/HandsProtocol.cs` → `HandsService.cs` → `GameHandsBackend.cs` | `HandsTests.cs` |
| 接水、倒水、物品使用 | `Liquids/` 或 `Items/` 中的 Protocol、Contracts、Service、Rules、GameBackend | `LiquidTests.cs`、`ItemUseTests.cs` |
| 改标签、改名、清除自定义名称 | `Labels/LabelProtocol.cs`、`LabelService.cs`、`GameLabelBackend.cs`；组装在 `AssistantController.OnGameLoaded` | `LabelTests.cs`、`LocalizationTests.cs`；实际窗口回调与同帧连续编辑需游戏实测 |
| 客人需求、预算和价格 | `Customers/CustomerProtocol.cs`、`CustomerService.cs`、`GameCustomerBackend.cs`、价格相关文件 | `CustomerTests.cs`、`PricingTests.cs` |
| 到店提醒 / 录音机记录 | `AssistantController.cs`、`Customers/VisitorRisk.cs`、`RecorderHistory.cs`、`UI/VisitNoticeView.cs` | `ChatTests.cs`、`CustomerTests.cs` |
| 鉴定 / 水质 / 文档读取 | `Inspection/` 或 `Documents/` 的协议、服务、原生适配与纯判断类 | `InspectionTests.cs`、`DocumentTests.cs` |
| 聊天历史、保存、裁剪或实况刷新 | `Services/ConversationMemory.cs`、`ConversationStore.cs`、`TokenEstimator.cs`、`Hands/HandsAgent.cs` | `ChatTests.cs`、`ContextTests.cs`、`FreshnessTests.cs` |
| 默认提示词或语言 | `Services/AssistantPrompt.cs`、`Localization/`；UI 字符串使用 `T` / `F` | `PromptTests.cs`、`LocalizationTests.cs`、`FreshnessTests.cs`、`InspectionTests.cs`；默认提示词升级须保留旧模板识别 |
| HTTP / 服务商兼容 | `Services/OpenAiApiClient.cs`、`ChatTurn.cs`、`ConnectionOptions.cs` | `Program.cs`、`HandsTests.cs`、`PromptTests.cs` |
| 发布版本、简介与包内容 | `AGENTS.md` 发布清单、`workshop/`、`scripts/` | `tests/WorkshopPackage.Tests.ps1` 与发布后核验 |

表中 `src/` 内路径省略了共同前缀。测试用例的可执行入口是 `tests/Program.cs`，不是测试框架的自动发现；`DurabilityTests` 由 `RunHandsTests` 调用。

## 路径速查

下面的 `<game>` 是安装目录，默认 `C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen`；`<data>` 是游戏的 `Application.persistentDataPath`，Windows 通常位于 `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen`。使用这些占位说明，避免在仓库记录个人绝对路径。

| 路径 | 内容与来源 |
| --- | --- |
| `<game>/Probably Stolen_Data/Managed/` | 本机游戏 DLL 编译引用；主项目默认路径和 `GameDirectory` 覆盖项见 csproj |
| `bin/Release/netstandard2.1/ProbablyAI.dll` | 主项目 Release 编译输出 |
| `dist/ProbablyAI/` | `Build.ps1` 生成的本机包，包含 `docs/hands-api.md` |
| `<game>/Mods/ProbablyAI/` | `Build.ps1 -Install` 的安装目的地；只覆盖脚本允许的 7 个文件 |
| `dist/workshop/<version>/content/` | 工坊上传内容，只有允许的 6 个文件；中文 README 来自 `workshop/README.zh.md`，不是根 README；不含 `docs/` |
| `dist/workshop/<version>/` | `preview.jpg`、`workshop.vdf`、`package-inventory.json` 位于 content 之外 |
| `workshop/release-<version>.inventory.json` | 纳入 Git 的发布哈希快照；必须对应实际上传包 |
| `<data>/ModConfigs/probably_ai.json` | 原生 `ModConfig` 设置：立绘、快捷键和提醒；入口 `AssistantSettings` |
| `<data>/ModData/probably_ai/connection.local.json` | F7 连接、可选密钥、提示词、思考级别、历史及工具限制；入口 `ModEntry` / `ConnectionSettingsStore` |
| `<data>/ModData/probably_ai/chat-history-<saveSlotId>.local.json` | 当前存档槽完整聊天历史；无槽位时使用 `chat-history.local.json`；入口 `AssistantController.CreateConversation` |
| `<data>/ModData/probably_ai/chat-window.local.json` | 聊天窗口位置；入口 `UI/ChatWindow.cs` |
| `<data>/Player.log` | 游戏运行日志，Mod 日志带品牌名称前缀 |
| `.cache/workshop-publisher/` | 本机可选 Steamworks 上传工具；不在 Git 中，缺失时按发布文档使用 SteamCMD |

这些玩家配置和历史路径仅用于定位，不应修改、提交或打包。Mod ID 固定为 `probably_ai`，安装文件夹使用 `ProbablyAI`，不要混用两者。

## 常见修改步骤

新增游戏工具时，优先沿用已有领域的分层：

1. 在 `*Protocol.cs` 声明稳定工具名、参数类型、必填项及 `additionalProperties=false`；在 Contracts 中定义读数 / 后端接口。
2. 在 Service 中校验参数和映射结果；可分离的纯规则放在 Rules / Assessment 中，原生对象访问放在 GameBackend 中，遵守游戏的所有权、锁定和容量规则。
3. 在服务的 `Register` 或 `ToolRegistry.ForHands` 接入分发；新增领域则在 `AssistantController.OnGameLoaded` 组装，正确设置 `changesState`。
4. 补英文工具说明与 UI 翻译，更新 `docs/hands-api.md`。如果改动默认提示词，按 `AGENTS.md` 执行旧默认迁移。
5. 补模拟测试和必要的原生接口桩；编译后完整运行测试。原生 UI 与未模拟的行为明确列为游戏实测项。

新增物品字段通常不需要新增工具：扩展 `ItemInfo` 与快照采集，`inspect_item`、`find_items`、`list_container` 共用这份数据。`HandsProtocol.Json` 统一转成 snake_case 并省略 null；未知字段用 null 表达，不要用零冒充已核实值。

新增 F7 配置时，检查 `ConnectionOptions` 默认值、`Copy`、`Normalized`、窗口创建 / 读取 / 保存回填 / 忙碌禁用与持久化测试。原生 Mods 齿轮设置则放在 `AssistantSettings`，保持已有键稳定。

## 构建、测试与搜索

在仓库根目录执行：

```powershell
git status --short
dotnet build .\ProbablyAI.csproj -c Release --nologo
dotnet run --project .\tests\ProbablyAI.Tests.csproj -c Release
```

游戏不在默认目录时，**主项目与测试都传入**相同覆盖值：

```powershell
dotnet build .\ProbablyAI.csproj -c Release '-p:GameDirectory=D:\SteamLibrary\steamapps\common\Probably Stolen'
dotnet run --project .\tests\ProbablyAI.Tests.csproj -c Release '-p:GameDirectory=D:\SteamLibrary\steamapps\common\Probably Stolen'
```

主项目通过 `src/**/*.cs` 收集源码；测试项目使用显式 Compile 列表并链接部分生产源码和原生接口桩。新增源码或测试时检查 `tests/ProbablyAI.Tests.csproj`；新增测试组还要接入 `Program.cs` 或已有测试组。使用 `dotnet run`，不要用 `dotnet test` 的无输出代替通过证明。当前基线为 **227 项**，以后以实际输出为准。

测试覆盖纯逻辑、模拟 HTTP、工具循环及部分用原生接口桩执行的生产适配逻辑；并不等于完整 Unity 集成测试。`GameHandsBackend` 的实际场景遍历、窗口显示和物品原生回调仍需游戏实测。新增逻辑优先补可模拟测试，不为单纯文档修改新增运行时测试。

定向搜索示例（PowerShell）：

```powershell
rg --files src tests docs scripts workshop
rg -n 'inspect_item|durability' src/Hands tests/DurabilityTests.cs docs/hands-api.md
rg -n 'MaxToolRounds|MaxToolCalls' src tests
rg -n 'Register\(|OnGameLoaded|ToolsReady' src
rg -n 'connection.local|chat-history|chat-window' src
```

优先在对应领域搜索，避免递归搜整个游戏目录或 `.cache/full`。需要核对原生行为时才查本机游戏 DLL / 已有研究缓存；缓存不是权威版本，也不是新环境必须具备的目录，以当前安装的游戏程序集和运行行为为准。不要从真实配置或日志复制密钥到测试。

安装、工坊打包、上传与 Git 推送的完整步骤见 `AGENTS.md`。更改目录、组装入口、持久化文件名或测试接线时同步维护本页，避免再次依靠全仓搜索定位。
