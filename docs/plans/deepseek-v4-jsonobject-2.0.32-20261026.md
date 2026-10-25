# DeepSeek V4 JSON Schema 调用接口的 JSON Object 兼容映射（SDK 2.0.32）

## 目标、事实与边界

用户明确要求在VllmDeepseekV4ChatClient内部把JSON Schema请求转换成JSON Object，让调用代码继续使用同一个VllmChatClient/ChatOptions.ResponseFormat接口。不得靠绕过RuleMaker探针或修改调用方完成兼容。优先适配当前exact模型deepseek-v4.1-flash-1017；其他已知支持Schema的V4快照不能未经验证统一降级，其他provider/client语义不变。

阿里云公开文档明确该模型JSON Object支持、JSON Schema不支持；原生Anthropic参数文档本身的type表仍写json_schema，需以最小实测补证。Root已对原Aliyun Anthropic端点发出一条经过真实WireCapture的output_config.format.type=json_object请求，HTTP200且返回预期布尔对象；正文/metadata实际SHA匹配。该条真实消耗1次；旧DeepSeek预检5次保留，总6、后续最多58，所有重试/错误/预检计数。三个本地捕获配置/路由拒绝零转发，另有零付费记录，不算真实模型请求，不能伪造通过。Sonnet五次已完成，原始数据不变。

本方案取代尚未实施的“仅放宽Schema预检门槛”候选方案。SDK2.0.31 NuGet公开下载与独立消费已通过；QRE0.25.4公开SDK验证844项通过、CI/Release成功。新SDK代码从78c5985开始，版本2.0.32；QRE仅在新SDK验证后更新为0.25.5。先尝试公开包；包验证滞后可依用户已经授权使用有指纹的本地构建包，必须声明来源。

## 实现合同

1. 只对有效模型ID为deepseek-v4.1-flash-1017的VllmDeepseekV4ChatClient生效，包含options.ModelId覆盖构造器默认ID的情况。调用方继续传ChatResponseFormat.ForJsonSchema(...)，不新增必填开关、调用代码不改。Schemaless ChatResponseFormat.Json同样正确走JSON Object。
2. OpenAI Chat Completions出站response_format精确为{type:json_object}。修正继承V3把ResponseFormat清空的行为；移除SDK自动生成的旧format/structured_outputs/schema约束，防止另一字段继续携带json_schema。其它采样、工具、思考、预算参数保留。
3. Anthropic出站output_config.format精确为{type:json_object}，不发送schema、旧output_format或OpenAI response_format。必要时在基类增加一个最小可覆写格式映射钩子，使V4在基类“无schema JSON拒绝”之前处理目标模型；非目标模型保留原有拒绝与原生Schema映射。DTO允许json_object省略Schema，正式Claude/其他provider的json_schema仍保留原样完整schema。
4. Responses模式也要正确映射text.format.type=json_object，不能从Chat覆盖后重新用原Schema生成text.format。三个模式各自按其协议发送字段，不跨协议发送字段，不自动改端点/协议。
5. 原Schema不丢失：在出站消息副本中追加明确的JSON对象输出指令和原Schema原值，提示模型字段、类型、required、enum等约束。Schema参数不能因caller随后Dispose而引用无效JsonElement。已有system文本与内容顺序保留，只增加单独的补充指令；不要改调用方messages/options、嵌套字典或原始签名块。每次调用只注入一次，流/非流共用确定性转换，不在持久历史里写入这条SDK指令。
6. 工具input_schema/parameters保持不变，不能因JSON输出降级而删除或转换工具schema；tool_use/tool_result/签名与遮蔽块回放保持。原有安全标签过滤逻辑保留。
7. JSON Object只保证JSON对象语法，不宣称服务器严格执行Schema。响应/流式更新用允许的元数据显式记录requested=json_schema、actual=json_object、providerSchemaEnforced=false（schema本身不写公开诊断），文档说明由现有业务校验器检查字段/类型。SDK不新增自动修复、重试或模型调用；不引入重型JSON Schema校验依赖。JSON无法满足明确的非object根Schema时，在HTTP前给出有类型错误，不静默包裹改变返回形状；允许对象或未限制根类型的Schema照常映射。JSON语法/业务Schema验证不得被当作服务端严格Schema保证。
8. VllmChatOptions的ThinkingEnabled等派生属性必须保留；不得用丢失派生类型属性的简单options.Clone实现降级。支持一次枚举输入和并行调用，不使用共享可变请求状态。

## 文件职责及并行顺序

