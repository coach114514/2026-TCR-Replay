# 2026 TCR Replay · 赛场回放器

**半成品 / Alpha，正在开发，尚未完全做好。**

非官方的 RoboMaster 2026 赛场回放与开发者仿真工具，采用 Unity 6.3 LTS **6000.3.25f1** 与 Python 离线工具。本项目已有可运行功能，但日志融合、场地/坐标校准、导航鲁棒性和跨平台验收仍有待办。部分姿态、射击目标与弹道属于推定或仿真，不能当成精确比赛事实。

首发为 `v0.1.0-alpha.1` 预发布。公开版包含**原创合成比赛与简化场地**，不是实际比赛数据；完整实现源码已公开，正式场地、车辆和赛事数据需在本地另行准备。见[已知问题](KNOWN_ISSUES.md)、[开发路线](ROADMAP.md)与[资源说明](THIRD_PARTY_NOTICES.md)。

## 快速使用

从 [Releases](https://github.com/coach114514/2026-TCR-Replay/releases) 下载对应系统的 **demo** 压缩包，解压整个目录：

- Linux：执行 `./run.sh` 或 `./TCRReplay.x86_64`。
- Windows：双击 `TCRReplay.exe`；必须保留同目录所有文件。

进入“回放模式”即可查看合成比赛、拖动时间轴、切换视角、开启模拟点云。程序菜单明确标注半成品；公开示例未附正式车辆机构资源，开发者入口会显示资源准备提示。

## 从源码运行

需要 Python 3.10+ 和 Unity **6000.3.25f1**，Unity 授权由使用者自行配置。

```bash
git clone https://github.com/coach114514/2026-TCR-Replay.git
cd 2026-TCR-Replay
python3 -m match_replay.tools.make_demo
```

在 Unity Hub 添加 `match_replay/unity`，打开 `Assets/Scenes/Replay.unity` 后点击 Play。生成合成示例无需 pip 安装。正式数据、生成资源和 Unity 缓存不进入 Git；首次克隆后须先生成示例或导入自己的资源包。

构建菜单：`TCR Replay → Build Linux / Build Windows`。构建会重建主场景，自定义场景请另存。也可使用 CLI：

```bash
"$UNITY_EDITOR" -batchmode -nographics -quit \
  -projectPath "$PWD/match_replay/unity" \
  -executeMethod ReplayBuild.Linux -logFile /tmp/tcr-build.log
```

`UNITY_EDITOR` 指向本机 Editor 可执行文件；Windows 使用 `ReplayBuild.Windows`。可通过 `TCR_REPLAY_OUTPUT` 指定完整输出文件路径。Editor 版本和 UPM 依赖均保留在工程中，具体见[构建与资源准备](docs/BUILDING.md)。

## 功能状态

| 模块 | 当前状态 |
|---|---|
| 对局回放、时间跳转、学校检索及标签筛选 | 已实现；本地正式数据覆盖 613 局，公开示例为 2 局合成数据 |
| 自由相机、第一人称、标签与显示设置 | 已实现；依赖图形后端/硬件，未承诺所有环境一致 |
| 开发者驾驶、独立轮组、惯性、有限功率与制动 | 实验性实现，需要本地正式模型和机构资源 |
| 靶车、自瞄、重力弹道、前哨/基地/能量机关训练 | 实验性实现，规则及几何仍有近似 |
| Livox Avia 模拟扫描、红蓝机位、点云显示 | 已实现；近似扫描与冻结帧几何，不是厂家真实回波仿真 |
| 点云单帧接口 | 已实现进程内订阅、取帧、冻结外参、重置通知；无订阅默认不复制 |
| 导航 | 基础实现，完整鲁棒性仍未完成 |
| 全历史日志 / 无线电 IQ / 裁判串口融合 | 尚未完成 |
| 标准游戏模式 | 尚未开发 |
| 点云录制、文件导出、Python/ROS 桥接 | 尚未开发，仅预留数据接口 |
| Windows 实机验收 | 待完成；构建成功不等于实际运行已验收 |

## 操作与接口

回放：P 播放/暂停；1 观察、2 自由相机、3 第一人称；鼠标控制朝向；WASD 移动；空格/Shift 上下；Caps Lock 快慢；Esc 释放鼠标；F9 标签；F10 纯场地；F11 全屏；F8 显示设置。

模拟点云默认关闭，界面“点云”与“点云设置”可选择红/蓝/双机位、10/20Hz、叠加/观察窗和有界累积。扩展接口通过 `ReplayViewer.LidarFrames` 获取，详见[点云接口](match_replay/docs/点云帧接口.md)与[架构说明](docs/ARCHITECTURE.md)。

正式资源齐备时，开发者模式提供 WASD 驾驶、Shift 自旋、Ctrl 电容爆发、左键发弹、右键自瞄、F6 编辑、Tab 地图、F7 弹道和反引号控制台。数值、运动和机械状态有仿真近似；统一云台参照移动仍列为待办。

## 数据与资源

[资源清单](assets-manifest.json)区分随仓库提供的字体/合成示例与不分发的正式资源。代码许可不覆盖官方 CAD、手册图片、战队模型或赛事数据。不得将区域赛记录叠加全国赛场地的对照画面称为已校准复原。

已有合法取得的转换资源包可本地导入：

```bash
python3 -m match_replay.tools.import_resources --source /path/to/replay --replace-demo
```

导入工具会验证包结构并保留被替换的示例备份，不上传资源。数据导出使用显式 `--database` 或 `--archive` 路径；全库学校拼音需安装 `match_replay/tools/requirements-library.txt`。详细步骤见[构建与资源准备](docs/BUILDING.md)。

## 验证与贡献

```bash
python3 -m unittest discover -s match_replay/tests -v
python3 tools/check_publication.py
```

Unity 构建执行契约/数据校验；Linux 原生支持 `--lidar-api-smoke OUTPUT`、`--replay-lidar-smoke OUTPUT`、`--replay-smoke` 等。正式模型专项测试需要相应资源，不能用合成示例替代。验证范围见[验收记录](docs/VALIDATION.md)。

欢迎提交可复现问题和 PR，见 [CONTRIBUTING](CONTRIBUTING.md)。开源状态和已知问题优先于历史“通过”表述；没有覆盖的场景仍可能出现错误。

## 许可

原创代码与原创合成示例采用 [Apache-2.0](LICENSE)，版权与第三方声明见 [NOTICE](NOTICE) 和 [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES.md)。Unity 及第三方依赖适用各自条款；项目与 RoboMaster、DJI、Livox、Unity 不存在官方认证关系。
