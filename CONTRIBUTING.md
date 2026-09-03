# 贡献指南

感谢你愿意改进 Floating Phrases。

## 开发环境

- Windows 10 或 Windows 11
- .NET 8 SDK
- Git

## 开发流程

1. Fork 仓库并从 `main` 创建主题分支。
2. 保持改动聚焦；较大的功能建议先创建 Issue 说明使用场景。
3. 不要提交 `%APPDATA%\FloatingPhrases` 中的真实数据、个人路径、凭据、构建输出或 IDE 文件。
4. 行为或数据格式发生变化时，在 `FloatingPhrases.SelfTest` 中补充回归检查。
5. 提交 Pull Request 前运行：

```powershell
dotnet build .\FloatingPhrases.csproj -c Release
dotnet run --project .\FloatingPhrases.SelfTest\FloatingPhrases.SelfTest.csproj -c Release
git diff --check
```

Pull Request 请说明问题、方案、验证结果和用户可见变化。UI 改动请附截图；涉及兼容性或数据迁移时请写明回滚方法。

## 安全问题

请不要用公开 Issue 报告漏洞，按 [SECURITY.md](SECURITY.md) 使用 GitHub 私密漏洞报告。
