# 代码提交如何自动更新推送到 NuGet 

## 📌 问题回答

**问题**：告诉我现在的代码提交内容该怎么写，才能自动更新推送到 nuget

**答案**：您的项目已经完全配置好了自动发布到 NuGet.org 的功能！只需要完成一次性的 API Key 配置，之后每次提交代码就可以自动发布。

---

## ✅ 第一步：一次性配置（必需）

### 配置 NuGet API Key

1. 访问 https://www.nuget.org/ 并登录您的账户

2. 点击右上角用户名 → **API Keys**

3. 点击 **Create** 创建新的 API Key：
   - **Key Name**: `GitHub Actions - vllmchatclient`
   - **Package Owner**: 选择您的账户
   - **Scopes**: 勾选 `Push` 和 `Push New Packages and Package Versions`
   - **Glob Pattern**: 输入 `Ivilson.AI.VllmChatClient`
   - **Expiration**: 建议 365 天
   - 点击 **Create**

4. **重要**：立即复制显示的 API Key（只会显示一次）

5. 在 GitHub 仓库中添加 Secret：
   - 进入您的 GitHub 仓库
   - 点击 **Settings** → **Secrets and variables** → **Actions**
   - 点击 **New repository secret**
   - Name 输入：`NUGET_API_KEY`
   - Value 粘贴：刚才复制的 API Key
   - 点击 **Add secret**

✅ **完成！这是唯一需要手动配置的步骤。**

---

## 🚀 第二步：日常使用（自动发布）

配置完成后，有两种方式自动发布到 NuGet：

### 方法一：使用 GitHub Release 工作流（最简单，推荐）

1. 在 GitHub 仓库页面，点击 **Actions** 标签
2. 在左侧选择 **Release** 工作流
3. 点击右侧的 **Run workflow** 按钮
4. 在弹出的对话框中：
   - 输入新版本号（例如：`1.9.0`）
   - 选择是否为预发布版本（一般选择 `false`）
   - 点击绿色的 **Run workflow** 按钮

**自动完成的操作**：
- ✅ 自动更新项目文件中的版本号
- ✅ 自动提交版本更新
- ✅ 自动创建 Git 标签（如 `v1.9.0`）
- ✅ 自动构建 NuGet 包
- ✅ 自动创建 GitHub Release
- ✅ **自动发布到 NuGet.org**

等待 2-5 分钟，您的新版本就会出现在 NuGet.org 上！

### 方法二：通过代码提交自动发布

如果您想通过代码提交触发自动发布：

1. **更新版本号**：
   ```bash
   # 编辑文件：Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
   # 找到这一行并修改版本号：
   <Version>1.9.0</Version>
   ```

2. **提交并推送到主分支**：
   ```bash
   git add Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
   git commit -m "chore: release version 1.9.0"
   git push origin main
   ```

**自动完成的操作**：
- ✅ 自动触发 GitHub Actions
- ✅ 自动构建项目
- ✅ 自动创建 NuGet 包
- ✅ **自动发布到 NuGet.org**

---

## 📝 提交信息的写法（可选，但推荐）

为了更好地管理版本，建议使用语义化的提交信息：

### 版本发布类提交
```bash
git commit -m "chore: release version 1.9.0"
git commit -m "chore: bump version to 1.9.0"
```

### 功能开发类提交
```bash
git commit -m "feat: 添加 Claude 4.6 思维链支持"
git commit -m "feat: 支持 OpenAI GPT 系列模型"
```

### Bug 修复类提交
```bash
git commit -m "fix: 修复流式函数调用错误"
git commit -m "fix: 解决 DeepSeek V3.2 解析问题"
```

### 文档更新类提交
```bash
git commit -m "docs: 更新 API 文档"
git commit -m "docs: 添加使用示例"
```

### 代码重构类提交
```bash
git commit -m "refactor: 简化基类逻辑"
git commit -m "refactor: 优化流式处理"
```

### 自动版本递增（高级用法）

如果您想要根据提交信息自动递增版本号，可以在提交信息中添加特殊标记：

```bash
# 主要版本更新（1.x.x → 2.0.0）- 破坏性变更
git commit -m "feat: 重构 API 接口 +semver: major"

# 次要版本更新（1.8.x → 1.9.0）- 新功能
git commit -m "feat: 添加新模型支持 +semver: minor"

# 补丁版本更新（1.8.0 → 1.8.1）- Bug 修复
git commit -m "fix: 修复内存泄漏 +semver: patch"
```

---

## 🔍 验证发布是否成功

发布完成后，可以通过以下方式验证：

1. **查看 GitHub Actions 日志**：
   - 进入仓库的 **Actions** 标签
   - 查看最新的工作流运行
   - 确认所有步骤都显示绿色的 ✓

2. **查看 GitHub Releases**：
   - 进入仓库的 **Releases** 页面
   - 确认新版本已创建

3. **查看 NuGet.org**：
   - 访问：https://www.nuget.org/packages/Ivilson.AI.VllmChatClient/
   - 确认新版本已显示（可能需要等待 10-15 分钟索引更新）

4. **测试安装**：
   ```bash
   dotnet add package Ivilson.AI.VllmChatClient --version 1.9.0
   ```

---

## ❓ 常见问题

### Q1: 为什么发布失败？

**检查以下项**：
1. ✅ `NUGET_API_KEY` Secret 是否正确配置？
2. ✅ API Key 是否已过期？需要重新生成
3. ✅ 版本号是否已存在？NuGet 不允许覆盖已发布的版本
4. ✅ 网络是否正常？查看 GitHub Actions 日志

### Q2: 如何查看详细的错误信息？

1. 进入仓库的 **Actions** 标签
2. 点击失败的工作流运行
3. 展开红色的 ✗ 标记的步骤
4. 查看详细日志

### Q3: 版本号冲突怎么办？

**解决方案**：
- NuGet 不允许覆盖已发布的版本
- 必须使用新的版本号
- 建议采用递增方式：1.8.0 → 1.8.1 → 1.9.0 → 2.0.0

### Q4: API Key 在哪里生成？

访问 https://www.nuget.org/account/apikeys

### Q5: 发布成功但在 NuGet.org 上看不到？

**可能原因**：
- NuGet.org 索引更新通常需要 10-15 分钟
- 首次发布可能需要人工审核
- 刷新浏览器缓存或稍后再试

---

## 📖 详细文档

如需更详细的信息，请查看：

- 📄 [完整中文指南](NUGET_PUBLISHING_GUIDE.md) - 详细配置和故障排查
- 📄 [English Guide](NUGET_PUBLISHING_GUIDE_EN.md) - Full English documentation
- 📄 [快速参考](NUGET_QUICK_REFERENCE.md) - 快速查阅手册

---

## 🎉 总结

### 配置一次，永久使用

1. **一次性配置**：
   - ✅ 在 NuGet.org 创建 API Key
   - ✅ 在 GitHub 添加 `NUGET_API_KEY` Secret

2. **日常使用**（二选一）：
   - 🚀 **简单方式**：GitHub Actions → Release → Run workflow → 输入版本号 → 运行
   - 💻 **代码方式**：更新 `.csproj` 版本号 → 提交 → 推送到 main

3. **自动完成**：
   - ✅ 构建
   - ✅ 打包
   - ✅ 发布到 NuGet.org
   - ✅ 创建 GitHub Release

**就是这么简单！** 🎊

---

**当前包信息**：
- 包名：`Ivilson.AI.VllmChatClient`
- 当前版本：`1.8.0`
- NuGet 地址：https://www.nuget.org/packages/Ivilson.AI.VllmChatClient/

**创建日期**：2026-02-11
