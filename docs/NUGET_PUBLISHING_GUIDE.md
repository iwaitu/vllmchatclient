# NuGet 自动发布指南

本指南详细说明如何通过代码提交自动发布 NuGet 包到 NuGet.org。

## 📋 目录

- [前置要求](#前置要求)
- [配置说明](#配置说明)
- [自动发布流程](#自动发布流程)
- [提交信息规范](#提交信息规范)
- [版本管理](#版本管理)
- [故障排查](#故障排查)

## 🔧 前置要求

### 1. 配置 NuGet API Key

在 GitHub 仓库中配置 NuGet API Key：

1. 访问 [NuGet.org](https://www.nuget.org/)
2. 登录后，进入 **API Keys** 页面
3. 创建新的 API Key：
   - **Key Name**: `GitHub Actions - vllmchatclient`
   - **Package Owner**: 选择你的账户
   - **Scopes**: 选择 `Push` 和 `Push New Packages and Package Versions`
   - **Glob Pattern**: `Ivilson.AI.VllmChatClient`
   - **Expiration**: 建议设置为 365 天（可按需调整）
4. 复制生成的 API Key

5. 在 GitHub 仓库中添加 Secret：
   - 进入仓库的 **Settings** → **Secrets and variables** → **Actions**
   - 点击 **New repository secret**
   - **Name**: `NUGET_API_KEY`
   - **Value**: 粘贴刚才复制的 NuGet API Key
   - 点击 **Add secret**

## ⚙️ 配置说明

### 现有配置

本仓库已经配置好以下文件，**无需修改**：

#### 1. GitHub Actions 工作流

- **`.github/workflows/build-and-publish.yml`**: 主要的构建和发布工作流
  - 在每次推送到 `main`/`master` 分支时自动构建
  - 在创建 Release 时自动发布到 NuGet.org
  - 在创建 `v*` 标签时触发构建

- **`.github/workflows/release.yml`**: 手动触发的发布工作流
  - 支持手动指定版本号
  - 自动更新 `.csproj` 文件中的版本号
  - 创建 Git 标签和 GitHub Release
  - 自动发布到 NuGet.org

#### 2. 项目配置

- **`Microsoft.Extensions.AI.VllmChatClient.csproj`**: NuGet 包配置
  ```xml
  <PackageId>Ivilson.AI.VllmChatClient</PackageId>
  <Version>1.8.0</Version>
  <Authors>iwaitu</Authors>
  <Description>.NET library for the vllm server client</Description>
  ```

#### 3. 版本管理

- **`GitVersion.yml`**: 自动版本管理配置
  - 支持语义化版本控制（Semantic Versioning）
  - 根据分支和提交信息自动递增版本号

## 🚀 自动发布流程

### 方式一：通过 GitHub Release 发布（推荐）

这是最简单、最直接的发布方式：

1. **手动触发 Release 工作流**：
   ```bash
   # 在 GitHub 网页上操作
   # 进入 Actions → Release → Run workflow
   # 输入版本号（如 1.9.0）
   # 选择是否为预发布版本
   # 点击 Run workflow
   ```

2. **工作流会自动**：
   - ✅ 更新 `.csproj` 文件中的版本号
   - ✅ 提交版本更新
   - ✅ 创建 Git 标签 `v1.9.0`
   - ✅ 构建 NuGet 包
   - ✅ 创建 GitHub Release
   - ✅ 发布到 NuGet.org

### 方式二：通过推送到主分支自动发布

1. **更新版本号**（在 `Microsoft.Extensions.AI.VllmChatClient.csproj` 中）：
   ```xml
   <Version>1.9.0</Version>
   ```

2. **提交并推送**：
   ```bash
   git add Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
   git commit -m "chore: bump version to 1.9.0"
   git push origin main
   ```

3. **GitHub Actions 会自动**：
   - ✅ 构建项目
   - ✅ 创建 NuGet 包
   - ✅ 发布到 NuGet.org

### 方式三：通过 Git 标签触发

1. **创建并推送标签**：
   ```bash
   git tag v1.9.0
   git push origin v1.9.0
   ```

2. **触发构建流程**（但不会自动发布到 NuGet）

## 📝 提交信息规范

为了支持自动化版本管理，建议使用语义化提交信息（Conventional Commits）：

### 版本递增规则

根据 `GitVersion.yml` 配置：

- **主要版本（Major）**：破坏性变更
  ```bash
  git commit -m "feat: 重构 API 接口 +semver: major"
  git commit -m "BREAKING CHANGE: 移除旧版本支持 +semver: breaking"
  ```

- **次要版本（Minor）**：新功能
  ```bash
  git commit -m "feat: 添加新的聊天模型支持 +semver: minor"
  git commit -m "feature: 支持流式函数调用 +semver: feature"
  ```

- **补丁版本（Patch）**：Bug 修复
  ```bash
  git commit -m "fix: 修复流式调用错误 +semver: patch"
  git commit -m "bugfix: 修正内存泄漏问题 +semver: fix"
  ```

### 提交类型示例

```bash
# 功能开发
git commit -m "feat: 添加 Claude 4.6 思维链支持"

# Bug 修复
git commit -m "fix: 修复 DeepSeek V3.2 思维链解析错误"

# 文档更新
git commit -m "docs: 更新 NuGet 发布指南"

# 代码重构
git commit -m "refactor: 简化基类逻辑"

# 性能优化
git commit -m "perf: 优化流式响应处理"

# 测试相关
git commit -m "test: 添加 GLM 4.7 测试用例"

# 构建/CI 相关
git commit -m "chore: 更新 GitHub Actions 工作流"

# 版本发布
git commit -m "chore: release version 1.9.0"
```

## 🔢 版本管理

### 手动管理版本号

直接编辑 `Microsoft.Extensions.AI.VllmChatClient.csproj`:

```xml
<Version>1.9.0</Version>
```

### 语义化版本格式

版本号格式：`主版本.次版本.补丁版本[-预发布标识]`

示例：
- `1.8.0` - 正式版本
- `1.9.0-beta.1` - 预发布版本
- `2.0.0` - 主要版本更新

### 版本号建议

- **主版本（Major）**：不兼容的 API 变更
- **次版本（Minor）**：向后兼容的功能新增
- **补丁版本（Patch）**：向后兼容的问题修正

当前版本：**1.8.0**

## 🔍 故障排查

### 问题 1: 推送到 NuGet 失败

**错误信息**：`Response status code does not indicate success: 403 (Forbidden)`

**解决方案**：
1. 检查 `NUGET_API_KEY` 是否正确配置
2. 验证 API Key 是否过期
3. 确认 API Key 权限包含 `Push` 和 `Push New Packages`
4. 检查包 ID 是否匹配（必须是 `Ivilson.AI.VllmChatClient`）

### 问题 2: 版本号冲突

**错误信息**：`A package with version '1.8.0' already exists`

**解决方案**：
1. 更新 `.csproj` 文件中的版本号到新版本
2. 确保每次发布使用不同的版本号
3. NuGet 不允许覆盖已发布的版本

### 问题 3: 构建失败

**解决方案**：
1. 在本地运行测试：
   ```bash
   dotnet restore
   dotnet build --configuration Release
   dotnet pack --configuration Release
   ```
2. 检查 GitHub Actions 日志中的详细错误信息
3. 确保所有依赖项都可用

### 问题 4: 工作流未触发

**检查项**：
1. 确认推送到了 `main` 或 `master` 分支
2. 检查 `.github/workflows/build-and-publish.yml` 文件是否存在
3. 查看 GitHub Actions 页面的工作流运行历史

### 问题 5: 发布成功但 NuGet.org 上看不到包

**可能原因**：
1. NuGet.org 索引更新可能需要 10-15 分钟
2. 包可能在审核中（首次发布）
3. 检查 NuGet.org 上的 "Manage Packages" 页面

## 📊 工作流执行状态

查看工作流执行状态：
1. 访问仓库的 **Actions** 页面
2. 查看最近的工作流运行记录
3. 点击具体的运行查看详细日志

## 🎯 快速开始

### 完整发布流程（使用 Release 工作流）

```bash
# 1. 确保代码是最新的
git pull origin main

# 2. 在 GitHub 网页上手动触发 Release 工作流
#    进入 Actions → Release → Run workflow
#    输入新版本号（如 1.9.0）
#    点击 Run workflow

# 3. 等待工作流完成（约 2-5 分钟）

# 4. 验证发布
#    - 检查 GitHub Releases 页面
#    - 访问 https://www.nuget.org/packages/Ivilson.AI.VllmChatClient/
#    - 确认新版本已发布
```

### 简化发布流程（推送到主分支）

```bash
# 1. 更新版本号
# 编辑 Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
# 修改 <Version>1.9.0</Version>

# 2. 提交并推送
git add Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
git commit -m "chore: bump version to 1.9.0"
git push origin main

# 3. 等待 GitHub Actions 完成构建和发布

# 4. 验证发布
# 访问 https://www.nuget.org/packages/Ivilson.AI.VllmChatClient/
```

## 📚 相关文档

- [NuGet 官方文档](https://docs.microsoft.com/zh-cn/nuget/)
- [GitHub Actions 文档](https://docs.github.com/cn/actions)
- [语义化版本控制](https://semver.org/lang/zh-CN/)
- [Conventional Commits](https://www.conventionalcommits.org/zh-hans/)

## 🆘 需要帮助？

如果遇到问题，请：
1. 查看 GitHub Actions 日志获取详细错误信息
2. 参考上述故障排查部分
3. 在 GitHub Issues 中提问

---

**最后更新**: 2026-02-11
