# TypedRequestContext (Agent Notes)

This repository contains a small, warning-free .NET library for **typed request context** in ASP.NET Core.

- Core package: `TypedRequestContext` (middleware, attributes, accessor, optional correlation id)
- Optional package: `TypedRequestContext.Propagation` (serialize/deserialize + outbound header provider)
- Source projects live under `src/`; tests live under `tests/`.

Build: `dotnet build TypedRequestContext.slnx -c Debug`

For more information see 'README.md'