# Contributing

1. Open an issue first for anything beyond a small fix, so the API shape can be agreed on.
2. Fork, branch from `main` (`feat/<topic>` or `fix/<topic>`).
3. `dotnet build && dotnet test` must pass locally. Warnings are errors, missing XML docs are errors.
4. Add or update tests for every behaviour change. Test names: `Method_Scenario_Expectation`.
5. Keep public API changes minimal and explain them in the PR description.
6. Use conventional commit messages (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).
7. Open a PR against `main`. CI runs build + test on Linux.

Style is enforced by `.editorconfig` and `EnforceCodeStyleInBuild`; run `dotnet format` before pushing.

By contributing you agree your code is released under the MIT license.
