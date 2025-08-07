# GitHub Actions CI/CD ����ָ��

����Ŀ������������ GitHub Actions �������������Զ������������Ժͷ��� .NET NuGet ����

## ?? ���ٿ�ʼ

### ? ��Ŀ����ȫ������ɣ�

- **��ǰ�汾**: 1.5.0
- **����״̬**: ? ����
- **CI/CD ״̬**: ? ������
- **�汾����**: ? GitVersion �Զ���

### ?? ����ʹ��

1. **���� NuGet API Key**��
   ```
   GitHub �ֿ� �� Settings �� Secrets �� Actions �� New repository secret
   Name: NUGET_API_KEY
   Value: ��� NuGet.org API Key
   ```

2. **��ʼ�Զ�������**��
   ```bash
   # �¹���
   git commit -m "feat: ����¹��� +semver: minor"
   git push origin main
   # �� �Զ������汾 1.6.0
   
   # Bug�޸�  
   git commit -m "fix: �޸����� +semver: patch"
   git push origin main
   # �� �Զ������汾 1.5.1
   ```

3. **�鿴�������**��
   - ǰ�� GitHub Actions ҳ��鿴����״̬
   - NuGet.org �鿴�����İ�

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

### 2. ��Ŀ����˵��

��Ŀ������Ϊʹ�� GitVersion �Զ�����汾�ţ�

```xml
<PropertyGroup>
  <PackageId>Ivilson.AI.VllmChatClient</PackageId>
  <!-- Version �� GitVersion �Զ����������ֶ����� -->
  <Authors>iwaitu</Authors>
  <Description>.NET library for the vllm server client</Description>
  <!-- ��������Ϣ... -->
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
- ��������������� (`Microsoft.Extensions.AI.VllmChatClient.sln`)
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

### 4. `validate-config.yml` - ������֤������

**��������:**
- �ֶ�����
- �������ļ����ʱ

**����:**
- ��֤ GitVersion ����
- ���԰汾����
- ��֤��������ļ�
- ��֤��������

## ?? ʹ�÷���

### �Զ��������Ƽ���

1. �� commit ��Ϣ�а����汾���ƹؼ��ʣ�
   ```bash
   # С�汾���£��¹��ܣ�
   git commit -m "feat: ����¹��� +semver: minor"
   git push origin main
   
   # �������£�bug�޸���
   git commit -m "fix: �޸����� +semver: patch"
   git push origin main
   
   # ���汾���£��ƻ��Ը��ģ�
   git commit -m "feat!: �ش���� +semver: major"
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
4. ����汾�ţ��� `1.6.0`��
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
��   ��   ������ validate-config.yml      # ������֤
��   ������ README.md                    # ���ĵ�
��   ������ PROJECT_COMPLETION_REPORT.md # ��Ŀ��ɱ���
������ Microsoft.Extensions.AI.VllmChatClient/  # ����Ŀ
������ VllmChatClient.Test/             # ������Ŀ
������ GitVersion.yml                   # �汾��������
������ Microsoft.Extensions.AI.VllmChatClient.sln  # ��������ļ�
```

## ?? �汾��ʷ׷��

### ��ǰ�汾״̬:
- **��һ�������汾**: 1.4.8 (�ѷ����� NuGet)
- **��ǰ�����汾**: 1.5.0 (�� GitVersion �Զ�����)
- **�汾Դ**: Git ��ǩ `v1.4.8`

### �汾����ʾ��:
```bash
# �� 1.5.0 ��ʼ�İ汾����·��
1.5.0 �� 1.5.1 (patch: bug�޸�)
1.5.0 �� 1.6.0 (minor: �¹���)
1.5.0 �� 2.0.0 (major: �ƻ��Ը���)
```

## ?? ע������

1. **API Key ��ȫ**: ȷ�� NuGet API Key ������ GitHub Secrets �У���ҪӲ����
2. **�汾����**: ���ڰ汾��ȫ�� GitVersion ������Ҫ�ֶ��޸���Ŀ�ļ��еİ汾��
3. **���Ը���**: ȷ�����㹻�ĵ�Ԫ���Ը��ǹؼ�����
4. **����Ψһ**: ȷ�� PackageId �� NuGet.org ����Ψһ��
5. **Git ��ǩ**: ��Ҫ�����汾Ӧ�ô��� Git ��ǩ�Ա�汾׷��

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

# �鿴�汾��ϸ��Ϣ
dotnet-gitversion /showvariable FullSemVer
```

### �����Ͳ���:
```bash
# �����������
dotnet build Microsoft.Extensions.AI.VllmChatClient.sln

# ���в���
dotnet test VllmChatClient.Test/VllmChatClient.Test.csproj

# ���� NuGet ��
dotnet pack Microsoft.Extensions.AI.VllmChatClient/Microsoft.Extensions.AI.VllmChatClient.csproj --output artifacts
```

## ?? �����Ų�

### ��������

1. **NuGet ����ʧ��**: ��� API Key �Ƿ���ȷ����
2. **�汾�Ų���ȷ**: ��� GitVersion.yml ���ú� commit ��Ϣ��ʽ
3. **MSB1011 ����**: ȷ��ʹ����ȷ�Ľ�������ļ�
4. **����ʧ��**: �����Ŀ������ .NET �汾
5. **Ȩ�޴���**: ȷ�� GITHUB_TOKEN ���㹻Ȩ��
6. **GitVersion ����**: ȷ�� GitVersion.yml �����﷨��ȷ

### �汾��ͻ�������

��������汾��ͻ�����ԣ�
1. �����ʵ��� Git ��ǩ��`git tag v1.x.x`
2. ���ͱ�ǩ��Զ�̣�`git push origin --tags`
3. �������� GitVersion��`dotnet-gitversion`

### �鿴��־

�� GitHub Actions ҳ����Բ鿴��ϸ�Ĺ�����־������������⡣

## ?? ���ʵ��

1. **���廯�ύ**: ʹ�������� commit ��Ϣ��ʽ
2. **��֧����**: ʹ�ú���ķ�֧��������
3. **���Ը���**: ���ָ������ĵ�Ԫ����
4. **�汾����**: �� GitVersion �Զ�����汾��
5. **��ȫ��**: ���ƹ��� API Keys ��������Ϣ
6. **��ǩ����**: ��Ҫ�������� Git ��ǩ
7. **�ĵ�����**: ��ʱ���°汾����ĵ�

## ?? ��Ŀ״̬

### ? ��������ã�
- [x] GitHub Actions ����������
- [x] GitVersion �汾����
- [x] �Զ������ͷ���
- [x] ��������ļ�����
- [x] ������Ŀ����
- [x] ���������Ż�

### ?? �������ã�
�����Ŀ���ھ߱���**��ҵ���� CI/CD ����**��ÿ�δ������Ͷ����Զ�����汾�������������Ժͷ������̡�

**׼����ʼ�Զ�������֮������** ??

## ?? ������Ϣ

- ?? �鿴 [��Ŀ��ɱ���](.github/PROJECT_COMPLETION_REPORT.md) �˽���ϸ������Ϣ
- ?? ���� [GitHub Actions ҳ��](../../actions) �鿴����״̬  
- ?? �� [NuGet.org](https://www.nuget.org/packages/Ivilson.AI.VllmChatClient) �鿴�����İ