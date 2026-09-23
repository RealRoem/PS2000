# CLAUDE.md

## Committing
- **Never `git commit` (or `git push`) on your own initiative.** Stage/show diffs if useful,
  but only create a commit when the user explicitly asks for one in that turn.

## General code practices
- **Minimal diffs**: change only what the task requires. No drive-by refactors, no renaming
  things you're passing through, no "while I'm here" cleanup in the same commit/change.
- **No speculative abstractions**: don't add config options, interfaces, or generic layers for
  a use case that doesn't exist yet. Three similar lines beat a premature helper.
- **No dead code**: don't leave old implementations commented out "just in case" — git history
  already has it. Delete fully or don't touch it.
- **Match existing patterns**: before introducing a new pattern (DI style, naming, error
  handling), check how the surrounding code already does it and follow that instead.
- **Comments explain *why*, not *what***: only add a comment for a non-obvious constraint,
  workaround, or invariant (see how `Ps2000Client.cs` and `assignment1.md` document *why*
  behind each design choice — that's the bar). Skip comments that just restate the code.
- **No error handling for impossible cases**: only guard actual boundaries (hardware I/O,
  user input, network). Don't wrap internal calls in try/catch "to be safe."
- **Verify before claiming done**: build/run and exercise the actual change (UI in the
  browser, hardware behavior if touching `Ps2000Client`) rather than assuming it works from
  reading the diff.
- **Ask before destructive or hard-to-reverse actions**: force-push, `git reset --hard`,
  deleting files/branches, overwriting uncommitted work, or anything touching the real
  power-supply hardware in a way that could be unsafe.
