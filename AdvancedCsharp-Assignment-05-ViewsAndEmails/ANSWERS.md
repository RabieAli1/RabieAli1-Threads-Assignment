# Assignment 05 — Answers

Student name: Rabie Ali  
Group / ID: 3

## Failures you observed

- `InvalidOperationException`: Two workers tried to dequeue from the regular `Queue` at the same time when it was empty, causing a crash.
- Lost view counts: The actual views were much lower than 50,000 because many threads read and wrote to the `Dictionary` at the same time.

## What you changed

- Replaced `Dictionary` with `ConcurrentDictionary` and used `AddOrUpdate()` to count views atomically.
- Replaced `Queue` with `ConcurrentQueue` and used `TryDequeue()` so workers take jobs safely without crashing.
- Checked `ProducersDone && EmailJobs.IsEmpty` to exit worker threads cleanly.

## Collection safety vs business-rule safety

- A thread-safe collection only stops the collection itself from crashing or corrupting internally.
- It does not make business operations safe if they take multiple steps (read, then update).
- For example, `Views[key] = Views[key] + 1` reads first and writes second. Even with a concurrent collection, threads can overwrite each other unless the whole operation is atomic (like `AddOrUpdate`).
