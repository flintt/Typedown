# Notes for agents working in this repository

## The to-do list: docs/todo.md and its summary table

`docs/todo.md` opens with a summary table (总表) of everything still open: priority, the item, its state (待做 / 待复测 /
待评估 / 暂缓) and the section further down that has the details. Keep it true:

- **Adding an item** anywhere in todo.md (a reader's report, a failure found, an idea): add its row to the table too, with
  its priority, state and section.
- **Finishing an item**: take its row out of the table (or write the commit that did it), and mark the section's entry
  done (✅ and the commit).
- **A state changing** (tested again, put off, decided against): change the row.
- **Asked what is left to do**: answer from the table, checked against the recent commits (`git log`), not from memory.
- When the table and the sections disagree, fix the table in the same commit as the change.

Write todo.md in Chinese, as it is.
