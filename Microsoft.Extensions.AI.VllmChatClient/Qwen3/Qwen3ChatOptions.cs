namespace Microsoft.Extensions.AI
{
    public class Qwen3ChatOptions : ChatOptions
    {
        public bool NoThinking { get; set; } = false;
        //设置Top P为0.9，TopK为20
        public float? TopP { get; set; } = 0.9f;
        public int? TopK { get; set; } = 20;
        public float? Temperature { get; set; } = 0.95f;
    }
}
