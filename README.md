# projectLearningV0.1

Unity 2D 学习项目 - 0.4 版本（Monster System）

## 快速开始

### 版本信息
- Unity 版本：2022.3.14f1c1 (URP 2D)
- 当前版本：0.4（Monster System）
- 主场景：`Assets/Scenes/GamePlayScene.unity`
- 测试场景：`Assets/Scenes/NPCTestScenes/TestEnemy.unity`

### 核心功能
- ✅ CastleDB 数据驱动的敌人系统
- ✅ 玩家属性与能力系统
- ✅ 自动化 Prefab 同步工具
- ✅ 完整的导入/同步/验证工作流

## 📚 文档索引

### 新手必读
- **[Stage4-Workflow.md](Docs/Stage4-Workflow.md)** - **⭐ 标准工作流程（推荐从这里开始）**
  - CastleDB → Import → Sync → Play 完整流程
  - 常见错误与解决方案
  - 日志文件速查

### 开发者指南
- **[CLAUDE.md](CLAUDE.md)** - 项目架构与代码导航
  - 快速信息表
  - 目录结构图
  - 核心类速查
  - 问题定位指南

- **[monster-system-0.2-plan.md](Docs/monster-system-0.2-plan.md)** - 权威技术规划
  - 阶段拆解与验收标准
  - 数据链路设计
  - 架构决策记录

### 测试与验证
- **[Stage4-Testing-Checklist.md](Docs/Stage4-Testing-Checklist.md)** - 测试清单
  - 端到端测试步骤
  - 功能验证清单
  - 问题记录模板

### 工具手册
- **[castledb_basic_manual.md](Docs/castledb_basic_manual.md)** - CastleDB 基础操作

## 🚀 快速操作

### 标准工作流（4步）

```
1. 编辑 CastleDB
   → 打开 Data/CastleDbDemo/MonsterSystem.cdb
   → 修改 NPC/Player/Ability 等数据
   → 保存并复制到 Assets/Resources/Data/CastleDbDemo/

2. 导入数据
   → Unity: Tools → CastleDB → Import All

3. 同步 Prefab
   → Unity: Tools → CastleDB → Sync NPC Prefabs

4. 测试运行
   → 打开 TestEnemy.unity 或 GamePlayScene.unity
   → 点击 Play 验证
```

详细说明请查看 [Stage4-Workflow.md](Docs/Stage4-Workflow.md)

### Unity 菜单工具

| 菜单路径 | 功能 | 用途 |
|---------|------|------|
| `Tools → CastleDB → Import All` | 导入 CastleDB 数据 | 生成 Profile/PlayerConfig/AbilityCatalog |
| `Tools → CastleDB → Sync NPC Prefabs` | 同步 NPC Prefab | 自动创建检测区、添加组件 |
| `Tools → CastleDB → Revert Last Import` | 回滚导入 | 撤销上次 Import |
| `Tools → CastleDB → Revert Last Sync` | 回滚同步 | 撤销上次 Sync |
| `Tools → Stage1 → Validate Enemy Prefabs` | 验证 Prefab | 检查配置完整性 |

## 📂 关键目录

```
Assets/
├── Scenes/
│   ├── GamePlayScene.unity          # 主游戏场景
│   └── NPCTestScenes/TestEnemy.unity # 测试场景
│
├── Scripts/
│   ├── PlayerController.cs          # 玩家控制器
│   ├── Enemy/EnemyAgentBase.cs      # 敌人基类
│   └── Data/CastleDB/               # CastleDB 系统
│
├── Resources/
│   ├── Data/CastleDbDemo/MonsterSystem.cdb  # 数据源（必需）
│   ├── Profiles/                     # 敌人配置（自动生成）
│   ├── Config/                       # 玩家/能力配置（自动生成）
│   └── Prefabs/Enemy/                # 敌人 Prefab
│
└── Tests/                            # 自动化测试
    ├── EditMode/
    └── PlayMode/

Docs/                                 # 文档
├── Stage4-Workflow.md                # 工作流程（推荐阅读）
├── Stage4-Testing-Checklist.md      # 测试清单
└── monster-system-0.2-plan.md       # 技术规划

Logs/                                 # 日志输出（不在 Git 中）
├── CastleDbImport.log
├── CastleDbSync.log
└── CastleDBImport/Backups/
```

## 🎯 开发路线图

| 阶段 | 状态 | 描述 |
|------|------|------|
| Stage 0 | ✅ | 基线梳理 & CastleDB 工程搭建 |
| Stage 1 | ✅ | CastleDB 接口层（DTO/Service/日志/测试） |
| Stage 2A | ✅ | 敌人数值链路（CastleDB → Profile → Runtime） |
| Stage 2B | ✅ | 检测区/zoneBindings 一致性 |
| Stage 3A | ✅ | 玩家基础属性 & 攻击伤害链路 |
| Stage 3B | ✅ | 能力系统接口（AbilityCatalog/IPlayerAbility/调度器） |
| Stage 4 | ✅ | Prefab 同步 & 热更新 & 工作流文档 |

## ⚠️ 常见问题

### Q: 修改了 CastleDB 但游戏没变化？
A: 确保执行了完整流程：
1. 保存 `.cdb` 并复制到 `Assets/Resources/Data/CastleDbDemo/`
2. 运行 `Import All`
3. 运行 `Sync NPC Prefabs`
4. 重新 Play

### Q: Import All 失败？
A: 检查：
- `schemaVersion` 是否为 `0.4`
- Required 字段是否都填写了
- 查看 `Logs/CastleDbImport.log` 了解详情

### Q: 敌人没有攻击判定？
A: 检查：
- `zoneBindings` 是否包含 `PrimaryAttack`
- 运行 `Validate Enemy Prefabs` 查看详细问题

更多问题请查看 [Stage4-Workflow.md](Docs/Stage4-Workflow.md) 的"常见错误与解决方案"章节。

## 🤝 贡献指南

### 提交前检查
- [ ] 运行 `Import All` 和 `Sync NPC Prefabs`
- [ ] 运行 `Validate Enemy Prefabs` 确保无错误
- [ ] 在测试场景中验证
- [ ] 提交 `.cdb` 文件和生成的资产

### 不要提交
- `Logs/` 目录（临时日志和备份）
- `Library/`、`Temp/`、`obj/`、`Builds/` 等 Unity 生成文件

## 📝 许可证

本项目仅用于学习目的。

---

**最后更新**：2025-12-21（Stage 4 完成）
**Unity 版本**：2022.3.14f1c1
**项目版本**：0.4
