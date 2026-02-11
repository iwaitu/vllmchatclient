namespace Microsoft.Extensions.AI
{
    public class Qwen3ChatOptions : VllmChatOptions
    {
        public bool NoThinking { get; set; } = false;
        //设置Top P为0.9，TopK为20
        public new float? TopP { get; set; } = 0.9f;
        public new int? TopK { get; set; } = 20;
        public new float? Temperature { get; set; } = 0.95f;
    }
}
