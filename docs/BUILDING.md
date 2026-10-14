# 构建与资源准备

## 合成示例

Python 3.10+ 标准库即可：从仓库根执行 `python3 -m match_replay.tools.make_demo`。存在正式资源时拒绝覆盖；重新生成自己的示例需 `--force`，且只允许替换标记为 synthetic-demo 的目录。

在 Unity 6000.3.25f1 打开 match_replay/unity。UPM 按 manifest 与锁文件解析依赖；不要复制 Library/PackageCache。菜单构建或 README 的 CLI 会生成场景、运行纯契约和包结构检查。Windows 模块为 Windows Build Support (Mono)，Linux 为对应平台模块。普通 Player 不需要 Python、CAD 库或 Editor。

## 已转换的完整资源包

文件夹须包含 catalog.json、fields_catalog.json、比赛 JSON/gzip、场地纹理/几何、正式车辆及机构/碰撞配置。先检查：

```bash
python3 -m match_replay.tools.import_resources --source /path/to/replay --dry-run
python3 -m match_replay.tools.import_resources --source /path/to/replay --replace-demo
```

工具验证路径边界、比赛身份、格式、字段和主要引用，拒绝符号链接及替换已有外部资源。保留旧示例备份；不上传资源，也不授予新的使用/分发许可。完整机械网格和机构契约继续由 Unity ReplayBuild.Verify 校验。正式资源导入后重新构建；程序资源位于 TCRReplay_Data/StreamingAssets/replay。

## 官方数据导出

自行从 THIRD_PARTY_NOTICES 中的官方来源取得数据库。推荐直接指定已解压 SQLite；只读访问：

```bash
python3 -m pip install -r match_replay/tools/requirements-library.txt
python3 -m match_replay.tools.cli --database /path/to/dataset.sqlite list-matches
python3 -m match_replay.tools.cli --database /path/to/dataset.sqlite export-library
```

export-library 只更新比赛/检索目录，须先备好与记录身份一致的场地资源。不要将合成场地当作正式区域赛场地。单局 export 的区域赛资源准备可显式传 `--map-image` 和 `--field-mesh`，当前 PLY 解析面向原工程五浮点顶点/三角面的布局，不能视为通用 PLY 导入器。压缩包模式需 bsdtar (libarchive-tools)，或手动解压并传 --database。

## 从源 CAD 转换（进阶，实验性）

转换工具包含 STEP 字节索引/装配提取、OCP 三角化、配色减面、SolidWorks 保存网格读取及机械/碰撞分组。依赖见 requirements-cad.txt。普通回放无需安装这些依赖。

```bash
python3 -m venv .venv-cad
.venv-cad/bin/python -m pip install -r match_replay/tools/requirements-cad.txt
.venv-cad/bin/python -m match_replay.tools.step_index /path/to/RMUC2026_V2.0.0.stp
.venv-cad/bin/python -m match_replay.tools.step_convert --source /path/to/RMUC2026_V2.0.0.stp --definition 343127
```

正式场地当前针对 RMUC2026_V2.0.0 的产品定义和坐标，不是任意 STEP 的自动场地识别。分组组件还需定义 740421、1402169、2702539、3648370、5891081、6919696、7011105、181869、196117、41070、56906。随后运行 step_colors（显式 --source）及 step_assemble --source PATH --colored。

车辆与机构的完整转换流水线仍依赖已核对源包、零件组和缓存，尚未做成一键通用转换：扫地源目录使用 ~/.cache/tcr-robot-models/sweeper-source/扫地机器人v2，麦轮零件缓存由 robot_prepare_jiageng 准备，后续 robot_sweeper/robot_jiageng、mechanism_cad、developer_base、vehicle_rig 等按具体模型运行。其版本敏感性、缺失引用和手册贴图准备仍需人工核实。取得不同版本时先检查源校验和和几何契约，不能声称这里的说明已验证所有第三方 CAD。

首发保证的复现路径是合成示例。完整本地源包的历史流水线需要继续工程化，属于半成品边界；直接导入合法拥有的已转换资源包是当前完整功能恢复的较短路径。
