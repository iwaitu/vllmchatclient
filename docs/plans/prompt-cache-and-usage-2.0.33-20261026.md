# VllmChatClient 2.0.33：显式提示缓存与统一缓存计量

## 1. 目标与当前证据

用户要求由 Claude 修复 SDK 并升级版本。工作目录 D:/github/Microsoft.Extensions.AI.VllmChatClient，基线 a61aac9（2.0.32）。本次仅修改、验证和发布该 SDK；不自动升级 QRE/RuleMaker，不重跑或改写已封存的模型比较批次。

Root 已核对官方文档与实际源码：
- Claude 自动提示缓存需请求顶层 cache_control；现有原生 Anthropic 请求 DTO/构造器没有该映射。38 次捕获的 Claude 请求均无 cache_control；37 次完整用量缓存读写为零，1 次未知。已有报告的零缓存费用不是漏减。
- Sonnet 5.5 专用 UsageAccumulator 已处理 input_tokens、cache_read_input_tokens、cache_creation_input_tokens 及 5m/1h 子计量，输入总数为三者之和。
- 通用 VllmAnthropicUsage 仅 input_tokens/output_tokens，ParseVllmAnthropicUsage 丢失缓存。Chat Completions/Responses Usage DTO 同样没有 cached_tokens 细分。
- 官方 input_tokens 在 Anthropic 中不含缓存，但 OpenAI 兼容 prompt_tokens/input_tokens 通常已含缓存，不能统一相加。
- 原有 Sonnet 签名回放、DeepSeek1017 JSON Object 映射必须保持。

参考：https://platform.claude.com/docs/en/build-with-claude/prompt-caching 、https://platform.claude.com/docs/zh-CN/about-claude/pricing 。这些是设计材料，不授权评委运行网络、测试或接触凭据。

## 2. 默认行为与公共缓存配置

新增公开的强类型 VllmAnthropicCacheControl（具体文件 Public/VllmAnthropicCacheControl.cs），包含 type=ephemeral 和可选 ttl（仅 5m 或 1h）。通过现有 ChatOptions.AdditionalProperties 的专用键 anthropic.cache_control 显式传入此类型实例；值为 null 表示关闭，键缺失维持原行为。只接受该类型，拒绝任意字符串/匿名对象，文档给出可直接编译的示例。该键不与 SDK 的格式映射保留键混淆。

原生 Anthropic 请求构造时，将选项验证并复制到可省略的顶层 cache_control；不得修改调用方对象/历史/其它参数，不能附带发送专用配置键。未配置时线上请求形状不变。显式非空配置用于非 Anthropic API 模式必须在 HTTP 前清晰拒绝，不能默默丢弃；其它已有 provider 参数行为不变。兼容 Anthropic 的第三方是否支持此字段由调用方显式选择，本 SDK 不自动探测/重试/切换协议。不提供逐块断点、全局配置重构或默认开启，避免扩大范围。

类型名/属性名/配置键均固定为上述名称。公开类型不可变：构造函数接受可选 string? ttl=null，构造时仅接受 null/5m/1h；Type 为固定只读 ephemeral，Ttl 只读。示例为 new VllmAnthropicCacheControl("5m")。请求构造再次防御性验证并拷贝内部 DTO；同一实例可安全并发复用。生成器注册强类型 DTO，NativeAOT 禁用反射仍可运行。

## 3. 统一用量语义

将 Sonnet 的 Anthropic 用量合并逻辑抽取为内部共享实现，供 Sonnet 专用和通用 Anthropic 流式/非流式使用。字段 nullable long：缺失不冒充明确零；读/写计量分别保存；已返回零必须保留。Anthropic InputTokenCount=普通输入+缓存读取+缓存创建，TotalTokenCount=总输入+输出。缓存创建总量缺失而 5m/1h 子字段存在时才从子项求和；同时有总量与子项不重复加。AdditionalCounts 保留原生 input_tokens、output_tokens、cache_read_input_tokens、cache_creation_input_tokens，以及 cache_creation.ephemeral_5m_input_tokens / ephemeral_1h_input_tokens（只暴露实际已知字段，推导值与原始存在性区分）。不臆造未知 TTL 或费用。

Chat Completions 保留 prompt_tokens_details.cached_tokens；Responses 保留 input_tokens_details.cached_tokens。标准输入仍使用提供商总输入，不再次加缓存。AdditionalCounts 使用各自原生带点字段名；可增加相同文档化别名 cached_input_tokens 方便统一读取，但别名不能被加进总用量或当成另外一次消耗。文档说明跨协议读取规则及 Anthropic 普通输入不是总输入。输出 reasoning 细项若保留也只能是输出子集，不重复计入输出。