必须先三方计划通过。Claude在已经/rc验证、完整权限的独立终端负责生产源文件：Deepseek/VllmDeepseekV4ChatClient.cs及一个必要的私有helper；Public/VllmBaseChatClient.cs最小钩子、Public/VllmAnthropicMessagesRequest.cs必要DTO调整。只改这些文件、只读其它code-only源码，不读环境变量/凭据/私密cohort。Claude不得执行任何构建、测试、API或网络命令，不设任务总时限。

Root负责VllmChatClient.Test新增DeepseekV41JsonObjectCompatibilityTests.cs、离线NativeAOT smoke场景、csproj版本/CHANGELOG/README及打包/CI。测试和文档源编写可与Claude生产代码编写并行，文件不重叠；测试执行必须等Claude交付、独立静态复核后，由Root独占执行。Antigravity只做计划与最终代码静态评审。任何未获批准的协议/语义扩张先回到计划，不边写边扩大。

SDK验证/发布严格先于QRE接入；QRE先更新依赖和VerifiedSdkVersion、版本断言，保持现有准确的DeepSeek JSONObjectOnly能力描述，再运行适当离线回放/单元测试和独立包消费，CI后接入新RuleMaker隔离批次。RuleMaker只更新依赖、测试环境及报告，不修改生成调用/候选判据、不全局吞掉Schema错误。旧Schema失败是历史观察，不能改写。

## Root零付费验收矩阵

- 三协议×流/非流：捕获实际序列化请求，断言确切的JSON Object字段、完全没有遗留服务端Schema字段；system中的原Schema逐值一致且有JSON关键词；工具schema逐值不变。options.ModelId覆盖匹配与非匹配、裸Json、null/text格式均覆盖。
- 现有Claude原生Schema、Sonnet signed replay/forced tool/AOT、非目标V4行为必须保持；不得把Claude裸Json也变成允许发送。
- 原messages/options（含VllmChatOptions、AdditionalProperties、工具、JsonElement）调用前后相同；同一输入重复调用不累加补充指令、并行调用不交叉污染；单次枚举输入正确。
- 对象Schema允许、显式数组/字符串根Schema零HTTP类型错误；输出元数据准确且不包含schema原文，stream原有usage/finish/content顺序与计数不受影响。
- NativeAOT关闭反射后真实编译并执行三协议JSON Object转换 smoke；仅使用fake handler/loopback，不读取模型凭据。SDK离线选用精确白名单/已知fake全集，禁止Deepseek*/Claude*广泛筛选触发真实测试。
- Root的测试进程移除provider环境变量并阻断外部代理，所有真实请求仅在专用Capture中执行且计入58次剩余额度。

## 真实接入与最终交付

离线与AOT、CI通过后，固定新SDK/QRE源码、输入、业务f32/r1版本、参数、binary/package/hash、诊断与预算新批次。先真实完整生产连接预检；结构化输出探针通过时须标注JSONObjectOnly、非严格Schema支持。若其他步骤失败或新wire出现未知字段/错误，停止保存记录；不得把故障归为模型能力。通过后固定p0-p4五次任务，不补抽或重置任何消耗。

剩余58次包括新完整预检预计6次和所有工作流调用，预检后预计52次可用；其他模型历史1–8次/任务仅为经验，五任务5–40次，不是保证。总真实转发最多64（已用6+新58），下一次本地拒绝，incomplete/budget_limit单列。保留所有原始请求/响应/取消/失败/DB，关停自有进程。

最终六模型PDF/HTML保留Sonnet已测结果、DeepSeek新五次结果、旧失败及这次兼容发现；核算真实捕获的预检/工作流/错误/缓存用量和公开单价参考，缺失费用未知而非0，币种不强制换汇。明确模型/SDK/协议版本和能力保证差异，不作为纯模型因果实验。公开导出仅指标、SHA和允许状态，不包含私有推理、签名、raw或凭据。

## v2 冻结细节及评审处理

CLA-001：Responses采用DeepSeek官方Responses API所列`text.format.type=json_object`，依据 https://api-docs.deepseek.com/api/create-response/ 的text.format章节（2026-10-26核对）。阿里云Responses文档未列json_object/text.format，仅宣称列明参数才处理；故离线证明SDK规范序列化，不宣称阿里云Responses已实测支持或一定强制JSON。本次真实业务仍使用已实测的Anthropic协议。授权Claude新增/修改`Public/VllmResponsesRequest.cs`的可省略Text DTO及必要source-generation上下文注册；仅目标V4响应格式转换使用Text并清空SDK生成的顶层ResponseFormat，其他客户端保留原DTO/序列化行为。三协议从同一个V4 ToVllmChatRequest入口作同一转换决定并只注入一次消息；Responses在派生request创建后把目标JSON Object转换成text.format，不能再用原Schema生成格式字段。测试断言只有text.format，不含顶层response_format、format、structured_outputs或服务端schema。

