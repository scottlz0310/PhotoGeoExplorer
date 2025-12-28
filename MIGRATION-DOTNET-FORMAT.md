# Migration from dotnet-format to built-in dotnet format

## Background

The standalone `dotnet-format` tool has been deprecated since .NET 6.0 and integrated into the .NET SDK as a built-in command. This repository has been updated to use the built-in `dotnet format` command instead of the deprecated standalone tool.

## Changes Made

### 1. Removed standalone tool dependency
- **File**: `.config/dotnet-tools.json`
- **Change**: Removed the `dotnet-format` tool entry (version 5.1.250801)
- **Reason**: The standalone tool is deprecated and no longer needed

### 2. Updated pre-commit hooks
- **File**: `lefthook.yml`
- **Before**: `dotnet tool restore && dotnet tool run dotnet-format --check PhotoGeoExplorer.sln`
- **After**: `dotnet format --verify-no-changes PhotoGeoExplorer.sln`
- **Benefits**: Faster execution (no tool restore needed), uses SDK-integrated command

### 3. Updated CI/CD workflow
- **File**: `.github/workflows/quality-check.yml`
- **Changes**:
  - Removed "Restore dotnet tools" step
  - Updated format check command: `dotnet format --verify-no-changes PhotoGeoExplorer.sln`
- **Benefits**: Simpler workflow, faster CI execution

## Command Reference

### Old Command (Deprecated)
```bash
dotnet tool restore
dotnet tool run dotnet-format --check PhotoGeoExplorer.sln
```

### New Command (Built-in)
```bash
# Check formatting without making changes
dotnet format --verify-no-changes PhotoGeoExplorer.sln

# Apply formatting
dotnet format PhotoGeoExplorer.sln
```

## Key Differences

| Feature | Old (dotnet-format) | New (dotnet format) |
|---------|---------------------|---------------------|
| Installation | Separate tool via `.config/dotnet-tools.json` | Built into .NET SDK |
| Check mode | `--check` | `--verify-no-changes` |
| Invocation | `dotnet tool run dotnet-format` | `dotnet format` |
| Restore needed | Yes (`dotnet tool restore`) | No |

## Benefits of Migration

1. **No separate tool installation**: The command is built into the .NET SDK
2. **Faster execution**: No need to restore tools before running
3. **Better integration**: Works seamlessly with SDK tooling
4. **Future-proof**: Actively maintained as part of the .NET SDK
5. **Simpler configuration**: No separate tool manifest needed

## Testing

To verify the migration works correctly:

```bash
# Check formatting (should exit with error if formatting is needed)
dotnet format --verify-no-changes PhotoGeoExplorer.sln

# Apply formatting
dotnet format PhotoGeoExplorer.sln
```

## Additional Resources

- [dotnet format documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
- [Code style analysis overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)
- [.NET SDK built-in tools](https://learn.microsoft.com/en-us/dotnet/core/tools/)
