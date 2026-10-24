# Assignment 02 — Answers

Student name:  Rabie Ali
Group / ID:

## What is wrong

- There is a **race condition** because many threads access and change `TablesLeft` and `Booked` at the same time.
- Multiple threads pass the check (`TablesLeft <= 0`) together before the count is updated.
- This causes overbooking: `Booked` becomes greater than 5, and `TablesLeft` goes below 0.

## How you fixed it

- I used a `lock` with a private `Lock` object.
- The `lock` makes sure only one thread at a time can check and update the table count.
- Now the code is thread-safe, and every run gives `Booked=5` and `TablesLeft=0`.
