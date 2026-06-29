# Security Policy

## Supported Versions

| Version | Supported |
|---|---|
| `main` (latest) | ✅ |
| `stage` | ⚠️ Staging only |
| Older commits | ❌ |

---

## Reporting a Vulnerability

PULSO processes names, phone numbers, GPS locations, and personal messages from people in emergency situations. We take security and privacy extremely seriously.

**Please do NOT open a public GitHub issue for security vulnerabilities.**

Instead, report them privately by emailing:

📧 **dev@roineradrianza.com**

Include in your report:
- A clear description of the vulnerability.
- Steps to reproduce it.
- The potential impact (e.g. data exposure, bypass, injection).
- Any proof-of-concept code or screenshots (if applicable).

### What to expect

- **Acknowledgment** within 48 hours.
- **Status update** within 7 days with a triage decision.
- **Coordinated disclosure**: we will work with you to agree on a disclosure timeline before publishing any public fix or advisory.

We credit researchers in our release notes unless they prefer to remain anonymous.

---

## Public-by-design data

PULSO exposes an **[Open Data API](README.md#open-data-api)** (`/api/v1/public/*`). Report
**content is public by design**: report text, voice-note transcriptions, declared locations,
GPS coordinates, and the names of missing/found people are intentionally returned by this API
so the information is not privatized. This is **not** a vulnerability.

What is **never** exposed and *would* be a vulnerability:
- The reporter's phone number column (`sender_phone`).
- Media/storage URLs (`media_file_url`).
- Any field outside the documented public allowlist.

## Scope — What we want to hear about

- SQL injection or unsafe database queries
- **Unintentional** PII exposure: `sender_phone`, media URLs, or any field leaking outside the public allowlist
- Authentication/authorization bypass on webhook endpoints
- Rate-limit bypass enabling abuse of the reporting system
- SSRF or data exfiltration via the geocoding layer
- Secrets or credentials inadvertently committed or exposed

## Out of Scope

- UI/UX bugs or feature requests (use GitHub Issues)
- Vulnerabilities in third-party services (Supabase, Telegram, Google Gemini)
- Issues in dependencies without a demonstrated impact on PULSO
- Denial of service via legitimate high-volume traffic during a real emergency event
