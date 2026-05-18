---
name: Bug report
about: Create a report to help us improve
title: ''
labels: bug
assignees: ''

---

**Describe the bug**
A clear and concise description of what the bug is.

**To Reproduce**
Steps to reproduce the behavior:
1. Run `fcdnx ...` (or library call) with the following inputs
2. Observed result
3. Expected result

If running via the library, please include the calling code snippet
(`DnxLiteRunner` / `ToolInvocationRequest`).

**Expected behavior**
A clear and concise description of what you expected to happen.

**Logs**
Re-run with `--verbosity diag` (CLI) or `LogLevel.Trace` (library) and paste the
relevant log lines.

**Environment**
- Package(s) and version: (e.g., `FieldCure.ToolHost 0.1.0`, `FieldCure.ToolHost.Cli 0.1.0`)
- .NET runtime: output of `dotnet --info`
- OS: (Windows / Linux / macOS) and architecture (x64 / arm64)
- Distribution channel: (regular install / MS Store / MSIX / Docker / CI image)
- NuGet feed: (nuget.org only / Azure Artifacts / GitHub Packages / private)

**Additional context**
Add any other context about the problem here.
