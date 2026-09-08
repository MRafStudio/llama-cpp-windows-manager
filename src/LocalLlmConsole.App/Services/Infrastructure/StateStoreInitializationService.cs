namespace LocalLlmConsole.Services;

public sealed record StateStoreInitializationRequest(
    string WorkspaceRoot,
    string DatabasePath,
    Func<StateStore> CreateStateStore,
    Func<string, string>? QuarantineDatabaseFiles = null);

public sealed record StateStoreInitializationResult(
    StateStore StateStore,
    AppSettings Settings);

public sealed class StateStoreInitializationService
{
    public async Task<StateStoreInitializationResult> InitializeAsync(StateStoreInitializationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CreateStateStore);

        if (string.IsNullOrWhiteSpace(request.WorkspaceRoot))
            throw new ArgumentException("Workspace root is required.", nameof(request.WorkspaceRoot));
        if (string.IsNullOrWhiteSpace(request.DatabasePath))
            throw new ArgumentException("Database path is required.", nameof(request.DatabasePath));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var stateStore = request.CreateStateStore();
            try
            {
                await stateStore.InitializeAsync();
                var loaded = await stateStore.GetAppSettingsAsync(request.WorkspaceRoot);
                var settings = await new WorkspaceRelocationService()
                    .ApplyAsync(stateStore, loaded, request.WorkspaceRoot);
                return new StateStoreInitializationResult(stateStore, settings);
            }
            catch (SqliteException) when (attempt == 0)
            {
                await stateStore.DisposeAsync();
                await QuarantineDatabaseFilesAsync(
                    request.DatabasePath,
                    request.QuarantineDatabaseFiles ?? StateStore.QuarantineDatabaseFiles);
            }
        }

        throw new InvalidOperationException("Unable to initialize the application state database.");
    }

    private static async Task<string> QuarantineDatabaseFilesAsync(
        string databasePath,
        Func<string, string> quarantine)
    {
        IOException? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                return quarantine(databasePath);
            }
            catch (IOException ex) when (attempt < 4)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(75 * (attempt + 1)));
            }
        }

        throw lastError ?? new IOException("Failed to quarantine corrupt database files.");
    }
}