流式 usage 是单次响应的累计值快照，不能按事件累加。跨 message_start/message_delta 合并最新字段，部分更新不清零既有字段；标准 ToChatResponse 聚合后也不得翻倍。每次响应独立，工具续轮是新的响应，应跨调用正常累加。最终发出一次聚合用量；结束/取消/异常不伪造缺失字段和完整用量。保留现有 Sonnet 回放/opaque 内容及停用行为，避免为计量改动内容语义。计数验证非负且可容纳 long；非法/溢出数据不得静默产生负数或费用，清晰报出协议计量错误。没有 usage 时仍为未知，不编造零用量。

### 3.1 缺失字段的确定合同（覆盖 Sonnet 旧的无条件 ??0 行为）

Anthropic 只有出现 input_tokens 时标准 InputTokenCount 才有值；其值加上出现的缓存读取及创建总数（总数缺失时从出现的5m/1h子项求和）。缓存字段整体缺失视为不加缓存数，但 AdditionalCounts 不生成不存在的原始键。只有出现 output_tokens 时标准 OutputTokenCount 才有值；Input/Output 同时已知才生成 TotalTokenCount。只有 output 或只有 cache_read 时，Input 与 Total 均为 null；只有 input 时 Output 与 Total 为 null；显式零保留。usage 整体缺失则 Usage=null。Chat/Responses 同样保留标准输入/输出缺失的 null，若提供商 total_tokens 独立已知可原样保留，否则仅从同时已知的输入输出推导。出现合法已知字段时可以返回部分 UsageDetails，但不能标为完整账单。

这是对 Sonnet 原先缺失字段默认为0的有意纠正，对完整有效的响应数值不变。README/CHANGELOG 同时标出通用 Anthropic InputTokenCount 从普通输入改为包含缓存的总输入的行为变化，原始普通输入保留在 AdditionalCounts["input_tokens"]。

### 3.2 流式发射与异常合同

三协议每次正常完成的响应恰好发出一个 UsageChatResponseUpdate，其自定义 Usage 与 Contents 中唯一 UsageContent 指向同一份 UsageDetails；在最终 finish 更新前发出，普通内容与工具增量相对顺序不变，可暂存 finish 信息直至用量完成。Anthropic 正常终止为 message_stop；Chat Completions 为 [DONE]；Responses 为 response.completed（可兼容随后 [DONE]）；无 usage 不额外发用量更新。保留并复核现有 Sonnet 最后一个更新为 finish 的合同。

取消、解析/网络异常不补发用量；EOF 前没有正常终止标志的截断保持原先错误/结束机制，但不冒充正常完成、不得生成用量更新。已发出的内容允许保留；正常完成但缺失某些计量字段，仍可发含 null 字段的部分用量。非法计量（负数、非整数、超出 long、已知字段求和溢出）在解析/合并时抛 InvalidOperationException，固定文字 "Invalid provider usage counters."，不含原始值或响应内容，且错误后不再发用量。已有每帧用量更新必须移除，避免与新增最终更新重复。

### 3.3 兼容范围

本轮不猜测未识别的厂商缓存字段或第三方改变的 input_tokens 含义：它们不参与已识别字段的总量运算。README 提示非官方网关先用 raw usage 验证协议语义；不保证所有 Anthropic 兼容网关均遵循官方 input_tokens 不含缓存的合同。DeepSeek 顶层 prompt_cache_hit_tokens 等专属扩展不在本次支持承诺中，后续按实际接口证据增加。额外测试未识别字段不改变已知总数。

## 4. 文件和归属

Claude 独占 SDK 生产代码、离线测试、NativeAOT 示例、README、CHANGELOG、SDK csproj 版本及两份 CI 离线测试白名单。预期：
- Public/VllmAnthropicCacheControl.cs（新增）；Public/VllmAnthropicMessagesRequest.cs（可选配置）。
- Public/VllmAnthropicMessagesResponse.cs、VllmChatResponse.cs、VllmResponsesResponse.cs（缓存 DTO）。
- Public/VllmBaseChatClient.cs（缓存配置验证/映射、三协议流式非流式用量集成）；Claude/VllmClaudeSonnet55.cs（共享提取但保留语义）；内部共享用量 helper（新增）。
- Public/JsonContext.cs（sourcegen）。
- VllmChatClient.Test/PromptCacheUsageCompatibilityTests.cs（新增精确命名离线 fake 测试）；原有 Sonnet 测试需要时补充。
- samples/NativeAotSmoke/Program.cs（请求配置与非零缓存用量 smoke）。
- Microsoft.Extensions.AI.VllmChatClient.csproj（2.0.33）；README.md、CHANGELOG.md；.github/workflows/build-and-publish.yml、release.yml（仅新增精确测试类白名单）。

