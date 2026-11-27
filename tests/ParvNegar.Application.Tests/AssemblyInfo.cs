using Xunit;

// SessionManager allows one Argon2id operation per process (memory bound for 32-bit PCs) and answers
// "Busy" to a concurrent one. Tests therefore run sequentially; the concurrency itself has its own test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
