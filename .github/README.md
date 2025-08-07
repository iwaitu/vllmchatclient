# GitHub Actions CI/CD ����ָ��

����Ŀ������������ GitHub Actions �������������Զ������������Ժͷ��� .NET NuGet ����

## ?? ��������

- ? **�Զ�����**: ÿ�����͵� main/master ��֧ʱ�Զ�����
- ? **�Զ�����**: ���е�Ԫ���ԣ�������ڣ�
- ? **�Զ�����**: ���� NuGet ���� NuGet.org
- ? **�汾����**: ʹ�� GitVersion �Զ�����汾��
- ? **Release ����**: �ֶ����� release ����
- ? **��Ŀ�귢��**: ֧�ַ����� NuGet.org �� GitHub Packages
- ? **�������֧��**: ���������� .sln �ļ�����

## ?? ǰ��Ҫ��

### 1. ���� NuGet API Key

1. ���� [NuGet.org](https://www.nuget.org/) ����¼
2. ǰ�� **Account Settings** > **API Keys**
3. �����µ� API Key��ѡ���ʵ���Ȩ��
4. �� GitHub �ֿ������� Secret��
   - ǰ�� **Settings** > **Secrets and variables** > **Actions**
   - ��� **New repository secret**
   - Name: `NUGET_API_KEY`
   - Value: ��� NuGet API Key

### 2. ������Ŀ�ļ�

ȷ����� `.csproj` �ļ�������Ҫ�İ���Ϣ��

```xml
<PropertyGroup>
  <PackageId>��İ���</PackageId>
  <Version>1.0.0</Version>
  <Authors>������</Authors>
  <Description>������</Description>
  <PackageProjectUrl>��Ŀ��ַ</PackageProjectUrl>
  <RepositoryUrl>�ֿ��ַ</RepositoryUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
</PropertyGroup>
```

## ?? ������˵��

### 1. `build-and-publish.yml` - ������������

**��������:**
- ���͵� `main` �� `master` ��֧
- ���� Pull Request
- ���� Release

**����:**
- �ָ�������
- ���������������
- ���в�����Ŀ
- ���� NuGet ��
- ������ NuGet.org������ main ��֧�� release ʱ��

### 2. `auto-version.yml` - �Զ��汾����

**��������:**
- ���͵� `main` �� `master` ��֧
- �ֶ�����

**����:**
- ʹ�� GitVersion 6.x �Զ�����汾��
- ������Ŀ�ļ��еİ汾
- ���� Git ��ǩ

**�汾���ƹ���:**
- `+semver: major` �� `+semver: breaking` - ���汾�� +1
- `+semver: minor` �� `+semver: feature` - �ΰ汾�� +1
- `+semver: patch` �� `+semver: fix` - �����汾�� +1

### 3. `release.yml` - �ֶ�����������

**��������:**
- �ֶ�������workflow_dispatch��

**����:**
- �ֶ�ָ���汾��
- ������Ŀ�汾
- ���� GitHub Release
- ���������� NuGet ��

## ?? ʹ�÷���

### �Զ��������Ƽ���

1. �� commit ��Ϣ�а����汾���ƹؼ��ʣ�
   ```bash
   git commit -m "feat: ����¹��� +semver: minor"
   git push origin main
   ```

2. ϵͳ���Զ���
   - �����°汾��
   - ������Ŀ
   - ���� NuGet ��
   - ������ NuGet.org

### �ֶ�����

1. ǰ�� GitHub Actions ҳ��
2. ѡ�� "Release" ������
3. ��� "Run workflow"
4. ����汾�ţ��� `1.5.0`��
5. ѡ���Ƿ�ΪԤ�����汾

### �����Զ�����

����㲻�봥���Զ��汾���»򹹽����� commit ��Ϣ����ӣ�
- `[skip version]` - �����汾����
- `[skip ci]` - �������� CI ����

## ??? ��֧����

- **main/master**: �ȶ���֧��ÿ�����Ͷ��ᴥ������
- **feature/***: ���ܷ�֧���ṹ�������������汾�Ű��� minor ����
- **release/***: ������֧�������Ӱ汾��
- **hotfix/***: ���޸���֧��patch �汾����
- **develop**: ������֧��minor �汾����

## ?? ��Ŀ�ṹ

```
VllmChatClient/
������ .github/
��   ������ workflows/
��   ��   ������ build-and-publish.yml    # ������������
��   ��   ������ auto-version.yml         # �Զ��汾����
��   ��   ������ release.yml              # �ֶ�����
��   ������ README.md                    # ���ĵ�
������ Microsoft.Extensions.AI.VllmChatClient/  # ����Ŀ
������ VllmChatClient.Test/             # ������Ŀ
������ GitVersion.yml                   # �汾��������
������ VllmChatClient.sln              # ��������ļ�
```

## ?? ע������

1. **API Key ��ȫ**: ȷ�� NuGet API Key ������ GitHub Secrets �У���ҪӲ����
2. **�汾��ͻ**: �����ֶ��޸���Ŀ�ļ��еİ汾�ţ��� GitVersion �Զ�����
3. **���Ը���**: ȷ�����㹻�ĵ�Ԫ���Ը��ǹؼ�����
4. **����Ψһ**: ȷ�� PackageId �� NuGet.org ����Ψһ��

## ?? �Զ�������

�����ͨ���޸������ļ����Զ��幤������

- `.github/workflows/build-and-publish.yml` - ����������
- `.github/workflows/auto-version.yml` - �汾����
- `.github/workflows/release.yml` - ��������
- `GitVersion.yml` - �汾�������

## ??? ���ز���

### ���� GitVersion:
```bash
# ��װ GitVersion ����
dotnet tool install --global GitVersion.Tool

# �鿴��ǰ�汾
dotnet-gitversion
```

### �����Ͳ���:
```bash
# �����������
dotnet build VllmChatClient.sln

# ���в���
dotnet test VllmChatClient.Test/VllmChatClient.Test.csproj

# ���� NuGet ��
dotnet pack Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj
```

## ?? �����Ų�

### ��������

1. **NuGet ����ʧ��**: ��� API Key �Ƿ���ȷ����
2. **�汾�Ų���ȷ**: ��� GitVersion.yml ���ú� commit ��Ϣ��ʽ
3. **����ʧ��**: �����Ŀ������ .NET �汾
4. **Ȩ�޴���**: ȷ�� GITHUB_TOKEN ���㹻Ȩ��
5. **GitVersion ����**: ȷ�� GitVersion.yml �����﷨��ȷ

### �鿴��־

�� GitHub Actions ҳ����Բ鿴��ϸ�Ĺ�����־������������⡣

## ?? ���ʵ��

1. **���廯�ύ**: ʹ�������� commit ��Ϣ��ʽ
2. **��֧����**: ʹ�ú���ķ�֧��������
3. **���Ը���**: ���ָ������ĵ�Ԫ����
4. **�汾����**: �� GitVersion �Զ�����汾��
5. **��ȫ��**: ���ƹ��� API Keys ��������Ϣ