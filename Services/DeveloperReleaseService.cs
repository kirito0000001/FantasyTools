using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FantasyTools.Models;

namespace FantasyTools.Services;

internal sealed class DeveloperReleaseService
{
    private const string DeveloperRootEnvironmentName = "FANTASYTOOLS_DEVELOPER_ROOT";
    private const string GiteeTokenEnvironmentName = "FANTASYTOOLS_GITEE_TOKEN";
    private const string DefaultOutputRoot = @"D:\DabaoV";

    public Task<DeveloperReleaseEnvironment> InspectAsync()
    {
        return Task.Run(Inspect);
    }

    public DeveloperReleaseEnvironment Inspect()
    {
        var projectRoot = FindProjectRoot();
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return new DeveloperReleaseEnvironment(
                false,
                string.Empty,
                string.Empty,
                FindPowerShell(),
                false,
                false,
                false,
                DefaultOutputRoot,
                string.Empty);
        }

        var currentVersion = ReadProjectVersion(projectRoot);
        var githubReady = IsCommandSuccessful("gh.exe", ["auth", "token"], TimeSpan.FromSeconds(8));
        var giteeReady = !string.IsNullOrWhiteSpace(GetEnvironmentValue(GiteeTokenEnvironmentName)) ||
            !string.IsNullOrWhiteSpace(GetEnvironmentValue("GITEE_TOKEN"));
        var bandizipReady = !string.IsNullOrWhiteSpace(FindBandizip());

