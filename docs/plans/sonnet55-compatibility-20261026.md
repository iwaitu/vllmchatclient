# Sonnet5.5 最小原生兼容与五任务续跑

## 授权、证据与范围

用户要求六模型PDF及成本核算，Sonnet5.5跑5次；最后明确顺序“先跑sonet5.5，然后修改代码跑deepseek”。当前正式SDK2.0.30、QRE0.25.3、RuleMaker已验收发布。新批Sonnet首text请求400，明示thinking.disabled应between_tools；只消耗1次，五任务尚未开始。DeepSeek另批5次预检后因schema400停止，DeepSeek代码/任务不在本阶段修改。

官方依据：https://platform.claude.com/docs/en/models/sonnet-5-5/whats-new-sonnet-5-5 。Sonnet5.5最低思考是between_tools（无upfront thinking，仍有工具间progress thinking），只支持auto/none工具选择，thinking块须原样回传，签名绑定模型/会话/前缀。不能用改模型名、静默降级或删掉失败记录冒充兼容。

已读代码：SDK原生thinking仅enabled/disabled；native response/stream DTO无signature字段；SDK原生回放重构thinking字符串；QRE MeaiRuntimeProtocolAdapter本来已支持TextReasoningContent.ProtectedData↔RuntimeReasoningItem/Delta，通常逐item保存；RuleMaker LlmConnectionProbe.ToolsAsync强制RequireSpecific。RuntimeContext会硬截断过长ProtectedData，Sonnet签名前缀还可能因上下文变化失效，不能许诺所有压缩场景均支持。

## 实现边界

1. SDK2.0.31：精确Sonnet5.5族（规范化、明确边界，含日期版，排除5-50等）在原生Anthropic中将ThinkingEnabled=false映射为仅`thinking:{type:between_tools}`，true映射adaptive；旧Claude/DeepSeek/Chat/Responses不改变。强制工具选择不静默改Auto，文档明确不支持。API合法字段不伪造预算/强制工具语义。
2. Sonnet普通和流式的native thinking/redacted块用标准MEAI TextReasoningContent.ProtectedData携带版本化不透明块，保留type、thinking、signature及未知JSON属性；不把不透明数据当提示词/文本，不输出到公共日志。每个完成块独立携带，顺序不变，不把多个签名拼接；流式signature_delta与thinking_delta按index归属，block stop之后才发完整opaque项，保留工具ID/参数/finish/usage/取消。Sonnet路径避免旧legacy reasoning重复发成正文。原始对象/HTTP捕获不改；旧模型默认行为继续通过原回归。输入识别该明确版本的ProtectedData并还原原JSON块，非法/截断载荷明确失败，禁止当字符串发送或悄悄丢弃。
3. QRE0.25.4：复用既有标准ProtectedData链，并修复RuntimeContext的opaque硬截断，升SDK引用/矩阵与版本，新增实测SDK→Protocol→Engine历史→SDK普通/流式工具往返，确认不丢失或拼接签名及未知字段。若发现真实RequiredToolName会强制specific、ProtectedData被截断或前缀在实际工作流被改，作为阻塞，须在本方案范围内补明确host capability策略再复审；不得自动忽略强制合同、删thinking或改金样。
4. RuleMaker：只对明确Sonnet5.5的连接探针使用Auto，保留提示词要求、随机nonce、实际必须恰好一个正确工具调用、tool result回传及最终回答校验。其它模型的RequireSpecific不变。生成本身若无RequiredToolName则维持原Auto，不做全局降级。新源与旧f32比较只含必要依赖/探针兼容更改，输入、课程、生成提示/算法、预算和评分不变；无法保持的差异在PDF披露。

## 串行/并行与文件归属

- 三方同稿通过前不改生产源码。Codex先独立评审，Claude/Antigravity真实独立评审。
- 通过后Claude独占SDK Claude/VllmClaudeChatClient.cs、Public/VllmBaseChatClient.cs、Anthropic请求/响应DTO、必要JsonContext和新增Sonnet55NativeCompatibilityTests.cs；只改代码，**禁止运行任何命令形式的测试、构建、真实API或读取凭据**。Root独占版本2.0.31/CI精确白名单/README/AOT、所有测试执行、QRE链和RuleMaker探针。允许并行文件不得覆盖。Claude已/rc，持续会话不设总时限。
- 所有离线测试由Root执行精确白名单，测试子进程移除模型凭据并把默认公网HTTP代理指向未监听回环端口；fake handler/loopback仍可用。禁止Broad Deepseek/Claude过滤器再次误选联网测试。
- SDK离线+真正AOT通过→提交推送→两CI与正式NuGet发布→仅官方源空缓存、提交/DLL/发布资产和独立consumer回放验证。QRE随后更新/回归/发布/正式包与CLI验收。RuleMaker更新/探针测试/零模型生产观测→新运行时封存→剩余额度内Sonnet预检与五任务。DeepSeek适配在Sonnet任务结束后另行处理。

