# ?? CI/CD ���Բ����������

## ? ���������

### ?? GitHub Actions ����ȫ������������

�����Ѿ����� GitHub Actions ������**��ȫ����**�� CI/CD ���������в��ԣ�ԭ�����£�

1. **?? API Key ����**: ���м��ɲ��Զ���Ҫ��ʵ�� API Key
2. **?? �ⲿ����**: �����������ⲿ AI ����Ŀ�����
3. **?? ��ʱ������**: ĳЩ���Կ�����Ҫ�ϳ�ʱ�����
4. **?? �ɱ�����**: ������ÿ�ι���ʱ���ø��� API

## ?? ��ǰ����������

### ?? `build-and-publish.yml` - ������������
```yaml
# ��ȫ�������в�������
- name: Skip all tests in CI/CD
  run: |
    echo "?? Skipping all tests in CI/CD environment"
    echo "?? Tests require API keys and external service connections"
    echo "?? To run tests locally:"
    echo "   1. Set environment variables for API keys"
    echo "   2. Run: dotnet test"
    echo "? Build completed successfully without test execution"
```

### ? `validate-config.yml` - ������֤������
```yaml
# �������в���
- name: Skip all tests
  run: |
    echo "?? Skipping all tests in validation workflow"
    echo "?? Tests require API keys for external AI services"
    echo "?? Validation focused on build and configuration integrity"
```

## ??? CI/CD ����

### ? �Զ�ִ�еĲ��裺
1. **?? ������**
2. **?? .NET ��������**
3. **?? NuGet ������**
4. **?? ������ָ�**
5. **??? �����������**
6. **?? NuGet ������**
7. **?? �Զ������� NuGet.org**

### ?? �����Ĳ��裺
- ? ��Ԫ��������
- ? ���ɲ�������
- ? �κ���Ҫ API Key �Ĳ���

## ?? ���ز���ָ��

### �����������У���Ҫ API Key��:

#### 1. ���û�������:
```powershell
# Windows PowerShell
$env:DASHSCOPE_API_KEY = "your-dashscope-api-key"
$env:OPENROUTER_API_KEY = "your-openrouter-api-key"
$env:DEEPSEEK_API_KEY = "your-deepseek-api-key"
$env:OPENAI_API_KEY = "your-openai-api-key"
```

```bash
# Linux/macOS
export DASHSCOPE_API_KEY="your-dashscope-api-key"
export OPENROUTER_API_KEY="your-openrouter-api-key"
export DEEPSEEK_API_KEY="your-deepseek-api-key"
export OPENAI_API_KEY="your-openai-api-key"
```

#### 2. ���в���:
```bash
# �������в��ԣ���Ҫ API Key��
dotnet test

# �����ض�������
dotnet test --filter "FullyQualifiedName~Qwen2507ChatTests"

# �����ض����Է���
dotnet test --filter "ChatTest"
```

### ����֤����:
```bash
# ֻ����������
dotnet build Microsoft.Extensions.AI.VllmChatClient.sln --configuration Release
```

## ?? ���Ը��ǵķ���

### ?? ֧�ֵ� AI ����
- **Qwen (ͨ��ǧ��)**: Qwen2507, Qwen3, QwQ ϵ��
- **GPT-OSS**: OpenRouter �ϵ� GPT-OSS ģ��  
- **DeepSeek**: DeepSeek-R1 ����ģ��
- **Gemma**: Google Gemma3 ϵ��
- **GLM**: ���� GLM-4 ϵ��

### ?? ���Թ��ܣ�
- ��������Ի�
- ��ʽ��Ӧ����
- �������ü���
- JSON ��ʽ���
- ����������
- ��ǩ��ȡ
- ���ֶԻ�

## ?? ����

### ? CI/CD �ȶ��ԣ�
- ?? **���ٹ���**: ����ȴ���ʱ��� API ����
- ?? **��ȫ��**: ����Ҫ�� GitHub Secrets �д洢��� API Key
- ?? **�ɱ�����**: ����ÿ�ι��������� API ���÷���
- ? **�ɿ���**: �����ⲿ���������Ӱ��

### ?? �������飺
- ?? **������������**: �����߿����ڱ�������ȫ�����
- ?? **�������**: ����ѡ���Ե������ض�����Ĳ���
- ?? **��ϸ����**: ���ز����ṩ�����Ĵ�����Ϣ�͵������

## ?? ��������

### ?? ������������
```mermaid
graph LR
    A[���ؿ���] --> B[���ز���<br/>��ҪAPI Key]
    B --> C[�ύ����]
    C --> D[GitHub Actions<br/>��������]
    D --> E[�����ɹ�]
    E --> F[�Զ�����<br/>NuGet��]
```

### ?? ������֤��
1. **????? �����׶�**: �����߱���������������
2. **?? ���ɽ׶�**: CI/CD רע�ڹ����ʹ��
3. **?? �����׶�**: �Զ�����������������֤�Ĵ���

## ?? �������

�����Ŀ����ӵ�У�
- ? **�ȶ��� CI/CD ����** - ������ȱ�� API Key ��ʧ��
- ? **���ٵĹ���ʱ��** - ������ʱ���ⲿ API ����
- ? **���Ĳ��Բ���** - ���ؿ��Խ�����������
- ? **�Զ�������** - �����ɹ����Զ������� NuGet

**���������רע�ڴ��뿪����CI/CD ���̽��ȶ��ɿ��ش������ͷ�����** ??