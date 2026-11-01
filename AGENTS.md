# AGENTS.md

本文件面向在本仓库工作的 AI 编码代理，说明开发、测试、发布和 Steam 创意工坊上传流程。
先读本文件，再按需查阅下列文档；两者冲突时，以本文件 + 当前脚本行为为准，并同步修正文档。

## 先读什么

| 文档 | 用途 |
| --- | --- |
| `README.md` / `README.en.md` | 功能、安装、构建、发布包说明 |
| `docs/hands-api.md` | 容器 / 物品 / 客人 / 鉴定工具的协议与行为 |
| `docs/workshop-publishing.md` | SteamCMD 上传、VDF、公开条目信息 |
| `CHANGELOG.md` | 版本更新记录 |
| `workshop/description.*.bbcode` | 创意工坊商店页文案与更新日志 |
| `workshop/changenote.*.txt` | Steam 更新备注（按语言） |
| `scripts/*.ps1` | 构建、安装、打包、校验的实际行为 |

## 项目事实

- 游戏：Probably Stolen（Unity 2021.3.45f2，应用版本 0.46D / VERSION 100-Rev3），Steam App ID `4348910`。
- Mod ID：`probably_ai`。不要改；改名会重置玩家的连接配置和立绘设置。
- 运行时：`netstandard2.1`，引用本机游戏目录下的 `Assembly-CSharp.dll` 和 UnityEngine 程序集。
- 默认游戏目录：`C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen`。不同机器用 `-GameDirectory` / `-p:GameDirectory=...` 覆盖。
- 创意工坊条目：`3830081737`。更新时复用该 ID，不要新建条目。
- 当前版本以 `manifest.xml` 的 `ModVersion` 为准；发布时还要同步 `ProbablyAI.csproj`、README 和文档里的版本号。

## 开发流程

### 环境

- .NET 10 SDK。
- 游戏已安装并至少启动过一次（需要 `Probably Stolen_Data\Managed` 下的 DLL）。
- 本机 Steam 客户端（仅在需要上传创意工坊时）。

### 构建与测试

```powershell
# 只编译
dotnet build .\ProbablyAI.csproj -c Release --nologo

# 游戏在非默认目录
dotnet build .\ProbablyAI.csproj -c Release -p:GameDirectory='D:\SteamLibrary\steamapps\common\Probably Stolen'

# 自动测试（全模拟 HTTP 和游戏状态，不使用真实密钥或存档）
dotnet run --project .\tests\ProbablyAI.Tests.csproj -c Release
```

测试输出是最终依据；当前为 175 项。提交前必须全绿。所有新增逻辑优先补纯逻辑测试；涉及 Unity 原生对象的适配层可以只做编译验证，但要在说明中写清未覆盖的部分。

### 安装到本机游戏

```powershell
# 先关闭游戏；脚本会覆盖 Mods\ProbablyAI
.\scripts\Build.ps1 -Install
```

只允许通过该脚本把 Mod DLL、manifest、README、立绘、cover 和 `docs/hands-api.md` 复制到 `Mods\ProbablyAI`。不要手工改游戏目录、存档、游戏 DLL 或其他 Mod。

### 代码规则

- 所有物品 / 容器 / 客人操作都在 Unity 主线程执行；`GameHandsBackend` 已有线程检查。
- 不绕过游戏原生的所有权、锁定、容量和取放规则；不新增自动买卖、报价提交、攻击或改存档能力。
- 新增 UI 文本必须用 `T("中文")`，并在 `src/Localization/EnglishTranslations.cs` 补英文；测试会拒绝漏翻或格式占位符不一致。
- 不要在 `IMod.Init` 或静态构造函数中调用 `LocHelper` / `LocalizationSettings`。游戏本地化在 `MainMenuUIController.Start` 阶段才初始化；提前读取会让 Addressables 提前启动，并可能使其他 Mod 的共享异步句柄失效。需要语言时，在 `OnEnable` 后用协程等待 `InitializationOperation`，再订阅 `SelectedLocaleChanged`。
- 新增 `ModConfig` 键要稳定、可持久化；不要重命名已有键。
- 修改默认提示词时，必须把旧默认字符串加入 `AssistantPrompt.Normalize` 的识别列表，并更新 `PromptTests` / `FreshnessTests` / `InspectionTests`，否则玩家保存过的旧默认不会自动升级。
- 立绘的待机呼吸以脚底为锚点改变高度，不是整体平移；改动时保持 `PortraitBreathing.Offset` 的纯函数可测性。
- 设备供电 / 充电中的电池属于自用库存：`in_use=true`，`find_items` 默认不返回；不要把它当作可挑选商品。手动 UI 仍可直接查看和移动。

