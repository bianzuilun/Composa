param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../dist/mcp-client-config'),
    [switch]$RegisterCodex
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw 'Choose the Composa executable you actually run.' }
$composaExecutable = (Resolve-Path -LiteralPath $Executable).Path
$configDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($configDirectory) | Out-Null
$encodedExecutable = ConvertTo-Json $composaExecutable -Compress
$utf8 = [Text.UTF8Encoding]::new($false)

$codex = @"
[mcp_servers.composa]
command = $encodedExecutable
args = ["--mcp"]
startup_timeout_sec = 60
tool_timeout_sec = 300
"@
$dsh = @"
- id: mcp-composa
  name: '@deepseek-ai/dsh-mcp-client'
  config:
    serverName: composa
    transport: stdio
    command: $encodedExecutable
    args: ['--mcp']
    toolCallTimeoutMs: 300000
"@
$claude = @{ mcpServers = @{ composa = @{ command = $composaExecutable; args = @('--mcp') } } } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $configDirectory 'codex.toml'), $codex + "`n", $utf8)
[IO.File]::WriteAllText((Join-Path $configDirectory 'dsh-plugin.yml'), $dsh + "`n", $utf8)
[IO.File]::WriteAllText((Join-Path $configDirectory 'mcp-servers.json'), $claude + "`n", $utf8)

if ($RegisterCodex) {
    & codex mcp add composa -- $composaExecutable --mcp
    if ($LASTEXITCODE -ne 0) { throw 'Codex MCP registration failed.' }
}
Write-Output "Client configuration fragments: $configDirectory"
Write-Output 'Enable Help > Allow AI Control in the updated Composa. Merge fragments into existing client settings; do not replace unrelated plugins/servers.'
