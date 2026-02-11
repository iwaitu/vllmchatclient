# NuGet 自动发布快速指南 / Quick Reference

## 🎯 核心要点 / Key Points

本项目已完全配置好自动发布到 NuGet.org 的功能，**只需一次性设置 API Key，之后提交代码即可自动发布**。

This project is fully configured for automatic publishing to NuGet.org. **Just set up the API Key once, then commits will trigger automatic publishing**.

---

## ✅ 一次性配置 / One-Time Setup

### 必需步骤：配置 NuGet API Key

**中文步骤**：
1. 访问 https://www.nuget.org/ 并登录
2. 进入 API Keys 页面创建新 Key
3. 复制生成的 API Key
4. 在 GitHub 仓库中添加 Secret：
   - 路径：Settings → Secrets and variables → Actions
   - Name: `NUGET_API_KEY`
   - Value: 粘贴你的 API Key

**English Steps**:
1. Visit https://www.nuget.org/ and sign in
2. Go to API Keys page and create a new key
3. Copy the generated API Key
4. Add Secret in GitHub repository:
   - Path: Settings → Secrets and variables → Actions
   - Name: `NUGET_API_KEY`
   - Value: Paste your API Key

✅ **完成后，一切就绪！/ After this, you're all set!**

---

## 🚀 日常使用 / Daily Usage

### 方法一：通过 GitHub Release 发布（推荐）/ Method 1: Publish via GitHub Release (Recommended)

**最简单的方式 / Simplest way**:

1. 在 GitHub 网页上：**Actions** → **Release** → **Run workflow**
2. 输入新版本号（如 `1.9.0`）
3. 点击 **Run workflow**
4. ✅ 等待 2-5 分钟，自动完成发布！

**What it does automatically**:
- ✅ 更新版本号 / Updates version number
- ✅ 创建 Git 标签 / Creates Git tag
- ✅ 构建 NuGet 包 / Builds NuGet package
- ✅ 创建 GitHub Release / Creates GitHub Release
- ✅ 发布到 NuGet.org / Publishes to NuGet.org

---

### 方法二：通过代码提交自动发布 / Method 2: Auto-Publish via Code Commit

**对于日常开发 / For daily development**:

1. **更新版本号 / Update version**:
   ```bash
   # 编辑 / Edit: Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
   # 修改 / Change: <Version>1.9.0</Version>
   ```

2. **提交并推送 / Commit and push**:
   ```bash
   git add Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
   git commit -m "chore: bump version to 1.9.0"
   git push origin main
   ```

3. ✅ **自动发布！/ Auto-publishes!**

---

## 📝 提交信息建议 / Commit Message Guidelines

使用语义化提交信息可以更好地管理版本：

Using semantic commit messages helps with version management:

```bash
# 新功能 / New features
git commit -m "feat: add new chat model support"

# Bug 修复 / Bug fixes
git commit -m "fix: resolve streaming call error"

# 文档更新 / Documentation
git commit -m "docs: update API documentation"

# 版本发布 / Version release
git commit -m "chore: release version 1.9.0"
```

**版本控制提示 / Version control hints**:
- 主要更新（破坏性变更）/ Major (breaking): `+semver: major`
- 次要更新（新功能）/ Minor (new features): `+semver: minor`
- 补丁更新（修复）/ Patch (fixes): `+semver: patch`

---

## 🔍 验证发布 / Verify Publication

发布完成后 / After publishing:

1. 访问 / Visit: https://www.nuget.org/packages/Ivilson.AI.VllmChatClient/
2. 检查 GitHub Releases / Check GitHub Releases
3. 等待 10-15 分钟索引更新 / Wait 10-15 minutes for indexing

---

## 📖 详细文档 / Detailed Documentation

需要更多信息？查看完整指南：

Need more information? See full guides:

- [中文完整指南 / Chinese Full Guide](NUGET_PUBLISHING_GUIDE.md)
- [English Full Guide](NUGET_PUBLISHING_GUIDE_EN.md)

包含以下内容 / Including:
- ✅ 详细配置步骤 / Detailed configuration steps
- ✅ 故障排查 / Troubleshooting
- ✅ 高级用法 / Advanced usage
- ✅ 常见问题解答 / FAQ

---

## ⚠️ 常见问题 / Common Issues

### Q: 为什么发布失败？/ Why did publishing fail?

**A**: 检查以下项 / Check these:
1. ✅ `NUGET_API_KEY` 是否正确配置 / Is `NUGET_API_KEY` correctly configured?
2. ✅ API Key 是否过期 / Has API Key expired?
3. ✅ 版本号是否已存在 / Does version already exist?

### Q: 版本号冲突怎么办？/ What about version conflicts?

**A**: NuGet 不允许覆盖已发布的版本，必须使用新版本号。

NuGet doesn't allow overwriting published versions. Must use a new version number.

### Q: 需要等多久才能看到包？/ How long until package is visible?

**A**: 通常 10-15 分钟。首次发布可能需要审核。

Usually 10-15 minutes. First-time publishing may require review.

---

## 🎉 总结 / Summary

1. **一次性设置**：配置 `NUGET_API_KEY` Secret
2. **日常使用**：
   - 简单方式：GitHub Actions → Release → Run workflow
   - 开发方式：更新版本号 → 提交 → 推送
3. **自动完成**：构建、打包、发布全自动！

**One-time setup**: Configure `NUGET_API_KEY` Secret  
**Daily use**: Update version → Commit → Push  
**Automatic**: Build, pack, and publish automatically!

---

**当前版本 / Current Version**: 1.8.0  
**包名 / Package Name**: Ivilson.AI.VllmChatClient  
**最后更新 / Last Updated**: 2026-02-11