## 发布流程

### 1. 版本与更新日志

发布前逐项检查并同步：

| 文件 | 内容 |
| --- | --- |
| `ProbablyAI.csproj` | `<Version>` |
| `manifest.xml` | `<ModVersion>` |
| `README.md` | 顶部“当前版本”、`dist/workshop/<version>` 路径 |
| `README.en.md` | 底部 `Version ...` |
| `docs/hands-api.md` | 标题版本 |
| `docs/workshop-publishing.md` | 示例路径中的版本 |
| `CHANGELOG.md` | 新版本条目 |
| `workshop/description.zh.bbcode` / `.en.bbcode` | “更新日志 / Changelog”新条目 |
| `workshop/changenote.zh.txt` / `.en.txt` | Steam 更新备注 |
| `workshop/README.zh.md` | 玩家指南中的功能说明（如有变化） |

版本号使用 `0.7.x` 形式。更新日志写玩家能理解的行为变化，不写内部实现细节。

### 2. 打包

```powershell
.\scripts\Prepare-Workshop.ps1
.\scripts\Verify-WorkshopPackage.ps1 -ContentFolder '.\dist\workshop\<version>\content'
```

`Prepare-Workshop.ps1` 会：

1. 调用 `Build.ps1` 生成 Release DLL；
2. 按 `manifest.xml` 的版本创建 `dist/workshop/<version>`；
3. 只复制允许列表的 6 个文件到 `content`；
4. 校验 preview 小于 1 MB；
5. 生成 `package-inventory.json` 和 `workshop.vdf`（changenote 读取 `workshop/changenote.en.txt`）。

打包后把校验快照纳入版本库：

```powershell
Copy-Item '.\dist\workshop\<version>\package-inventory.json' `
  '.\workshop\release-<version>.inventory.json'
```

`content` 必须只有：`ProbablyAI.dll`、`manifest.xml`、`README.md`、`README.en.md`、`preview.png`、`Textures/deepseek-npc.png`。不要把项目根目录、`.cache`、`dist` 以外的本机配置、存档、日志、PDB 或密钥打包进去。

### 3. 上传到创意工坊

**方式 A：SteamCMD（可复现的正式路径）**

1. 安装 SteamCMD，使用拥有游戏的账号登录；账号、密码、Steam Guard 由本人操作，禁止写入脚本、提交或聊天。
2. 在 SteamCMD 中执行：

```text
login <你的账号>
workshop_build_item "<仓库根目录>\dist\workshop\<version>\workshop.vdf"
quit
```

3. 首次创建时 `publishedfileid=0`；本项目已有条目 `3830081737`，不要重复创建。
4. 若 Steam 要求接受 Workshop 法律协议，由账号本人在条目页面完成；代理不能代勾选。

**方式 B：本机 Steamworks 发布工具（可选，便于自动更新中英文描述）**

工具位于 `.cache/workshop-publisher`，**被 `.gitignore` 忽略，不属于仓库内容**；换机器可能不存在，此时用方式 A。它使用已登录的 Steam 客户端和 Steamworks UGC，不读取账号密码。

```powershell
# 构建（需要本机游戏 DLL 中的 Steamworks 程序集）
dotnet build .\.cache\workshop-publisher\Publisher.csproj -c Release

