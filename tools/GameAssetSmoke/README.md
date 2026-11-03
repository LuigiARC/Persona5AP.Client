# Automatic game-cache integration check

This console check compiles the production cache/patch code and reads the player's
installed BASE.CPK and EN.CPK directly, without changing either archive. Temporary
generated files are removed after the run. No game files belong in this directory.

Supply an untrimmed CriFsV2Lib build compatible with the test host:

```text
dotnet run --project tools/GameAssetSmoke/GameAssetSmoke.csproj -p:CriFsLibraryPath=/path/to/CriFsV2Lib.dll -- /path/to/mod /path/to/game/CPK /optional/private/expected/CPK
```

For Linux, build an untrimmed reader from its upstream source with the installed
.NET SDK and the matching API definitions. This test compiles the client's vendored
API source automatically. Windows ReadyToRun reader binaries cannot execute on
Linux. The production mod uses the player's existing CRI loader implementation.

Checks cover cold preparation, optional comparison with private known-working
overrides, warm cache reuse, cache corruption/repair, CRI binding routes and
priority, damaged patches, unsupported originals, missing CPKs, unsafe paths,
malformed delta headers, and original archive size/timestamp preservation.
