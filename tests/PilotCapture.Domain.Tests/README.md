# Domain tests

Planned meaningful domain checks for the 0.1 milestone:

- A subject can have memberships in multiple groups.
- A capture set can contain multiple images for one subject in one session.
- An unidentified subject has an explicit identity status and is not inferred from a filename.
- Supported station codes are limited to `s10`, `s20`, `s30`, and `s40`.

The .NET SDK is not available in the bootstrap workspace, so executable test projects are deferred until a .NET 10 environment is available.
