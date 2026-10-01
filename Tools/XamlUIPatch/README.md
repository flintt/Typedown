# Typedown.XamlUI 1.0.2

`nupkgs/Typedown.XamlUI.1.0.2.nupkg` is the vendored 1.0.1 package (upstream https://github.com/byxiaozhi/Typedown.XamlUI,
whose source has the same code) with one change in `Dispatcher.PostTask`:

    var taskId = curTaskId++;                                        // 1.0.1
    var taskId = (uint)(Interlocked.Increment(ref curTaskId) - 1);   // 1.0.2

Callbacks are posted to a window's UI thread from any thread (await continuations, timers, the automation pipe).
Two threads posting at once could take the same number; the second `TryAdd` under it failed and its callback was
dropped - an await whose continuation never ran, the UI thread idle (E2E C01 hung a minute, about one full run in
eight; E2E D01 lost 1-2 of 8000 posts from 8 threads before, none after).

Made with this tool, which checks the instructions it replaces and changes nothing else (decompiling the original and
the patched assembly differs in that line only):

    dotnet run --project Tools/XamlUIPatch -- show  <Typedown.XamlUI.dll>
    dotnet run --project Tools/XamlUIPatch -- patch <1.0.1 Typedown.XamlUI.dll> <patched dll>

Both copies in the package (`lib/netstandard2.0`, `lib/uap10.0.18362`) were the same file and are replaced by the
patched one; the nuspec says 1.0.2. Everything else in the package is unchanged.
