# ADR 0009: HQ user authentication and browser sessions

- Status: Accepted
- Date: 2026-09-28

## Context

HQ is a multi-tenant application containing sensitive business information and information about children. Browser authentication must protect company data and work cleanly with an API and static web client served from the same origin.

ASP.NET Core Identity provides user, password, verification, and account management APIs for an API backend. Microsoft recommends cookie authentication for browser-based applications because the browser handles cookies without exposing them to JavaScript.

## Decision

- Use ASP.NET Core Identity for initial HQ user accounts and account security workflows.
- Use secure, HTTP-only, same-site cookies for browser sessions. Do not store access tokens in local storage.
- Serve the web client and HQ API from one origin. Require anti-forgery protection for state-changing cookie-authenticated requests.
- Use company membership records for tenant-scoped roles and permissions. Do not use a global Identity role to grant access across every company account.
- Keep Capture installation credentials separate from interactive staff user accounts.
- Make initial company access invite-based. Public self-registration can be added with a complete onboarding and abuse-control flow.
- Require email confirmation before enabling production account access. Add account recovery, rate limits/lockout, and multi-factor authentication before general commercial onboarding.
- Abstract outbound email delivery behind an application interface; choose the production delivery provider when account invitation and recovery are implemented.

## Consequences

- The frontend can use standard same-origin requests and does not need to manage bearer tokens.
- Identity data and company memberships have separate responsibilities: authentication identifies a person, while membership authorizes that person for a company.
- Email delivery and production key/secret management remain explicit deployment tasks.
- Authentication endpoints and anti-forgery behavior require integration tests before production deployment.

## References

- [Microsoft: Use Identity to secure a Web API backend for SPAs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)
- [Microsoft: Introduction to Identity on ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)
