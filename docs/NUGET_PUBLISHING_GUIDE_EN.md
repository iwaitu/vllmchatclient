# NuGet Automatic Publishing Guide

This guide explains how to automatically publish NuGet packages to NuGet.org through code commits.

## 📋 Table of Contents

- [Prerequisites](#prerequisites)
- [Configuration](#configuration)
- [Automatic Publishing Workflow](#automatic-publishing-workflow)
- [Commit Message Conventions](#commit-message-conventions)
- [Version Management](#version-management)
- [Troubleshooting](#troubleshooting)

## 🔧 Prerequisites

### 1. Configure NuGet API Key

Configure the NuGet API Key in your GitHub repository:

1. Visit [NuGet.org](https://www.nuget.org/)
2. Sign in and navigate to **API Keys** page
3. Create a new API Key:
   - **Key Name**: `GitHub Actions - vllmchatclient`
   - **Package Owner**: Select your account
   - **Scopes**: Select `Push` and `Push New Packages and Package Versions`
   - **Glob Pattern**: `Ivilson.AI.VllmChatClient`
   - **Expiration**: Recommended 365 days (adjust as needed)
4. Copy the generated API Key

5. Add the Secret to your GitHub repository:
   - Go to repository **Settings** → **Secrets and variables** → **Actions**
   - Click **New repository secret**
   - **Name**: `NUGET_API_KEY`
   - **Value**: Paste the NuGet API Key you just copied
   - Click **Add secret**

## ⚙️ Configuration

### Existing Configuration

The repository is already configured with the following files, **no modifications needed**:

#### 1. GitHub Actions Workflows

- **`.github/workflows/build-and-publish.yml`**: Main build and publish workflow
  - Automatically builds on every push to `main`/`master` branch
  - Automatically publishes to NuGet.org when a Release is created
  - Triggers build when `v*` tags are created

- **`.github/workflows/release.yml`**: Manual release workflow
  - Supports manual version specification
  - Automatically updates version in `.csproj` file
  - Creates Git tag and GitHub Release
  - Automatically publishes to NuGet.org

#### 2. Project Configuration

- **`Microsoft.Extensions.AI.VllmChatClient.csproj`**: NuGet package configuration
  ```xml
  <PackageId>Ivilson.AI.VllmChatClient</PackageId>
  <Version>1.8.0</Version>
  <Authors>iwaitu</Authors>
  <Description>.NET library for the vllm server client</Description>
  ```

#### 3. Version Management

- **`GitVersion.yml`**: Automatic version management configuration
  - Supports Semantic Versioning
  - Automatically increments version based on branches and commit messages

## 🚀 Automatic Publishing Workflow

### Method 1: Publish via GitHub Release (Recommended)

This is the simplest and most direct publishing method:

1. **Manually trigger the Release workflow**:
   ```bash
   # On GitHub website
   # Go to Actions → Release → Run workflow
   # Enter version number (e.g., 1.9.0)
   # Choose whether it's a prerelease
   # Click Run workflow
   ```

2. **The workflow will automatically**:
   - ✅ Update version in `.csproj` file
   - ✅ Commit version update
   - ✅ Create Git tag `v1.9.0`
   - ✅ Build NuGet package
   - ✅ Create GitHub Release
   - ✅ Publish to NuGet.org

### Method 2: Automatic Publishing via Main Branch Push

1. **Update version number** (in `Microsoft.Extensions.AI.VllmChatClient.csproj`):
   ```xml
   <Version>1.9.0</Version>
   ```

2. **Commit and push**:
   ```bash
   git add Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
   git commit -m "chore: bump version to 1.9.0"
   git push origin main
   ```

3. **GitHub Actions will automatically**:
   - ✅ Build the project
   - ✅ Create NuGet package
   - ✅ Publish to NuGet.org

### Method 3: Trigger via Git Tags

1. **Create and push tag**:
   ```bash
   git tag v1.9.0
   git push origin v1.9.0
   ```

2. **Triggers build process** (but won't automatically publish to NuGet)

## 📝 Commit Message Conventions

To support automated version management, use Conventional Commits:

### Version Increment Rules

Based on `GitVersion.yml` configuration:

- **Major Version**: Breaking changes
  ```bash
  git commit -m "feat: refactor API interface +semver: major"
  git commit -m "BREAKING CHANGE: remove legacy support +semver: breaking"
  ```

- **Minor Version**: New features
  ```bash
  git commit -m "feat: add new chat model support +semver: minor"
  git commit -m "feature: support streaming function calls +semver: feature"
  ```

- **Patch Version**: Bug fixes
  ```bash
  git commit -m "fix: resolve streaming call error +semver: patch"
  git commit -m "bugfix: fix memory leak issue +semver: fix"
  ```

### Commit Type Examples

```bash
# Feature development
git commit -m "feat: add Claude 4.6 thinking chain support"

# Bug fixes
git commit -m "fix: resolve DeepSeek V3.2 thinking chain parsing error"

# Documentation updates
git commit -m "docs: update NuGet publishing guide"

# Code refactoring
git commit -m "refactor: simplify base class logic"

# Performance improvements
git commit -m "perf: optimize streaming response handling"

# Testing
git commit -m "test: add GLM 4.7 test cases"

# Build/CI related
git commit -m "chore: update GitHub Actions workflow"

# Version releases
git commit -m "chore: release version 1.9.0"
```

## 🔢 Version Management

### Manual Version Management

Directly edit `Microsoft.Extensions.AI.VllmChatClient.csproj`:

```xml
<Version>1.9.0</Version>
```

### Semantic Version Format

Version format: `Major.Minor.Patch[-Prerelease]`

Examples:
- `1.8.0` - Stable release
- `1.9.0-beta.1` - Prerelease version
- `2.0.0` - Major version update

### Version Number Guidelines

- **Major**: Incompatible API changes
- **Minor**: Backward-compatible feature additions
- **Patch**: Backward-compatible bug fixes

Current version: **1.8.0**

## 🔍 Troubleshooting

### Issue 1: NuGet Push Failure

**Error message**: `Response status code does not indicate success: 403 (Forbidden)`

**Solution**:
1. Verify `NUGET_API_KEY` is correctly configured
2. Check if API Key has expired
3. Ensure API Key permissions include `Push` and `Push New Packages`
4. Verify package ID matches (must be `Ivilson.AI.VllmChatClient`)

### Issue 2: Version Conflict

**Error message**: `A package with version '1.8.0' already exists`

**Solution**:
1. Update version number in `.csproj` file to a new version
2. Ensure each release uses a different version number
3. NuGet doesn't allow overwriting published versions

### Issue 3: Build Failure

**Solution**:
1. Run tests locally:
   ```bash
   dotnet restore
   dotnet build --configuration Release
   dotnet pack --configuration Release
   ```
2. Check detailed error messages in GitHub Actions logs
3. Ensure all dependencies are available

### Issue 4: Workflow Not Triggered

**Checklist**:
1. Confirm push was to `main` or `master` branch
2. Verify `.github/workflows/build-and-publish.yml` exists
3. Check workflow run history in GitHub Actions page

### Issue 5: Published Successfully but Package Not Visible on NuGet.org

**Possible causes**:
1. NuGet.org indexing may take 10-15 minutes to update
2. Package might be under review (first-time publish)
3. Check "Manage Packages" page on NuGet.org

## 📊 Workflow Execution Status

Check workflow execution status:
1. Visit repository's **Actions** page
2. View recent workflow runs
3. Click on specific runs for detailed logs

## 🎯 Quick Start

### Complete Publishing Process (Using Release Workflow)

```bash
# 1. Ensure code is up to date
git pull origin main

# 2. Manually trigger Release workflow on GitHub
#    Go to Actions → Release → Run workflow
#    Enter new version number (e.g., 1.9.0)
#    Click Run workflow

# 3. Wait for workflow completion (about 2-5 minutes)

# 4. Verify publication
#    - Check GitHub Releases page
#    - Visit https://www.nuget.org/packages/Ivilson.AI.VllmChatClient/
#    - Confirm new version is published
```

### Simplified Publishing Process (Push to Main Branch)

```bash
# 1. Update version number
# Edit Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
# Change <Version>1.9.0</Version>

# 2. Commit and push
git add Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
git commit -m "chore: bump version to 1.9.0"
git push origin main

# 3. Wait for GitHub Actions to complete build and publish

# 4. Verify publication
# Visit https://www.nuget.org/packages/Ivilson.AI.VllmChatClient/
```

## 📚 Related Documentation

- [NuGet Official Documentation](https://docs.microsoft.com/en-us/nuget/)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Semantic Versioning](https://semver.org/)
- [Conventional Commits](https://www.conventionalcommits.org/)

## 🆘 Need Help?

If you encounter issues:
1. Check GitHub Actions logs for detailed error information
2. Refer to the Troubleshooting section above
3. Ask questions in GitHub Issues

---

**Last Updated**: 2026-02-11
