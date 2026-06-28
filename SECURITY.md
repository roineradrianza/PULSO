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

## Scope — What we want to hear about

- SQL injection or unsafe database queries
- Exposure of PII (names, phone numbers, GPS coordinates)
- Authentication/authorization bypass on webhook endpoints
- Rate-limit bypass enabling abuse of the reporting system
- SSRF or data exfiltration via the geocoding layer
- Secrets or credentials inadvertently committed or exposed

## Out of Scope

- UI/UX bugs or feature requests (use GitHub Issues)
- Vulnerabilities in third-party services (Supabase, Telegram, Google Gemini)
- Issues in dependencies without a demonstrated impact on PULSO
- Denial of service via legitimate high-volume traffic during a real emergency event
