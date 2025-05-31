### It can work for qwen3, qwq32b, gemma3, glm4, glm z1, deepseek-r1 on vllm==v0.8.5.

## project on github : https://github.com/iwaitu/vllmchatclient

1. VllmQwen3ChatClient
2. VllmQwqChatClient
3. VllmGemmaChatClient 
4. VllmGlm4ChatClient 
5. VllmGlmZ1ChatClient
6. VllmDeepseekR1ChatClient

support stream function call .

for model: qwq or qwen3 vllm deployment:
```
docker run -it --gpus all -p 8000:8000 \
  -v /models/Qwen3-32B-FP8:/models/Qwen3-32B-FP8 \
  --restart always \
  -e VLLM_USE_V1=1 \
  vllm/llm-openai:v0.8.5 \
  --model /models/Qwen3-32B-FP8 \
  --enable-auto-tool-choice \
  --tool-call-parser llama3_json \
  --trust-remote-code \
  --max-model-len 131072 \
  --tensor-parallel-size 2 \
  --gpu_memory_utilization 0.8 \
  --served-model-name "qwen3"
```

for model: gemma3 vllm deployment:
```
docker run -it --gpus all -p 8000:8000 \
  -v /models/gemma-3-27b-it-FP8-Dynamic:/models/gemma-3-27b-it-FP8-Dynamic \
  -v /home/lc/work/gemma3.jinja:/home/lc/work/gemma3.jinja \
  -e TZ=Asia/Shanghai \
  -e VLLM_USE_V1=1 \
  --restart always \
  vllm/llm-openai:v0.8.2 \
  --model /models/gemma-3-27b-it-FP8-Dynamic \
  --enable-auto-tool-choice \
  --tool-call-parser pythonic \
  --chat-template /home/lc/work/gemma3.jinja \
  --trust-remote-code \
  --max-model-len 128000 \
  --tensor-parallel-size 2 \
  --gpu_memory_utilization 0.8 \
  --served-model-name "gemma3" 
```
Qwen3 model sample
```csharp
[Description("Gets the weather")]
static string GetWeather() => Random.Shared.NextDouble() > 0.1 ? "It's sunny" : "It's raining";

public async Task StreamChatFunctionCallTest()
{
    IChatClient vllmclient = new VllmQwqChatClient(apiurl,null, "qwen3");
    IChatClient client = new ChatClientBuilder(vllmclient)
        .UseFunctionInvocation()
        .Build();
    var messages = new List<ChatMessage>
    {
        new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
        new ChatMessage(ChatRole.User,"今天天气如何？")
    };
    Qwen3ChatOptions chatOptions = new()
    {
        Tools = [AIFunctionFactory.Create(GetWeather)],
        NoThinking = true  //qwen3 only
    };
    string res = string.Empty;
    await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions))
    {
        res += update;
    }
    Assert.True(res != null);
}
```

QwQ model sample
```csharp
using Microsoft.Extensions.AI

string apiurl = "http://localhost:8000/{0}/{1}";

[Description("Gets the weather")]
static string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";

IChatClient vllmclient = new VllmQwqChatClient(apiurl,null, "qwq");
ChatOptions chatOptions = new()
{
    Tools = [AIFunctionFactory.Create(GetWeather)],
    NoThink = false
};
var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System ,"你是一个智能助手，名字叫菲菲"),
    new ChatMessage(ChatRole.User,"今天天气如何？")
};

private async Task<(string answer, string reasoning)> StreamChatResponseAsync(List<ChatMessage> messages, ChatOptions chatOptions)
{
    string answer = string.Empty;
    string reasoning = string.Empty;
    await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, chatOptions))
    {
        var updateText = update.ToString();
        if (update is ReasoningChatResponseUpdate reasoningUpdate)
        {
            if (!reasoningUpdate.Thinking)
            {
                answer += updateText;
            }
            else
            {
                reasoning += updateText;
            }
        }
        else
        {
            answer += updateText;
        }
    }
    return (answer, reasoning);
}

var (answer, reasoning) = await StreamChatResponseAsync(messages, chatOptions);
```
