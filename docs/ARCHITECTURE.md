# 逻辑架构

```mermaid
flowchart TD
  Source[用户本地 SQLite / CAD 资源] --> Tools[只读导出 / 几何转换 / 校验]
  Demo[原创合成示例生成器] --> Pack[本地 StreamingAssets 资源包]
  Tools --> Pack
  Pack --> Viewer[Unity 主菜单 / 资源配置]
  Viewer --> Replay[日志时间轴 / 搜索 / 插值与未知处理]
  Viewer --> Dev[开发者状态与控制台]
  Replay --> World[场地 / 车辆 / 机关]
  Dev --> Physics[120Hz 物理 / 轮组 / 功率与惯性]
  Physics --> World
  Dev --> Aim[观测滤波 / 自瞄 / 弹道与命中]
  Aim --> World
  World --> Sensor[独立扫描场景 / Avia 近似射线]
  Sensor --> Raw[原始单帧]
  Raw --> Display[有界累积 / GPU 显示]
  Raw --> API[可选不可变快照 / 订阅 / 最新帧 / 重置代次]
  API -.未来.-> External[录制 / 导出 / 算法桥接]
```

大部分 UI 与场景协调代码使用 ReplayViewer 的 partial 文件。DeveloperWorld 管理规则和可编辑状态，DeveloperPhysicsWorld/VehiclePhysics 管理刚体和轮组；历史回放保持日志驱动，不用开发者物理覆盖原始状态。

外部算法尚未连接。点云 API 属于进程内数据扩展，显示累积不能直接用作算法单时刻输入。正式资源与源码/合成示例分开，不在 Git 中存放缓存或赛事包。