## 验收与账本

Sonnet精确模型普通/流式wire thinking映射；旧model边界负例；强制tool不偷偷变Auto；signed/redacted/unknown块按顺序跨多工具roundtrip，原文、签名、ID、arguments逐值不变；SDK standard reasoning经过QRE协议/历史重建仍保留ProtectedData；native schema及既有SDK2.0.30 V4清洗守卫仍通过；取消/全空/finish/usage不丢；AOT关闭反射fake往返；禁止只测构造或只测独立过滤函数。

Sonnet已消耗1次，恢复批次最多63次转发，跨版本资源账本合计最多64；旧失败请求、封存和源码不改。DeepSeek已消耗5次、另批余59次保持暂停。SDK历史误选调用未知，单独披露、不计入六模型有效样本。价格从原始请求usage逐请求核算输入/输出/缓存，引用当日官方价，网关/优惠/实际账单未知部分不伪造。

该五任务批次不等于压缩/跨模型切换签名全生命周期认证。实际运行发生签名/前缀或上下文硬截断问题必须停止并保存证据，不重采样、不临时过滤思考来掩盖；发现阻塞再做有证据修订。版本占用另选新版本，不覆盖已发布包；不替换IA.Engine/Bundler或用户现有服务、配置、DB。


## 第二版明确合同（优先于上文的条件性措辞）

- Sonnet流式展示文本通过thinking delta只出现一次；每个block stop另发TextReasoningContent(Text=空字符串而非null, ProtectedData=版本化原始完整块)。出站Sonnet thinking/redacted仅由该opaque项重建，Text-only reasoning展示副本绝不重建为thinking；不生成legacy anthropic_thinking/reasoning_content工具附加副本，按Contents原顺序保留thinking/text/tool_use。空显示文本有signature的块也保留。未知块类型明确NotSupportedException失败，不偷偷丢；未知属性随opaque原样保留。SDK及QRE多thinking/redacted/text/多tool往返逐值/顺序/数量强断言，不能有额外无签名块。
- QRE RuntimeContext先原子预验证整条待追加/恢复消息的ProtectedData大小，永不Bound/截断非空ProtectedData。若估算超MaxItemTokens，追加前以固定code opaque_reasoning_item_too_large明确失败，不修改既有history/序号、不发下个HTTP；不过限逐字符保留，仍计入token估算。Root独占RuntimeContext.cs及针对性测试。实际RuleMaker已设置窗口，MaxItemTokens=HardInput=242048，并非QRE裸默认8000；TriggerInput215833、TargetInput189619。增加超限和边界测试、Engine实际0 HTTP失败测试。不是仅运行中遇到才修。
- Sonnet native required/具名tool选择在SDK发送前抛NotSupportedException，handler0次；Auto/None正常，其它模型required/tool保持原样。QRE已有ClassifyProviderErrors把NotSupportedException映射provider_request_rejected且非重试；必须验证无retry/空流fallback/HTTP。RuleMaker探针显式Auto但不接受没工具调用或nonce错误，不静默降级generation RequiredToolName；实际任务若有required则停并报告host能力冲突。
- Sonnet false发送exact between_tools且仅type，true发送exact adaptive且仅type；非Vllm ChatOptions/null保持不发thinking，让API默认adaptive，三态wire均测。官方whats-new页已由Root网页读取核对：between_tools无其它字段，无beta要求，默认effort high可接受；不伪造此为完全无thinking。所有其它模型语义不变。
- Root离线重建历史5次完整3C轨迹，报告估算最大输入与215833触发线的差距；这只是参考而非保证Sonnet不会长循环。所有relay转发（包括SDK/引擎重试、空流补发、失败）均计入余63，触限停止不扩额。实际前缀整理或签名400失败如实停止。
- Claude只能改独占源码/测试文件，Root独占执行所有构建/测试。Root把无秘密的编译/测试错误和确切失败断言回传，Claude在原独占文件修订并每轮列SHA/差异；Root不同时改这些文件。全迭代零真实API，正式包和Root白名单通过后才恢复预算内真实请求。
