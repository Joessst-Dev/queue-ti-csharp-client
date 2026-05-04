# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build QueueTi.slnx                  # Build the full solution
dotnet test QueueTi.Client.Tests/          # Run all tests
dotnet test QueueTi.Client.Tests/ --filter "FullyQualifiedName~SomeTestClass"  # Run a single test class
dotnet pack QueueTi.Client/                # Build NuGet package
```

## Architecture

This is a **proto-first** gRPC client library for the QueueTi distributed message queue service. The intended distribution model is as a NuGet package.

### Key design points

- **Generated code only in `QueueTi.Pb` namespace** — Grpc.Tools auto-generates C# from `QueueTi.Client/Proto/queue.proto` at build time. Never edit generated files manually.
- **Handwritten library code lives in `QueueTi` namespace** — wrapper classes, extension methods, and DI helpers go here.
- **Nullable and ImplicitUsings enabled** — use C# 10+ patterns; avoid `#nullable disable`.

### gRPC service contract (`queue.proto`)

The proto defines one service — `QueueService` — with these RPCs:

| RPC | Pattern | Purpose |
|-----|---------|---------|
| `Enqueue` | Unary | Publish a message to a topic |
| `Dequeue` | Unary | Pull one message (consumer group, visibility timeout) |
| `BatchDequeue` | Unary | Pull multiple messages at once |
| `Ack` | Unary | Acknowledge successful processing |
| `Nack` | Unary | Return a message with an error reason for retry |
| `Subscribe` | Server streaming | Receive messages in real time |

Messages carry a `topic`, `payload` (bytes), optional `key`, and a `metadata` string map. Consumer group identity is embedded in `Dequeue`/`Subscribe` requests.

### Test project

Tests use xUnit with `Microsoft.AspNetCore.TestHost` for hosting an in-memory gRPC server and `Grpc.Net.ClientFactory` for constructing the client under test. Integration tests should spin up the test server and call the real generated client rather than mocking gRPC internals.
