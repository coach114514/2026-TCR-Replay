# 第三方代码、资源与分发范围

本仓库的 Apache-2.0 仅覆盖原创代码和原创合成示例。素材可下载、帖子写“开源”，均不等于其内容适用本仓库代码许可证。

| 项目 | 来源 / 许可 | 本次分发 |
|---|---|---|
| 中文字体 Droid Sans Fallback | Google，Apache-2.0；见 licenses/Droid-font-notice.txt | 字体和版权说明随源码、程序包提供 |
| Unity SharpZipLib 1.4.2 | Unity Companion License；保留其内嵌上游声明，见 licenses/sharp-zip-lib | 源码以 UPM 声明依赖；程序集与声明随 Player 构建 |
| Unity 引擎和内置模块 | [Unity 软件条款](https://unity.com/legal/editor-terms-of-service/software)及适用运行时分发条款 | 不分发 Editor、激活文件或 PackageCache；Player 由合法授权 Editor 构建 |
| cadmpeg 格式参考文档 | [上游](https://github.com/cadmpeg/cadmpeg)，文档 CC BY 4.0 | 仅引用/致谢；不打包文档、源码或样本 CAD |
| 官方全国赛 STEP 和规则手册图像 | [官方资料中心](https://bbs.robomaster.com/wiki/20204847)，未据此认定有宽松再分发许可 | 原文件、派生场地网格、手册贴图均不随首发提供 |
| 官方区域赛数据及派生回放包 | [发布帖子](https://bbs.robomaster.com/article/1936220)；帖子标注 CC BY-NC-SA 4.0，附件的适用范围仍应核实 | 数据库和 613 局导出包均不随首发提供，使用者自行从来源获得 |
| 扫地机器人 v2.1 | [TCR 机械开源帖](https://bbs.robomaster.com/article/1943679)声明未经作者许可不得商用 | 原 CAD、派生视觉和碰撞网格不随首发提供，不覆盖为 Apache-2.0 |
| 嘉庚 26 麦轮步兵 CAD | 本地提供的“厦大嘉庚26赛季-麦轮步兵开源.zip”；未取得可直接覆盖的许可文本 | 原 CAD 和派生网格不随首发提供 |
| 原区域赛 PLY/地图 | 来自原雷达工程的 field 目录，资源单独许可尚未确认 | 不随首发提供 |
| 正式模型的编号字体/编号网格与参考照片 | 编号字体有单独 OFL 声明；照片仅参考，模型链仍涉及上游 CAD | 未打包正式编号网格与照片 |

Python 转换依赖不内嵌进源码：NumPy、SciPy、VTK、OpenCascade/OCP、fast-simplification 等由用户按需要安装，各自保留上游许可。普通示例生成仅使用 Python 标准库，普通 Player 运行不需要 CAD 转换环境。

正式资源可以在用户本地导入；导入行为不会改变资源原许可。再次打包、发布正式资源或完整程序前，应逐项确认自己的分发权限。公开 Releases 的预览程序仅包含本仓库原创合成示例，不包含上述正式模型/赛事包。

