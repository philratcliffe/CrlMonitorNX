# Repository Guidelines

- In all interactions and commit messages, be extremely consise and sacrifice grammar for the sake of concision

- Commits - Use 50/72 rule and write clear and concise Git commit messages
- Commits - never reference Claude or Codex in the commit messages


# Coding

## Compiler Warnings
  1. Compiler Warnings as Errors)

  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  - ALL compiler warnings fail the build

## .NET Code Analysis
  2. .NET Code Analysis)

  <AnalysisMode>All</AnalysisMode>
  <AnalysisLevel>latest</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <EnableNETAnalyzers>true</EnableNETAnalyzers>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  - AnalysisMode>All: Enables all code analysis rules
  - AnalysisLevel>latest: Uses latest analyzer versions
  - EnforceCodeStyleInBuild: Code style violations fail build
  - EnableNETAnalyzers: Enables .NET quality analyzers
  - GenerateDocumentationFile: Requires XML documentation (warns on missing docs)


- Always use TDD
- Always provide a detailed code comment if you have to disable a warning and raise it with human
- Always look for way to Minimise Complexity.
- reduce the number of places exceptions need to be handled,
- Follow A Philosophy of Software Design Books by John Osterhout
- Don't use Uncle Bob guidelines just for the sake of it


## Plans 
- At the end of each plan, give me a list of unresolved questions to answer, if any. Make questions extremely consise. Sacrifice grammar for the sake of concision.
- "Always explain using a detailed comment why you are disabling a warning.
- NEVER reference Claude in commits
- ALWAYS follow good git commit style 50/70 rule
- UK spelling - code, docs - unless given special exception.
- Make sure NO tests silently skip!
- Check and validate all inputs to prevent attacks through invalid size inputs, invalid data input


## Coding Style & Naming Conventions
The enforced `.editorconfig` sets four-space indentation, spaces not tabs, `LF` endings, and trimmed trailing whitespace. Prefer `var` when the type is evident, keep braces even around single statements, and group `using` directives with `System` first. Never suppress warnings without a documented reason—the build promotes them to errors. Run `dotnet format` before opening a PR to keep style clean.

## Security & Configuration Tips
Treat `config.json` and `uri_list.txt` as sensitive; never commit secrets and validate new fields before use. Enable verbose logging only when diagnosing issues (`touch enable-logging.txt`) and remove the flag afterwards to reduce noise.