        return new DeveloperReleaseEnvironment(
            true,
            projectRoot,
            currentVersion,
            FindPowerShell(),
            githubReady,
            giteeReady,
            bandizipReady,
            DefaultOutputRoot,
            Path.Combine(projectRoot, "ReleaseAssets"));
    }

    public async Task<DeveloperReleaseRunResult> RunAsync(
        DeveloperReleaseOperation operation,
        DeveloperReleaseEnvironment environment,
        string targetVersion,
        IProgress<string>? output,
        CancellationToken cancellationToken)
    {
        ValidateEnvironment(environment);
        var scriptName = operation == DeveloperReleaseOperation.PublishLatest
            ? "发布新版本.ps1"
            : "打包工具箱.ps1";
        var scriptPath = ResolveScriptPath(environment.ProjectRoot, scriptName);
        var arguments = BuildArguments(operation, environment, targetVersion);
        output?.Report($">>> {GetOperationDisplayName(operation)}");
        output?.Report($"工程：{environment.ProjectRoot}");
        output?.Report($"脚本：{scriptPath}");

        var exitCode = await RunPowerShellAsync(
            environment.PowerShellPath,
            scriptPath,
            arguments,
            environment.ProjectRoot,
            output,
            cancellationToken);
        var succeeded = exitCode == 0;
        return new DeveloperReleaseRunResult(
            succeeded,
            exitCode,
            succeeded ? $"{GetOperationDisplayName(operation)}完成。" : $"{GetOperationDisplayName(operation)}失败，exit code {exitCode}。");
    }

    public void SetGiteeToken(string token)
    {
        var normalized = token.Trim();
        Environment.SetEnvironmentVariable(
            GiteeTokenEnvironmentName,
            string.IsNullOrWhiteSpace(normalized) ? null : normalized,
            EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(
            GiteeTokenEnvironmentName,
            string.IsNullOrWhiteSpace(normalized) ? null : normalized,
            EnvironmentVariableTarget.Process);
    }

    public void LaunchGitHubLogin(DeveloperReleaseEnvironment environment)
    {
        LaunchInteractiveCommand(environment.PowerShellPath, "gh auth login");
    }

    public static string GetSuggestedVersion(string currentVersion, DeveloperReleaseOperation operation)
    {
        var core = currentVersion.Split('-', 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim().TrimStart('v', 'V');
        var parts = core.Split('.');
        if (parts.Length < 3 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            return string.Empty;
        }

        return operation == DeveloperReleaseOperation.PackStable
            ? $"{major + 1}.0.0"
            : $"{major}.{minor}.{patch + 1}";
    }

    private static IReadOnlyList<string> BuildArguments(
        DeveloperReleaseOperation operation,
        DeveloperReleaseEnvironment environment,
        string targetVersion)
    {
        if (operation == DeveloperReleaseOperation.PublishLatest)
        {
            return
            [
                "-OutputRoot", environment.OutputRoot,
                "-ReleaseAssetRoot", environment.ReleaseAssetRoot
            ];
        }

        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            throw new InvalidOperationException("请先填写目标版本号。");
        }

        return
        [
            "-Configuration", "Release",
            "-Runtime", "win-x64",
            "-OutputRoot", environment.OutputRoot,
            "-ReleaseAssetRoot", environment.ReleaseAssetRoot,
            "-Version", targetVersion.Trim(),
            "-Channel", operation == DeveloperReleaseOperation.PackBeta ? "beta" : "stable"
        ];
    }

    private static async Task<int> RunPowerShellAsync(
        string powerShellPath,
        string scriptPath,
        IReadOnlyList<string> arguments,
        string projectRoot,
        IProgress<string>? output,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellPath,
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(BuildPowerShellInvocation(scriptPath, arguments));
        startInfo.Environment["FANTASYTOOLS_SCRIPT_DIR"] = Path.Combine(projectRoot, "Scripts");
        startInfo.Environment[DeveloperRootEnvironmentName] = projectRoot;
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "zh-CN";
        startInfo.Environment["NO_COLOR"] = "1";

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 PowerShell 发布进程。");
        }

        var outputTask = PumpOutputAsync(process.StandardOutput, output, cancellationToken);
        var errorTask = PumpOutputAsync(process.StandardError, output, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private static async Task PumpOutputAsync(
        StreamReader reader,
        IProgress<string>? output,
        CancellationToken cancellationToken)
    {
        var buffer = new char[512];
        var currentLine = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (character is '\r' or '\n')
                {
                    ReportCurrentLine(currentLine, output);
                    continue;
                }

                currentLine.Append(character);
                if (currentLine.Length >= 2000)
                {
                    ReportCurrentLine(currentLine, output);
                }
            }
        }

        ReportCurrentLine(currentLine, output);
    }

    private static void ReportCurrentLine(StringBuilder currentLine, IProgress<string>? output)
    {
        if (currentLine.Length == 0)
        {
            return;
        }

        var line = currentLine.ToString().Trim();
        currentLine.Clear();
        if (!string.IsNullOrWhiteSpace(line))
        {
            output?.Report(line);
        }
    }

    private static string? FindProjectRoot()
    {
        var configuredRoot = GetEnvironmentValue(DeveloperRootEnvironmentName);
        if (IsProjectRoot(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot!);
        }

        foreach (var startPath in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(startPath);
            for (var depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                if (IsProjectRoot(directory.FullName))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }

    private static bool IsProjectRoot(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            File.Exists(Path.Combine(path, "FantasyTools.csproj")) &&
            Directory.Exists(Path.Combine(path, "Scripts"));
    }

    private static string ReadProjectVersion(string projectRoot)
    {
        var document = XDocument.Load(Path.Combine(projectRoot, "FantasyTools.csproj"));
        return document.Descendants("Version").Select(element => element.Value.Trim()).FirstOrDefault(value => value.Length > 0) ?? "0.0.0";
    }

    private static string ResolveScriptPath(string projectRoot, string scriptName)
    {
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "Scripts", scriptName);
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        var sourcePath = Path.Combine(projectRoot, "Scripts", scriptName);
        if (File.Exists(sourcePath))
        {
            return sourcePath;
        }

        throw new FileNotFoundException($"没有找到开发者脚本：{scriptName}");
    }

    private static string FindPowerShell()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "pwsh.exe"),
            FindOnPath("pwsh.exe")
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) ?? string.Empty;
    }

    private static string BuildPowerShellInvocation(string scriptPath, IReadOnlyList<string> arguments)
    {
        var invocationArguments = string.Join(' ', arguments.Select(QuotePowerShellArgument));
        return
            "[Console]::InputEncoding=[Text.UTF8Encoding]::new($false);" +
            "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);" +
            "$OutputEncoding=[Console]::OutputEncoding;" +
            "if(Get-Variable PSStyle -ErrorAction SilentlyContinue){$PSStyle.OutputRendering='PlainText'};" +
            "$ErrorActionPreference='Stop';" +
            "try{& " + QuotePowerShellLiteral(scriptPath) + " " + invocationArguments +
            ";$exitCode=$LASTEXITCODE;if($null -eq $exitCode){$exitCode=0};exit $exitCode}" +
            "catch{[Console]::Error.WriteLine(($_ | Out-String));exit 1}";
    }

    private static string QuotePowerShellLiteral(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string QuotePowerShellArgument(string value)
    {
        if (value.StartsWith("-", StringComparison.Ordinal) &&
            value.Length > 1 &&
            value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_'))
        {
            return value;
        }

        return QuotePowerShellLiteral(value);
    }

    private static string? FindBandizip()
    {
        var installedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bandizip", "bz.exe");
        return File.Exists(installedPath) ? installedPath : FindOnPath("bz.exe");
    }

    private static string? FindOnPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool IsCommandSuccessful(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var executable = FindOnPath(fileName);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = string.Join(' ', arguments.Select(QuoteArgument))
            });
            return process is not null && process.WaitForExit((int)timeout.TotalMilliseconds) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteArgument(string argument)
    {
        return argument.Contains(' ') ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument;
    }

    private static string? GetEnvironmentValue(string name)
    {
        return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process) ??
            Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) ??
            Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
    }

    private static void ValidateEnvironment(DeveloperReleaseEnvironment environment)
    {
        if (!environment.IsVisible || !IsProjectRoot(environment.ProjectRoot))
        {
            throw new InvalidOperationException("没有检测到 FantasyTools 开发者工程目录。");
        }

        if (!File.Exists(environment.PowerShellPath))
        {
            throw new FileNotFoundException("没有找到 PowerShell 7。开发者发布中心不回退到 Windows PowerShell 5.1。", environment.PowerShellPath);
        }
    }

    private static string GetOperationDisplayName(DeveloperReleaseOperation operation)
    {
        return operation switch
        {
            DeveloperReleaseOperation.PackStable => "打包正式版工具箱",
            DeveloperReleaseOperation.PackBeta => "打包测试版工具箱",
            _ => "发布最新包"
        };
    }

    private static void LaunchInteractiveCommand(string powerShellPath, string command)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = powerShellPath,
            Arguments = $"-NoExit -NoProfile -Command \"{command.Replace("\"", "`\"")}\"",
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        });
    }
}
