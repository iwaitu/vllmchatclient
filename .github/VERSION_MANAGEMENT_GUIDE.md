# ??? �汾�������˵��

## ?? ��ǰ����

### ? �Զ��汾���������ã�
- **GitVersion**: 6.3.0
- **GitVersion.MsBuild**: �Ѽ��ɵ���Ŀ��
- **��ǰ�汾**: 1.5.0
- **�汾����**: ���� Git ��ʷ�����廯�ύ��Ϣ

## ?? ������״̬

### ? ��Ծ�Ĺ�������
1. **build-and-publish.yml** - �������ͷ�������
2. **validate-config.yml** - ������֤
3. **release.yml** - �ֶ���������

### ?? �ѽ��õĹ�������
4. **auto-version.yml** - �ѽ��ã���Ϊ�� GitVersion.MsBuild ��ͻ

## ?? �汾����ʽ

### �Զ��汾������
```sh
# �����汾 (1.5.0 �� 1.5.1)
git commit -m "fix: �޸����� +semver: patch"

# �ΰ汾 (1.5.0 �� 1.6.0) 
git commit -m "feat: �¹��� +semver: minor"

# ���汾 (1.5.0 �� 2.0.0)
git commit -m "feat!: �ش���� +semver: major"
```

### �汾������̣�
1. **�ύ����** �� GitVersion �����ύ��ʷ
2. **����ʱ** �� GitVersion.MsBuild �Զ����ð汾
3. **����ʱ** �� NuGet ��ʹ�ü����İ汾��

## ? Ϊʲô���� auto-version.yml��

### ���⣺
- ��Ŀ�ļ���û�� `<Version>` ��ǩ
- GitVersion.MsBuild �ڹ���ʱ�Զ�ע��汾��
- �ֶ�������Ŀ�ļ������Զ��汾�����ͻ

### ������Ϣ��
```
Updating version to: 
InvalidOperation: The -replace operator allows only two elements to follow it, not 3.
```

### ����ԭ��
1. GitVersion ���Ϊ�գ���Ϊ����Ҫ�ֶ�����
2. PowerShell �﷨�������޸���������Ҫ��
3. �汾������Գ�ͻ

## ?? ��β鿴��ǰ�汾

### ���ز鿴��
```sh
# ʹ�� GitVersion ����
dotnet-gitversion

# ���߹����鿴
dotnet pack --output artifacts
dir artifacts  # �鿴���ɵİ��ļ���
```

### GitHub Actions �У�
�汾�Ż��ڹ�����־����ʾ�������� NuGet ��������

## ?? �����Ҫ�ֶ��汾����

�����ȷʵ��Ҫ�ֶ�����汾��

1. **�Ƴ� GitVersion.MsBuild**��
```xml
<!-- ɾ����������� -->
<PackageReference Include="GitVersion.MsBuild" Version="6.3.0" />
```

2. **����ֶ��汾**��
```xml
<PropertyGroup>
  <Version>1.5.0</Version>
  <!-- ��������... -->
</PropertyGroup>
```

3. **�������� auto-version.yml**��
���� `force_enable` ����Ϊ `'ENABLE'`

## ? �Ƽ�����

**���ֵ�ǰ����**��ʹ�����廯�ύ��Ϣ�� GitVersion �Զ�����汾��

```sh
# ? �õ��ύ��Ϣ
git commit -m "feat: ��� GPT-OSS-120B ֧�� +semver: minor"
git commit -m "fix: �޸���ʽ��Ӧ���� +semver: patch"
git commit -m "docs: ���� README �ĵ�"  # �������Ӱ汾��

# ���ͺ��Զ������ͷ���
git push origin main
```

��������ȷ���汾�������������ͬ���������ֶ�ά���汾�Ĵ���