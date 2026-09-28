# Pilot Capture tests

The `PilotCapture.Application.Tests` project contains executable checks for roster CSV parsing, including quoted delimiters, multiline fields, UTF-8 BOMs, and lossless preservation of irregular columns.

Planned domain checks for the 0.1 milestone:

- A subject can have memberships in multiple groups.
- A capture set can contain multiple images for one subject in one session.
- An unidentified subject has an explicit identity status and is not inferred from a filename.
- Supported station codes are limited to `s10`, `s20`, `s30`, and `s40`.
