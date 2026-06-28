# Contributing to PULSO

Thank you for your interest in contributing! PULSO is an emergency coordination platform — the quality and reliability of the code directly affects people in crisis situations. Please read this guide before opening a PR.

---

## Getting Started

1. **Fork** the repository and clone your fork.
2. Follow the [local setup instructions in the README](README.md#running-locally).
3. Create a **feature branch** from `stage` (not `main`):
   ```bash
   git checkout stage
   git pull origin stage
   git checkout -b feat/my-feature
   ```

---

## Development Workflow

| Branch | Purpose |
|---|---|
| `main` | Production — deployed automatically on push |
| `stage` | Staging — deployed automatically on push |
| `feat/*` | Your feature branch — open PR against `stage` |

**Never push directly to `main`.** All changes go through `stage` first, are verified in the staging environment, and are then merged to `main` for production.

---

## Commit Convention

We use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(api): add comment pagination endpoint
fix(pwa): correct timestamp timezone display
chore(ci): add path filters to workflows
docs: update README architecture diagram
```

Common types: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `perf`.

---

## Pull Request Checklist

Before submitting a PR, confirm:

- [ ] Code compiles: `dotnet build` (backend) and `pnpm build` (PWA).
- [ ] No secrets, API keys, or personal data added to code or comments.
- [ ] New endpoints are rate-limited and validate all inputs.
- [ ] Any new DTO types are registered in `PulsoJsonSerializerContext.cs` (required for AOT compatibility).
- [ ] PR description explains the *why*, not just the *what*.

---

## Code Style

**C# (Backend):**
- Follow the existing Minimal API pattern in `PulsoApiEndpoints.cs`.
- Use `record` types for DTOs.
- Keep endpoint handlers concise — extract logic into services if complex.

**Svelte / JavaScript (Frontend):**
- Use the existing CSS custom property system (no inline styles or ad-hoc values).
- DOM manipulation inside `buildPopup()` follows the established imperative pattern (Leaflet constraint).
- Toast notifications via `showToast(message, isError)` — never `alert()`.

---

## Reporting Issues

Use the GitHub Issue templates:
- 🐛 **Bug Report** — for something that's broken.
- 💡 **Feature Request** — for new ideas or improvements.

For **security vulnerabilities**, see [SECURITY.md](SECURITY.md).
