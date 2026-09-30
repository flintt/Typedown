# typedown-mcp

An MCP server (stdio) that lets an AI agent read and edit the documents open in a running Typedown, through the local
automation API (`docs/automation-api-spec.md`). The person turns it on in Typedown: **Settings > General > Allow local
automation**. While an agent is connected, the window title says so, and names the agent after each edit; the edited
text is briefly highlighted (Settings > General > Highlight changes made by programs).

## Tools

| Tool | What it does |
| --- | --- |
| `typedown_list_documents` | Open documents: id, window, path, title, revision, saved, shown |
| `typedown_read_document` | The current text (including unreported typing), revision, content hash; optional headings |
| `typedown_replace_text` | Replace `find` with `replacement`, only if it occurs exactly `expectedCount` times (default 1) at `baseRevision` |
| `typedown_replace_document` | Replace the whole text, only at `baseRevision` |
| `typedown_save_document` | Save to the document's file (not for untitled documents) |

Writes take `save`, `reveal` (bring the document to the front) and `allowFormattingChanges` (accept text whose
formatting the visual editor may rewrite on the person's first edit).

A failed call is a tool result with `isError: true` and `structuredContent` = `{ "error": <the API error, exactly as
typedownctl --json prints it>, "next": "<what to do>" }`. On `revision_conflict` or `match_count_mismatch` nothing was
written and the agent must read the document again; the server never retries with old text.

## Build and register

```
dotnet publish Tools/Typedown.Mcp -c Release -o <dir>
```

Claude Code:

```
claude mcp add typedown -- dotnet <dir>/typedown-mcp.dll
```

Other MCP hosts (`mcpServers` in their JSON configuration):

```json
{
  "mcpServers": {
    "typedown": { "command": "dotnet", "args": ["<dir>/typedown-mcp.dll"] }
  }
}
```

`--endpoint NAME` connects to another endpoint than the installed application's (the automation test host writes its
name to `automation-endpoint.txt`).
