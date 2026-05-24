# AGENTS.md

## Role
Act as a local autonomous coding agent.

## Main workflow
- Work only inside this repository.
- Take one task at a time.
- Use GitHub Issues as the task source.
- Create one branch per issue.
- Never push directly to main.
- Never merge pull requests.
- Make small, focused commits.
- Open a pull request when finished.
- You optimize codexs token usage, but you do not sacrifice code quality for it.
- If you run out of credits, stop working.

## Before coding
- Read the issue carefully.
- Check existing files before creating new ones.
- Reuse existing components, styles and patterns.
- Ask for clarification only if the task cannot be completed safely.

## During coding
- Keep changes minimal.
- Do not refactor unrelated code.
- Do not remove existing functionality.
- Do not add unnecessary dependencies.
- For HTML/CSS/JS, keep code simple and readable.
- For .NET/Blazor, follow existing project structure.

## Design tasks
For UI or web design tasks:
- Check desktop and mobile behavior.
- Keep spacing consistent.
- Use semantic HTML when possible.
- Maintain accessibility basics.
- Avoid visual clutter.
- Prefer reusable sections/components.

## Validation
Before finishing:
- Run the available checks.
- If package.json exists, inspect scripts first.
- If .NET project exists, run build/test when available.
- Fix errors caused by your changes.
- Make sure that your forms can only accept true data and handle errors gracefully.

## Completion
When done:
- Summarize changes.
- Mention commands executed.
- Mention files changed.
- Open or prepare a pull request.
- Leave notes if something could not be completed.