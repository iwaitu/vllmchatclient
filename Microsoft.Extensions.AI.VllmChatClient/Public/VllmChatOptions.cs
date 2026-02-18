namespace Microsoft.Extensions.AI;
public class VllmChatOptions : ChatOptions
{
    public bool ThinkingEnabled { get; set; } = false;
    
    /// <summary>
    /// 启用 legacy 文本工具调用兜底解析（例如 <tool_call>...</tool_call>）。
    /// 默认关闭以遵循标准 OpenAI 兼容模式（优先使用 tool_calls 字段）。
    /// </summary>
    public bool EnableLegacyToolCallTextFallback { get; set; } = false;

    /// <summary>
    /// 是否自动从运行目录下的 skills 目录加载 skill 指令并注入到系统消息中。
    /// </summary>
    public bool EnableSkills { get; set; } = false;

    /// <summary>
    /// 自定义 skills 目录路径。为空时默认使用 {CurrentDirectory}/skills。
    /// </summary>
    public string? SkillDirectoryPath { get; set; }
}
