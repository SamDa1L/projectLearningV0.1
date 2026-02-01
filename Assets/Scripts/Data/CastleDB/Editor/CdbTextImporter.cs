using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// CastleDB .cdb 文件的 ScriptedImporter
/// 让 Unity 将 .cdb 文件导入为 TextAsset，从而支持 Resources.Load<TextAsset>()
///
/// 工作原理：
/// - Unity 发现 .cdb 扩展名时，调用此 importer
/// - 读取 .cdb 文件的 JSON 文本内容
/// - 创建 TextAsset 并注册为主资产
/// - 导入后可通过 Resources.Load<TextAsset>("路径") 加载
///
/// 使用场景：
/// - CastleDB 导出的 .cdb 文件默认被 Unity 识别为 DefaultAsset
/// - 通过此 importer 让其成为 TextAsset，恢复 Resources.Load 链路
/// </summary>
[ScriptedImporter(1, "cdb")]
public class CdbTextImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        // 1. 读取源文件文本（使用 UTF-8 编码以支持中文）
        string jsonText = File.ReadAllText(ctx.assetPath, Encoding.UTF8);

        // 2. 创建 TextAsset 作为导入后的主资产
        var textAsset = new TextAsset(jsonText);
        textAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);

        // 3. 注册到 AssetImportContext
        ctx.AddObjectToAsset("text", textAsset);
        ctx.SetMainObject(textAsset);

        // 可选：添加日志便于调试（首次导入时可看到）
        // Debug.Log($"[CdbTextImporter] 已导入 .cdb 文件: {ctx.assetPath} → TextAsset");
    }
}
