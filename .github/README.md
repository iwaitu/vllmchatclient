# GitHub Actions CI/CD 设置指南

本项目包含了完整的 GitHub Actions 工作流，用于自动化构建、测试和发布 .NET NuGet 包。

## ?? 功能特性

- ? **自动构建**: 每次推送到 main/master 分支时自动构建
- ? **自动测试**: 运行单元测试（如果存在）
- ? **自动发布**: 发布 NuGet 包到 NuGet.org
- ? **版本管理**: 使用 GitVersion 自动管理版本号
- ? **Release 管理**: 手动触发 release 创建
- ? **多目标发布**: 支持发布到 NuGet.org 和 GitHub Packages
- ? **解决方案支持**: 包含完整的 .sln 文件管理

## ?? 前置要求

### 1. 设置 NuGet API Key

1. 访问 [NuGet.org](https://www.nuget.org/) 并登录
2. 前往 **Account Settings** > **API Keys**
3. 创建新的 API Key，选择适当的权限
4. 在 GitHub 仓库中设置 Secret：
   - 前往 **Settings** > **Secrets and variables** > **Actions**
   - 点击 **New repository secret**
   - Name: `NUGET_API_KEY`
   - Value: 你的 NuGet API Key

### 2. 配置项目文件

确保你的 `.csproj` 文件包含必要的包信息：

```xml
<PropertyGroup>
  <PackageId>你的包名</PackageId>
  <Version>1.0.0</Version>
  <Authors>作者名</Authors>
  <Description>包描述</Description>
  <PackageProjectUrl>项目地址</PackageProjectUrl>
  <RepositoryUrl>仓库地址</RepositoryUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
</PropertyGroup>
```

## ?? 工作流说明

### 1. `build-and-publish.yml` - 主构建工作流

**触发条件:**
- 推送到 `main` 或 `master` 分支
- 创建 Pull Request
- 创建 Release

**功能:**
- 恢复依赖项
- 构建整个解决方案
- 运行测试项目
- 创建 NuGet 包
- 发布到 NuGet.org（仅在 main 分支或 release 时）

### 2. `auto-version.yml` - 自动版本管理

**触发条件:**
- 推送到 `main` 或 `master` 分支
- 手动触发

**功能:**
- 使用 GitVersion 6.x 自动计算版本号
- 更新项目文件中的版本
- 创建 Git 标签

**版本控制规则:**
- `+semver: major` 或 `+semver: breaking` - 主版本号 +1
- `+semver: minor` 或 `+semver: feature` - 次版本号 +1
- `+semver: patch` 或 `+semver: fix` - 补丁版本号 +1

### 3. `release.yml` - 手动发布工作流

**触发条件:**
- 手动触发（workflow_dispatch）

**功能:**
- 手动指定版本号
- 更新项目版本
- 创建 GitHub Release
- 构建并发布 NuGet 包

## ?? 使用方法

### 自动发布（推荐）

1. 在 commit 消息中包含版本控制关键词：
   ```bash
   git commit -m "feat: 添加新功能 +semver: minor"
   git push origin main
   ```

2. 系统会自动：
   - 计算新版本号
   - 构建项目
   - 创建 NuGet 包
   - 发布到 NuGet.org

### 手动发布

1. 前往 GitHub Actions 页面
2. 选择 "Release" 工作流
3. 点击 "Run workflow"
4. 输入版本号（如 `1.5.0`）
5. 选择是否为预发布版本

### 跳过自动处理

如果你不想触发自动版本更新或构建，在 commit 消息中添加：
- `[skip version]` - 跳过版本更新
- `[skip ci]` - 跳过所有 CI 流程

## ??? 分支策略

- **main/master**: 稳定分支，每次推送都会触发发布
- **feature/***: 功能分支，会构建但不发布，版本号包含 minor 增量
- **release/***: 发布分支，不增加版本号
- **hotfix/***: 热修复分支，patch 版本增量
- **develop**: 开发分支，minor 版本增量

## ?? 项目结构

```
VllmChatClient/
├── .github/
│   ├── workflows/
│   │   ├── build-and-publish.yml    # 主构建工作流
│   │   ├── auto-version.yml         # 自动版本管理
│   │   └── release.yml              # 手动发布
│   └── README.md                    # 本文档
├── Microsoft.Extensions.AI.VllmChatClient/  # 主项目
├── VllmChatClient.Test/             # 测试项目
├── GitVersion.yml                   # 版本管理配置
└── VllmChatClient.sln              # 解决方案文件
```

## ?? 注意事项

1. **API Key 安全**: 确保 NuGet API Key 保存在 GitHub Secrets 中，不要硬编码
2. **版本冲突**: 避免手动修改项目文件中的版本号，让 GitVersion 自动管理
3. **测试覆盖**: 确保有足够的单元测试覆盖关键功能
4. **包名唯一**: 确保 PackageId 在 NuGet.org 上是唯一的

## ?? 自定义配置

你可以通过修改以下文件来自定义工作流：

- `.github/workflows/build-and-publish.yml` - 主构建流程
- `.github/workflows/auto-version.yml` - 版本管理
- `.github/workflows/release.yml` - 发布流程
- `GitVersion.yml` - 版本计算规则

## ??? 本地测试

### 测试 GitVersion:
```bash
# 安装 GitVersion 工具
dotnet tool install --global GitVersion.Tool

# 查看当前版本
dotnet-gitversion
```

### 构建和测试:
```bash
# 构建解决方案
dotnet build VllmChatClient.sln

# 运行测试
dotnet test VllmChatClient.Test/VllmChatClient.Test.csproj

# 创建 NuGet 包
dotnet pack Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
```

## ?? 问题排查

### 常见问题

1. **NuGet 发布失败**: 检查 API Key 是否正确设置
2. **版本号不正确**: 检查 GitVersion.yml 配置和 commit 消息格式
3. **构建失败**: 检查项目依赖和 .NET 版本
4. **权限错误**: 确保 GITHUB_TOKEN 有足够权限
5. **GitVersion 错误**: 确保 GitVersion.yml 配置语法正确

### 查看日志

在 GitHub Actions 页面可以查看详细的构建日志，帮助诊断问题。

## ?? 最佳实践

1. **语义化提交**: 使用清晰的 commit 消息格式
2. **分支管理**: 使用合理的分支命名策略
3. **测试覆盖**: 保持高质量的单元测试
4. **版本控制**: 让 GitVersion 自动管理版本号
5. **安全性**: 妥善管理 API Keys 和敏感信息