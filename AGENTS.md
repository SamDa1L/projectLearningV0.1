# AGENTS.md — projectLearningV0.1（Unity）

> 目的：给 Codex/LLM/新同事提供**可复用的项目上下文**（结构/进度/入口/工具/测试），以便在新对话中无需从零梳理。

## 快速信息

- **Unity**：`2022.3.14f1c1`（URP 2D）
- **Packages（关键）**：`com.unity.render-pipelines.universal@14.0.9`、`com.unity.inputsystem@1.7.0`、`com.unity.cinemachine@2.9.7`、`com.cammin.ldtkunity@6.11.2`、`com.unity.test-framework@1.1.33`
- **Build Settings 场景**：`Assets/Scenes/GamePlayScene.unity`、`Assets/Scenes/QuitScene.unity`
- **测试/验证场景**：`Assets/Scenes/NPCTestScenes/TestEnemy.unity`（包含 `Player`、`KnightEnemy`、`FlyingEye`、`UIManager` 等）
- **CastleDB 数据**：`Assets/Resources/Data/CastleDbDemo/MonsterSystem.cdb`（通过自定义 importer 作为 `TextAsset` 加载）
- **导入生成 Profile**：`Assets/Resources/Profiles/Profile_<NpcId>.asset`（`EnemyTuningProfile`）

## 目录导览（从头到尾）

- `Assets/Scenes/`：主要场景
  - `GamePlayScene.unity`：主游戏场景
  - `QuitScene.unity`：退出/返回场景（用于 Build Settings）
  - `NPCTestScenes/TestEnemy.unity`：测试/验证场景（PlayMode 测试使用）
- `Assets/Scripts/`：核心代码（按模块拆分）
  - `Data/CastleDB/Runtime/`：运行时 CastleDB 读取/解析/查询
  - `Data/CastleDB/Editor/`：编辑器导入工具（菜单项、生成 Profile、写入日志/备份）
  - `Enemy/`：敌人通用架构（`EnemyAgentBase`、`EnemyTuningProfile`、迁移适配器等）
  - `Editor/`：项目内 Editor 工具（Prefab/Scene 校验、检测区迁移工具等）
  - 其余：`PlayerController.cs`、`Damageable.cs`、`DetectionZone.cs`、`Projectile*.cs`、`UIManager.cs` 等
- `Assets/Resources/`：运行时可通过 `Resources.Load` 访问的资源
  - `Data/CastleDbDemo/MonsterSystem.cdb`：CastleDB 数据（TextAsset）
  - `Profiles/`：导入生成的 `EnemyTuningProfile`
  - `Prefabs/Enemy/`：敌人 Prefab（Knight/FlyingEye/Goblin 等）
  - `Config/GameplayConfig.asset`：全局配置（调试开关等）
- `Assets/Characters/`、`Assets/Items/`、`Assets/UI/`、`Assets/Art/`、`Assets/Audio/`：美术/音频/UI/角色/道具资源
- `Assets/Tests/`：测试（EditMode/PlayMode + asmdef）
- `Docs/`：阶段文档与计划（Stage1 报告、Stage2 规划、CastleDB 使用手册等）
- `Logs/`：运行/导入/快照等日志输出目录（详见下文）

## 当前进度（以文档与代码为准）

- **阶段 1：CastleDB 接口层（已落地）**
  - 运行时：`CastleDbService` + DTO + 自定义 JSON 解析（`SimpleJsonParser`）
  - 日志：`Logs/CastleDbLoad.log`
  - 测试：`Assets/Tests/EditMode/CastleDbServiceTests.cs`、`Assets/Tests/PlayMode/CastleDbIntegrationTest.cs`
- **阶段 2A：数值链路（CastleDB → Profile → Runtime）（核心链路已实现）**
  - `EnemyTuningProfile` + `Damageable.Configure()` + `EnemyAgentBase.ApplyTuningProfile()`
  - 编辑器导入：`Tools/CastleDB/Import All` 生成/更新 `Assets/Resources/Profiles/Profile_<NpcId>.asset`
  - 新增（Monster→Player）：`knockbackToPlayer` 击退缩放系数（CastleDB → Profile → `EnemyAgentBase.ApplyKnockbackToPlayerScale()` → `Attack.knockback`）
