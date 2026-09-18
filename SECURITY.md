# Security

## Reporting a vulnerability

Please report security issues privately. On this repository, use **Security → Report a vulnerability** (GitHub private vulnerability reporting). Please don't open a public issue for a security problem. If that form is ever unavailable, open an issue that only asks for a private contact, with no details.

You should get a first reply within 7 days. Fixes go out as a new commit on `main` with a note in [CHANGELOG.md](CHANGELOG.md).

## Supported versions

Only the latest commit on `main` is supported.

## Security model

The goal is a small, auditable program that handles one secret, your OpenRouter API key, safely.

**The key**
- It is stored in `key.dat` under `%LOCALAPPDATA%\Programs\OpenRouterBar`, encrypted with Windows DPAPI (`CurrentUser` scope). It is tied to your Windows account: copying `key.dat` to another account or machine does not reveal it. Administrators of the PC, or of your domain, can still recover it (see "Limits" below).
- `set-key.ps1` reads it from a hidden prompt or from standard input, never from a command-line argument, so it does not show up in the process list or shell history.
- The widget decrypts it only in memory, on a background thread, just before each request. The program never writes it to disk in plain text, logs it, or shows it. `status.txt` records results and errors, not the key. (Windows itself can page process memory to disk, as it can for any program.)

**Network**
- The only request the widget makes is `GET https://openrouter.ai/api/v1/key` (hard-coded), over TLS 1.2 or 1.3 with the .NET default certificate validation. Certificate revocation is not checked, which is also the .NET default.
- It does not follow redirects, so the key can't be forwarded to another host. Responses larger than 64 KB, or slower than 30 seconds, are rejected, and numbers that aren't finite are refused.
- It uses the Windows proxy settings, as any browser does. That can include automatic proxy discovery, and Windows may fetch certificate chains. TLS still protects the key end to end.
- There is no telemetry, no update checking, and no other network access.

**The program**
- It runs as the signed-in user and refuses to run elevated. `setup.ps1` starts it through Explorer, so it gets normal rights even when setup runs from an administrator window. Everything is installed under `%LOCALAPPDATA%`, and autostart uses the per-user `HKCU\...\Run` key.
- It has no plugins and runs no scripts or external commands. The only links it opens are the two fixed openrouter.ai pages in its menu.
- It only attaches to a taskbar window owned by the Windows shell process.
- There are no third-party dependencies, only the .NET Framework that ships with Windows.
- No prebuilt binaries are published. You build from source with the Windows C# compiler. The build ignores compiler response files (`/noconfig`) and references framework assemblies by absolute path, so nothing in the working folder can change what gets compiled in.

**Repository**
- GitHub Actions workflows run with least-privilege tokens (read-only, plus `security-events: write` for CodeQL to upload results), and every action is pinned to a full commit SHA. The repository only allows GitHub-owned actions pinned by SHA. Dependabot keeps those pins current, with a 7-day cooldown.
- Workflows use `pull_request`, never `pull_request_target`, and never persist the checkout token.
- CodeQL scans the C# code and the workflows.

## Limits of this model

DPAPI protects the key from other standard users of the PC and from someone who copies `key.dat` alone. It does **not** protect against:

- malware already running as your user, which can decrypt it the same way the widget does;
- administrators of the PC, or of your domain (through the domain DPAPI backup key);
- someone holding a backup of your profile together with your password.

Give the key a credit limit on OpenRouter, so that the most anyone could spend with it is capped.
