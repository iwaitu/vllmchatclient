# ??? 版本管理策略说明

## ?? 当前配置

### ? 自动版本管理（已启用）
- **GitVersion**: 6.3.0
- **GitVersion.MsBuild**: 已集成到项目中
- **当前版本**: 1.5.0
- **版本计算**: 基于 Git 历史和语义化提交消息

## ?? 工作流状态

### ? 活跃的工作流：
1. **build-and-publish.yml** - 主构建和发布流程
2. **validate-config.yml** - 配置验证
3. **release.yml** - 手动发布流程

### ?? 已禁用的工作流：
4. **auto-version.yml** - 已禁用，因为与 GitVersion.MsBuild 冲突

## ?? 版本管理方式

### 自动版本升级：
```sh
# 补丁版本 (1.5.0 → 1.5.1)
git commit -m "fix: 修复问题 +semver: patch"

# 次版本 (1.5.0 → 1.6.0) 
git commit -m "feat: 新功能 +semver: minor"

# 主版本 (1.5.0 → 2.0.0)
git commit -m "feat!: 重大更改 +semver: major"
```

### 版本计算过程：
1. **提交代码** → GitVersion 分析提交历史
2. **构建时** → GitVersion.MsBuild 自动设置版本
3. **发布时** → NuGet 包使用计算后的版本号

## ? 为什么禁用 auto-version.yml？

### 问题：
- 项目文件中没有 `<Version>` 标签
- GitVersion.MsBuild 在构建时自动注入版本号
- 手动更新项目文件会与自动版本管理冲突

### 错误信息：
```
Updating version to: 
InvalidOperation: The -replace operator allows only two elements to follow it, not 3.
```

### 根本原因：
1. GitVersion 输出为空（因为不需要手动管理）
2. PowerShell 语法错误（已修复但不再需要）
3. 版本管理策略冲突

## ?? 如何查看当前版本

### 本地查看：
```sh
# 使用 GitVersion 工具
dotnet-gitversion

# 或者构建查看
dotnet pack --output artifacts
dir artifacts  # 查看生成的包文件名
```

### GitHub Actions 中：
版本号会在构建日志中显示，并用于 NuGet 包命名。

## ?? 如果需要手动版本管理

如果你确实需要手动管理版本：

1. **移除 GitVersion.MsBuild**：
```xml
<!-- 删除这个包引用 -->
<PackageReference Include="GitVersion.MsBuild" Version="6.3.0" />
```

2. **添加手动版本**：
```xml
<PropertyGroup>
  <Version>1.5.0</Version>
  <!-- 其他属性... -->
</PropertyGroup>
```

3. **重新启用 auto-version.yml**：
设置 `force_enable` 输入为 `'ENABLE'`

## ? 推荐做法

**保持当前配置**，使用语义化提交消息让 GitVersion 自动管理版本：

```sh
# ? 好的提交消息
git commit -m "feat: 添加 GPT-OSS-120B 支持 +semver: minor"
git commit -m "fix: 修复流式响应问题 +semver: patch"
git commit -m "docs: 更新 README 文档"  # 不会增加版本号

# 推送后自动构建和发布
git push origin main
```

这样可以确保版本号与代码变更保持同步，避免手动维护版本的错误。