- **阶段 2B：检测区 / zoneBindings（进行中/部分落地）**
  - `EnemyAgentBase` 以 `zoneBindings` 作为检测区唯一数据源（Plan A）
  - 校验工具：`Tools/Stage1/Validate Enemy Prefabs`
  - 迁移工具：`Tools/Detection Zone/*`
  - 已知待办：`EnemyAgentBase` 中有 `TODO`（感知半径同步到 DetectionZone 的设想）

## 核心数据链路（CastleDB → Unity）

### 1) `.cdb` 文件如何被 Unity 读取

- `Assets/Scripts/Data/CastleDB/Editor/CdbTextImporter.cs`：为 `.cdb` 提供 `ScriptedImporter`，将其导入为 `TextAsset`（UTF-8），从而支持：
  - `Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem")`

### 2) 运行时加载与查询

- `Assets/Scripts/Data/CastleDB/Runtime/CastleDbJsonSource.cs`
  - 将 `.cdb`（JSON 文本）解析为 `Dictionary<string, object>` / `List<object>`
  - 内置 `SimpleJsonParser`（绕过 `JsonUtility` 对 `object/Dictionary` 的限制）
- `Assets/Scripts/Data/CastleDB/Runtime/CastleDbService.cs`
  - `Initialize(source)`：加载、版本校验（期望 `schemaVersion == 0.2`）、缓存
  - `GetNpcById/GetAllNpcs/GetDetectionZonesByNpcId/...`：查询接口
  - 写入 `Logs/CastleDbLoad.log`

### 3) 编辑器导入（Profile 生成）

- `Assets/Scripts/Data/CastleDB/Editor/CastleDbImporter.cs`
  - 菜单：`Tools/CastleDB/Import All`
  - 数据源：`Resources/Data/CastleDbDemo/MonsterSystem`（常量 `CASTLEDB_RESOURCE_PATH`）
  - 输出：`Assets/Resources/Profiles/Profile_<NpcId>.asset`
  - 日志：`Logs/CastleDbImport.log`
  - 备份：`Logs/CastleDBImport/Backups/...`（用于回滚）

## 敌人系统（v0.2 Plan A）

- `Assets/Scripts/Enemy/EnemyAgentBase.cs`：通用敌人基类
  - 状态机骨架（Idle/Chase/Attack/Hit/Dead）
  - 组件缓存（`Rigidbody2D/Animator/Damageable` 等）
  - **`zoneBindings`**：检测区绑定列表（Plan A 的唯一数据源），并提供 `GetDetectedTargetsForRole()` 等接口
  - `ApplyTuningProfile()`：把 `EnemyTuningProfile` 数值下发到运行时缓存，并调用 `damageable.Configure(...)`
  - Monster→Player 击退：`knockbackToPlayer` 下发到 `_knockbackToPlayer` 并缩放本敌人层级下 `Attack.knockback`
- `Assets/Scripts/Enemy/EnemyTuningProfile.cs`：敌人调参 ScriptableObject
  - `ApplyFromCastleDb(NpcEntry)`：导入时全量覆盖
  - `knockbackToPlayer`：怪物命中玩家的击退缩放系数（不影响玩家→怪物的 `knockbackMultiplier` 链路）
  - `Dump Profile Snapshot`（右键菜单/ContextMenu）：写入 `Logs/NotesLog/ConfigSnapshots/`
- `Assets/Scripts/Damageable.cs`：生命/无敌帧/受击事件
  - `Configure(DamageableStats?)`：接收 Profile 下发的数值包
- `Assets/Scripts/DetectionZone.cs`：2D Trigger 检测区（事件 + `detectedColliders` 缓存）
- `Assets/Scripts/Enemy/LegacyEnemyAdapter.cs`：旧敌人脚本迁移期适配器（可按 Profile 的 `useLegacyLogicFallback` 选择旧逻辑）

## 玩家 / 道具 / UI（快速定位）

- **Player**
  - Prefab：`Assets/Characters/Player/Player.prefab`
  - 输入：`Assets/Characters/Player/PlayerInputActions.inputactions`（Input System）
  - 逻辑：`Assets/Scripts/PlayerController.cs`
- **投射物/拾取物**
  - 逻辑：`Assets/Scripts/Projectile.cs`、`Assets/Scripts/ProjectileLauncher.cs`、`Assets/Scripts/HealthPickup.cs`
  - Prefab：`Assets/Items/Projectiles/Arrow.prefab`、`Assets/Items/Pickups/HealthPickUp.prefab`