# 检查登录与游戏所有权
& .\.cache\workshop-publisher\bin\Release\net10.0\Publisher.exe probe

# 更新 content + 中英文标题/简介/更新备注（推荐）
& .\.cache\workshop-publisher\bin\Release\net10.0\Publisher.exe update `
  ".\dist\workshop\<version>" "." 3830081737

# 只更新内容，不改简介
& .\.cache\workshop-publisher\bin\Release\net10.0\Publisher.exe update-content `
  ".\dist\workshop\<version>" "." 3830081737

# 查看条目状态（英文 / 中文各一次）
& .\.cache\workshop-publisher\bin\Release\net10.0\Publisher.exe inspect 3830081737
```

`update` 会先校验 `content` 允许列表、`package-inventory.json` 哈希和 preview 大小，再提交英文内容 + 描述、中文描述，并读取 `workshop/changenote.{en,zh}.txt` 作为更新备注。它要求 Steam 客户端已启动、已登录且拥有游戏。失败时不要连续重试；先检查条目 ID 和内容哈希，避免重复创建或重复提交。

### 4. 上传后核验

- `Publisher.exe inspect 3830081737`：标题、公开状态、文件大小、严格校验；中英文各查一次。
- 或用 Steam 公共 API 核对默认简介和公开状态：

```powershell
$body = @{ itemcount = 1; 'publishedfileids[0]' = 3830081737 }
$d = (Invoke-RestMethod -Method Post -Body $body `
  -Uri 'https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/').response.publishedfiledetails[0]
$d.visibility                 # 0 = 公开
$d.description.Contains('v<version>')
$d.file_size
```

匿名抓取社区页面可能返回“item hidden / no permission”，这不等于条目不可见；以 Steamworks 查询或公共 API 为准。中英文描述分别通过 `SetItemUpdateLanguage("english")` / `("schinese")` 提交。

### 5. 提交与推送

```powershell
git add -A
git commit -m "release: v<version> with <summary>"
git push origin main
```

- 分支是 `main`，不要 force push。
- 发布提交应包含：版本号、更新日志、changenote、`workshop/release-<version>.inventory.json`。
- `.cache/`、`dist/`、`bin/`、`obj/`、`*.local.json`、`.env`、密钥和日志不得提交。
- 推送后可用 `git ls-remote origin refs/heads/main` 确认远端 HEAD；`credential-manager-core is not a git command` 之类警告只要 push 成功可忽略。

## 不要做

- 不要在仓库、脚本、文档或聊天中保存 Steam 密码、Steam Guard、API Key 或本机用户路径（发布包会校验本机路径）。
- 不要上传整个项目、游戏目录、`ModData`、`.cache` 或包含连接配置的文件。
- 不要修改游戏 DLL、存档或玩家本机配置；安装只走 `Build.ps1 -Install`。
- 不要为“让功能成功”而绕过原生锁定、所有权、容量或经济规则。
- 不要在未跑测试和未校验发布包哈希的情况下上传。

## 常见问题

| 现象 | 处理 |
| --- | --- |
| 构建提示找不到游戏程序集 | 用 `-GameDirectory` / `-p:GameDirectory` 指向正确的游戏目录 |
| 安装失败或文件被占用 | 先完全关闭游戏，再执行 `Build.ps1 -Install` |
| `Steam API: k_ESteamAPIInitResult_...` 失败 | 启动并登录 Steam 客户端，确认账号拥有游戏 |
| 上传后条目仍显示旧版本 | 等待 Steam 处理，再用 `inspect` / 公共 API 核对 `time_updated` 和简介 |
| 社区页面提示条目隐藏 | 匿名访问可能被限制；改用 Steamworks/API 核验，不要据此重复创建条目 |
| 提示需要接受 Workshop 协议 | 由账号本人在条目页面接受，代理不要代替同意 |
| 两次构建的 DLL 哈希不同 | .NET 构建可能非确定性；以 `package-inventory.json` 和上传工具的校验为准 |
