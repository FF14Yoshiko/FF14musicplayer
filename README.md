# 全时刻音效触发器

作者：肘击

一个面向国服 XIVLauncherCN / Dalamud 的本地音效触发器插件。插件只使用客户端本地可见信息，监听聊天日志、战斗状态、地图切换、职业变化、HP、Buff、物品获得、击杀等事件，并按用户配置的方案、分组和规则播放本地音效。

## 安装

在 Dalamud 插件安装器里添加第三方插件仓库：

```text
https://raw.githubusercontent.com/FF14Yoshiko/FF14musicplayer/main/pluginmaster.json
```

添加后搜索“全时刻音效触发器”并安装。

## 功能

- 规则触发：技能使用、物品获得、地图切换、进出战斗、HP 变化、低血量、职业切换、Buff 获得/消失、击杀。
- 音效库：导入 MP3/WAV/OGG，自动复制到插件管理目录。
- 规则编辑：游戏内选择触发器、条件和动作，不需要手写 JSON。
- 方案管理：多套方案独立保存，可一键切换。
- 自动切换：进入指定地图自动切到指定方案，离开后切回默认方案。
- Buff 循环音效：获得 Buff 时循环播放，Buff 消失时自动停止。
- 分享包：导入/导出 `.sfxpack`，可分享整个方案、单个分组或单条规则。

## 合规说明

插件不会自动执行任何游戏操作，不读取或显示敌方未公开数据。所有音效播放都发生在本地。

## 开发构建

需要本机已安装 XIVLauncherCN / Dalamud API 15 开发文件。

```powershell
dotnet build .\AllTimeSoundTrigger.csproj -c Release
```

构建后可发布包位于：

```text
bin\Release\AllTimeSoundTrigger\latest.zip
```