CLA-002：确定根Schema兼容算法，不实现完整Schema求值。无Schema直接JsonObject；JsonElement true允许；false抛NotSupportedException且零HTTP；非object且非boolean的Schema值抛ArgumentException且零HTTP。对象Schema遇以下任一条件抛NotSupportedException且零HTTP：(a)根type为字符串且不等于object；(b)根type为数组且没有字符串object；(c)根const存在且不是JSON对象；(d)根enum为数组且全部项都不是JSON对象（含空数组）。其他情况允许映射，包括缺type、type=object、type=[object,null]、anyOf/oneOf/allOf/$ref及未能静态判断的组合；明确仅排除上述已知冲突，不声称静态证明剩余Schema可满足。异常消息固定控制描述，不包含schema原文。测试逐项覆盖上述分支，明确“抛异常+实际HTTP计数=0”或“允许+一次HTTP”，避免‘零HTTP类型错误’歧义。

CLA-003：Root必须在真实预检之前更新、测试、封存**派生测试合同**，不是改生产调用方。Root新建`.codex/n10/deepseek-jsonobject-probe-contract.py`及对应纯假测试，由新cohort tools/run.py.verify_probe_sequence调用；quality/align/analyze及生产LlmConnectionProbe调用保持不变。探针仍精确6请求、5检查全部通过；首4及取消第6的HTTP/协议/思考/max_tokens/nonce/工具参数/结果判据保持原合同。第5条native格式精确output_config.format={type:json_object}，无schema、旧output_format或response_format；max_tokens=256，system区有一次原固定Schema指令，解析后逐值等于{type:object,properties:{ok:{type:boolean}},required:[ok],additionalProperties:false}，HTTP200/实体完整/协议完整，JSON对象必须含ok=true且类型boolean，不能接受字符串true或缺字段。出现旧json_schema、少Schema指令、错字段/类型/工具nonce、错误/截断、缺取消都拒绝。对SDK元数据的三个键由离线真实SDK+MEAI聚合测试证明；主机现有探针报告不直接暴露每个ChatResponse.AdditionalProperties，不能从wire伪造这项证据，真实报告必须保留QRE准确JSONObjectOnly能力说明。合同SHA/纯假通过/实际包指纹先封存，再真实预检；禁止看到新失败后临时放宽。旧Schema400失败和这次json_object预检成功分别报告，不将后者称原生严格Schema成功。

CLA-004：消息指令只在V4 ToVllmChatRequest的统一入口添加一次，位于开头的system区：原system消息内容及相对顺序保持，新的独立system补充指令置于该区末尾、首个非system消息之前（若原输入没有system，则新system置首）；不追加到工具往返或对话尾部。Anthropic/Responses再转换只消费这一份已构造消息，不再注入。单次枚举输入先物化一次，不改变caller对象。系统指令采用稳定标记`VllmDeepSeekJsonObjectSchema`加原Schema JSON文本；无schema也含JSON object指令。每个请求此SDK标记出现恰一次，原历史里有同名普通文本不作删除或去重，测试中不预先放置此保留标记。Root新探针合同从标记后的JSON解析Schema，不用仅关键词匹配。

CLA-005：三个元数据键名固定：`vllm.structured_output.requested`（json_schema或json_object）、`vllm.structured_output.actual`（json_object）、`vllm.structured_output.provider_schema_enforced`（bool false）。非流式放ChatResponse.AdditionalProperties并保留经过ReasoningChatResponse重建后仍在；流式放第一个实际emit的ChatResponseUpdate.AdditionalProperties一次，不改Contents、不新增UsageContent。极端合法空流若没有任何更新，允许只含AdditionalProperties的末尾更新，不伪造usage/finish/text；异常/不完整流仍按原错误路径。Root断言ToChatResponse聚合后键值仍在、整流中一次、usage/finish/content顺序与计数保持。新元数据不带schema原文。

CLA-006：保留所有调用方非格式扩展键，不能整体清空ExtraBody/AdditionalProperties。仅移除SDK从ResponseFormat自动生成的format/structured_outputs。对调用方显式格式扩展（能覆盖请求格式的format、response_format、structured_outputs、output_config、output_format、text）不选择静默覆盖或透传冲突；在目标模型的JSON格式适配生效时抛NotSupportedException且零HTTP，固定错误描述不含值。这不要求常规ForJsonSchema调用方改代码；只有手工覆盖保留字段者得到明确错误。测试证明其它扩展参数保留、caller字典不变，冲突键逐一被拒。此决定是对‘透传显式structured_outputs’建议的有理由替代，防止旧Schema字段重新发送并导致相同400。
