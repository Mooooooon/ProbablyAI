# Steam 创意工坊发布与更新

游戏 App ID：`4348910`。游戏没有上传按钮；官方介绍使用 SteamCMD。也可以使用已登录 Steam 客户端的 Steamworks UGC 接口，不需要读取账号密码。任何账号验证、Steam Guard 或首次法律协议都由账号本人完成。

本项目已发布条目：[3830081737](https://steamcommunity.com/sharedfiles/filedetails/?id=3830081737)，中英文标题与简介均已上传，使用像素风鲸鱼娘封面。更新时继续使用这个 ID，避免重复创建。

## 发布前

```powershell
.\scripts\Prepare-Workshop.ps1
.\scripts\Verify-WorkshopPackage.ps1 -ContentFolder '.\dist\workshop\0.7.2\content'
```

只上传独立的 `content` 目录，不要选择整个项目、游戏安装目录或个人 ModData 目录。允许文件只有 `ProbablyAI.dll`、`manifest.xml`、`README.md`、`README.en.md`、`preview.png`、`Textures/deepseek-npc.png`。连接配置、API Key、存档、日志、`.git`、`.cache`、游戏 DLL、PDB 和上传辅助工具都不在其中。封面原图是 `assets/workshop-cover-v1.png`；上传预览使用约 473 KB 的 `assets/workshop-preview-v1.jpg`。

中英文标题与简介在 `workshop/`。公开条目 ID 保存于 `workshop/item.json` 后，Prepare 脚本会自动写入更新用 VDF；这个文件只记录公开条目 ID / URL，不存登录信息。校验清单 `package-inventory.json` 与 `workshop.vdf` 在 content 外，均不作为 Mod 内容上传。

创意工坊图库的附加截图保存在 `assets/workshop-preview-screenshot-1.png`、`assets/workshop-preview-screenshot-2.jpg`、`assets/workshop-preview-screenshot-3.jpg`、`assets/workshop-preview-screenshot-4.jpg`。后三张展示客人预算核对、食物选品与机械手鉴定结果，保留 2560 × 1440 分辨率，以质量 95 的 JPG 导出，单张均小于 1 MB。通过 Steamworks UGC 的 `AddItemPreviewFile` 追加到现有条目，不放进 Mod 的 content 目录。

## 用 SteamCMD 手动上传

1. 从 Valve 的 SteamCMD 页面安装工具，以拥有游戏的账号登录。不要把密码写进脚本或聊天；需要时在 SteamCMD 自己的登录提示中输入，并完成 Steam Guard。
2. 在 SteamCMD 中执行：

```text
login 你的Steam登录名
workshop_build_item "E:\coding\Probably AI\dist\workshop\0.7.2\workshop.vdf"
quit
```

3. 首次创建时 `publishedfileid=0`。SteamCMD 会把新 ID 写回 VDF；保存该公开 ID，后续更新使用相同 ID，避免重复创建条目。
4. 若 Steam 返回需要接受 Workshop 协议，在条目页面按提示由你本人阅读并接受。未完成这一步时条目可能无法公开可见。
5. VDF 的 `visibility=0` 为公开，`1` 为好友，`2` 为私有。准备脚本默认公开，与本次上架要求一致。英文简介先用于默认页面；中文标题、简介可在条目网页的语言编辑入口补充，或通过 UGC 的 `SetItemUpdateLanguage("schinese")` 设置。

官方资料：[Probably Stolen 发布指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md#steam-workshop)、[Valve SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD)、[Steamworks UGC](https://partner.steamgames.com/doc/api/ISteamUGC)。
