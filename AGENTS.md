# Repository Guidelines

**Security rule:** Never inspect or delete sensitive private keys or passwords (including env vars like `LICENSE_PASSPHRASE`). If a command requires them, let the human provide/run it.


## CRITICAL Coding Directives (Must Follow)
Non-negotiables

TDD first. Write a failing test before production code. No exceptions.

Minimise complexity; prefer simple, explicit code. Fewer concepts > cleverness.

Validate all external inputs; narrow attack surface.

Centralise exception handling; reduce the number of catch sites.

Warnings are errors. Missing docs are errors.

If a warning must be disabled, include a detailed justification comment and raise to a human reviewer.

Follow A Philosophy of Software Design (Ousterhout). Avoid “rules by ritual” (do not apply Uncle Bob guidelines blindly).

Commits: concise 50/72, never mention Codex/Claude/AI.

### pre-commit hook
Setup this pre-commit hook if it doesn't already exist

#!/bin/bash
dotnet format --verify-no-changes || {
  echo "❌ Style violations. Run: dotnet format"
  exit 1
}
dotnet build -warnaserror || exit 1



### .NET Code Analysis

Create a Directory.Build.props in the repo root with this content.
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisMode>All</AnalysisMode>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <Nullable>enable</Nullable>
    <WarningsAsErrors>$(WarningsAsErrors);CS1591</WarningsAsErrors> <!-- missing XML docs -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <!-- Force Release+checked arithmetic in CI to catch edge cases -->
  <PropertyGroup Condition="'$(CI)' == 'true'">
    <Optimize>true</Optimize>
    <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>
  </PropertyGroup>
</Project>


## set .editor-config

root = true

[*.cs]
dotnet_analyzer_diagnostic.severity = error
dotnet_diagnostic.CA*.severity = error
dotnet_diagnostic.IDE*.severity = error

### Style (fail build on violations)
csharp_style_var_elsewhere = true:error
csharp_style_expression_bodied_methods = false:error
csharp_prefer_static_local_function = true:error
dotnet_style_qualification_for_field = true:error
dotnet_style_qualification_for_property = true:error
dotnet_style_qualification_for_method = true:error

### Formatting
csharp_new_line_before_open_brace = all:error
indent_style = space
indent_size = 4
end_of_line = lf
insert_final_newline = true

### Documentation required
dotnet_diagnostic.CS1591.severity = error

## Plans 
- At the end of each plan, give me a list of unresolved questions to answer, if any. Make questions extremely consise. Sacrifice grammar for the sake of concision.
- "Always explain using a detailed comment why you are disabling a warning.
- NEVER reference Claude in commits
- ALWAYS follow good git commit style 50/70 rule
- UK spelling - code, docs - unless given special exception.
- Make sure NO tests silently skip!
- Check and validate all inputs to prevent attacks through invalid size inputs, invalid data input
- Do NOT reinvent the wheel when proven libraries exist; e.g., use CsvHelper for CSV output or BouncyCastle for ASN.1/CRL handling instead of custom parsers.


## Coding Style & Naming Conventions
The enforced `.editorconfig` sets four-space indentation, spaces not tabs, `LF` endings, and trimmed trailing whitespace. Prefer `var` when the type is evident, keep braces even around single statements, and group `using` directives with `System` first. Never suppress warnings without a documented reason—the build promotes them to errors. Run `dotnet format` before opening a PR to keep style clean.

## Security & Configuration Tips
Treat `config.json` as sensitive; never commit secrets (SMTP passwords, LDAP credentials) and validate new fields before use. Enable verbose logging only when diagnosing issues and use minimal log levels in production to reduce noise.
