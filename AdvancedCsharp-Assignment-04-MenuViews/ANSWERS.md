# Assignment 04 — Answers

Student name: Rabie Ali 
Group / ID:3 

## Why Expected and Actual disagree

- `Views++` is not atomic. It does three steps: read the value, add 1, and write it back.
- When multiple threads run at the same time, they can read the same value together.
- This causes a race condition and lost updates, so the actual count is lower than expected.

## What you changed

- I replaced `Views++` with `Interlocked.Increment(ref Views)`.
- This makes the increment atomic at the CPU level, so threads do not overwrite each other.

## When would you protect the update differently

- **`lock`**: when updating multiple variables together or checking conditions (for example, `if (Views < limit)`).
- **`SemaphoreSlim`**: when we have asynchronous code using `await` (like saving to a database).
- **Channels / Queues**: when traffic is very high and we want to collect views and save them in batches in the background.