Root 独占计划/评审、临时验证脚本/证据、构建/测试/网络/真实提供商调用、提交推送及发布；Antigravity 只读静态审查。保留 .omc/、.committee-review/ 和 consumer-evidence.json 既有内容。Claude 不运行 dotnet、测试、CLI 代理、任何外部请求，不读取环境变量/凭据或 private cohort，不 commit/push。需要执行验证时交给 Root。

## 5. 顺序与并行

依赖顺序：三方同稿评审通过 → Claude 实现配置/共享计量和测试 → Root 审核与编译 → 修复必要反馈 → 离线/NativeAOT 通过 → 控制量真实缓存验证 → 代码复核 → bump/提交推送/CI/发布 → 公开 NuGet 独立消费者验证。

可并行：Claude 实现期间 Root 准备独立验收和安全真实调用程序（不改 Claude 文件），Antigravity 待实现完成后静态复核已冻结 diff，Root 同时运行验证。代码与依赖升级不能在 SDK 完成前交叉实施。版本号可由 Claude 先写，发布必须最后执行。

## 6. 验收

精确 fake 测试覆盖：通用 Claude/Haiku、Sonnet、DeepSeek1017 原生 Anthropic 均有非零缓存；三协议各自流式/非流式；反复累计快照、字段缺失、明确零、仅子计量/同时总计量、5m/1h、超过 int.MaxValue、无 usage、错误计量；两次独立工具轮正确累计。Chat/Responses 缓存不二次相加；MEAI 流式聚合不重复用量。配置默认完全省略、显式5m/1h、null关闭、类型/ttl/type不合法和协议不匹配均零 HTTP 拒绝；调用方对象未修改。使用真实工厂/公开 IChatClient API，不只测私有 helper。原先 Sonnet 签名/opaque 和 DeepSeek JSON Object 全回归。

Root 只跑已审阅的精确 fake 白名单（现有安全499例及新类）；绝不使用 Deepseek*、Claude* 等可能命中真实 API 测试的宽过滤器。使用去提供商环境变量且禁止外网的 safe-offline-test.py 执行（先正常 restore，然后 --no-restore）。必须执行 NativeAOT 编译后运行，并验证缓存配置确实出现在 HTTP 请求、三协议非零用量都能从 AOT SDK 读出。

真实验证由 Root 独占，单独私密目录，不使用/改写模型比较批次。优先官方 Anthropic Sonnet5.5：相同稳定前缀、显式5m、至少达到官方缓存最小长度、两次顺序调用（第一次写，第二次读）；记录 SDK32/33 来源、参数和完整原始请求/响应 hash（私密保存，不打印内容/密钥/思考）。预先确认官方当前最小缓存长度并设置小输出上限。以实际 cache_creation_input_tokens>0 和后续 cache_read_input_tokens>0 为成功标准，且 SDK 计量与原始完全一致，不用 HTTP200 代替缓存成功。若首次实验因路由/权限/策略没有命中，保留结果，不宣称成功，不无界重试；Root 可有针对性补一次成对调用，合计最多4个转发请求。此调用数是 Root 验证预算，不是 Claude 任务时间/轮数限制。付费调用不交给代理。

## 7. 发布和回滚

核对2.0.33未占用后使用该版本；Root 审核源码/测试/产物及 Antigravity 静态反馈后显式暂存本次文件、提交推送，用既有 release.yml Trusted Publishing 流程发布，不改凭据和发布机制。确认 Build 与 Release CI 成功，并等待 NuGet 公开下载 HTTP200；新目录独立消费者 restore/use 2.0.33，核对 nuspec commit/DLL 与 public 包，不能仅凭上传201宣布可用。文档明确新版本不会自动开启缓存。若公共验证暂未完成应明确状态，不静默使用本地包冒充正式包。

回滚为调用方关闭缓存选项或固定2.0.32；修复出现协议回归则不发布，已发布不可覆盖同版本。无 IA.Engine、QRE、RuleMaker 生产部署变更。

## 8. 验收边界

官方支持缓存不保证第三方网关缓存策略；必须以 raw usage 判断。新增缓存配置会改变未来实验条件，因此不得将新缓存批次混入原成本/质量表。SDK 统一计量不自动等于 QRE/UI 会展示这些字段；本轮只保证 SDK 公共 UsageDetails/AdditionalCounts 和文档，后续调用方升级另立范围。价格核算不硬编码进 SDK。
