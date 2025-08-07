# GitHub Actions CI/CD ����ָ��

����Ŀ������������ GitHub Actions �������������Զ������������Ժͷ��� .NET NuGet ����

## ?? ��������

- ? **�Զ�����**: ÿ�����͵� main/master ��֧ʱ�Զ�����
- ? **�Զ�����**: ���е�Ԫ���ԣ�������ڣ�
- ? **�Զ�����**: ���� NuGet ���� NuGet.org
- ? **�汾����**: ʹ�� GitVersion �Զ�����汾��
- ? **Release ����**: �ֶ����� release ����
- ? **��Ŀ�귢��**: ֧�ַ����� NuGet.org �� GitHub Packages

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
- ������Ŀ
- ���в���
- ���� NuGet ��
- ������ NuGet.org������ main ��֧�� release ʱ��

### 2. `auto-version.yml` - �Զ��汾����

**��������:**
- ���͵� `main` �� `master` ��֧
- �ֶ�����

**����:**
- ʹ�� GitVersion �Զ�����汾��
- ������Ŀ�ļ��еİ汾
- ���� Git ��ǩ

**�汾���ƹ���:**
- `+semver: major` �� `+semver: breaking` - ���汾�� +1
- `+semver: minor` �� `+semver: feature` - �ΰ汾�� +1
- `+semver: patch` �� `+semver: fix` - �����汾�� +1
- `+semver: none` �� `+semver: skip` - �����Ӱ汾��

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
- **feature/***: ���ܷ�֧���ṹ�������������汾�Ű��� `alpha` ��ʶ
- **release/***: ������֧���汾�Ű��� `beta` ��ʶ
- **hotfix/***: ���޸���֧���汾�Ű��� `beta` ��ʶ

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

## ?? �����Ų�

### ��������

1. **NuGet ����ʧ��**: ��� API Key �Ƿ���ȷ����
2. **�汾�Ų���ȷ**: ��� GitVersion.yml ����
3. **����ʧ��**: �����Ŀ������ .NET �汾
4. **Ȩ�޴���**: ȷ�� GITHUB_TOKEN ���㹻Ȩ��

### �鿴��־

�� GitHub Actions ҳ����Բ鿴��ϸ�Ĺ�����־������������⡣