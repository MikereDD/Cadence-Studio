using CadenceStudio.Core.Updates;

// No install mode exists. Unknown, duplicate, or missing arguments fail closed.
if (args.Length != 3 || args[0] != "--dry-run" || args[1] != "--transaction")
{
    Console.Error.WriteLine("Usage: CadenceStudio.Updater.exe --dry-run --transaction <absolute transaction.json>");
    return 2;
}
UpdateTransaction? transaction = null;
FileStream? lease = null;
var ownsValidatedTransaction = false;
try
{
    var installRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    transaction = UpdateTransactionStore.Read(args[2], installRoot);
    var lockPath = Path.Combine(transaction.StagingRoot, "active.lock");
    UpdateTransactionPaths.Canonical(lockPath);
    lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    transaction = UpdateTransactionStore.Read(args[2], installRoot);
    ownsValidatedTransaction = true;
    transaction = UpdateTransactionStore.Transition(transaction, UpdateTransactionState.Validated, "Installation and staging paths validated.");
    var closed = UpdateProcessIdentity.IsClosed(transaction);
    transaction = UpdateTransactionStore.Transition(transaction, closed ? UpdateTransactionState.ProcessClosed : UpdateTransactionState.ProcessRunning,
        closed ? "Main process already exited." : "Main process identity matches; dry run leaves it running.");
    transaction = UpdateTransactionStore.Transition(transaction, UpdateTransactionState.DryRunCompleted,
        "No download, verification, extraction, backup, replacement, restart, or rollback performed.");
    Console.WriteLine($"Dry run completed: {transaction.TransactionId}");
    return 0;
}
catch (Exception exception)
{
    // Unvalidated input must never select a log destination.
    Console.Error.WriteLine($"Dry run rejected: {exception.GetType().Name}: {exception.Message}");
    if (ownsValidatedTransaction && transaction is not null)
    {
        try { UpdateTransactionStore.Transition(transaction, UpdateTransactionState.Failed, exception.GetType().Name); }
        catch (Exception persistenceException)
        {
            Console.Error.WriteLine($"Could not record Failed state: {persistenceException.GetType().Name}: {persistenceException.Message}");
        }
    }
    return 1;
}
finally
{
    lease?.Dispose();
}
