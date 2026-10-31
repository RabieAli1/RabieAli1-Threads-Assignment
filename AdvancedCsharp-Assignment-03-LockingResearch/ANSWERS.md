# Assignment 03 — Answers

Student name: Rabie Ali  
Group / ID: 3

1. **What is `System.Threading.Monitor` used for?**
   It is used to synchronize thread access to a shared object. It ensures that only one thread at a time can execute a block of code (critical section) to prevent race conditions.

2. **What does the C# `lock` statement compile to?**
   The `lock` statement is syntactic sugar that compiles into a `try...finally` block using `Monitor.Enter` and `Monitor.Exit`:
   ```csharp
   bool lockTaken = false;
   try
   {
       Monitor.Enter(lockObj, ref lockTaken);
       // code
   }
   finally
   {
       if (lockTaken)
           Monitor.Exit(lockObj);
   }
   ```

3. **List at least two differences between using `lock` and calling `Monitor` APIs directly.**
   - **Simplicity vs. Safety:** `lock` is cleaner and automatically releases the lock in a `finally` block, reducing the risk of errors. Calling `Monitor` manually requires you to write boilerplate code.
   - **Advanced Features:** `lock` only supports basic blocking lock/unlock. `Monitor` APIs give extra control, like setting a timeout with `TryEnter()` or inter-thread signaling with `Wait()` and `Pulse()`.

4. **When might a developer use `Monitor.TryEnter`, `Monitor.Pulse`, or `Monitor.Wait` instead of a plain `lock`?**
   - **`Monitor.TryEnter`:** When a thread should not wait forever. It allows specifying a timeout to avoid deadlocks or do other work if the lock is busy.
   - **`Monitor.Wait` and `Monitor.Pulse`:** When threads need to coordinate and signal each other (e.g., Producer-Consumer pattern). `Wait` temporarily releases the lock and sleeps until `Pulse` wakes it up.

5. **Write the same critical section twice: once with `lock`, once with `Monitor.Enter` / `Monitor.Exit`.**

   **Using `lock`:**
   ```csharp
   private readonly object _lockObj = new object();

   lock (_lockObj)
   {
       // Critical section
       counter++;
   }
   ```

   **Using `Monitor.Enter` / `Monitor.Exit`:**
   ```csharp
   private readonly object _lockObj = new object();
   bool lockTaken = false;

   try
   {
       Monitor.Enter(_lockObj, ref lockTaken);
       // Critical section
       counter++;
   }
   finally
   {
       if (lockTaken)
       {
           Monitor.Exit(_lockObj);
       }
   }
   ```

Links:
- [Monitor Class (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor)
- [lock statement - C# reference (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock)
