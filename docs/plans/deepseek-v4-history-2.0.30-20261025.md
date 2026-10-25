# DeepSeek V4 历史回放补丁 2.0.30

## 目标与已证实缺口

用户已授权验证新版SDK后更新QRE，并在DeepSeek支持不足时升级组件。当前正式2.0.29对应1afa599，正式NuGet身份及普通/流式显示过滤、工具、schema、取消等已核验；QRE接入专项发现普通Chat、普通和流式Anthropic历史回放仍含审核标签。三例已在QRE真实SDK+内存HTTP复现，交付证据为`D:/codeup/codexflow.queryruntime.engine/.committee-review/deepseek-v4-qre-delivery.md`及新专项测试。QRE831项通过包含这些known-boundary刻画，不代表接入闸门达标；QRE未发布，付费DeepSeek1017仍零调用。

原因已读源码：V4的`ToVllmChatRequest`仅处理CLR string，非流式raw `reasoning_content`通常是JsonElement string；base的原生转换优先使用`AnthropicContentBlocks`，绕开该reasoning字段。原生raw replay blocks还可能把已清理显示文本对应的旧text再次发送。不得改脚本改协议来规避。

## 最小实现与不变量

只在`Deepseek/VllmDeepseekV4ChatClient.cs`增加V4出站处理，不改V3或通用base、JSON Schema行为、采样和计量。Chat请求的assistant reasoning允许CLR string和JsonElement string，取字符串后同一过滤器处理；未知JSON类型保持原值，不把任意object.ToString当文本。

原生路径override `ToVllmAnthropicMessagesRequest`，在base生成的新请求DTO上处理assistant content数组：只清理明确`type=text`的`text`及未签名`type=thinking`的`thinking`字符串。用Utf8JsonWriter按属性复制，只替换目标字段，再Parse/Clone；保持未知属性和其JSON值、顺序、所有工具name/id/input、tool_result、user/system/图片/其它块。禁止全body替换，禁止递归扫描工具参数。不修改原输入ChatMessage/AdditionalProperties/RawRepresentation、raw DTO或私有HTTP捕获。

含非空signature的thinking块作为不透明签名对象原样保留；redacted_thinking和未知块原样保留，不宣称清理签名思考。SDK base已有签名映射边界不在本补丁扩展。测试须明确本次只保证传入opaque块不被V4修改，不能声称修复整个Anthropic签名功能。若发现新类型或未知布局，保持原样并在验收记录，不粗暴清洗。

不扩展SDK现有message_start输入usage映射、不改变thinking开关、工具调度或流式finish语义。保留2.0.29的分通道跨chunk状态机和函数参数不洗；不为本问题添加全provider过滤变换。多message/额外元数据若发现确切丢失不得扩大损失；不能为cleaning替换丢掉其它消息。

## 串行与并行归属

1. Codex主持独立评审后，真实Claude和Antigravity同稿评审；三方通过前不改生产源码。
2. 通过后Claude独占V4客户端及新增`VllmChatClient.Test/DeepseekV4HistoryReplayTests.cs`，修复+离线HTTP回归；Root并行独占csproj版本2.0.30、README/CHANGELOG、两条CI离线过滤清单增加新类。双方不覆盖文件，不动QRE已写源码、不额外付费调用；Claude会话已/rc连通，无总时限。Antigravity只读复核报告。
3. Root集成源码、正式离线测试集合、NativeAotSmoke；提交推送SDK后等待两条CI和正式NuGet发布成功。仅官方源空缓存恢复2.0.30，对发布包重跑独立真实IChatClient四路径历史回放以及2.0.29已有检查，核验commit/DLL/发布资产/来源。不用本地pack冒充NuGet，不重复推已占版本。
4. 2.0.30正式包所有闸门通过后，Root将待发布QRE0.25.3的7处引用和矩阵版本改2.0.30；把QRE三例known-boundary改为确切清理正例，保留V3阴性对照与四路径、原文、工具不变的硬断言。全量unit/SkillsWorkflow通过后再发QRE，等待正式包及CLI消费；随后RuleMaker升级及新冻结DeepSeek1017批次。旧封存不改，新SDK/QRE版本差异在五模型表明示。

## 离线验收

- 普通Chat、流式Chat、普通Anthropic、流式Anthropic：至少一次带ds_safety的响应→SDK显示清理→response.Messages不经宿主修改回传→捕获下次请求，未签名reasoning和原生assistant text无审核块，文字保留精准，调用方原始表示仍保留原文。
- Chat string和JsonElement string两种history来源；其它JSON类型不转义为错误文本。
- 工具输入恰含标签样式字符串仍逐值不变；工具name/id与tool-result关联不变，user/system/其它块保持。同fixture V3历史保留原标签证明默认族不变。
- 无标签history保持相同语义；未知属性不丢；显式opaque signed/redacted块保持不变；不因过滤全空丢finish/usage/工具；跨chunk和取消既有测试继续通过。
- 原生schema/thinking与Chat历史出站格式不变；不声称提供商强制schema。NativeAOT禁用反射默认值下JSON处理可运行；必要时在已有AOT smoke用fake transport增加V4路径。
- 所有真实提供商POST仍在独立新批次64上限内（历史已101次），本补丁测试阶段0付费请求；不追加重采样、不替换失败样本、不把纯模型对照与依赖改动混称。

## 风险与回滚

严格限制V4新请求DTO上的已知字段，保持原始实体可追溯。任何真实SDK契约丢失或AOT失败都阻止SDK/QRE发布，不以known-boundary通过冒充达标。版本已占用另选新版本而不覆盖；尚未发布的QRE只保持本地修改。回滚消费者引用至SDK2.0.28/QRE0.25.2可恢复既有运行时，旧服务和数据不替换。签名思考等明确例外须在文档披露，不把全面过滤作为未验证承诺。