- **UI**
  - 管理脚本：`Assets/Scripts/UIManager.cs`
  - Prefab：`Assets/Manager/UIManager.prefab`
  - UI Toolkit：`Assets/UI/UIDocument/`（`*.uxml` / `*.uss`）
  - 文本 Prefab：`Assets/UI/Text/HealthText.prefab`、`Assets/UI/Text/DamageText.prefab`
- **其它常用脚本**
  - 视差：`Assets/Scripts/ParallaxEffect.cs`
  - 音乐：`Assets/Scripts/MusicPlayer.cs`

## 全局配置（GameplayConfig）

- 脚本：`Assets/Scripts/Gameplay/GameplayConfig.cs`
- 资源：`Assets/Resources/Config/GameplayConfig.asset`
- 用途：集中管理调试开关（如 `debugMode`、`debugEnemyStateOverlay` 等）和阶段说明（`changelog`）

## 常用 Editor 工具（菜单）

- **CastleDB**
  - `Tools/CastleDB/Import All`
  - `Tools/CastleDB/Open Import Logs`
  - `Tools/CastleDB/Revert Last Import`
  - `Tools/CastleDB/Open Profile Directory`
- **Stage1 校验**
  - `Tools/Stage1/Validate Enemy Prefabs`：Prefab + Scene 双层校验（关键：`EnemyAgentBase`、`tuningProfile`、`zoneBindings`）
- **Detection Zone 迁移/辅助**
  - `Tools/Detection Zone/Batch Migrate All Prefabs`
  - `Tools/Detection Zone/Create Enemy Prefab`
  - `Tools/Detection Zone/List All Zones In Selected`
  - `Tools/Detection Zone/Recommend Migration`
  - `Tools/Detection Zone/Validate Naming Convention`
- **Console Exporter**
  - `Tools/Console Exporter/Flush Logs Now`
  - `Tools/Console Exporter/Open Logs Folder`

## 测试（建议的验证入口）

- `Assets/Tests/EditMode/`
  - `CastleDbServiceTests.cs`（接口层单测）
  - `DamageableTests.cs`（数值/无敌帧/事件等单测）
- `Assets/Tests/PlayMode/`
  - `CastleDbIntegrationTest.cs`（最小链路：Resources → Service → 查询）
  - `CastleDbBridgeTests.cs`、`KnightIntegrationTests.cs`（更贴近场景/角色行为的集成测试）

## 日志/快照输出

- `Logs/CastleDbLoad.log`：CastleDbService 初始化与统计
- `Logs/CastleDbImport.log`：Import All 导入会话日志
- `Logs/CastleDBImport/Backups/`：导入前 Profile 备份（用于回滚）
- `Logs/NotesLog/ConfigSnapshots/`：`EnemyTuningProfile` 快照（ContextMenu）

## 开发约定（给人类/代理）

- **不要手工改 Profile**：`Assets/Resources/Profiles/*.asset` 应由 `Tools/CastleDB/Import All` 维护；`EnemyTuningProfile.OnValidate()` 也会提示“下次导入会覆盖”。
- **版本/里程碑更新要同步本文件**：每次项目版本/阶段完结（例如 `0.2` 完结）都要更新 `AGENTS.md` 中的“快速信息/当前进度/入口场景/核心链路/工具菜单/测试与日志”，并在 `Docs/` 补充或更新对应的完成报告与验收步骤。
- **`.cdb` 必须可被加载**：当前导入与运行时都从 `Resources/Data/CastleDbDemo/MonsterSystem` 读取；修改数据时确保 `Assets/Resources/Data/CastleDbDemo/MonsterSystem.cdb` 同步更新。
- **Editor/Runtime 分离**：带 `UnityEditor` 的代码必须放在 `Editor` 目录；Runtime 代码不要引用 `UnityEditor`。
- **定位问题的默认入口**：
  - 数据不生效：先看 `Logs/CastleDbImport.log` 与 `Logs/CastleDbLoad.log`
  - 敌人数值不对：`EnemyTuningProfile` → `EnemyAgentBase.ApplyTuningProfile()` → `Damageable.Configure()`
  - 怪物打玩家击退不对：`EnemyTuningProfile.knockbackToPlayer` → `EnemyAgentBase.ApplyKnockbackToPlayerScale()` → `Attack.knockback`
  - 检测区/攻击判定：`EnemyAgentBase.zoneBindings` + `DetectionZone.detectedColliders`
  - Prefab 配置缺失：先跑 `Tools/Stage1/Validate Enemy Prefabs`
- **PowerShell 读中文文档**：若终端乱码，用 `Get-Content -Encoding UTF8 <file>`。
