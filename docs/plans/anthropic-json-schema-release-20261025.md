# Anthropic JSON Schema 适配、发布与下游验证

用户授权顺序：升级 VllmChatClient 代码/版本并测试推送；等待 CI 上传 NuGet；再升级 QRE；最后用新版 QRE 重试。当前 SDK2.0.27/QRE0.25.1；目标若未占用为2.0.28/0.25.2，发布前核对远端与NuGet。保留各仓库.omc与RuleMaker appsettings既有脏改动、原13次POST账本；不修改IA.Engine/生产服务，不做完整材料E2E。

## 修复边界
SDK的Anthropic DTO增加output_config.format，原生转换将ChatResponseFormat.ForJsonSchema中的Schema原样映射为{type:json_schema,schema:...}，不把OpenAI name/strict包装发给Anthropic，不修改原schema内容、不擅自补删约束。null/Text不发output_config；无schema的纯Json格式不能伪装严格结构化，明确拒绝并说明Anthropic要求schema。普通/流式共用映射，工具路径、thinking、其他协议行为保持原逻辑。本次不做strict tool use新功能。

测试必须从实际发出HTTP实体断言字段，而非仅检查返回JSON。覆盖用户四字段邮件schema、嵌套/required/additionalProperties/$ref保持、普通/流式、null/Text省略、schemaless Json异常、与tools共存。选择AnthropicApiModeTests现有类以进入CI离线筛选；NativeAOT smoke增加schema路径并验证生成序列化。源码版本2.0.28，README/变更说明注明能力与限制。独立审查diff后运行现有离线回归、build/pack、AOT smoke（CI必须实际成功）。

## 串行与可并行
三方同版评审通过前不实施。通过后Claude在独立已验证/rc的隐藏交互终端实施SDK生产代码+AnthropicApiModeTests（不提交/推送）；Codex可并行修改版本/说明/AOT smoke（文件不重叠）并负责验收/发布。Antigravity只读审查SDK结果；QRE升级依赖NuGet可用，不能提前拿本地SDK包冒充正式发布。

SDK本地通过后提交明确文件并推送master触发既有release.yml（用户已授权发布）；不强推、不混入.omc。等精确commit的CI/Release成功，核对v2.0.28指向该commit并从NuGet flatcontainer下载包核对nuspec/版本，证明公开可恢复，之后才改QRE。若CI失败，修复对应原因再发，不把GitHubRelease或push当NuGet成功；已发布版本不可覆写，若需要产品修复按下一补丁号发布。

QRE直接7处VllmChatClient引用同步升级已发布版本，Directory.Build.props升0.25.2；沿现有Models client factory增加原生schema普通/流式出站断言，验证CLI配置与Examples引用无残留2.0.27。Codex负责QRE代码/版本与测试，Claude可并行只读复核。QRE诊断也需同步：QreSemanticProjector.Anthropic白名单与语义投影识别output_config.format；能力矩阵VerifiedSdkVersion更新并区分schema支持与schemaless Json拒绝，不能再说协议无JSON字段；SdkTransportCapabilityTests原native Json夹具改为显式schema并新增无schema拒绝用例，保留其他模式原断言。运行相关Models/集成离线回归与正常CI，测试通过后推送main；等待既有CI->Release精确commit，核对Engine/Models NuGet和WindowsCLI版本。依赖项10.10.0如未变化不无故升级。

最后在RuleMaker独立验证环境更新到正式NuGet QRE0.25.2+SDK2.0.28（依赖覆盖有单独hash清单，业务源码仍固定f32+原r1课程资产；新编译产物不得宣称与旧29文件相同），使用同3C输入/参数/oracle新建批次，保留旧批次与无调用的原生准备目录。SDK升级后的预检应实际发output_config.format；两臂都标schema_requested，不能凭单个JSON声称已验证所有schema约束。使用已授权官方Haiku5.5；不得随用户示例切换Opus。先五项预检各6交换，成功后6对12任务AB/BA交替，实际raw->SDK->host追踪和数值oracle；上限51新POST/全局64维持，旧13不重置、不追加外层重试，API/捕获/配置错误停并标技术问题，不算模型业务失败。对照结论仍限制为不同模型+协议的系统表现，不声称纯模型归因。私有请求/响应和凭据不提交，公开报告只用允许字段/哈希。

验收：SDK NuGet公开可下载、QRE正式包/CLI可用、新消费环境解析到正确版本、普通/流式wire确有schema且同输入主业务实测结果有证据。任何阶段阻塞都保留实际状态，不宣称整个教程改造/新手可用性因此完成。

## 补充兼容与发布条件
Native schemaless Json 使用 NotSupportedException，消息含 Anthropic 与 JSON schema，且抛出前无HTTP发送；这是从静默丢弃到明确失败的bug修复，采用2.0.28补丁并在README/变更说明明确行为变化，QRE JsonObject同样受影响；不修改IA.Engine现有调用方。QRE矩阵新建JsonSchemaOutputConfigFormat转换及JsonObjectRequiresSchema限制，删除native协议不支持JSON的陈旧结论；常量、分析器验证、双语诊断文档、CHANGELOG同步。native schema/plain Text/null/无schema拒绝均从真实出站字节验证，其他provider断言保留。
真实预检前先官方只读metadata核对Haiku结构化能力并封存；若未知/不支持不贸然把400算模型质量失败。请求schema_requested不意味着全部约束已证实；refusal/max_tokens沿现有分类，API400按原始错误区分能力/不兼容等技术问题。完整官方依据https://platform.claude.com/docs/en/build-with-claude/structured-outputs#curl；schema原样透传，unsupported约束服务端返回400，文档说明不自动改写，不宣称JSON总会合规。
推送前核验版本与远端tag未占用；CI head_sha/tag目标/nuspec commit必须一致；NuGet包SHA与GitHubRelease附件比对；临时空包缓存、仅nuget.org源还原验证成功后才进入QRE阶段。恢复和RuleMaker消费均禁止本地替代包。
