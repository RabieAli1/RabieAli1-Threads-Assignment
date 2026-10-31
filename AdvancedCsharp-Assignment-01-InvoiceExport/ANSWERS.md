# Assignment 01 — Answers

Student name: Rabie Ali
Group / ID: 

## Problems with the current design

- **Orders run one by one (Sequential):** The old code used `await` inside the `foreach` loop. It waited for each order to finish completely before starting the next order.
- **Very slow for many orders:** Each order takes about 1 second. Processing 5 orders took 6.0 seconds. If we have 1,000 orders at night, it will take too long.
- **Blocking file operations:** The code used `File.ReadAllText` and `File.WriteAllText` synchronously, which blocked the thread during disk reading and writing.

Console output before the fix:
```text
Starting invoice export...
Customer files: C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\InvoiceExport\bin\Debug\net9.0\Data
Invoice output: C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices

Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1001.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1002.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1003.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1004.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1005.txt
Export finished in 6.0 s
```

## What you changed

- **Created `ProcessOrderAsync`:** Moved the work for one order into its own method. It keeps the exact same business flow (load order -> read customer -> write invoice).
- **Ran orders concurrently:** Used a `List<Task>` to start all order tasks together in the first loop with `tasks.Add(ProcessOrderAsync(order))`.
- **Awaited tasks one by one:** Used a second loop to `await` each task individually, following the assignment rules (no `Task.WhenAll`).
- **Used async file I/O:** Changed `File.ReadAllText` to `await File.ReadAllTextAsync` and `File.WriteAllText` to `await File.WriteAllTextAsync`.

Console output after the fix:
```text
Starting invoice export...
Customer files: C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\InvoiceExport\bin\Debug\net9.0\Data
Invoice output: C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices

Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1004.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1002.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1003.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1005.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1001.txt
Export finished in 1.9 s
```

The time dropped from **6.0 s** to **1.9 s** because all orders now run at the same time.

Console output after the fix:
```text
Starting invoice export...
Customer files: C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\InvoiceExport\bin\Debug\net9.0\Data
Invoice output: C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices

Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1004.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1002.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1003.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1005.txt
Wrote C:\Users\RABIE ALI\Desktop\pro\RabieAli1-Threads-Assignment\AdvancedCsharp-Assignment-01-InvoiceExport\invoices\invoice-1001.txt
Export finished in 1.9 s
```

The time dropped from **6.0 s** to **1.9 s** because all orders now run at the same time.

