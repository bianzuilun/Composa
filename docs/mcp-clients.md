# Connect Claude, Codex and DeepSeek Harness

Enable **Help > Allow AI Control** in the Composa build that contains the tools you want. Each client starts its own `composa --mcp` bridge; all bridges address the same running editor. This is local stdio MCP, with a private per-user pipe/socket, rather than an HTTP service. No API key belongs in the Composa configuration.

The published 1.5.3 build predates the MCP coverage additions described here. Updating client configuration alone cannot add tools to an older editor. Use `get_capabilities` and `tools/list` to check the running editor, then begin with `list_documents` and `describe_document`. Tool availability follows the editor that owns the pipe, which may differ from the bridge executable if you have two installations.

## Codex

Register the actual installed or portable executable using the Codex CLI:

```powershell
codex mcp add composa -- 'C:/Users/25828/AppData/Local/Programs/Composa/composa.exe' --mcp
```

Replace the executable path with the one you run. The equivalent user configuration is:

```toml
[mcp_servers.composa]
command = 'C:/Users/25828/AppData/Local/Programs/Composa/composa.exe'
args = ['--mcp']
startup_timeout_sec = 60
tool_timeout_sec = 300
```

Use `codex mcp get composa` to inspect the registration. Restart the client/session if it cached an earlier tool list. CLI, IDE and desktop configurations share the user MCP configuration as described in the [official Codex MCP guide](https://developers.openai.com/codex/mcp/).

## DeepSeek Harness 0.2.1-rc.2

Add this entry to the plugins list of the profile you use, alongside its existing plugins:

```yaml
- id: mcp-composa
  name: '@deepseek-ai/dsh-mcp-client'
  config:
    serverName: composa
    transport: stdio
    command: 'C:/Users/25828/AppData/Local/Programs/Composa/composa.exe'
    args: ['--mcp']
    toolCallTimeoutMs: 300000
```

Keep the executable path and argument separate. This fragment is a plugin entry, **not** a complete replacement for a Harness profile. On Linux use the installed `composa` command or an absolute path to its executable instead.

The version-pinned [official MCP client documentation](https://github.com/deepseek-ai/deepseek-harness/blob/dsh-v0.2.1-rc.2/packages/mcp/mcp-client/README.md) documents the stdio transport, tool-list refresh, reconnect policy and `toolCallTimeoutMs`. Its public names are `mcp__composa__liquify`, `mcp__composa__filter_camera_raw`, and so on. Image results require a vision-capable model/route and Harness image attachments; text-only models can still call editing tools and read numeric scopes.

## Claude

Claude Code uses the same executable:

```powershell
claude mcp add composa -- 'C:/Users/25828/AppData/Local/Programs/Composa/composa.exe' --mcp
```

For clients using `mcpServers` JSON, add a `composa` entry with `command` set to that path and `args` set to `["--mcp"]`. Do not replace unrelated server registrations.

## Check the connection

1. Start the updated Composa and enable AI control. Close an older editor that still owns the pipe before starting another build.
2. Start or reload the MCP client and discover its tools.
3. Call `get_capabilities`, `list_documents` and `describe_document`.
4. On a disposable raster document, call `liquify` with `points: [[20,20],[30,20],[40,22]]`, `size: 24`, `strength: 0.4`.
5. Call `render`, then `undo` to inspect and take back the stroke.

`--mcp --launch` also starts the editor once if it is absent; AI control still needs to be enabled. A bridge with no editor behind it exposes an empty tool list, so a successful MCP handshake alone does not verify editor connectivity.

Multiple clients can connect simultaneously. They share documents and history: coordinate edits by document and layer IDs, and avoid having agents edit the same document concurrently. A UI drag or open preview blocks other editing operations. Client timeout/cancellation does not promise that an editing operation was rolled back; inspect the document/history before retrying an uncertain call. File overwrite protections apply to export/save paths.
