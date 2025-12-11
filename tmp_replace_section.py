# -*- coding: utf-8 -*-
from pathlib import Path
path = Path('Docs/monster-system-0.2-plan.md')
text = path.read_text(encoding='utf-8')
start = text.index('3. **CastleDB Demo 工程搭建**')
end = text.index('**验收标准**', start)
new = '''3. **CastleDB Demo 工程搭建**（以 Docs/castledb_basic_manual.md 为唯一操作指南）  
   1. **准备目录与 .cdb 文件**：在项目根目录创建 Data/CastleDbDemo/，CastleDB 中执行 File → New 并保存为 Data/CastleDbDemo/MonsterSystem.cdb。如 Unity 需读取，直接复制最新 .cdb 到 Assets/Data/CastleDbDemo/ 即可，无需另导出 JSON。  
   2. **创建基础 Sheet**：底部 New Sheet 依次新建 NPC、Ability、Player 等表（命名与 Unity DTO 对齐，可右键重命名/删除）。  
   3. **NPC 表列配置（全部 Required）**：  
      - id（Unique Identifier）、displayName（Text）、prefabName（Text）、nimationTrigger（Text）。其中 nimationTrigger 记录“主攻击 Trigger 名”，链路为 CastleDB → EnemyTuningProfile.animationTrigger → EnemyAgentBase 统一触发攻击动画，受击/死亡 Trigger 仍在 Animator 固定命名。  
      - 数值列：maxHealth、ttackDamage、moveSpeed、ttackRange、ttackCooldown、invincibleDuration、knockbackMultiplier（Float/Integer）。  
      - 布尔列：enableDeathAnimation、useLegacyLogicFallback（Boolean，设置合理默认值）。  
   4. **检测区配置（0.2 仅做 Role → 子节点名）**：  
      - 在底部 Edit Types 中新建 Enum DetectionZoneRole，枚举值：PrimaryAttack、SecondaryAttack、Cliff、Alert、Lookout、Custom。  
      - 回到 NPC 表点击 New Column 新增 detectionZones（Column type 选 List，保存后 CastleDB 会生成子表，如 NPC_detectionZones）。在该子表中：  
        1）通过 New Column 添加 ole 列（类型选 Enum，绑定 DetectionZoneRole，勾 Required）。  
        2）再添加 childId 列（Text，勾 Required，填写 Prefab 子物体名称，如 "sword_hitbox"）。  
      - 之后在 NPC 表内编辑某行的 detectionZones 单元格时，CastleDB 会弹出迷你表格；为 Knight 等敌人添加 PrimaryAttack + "sword_hitbox"、Cliff + "cliff_probe" 等记录。Import + Sync 工具据此校验 Prefab 是否存在对应子物体并生成/更新 zoneBindings。**注意**：0.2 不从 CastleDB 覆盖 Collider 形状/尺寸，几何参数仍在 Prefab Inspector 中维护，未来如需数据化再在该子表中扩展 shape/size/offset 等列。  
   5. **其他 Sheet**：Ability、Player 等按同样方式添加 id、描述、冷却/消耗等列，全部 Required；引用列可使用 Reference 或 Text。  
   6. **Meta Sheet（schemaVersion 来源）**：  
      - 新建 Meta 表，添加 key、alue 两列（Text + Required），并录入 key="schemaVersion"、alue="0.2"。后续若需全局 Flag 可继续新增行（如 key="featureFlags"）。  
      - Unity 端约定：定义 MetaEntry DTO，CastleDbService 在加载 .cdb 时先解析 Meta 表并读取 schemaVersion，再决定是否允许导入。  
   7. **录入示例与记录基线**：使用 New Line 在各 Sheet 中录入 Knight、FlyingEye 等样例数据，确保 Required 字段（含 nimationTrigger、detectionZones.role/childId）都有值；导入前可挂载 CastleDbJsonDump 检查 .cdb 并在 Logs/NotesLog/CodexProjectLogs/0.2基线快照.md 记录路径、约束与示例，便于复盘。  
'''
text = text[:start] + new + text[end:]
path.write_text(text, encoding='utf-8')
