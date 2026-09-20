# Optional OpenAI narrative integration

OpenAI is an optional external narrative provider. OpenCareer remains offline-first.

## Configuration

Local development reads:

- `OPENAI_API_KEY`
- `OPENAI_MODEL` (defaults to `gpt-5.6-luna`)

Never commit a real API key. `.env`, local settings, key files and secret files are gitignored.

The application must continue to launch and play normally when the key is missing or the provider is unavailable.

## Authority boundary

OpenAI may generate:

- dispatcher briefings;
- passenger personalities and dialogue;
- copilot dialogue;
- post-flight narrative summaries;
- career storytelling;
- route explanations;
- mission flavor text.

OpenAI may not authoritatively decide or change:

- `FlightSession` state;
- mission completion;
- balances or transactions;
- rewards;
- reputation;
- licenses, ratings, or qualifications;
- aircraft ownership or access;
- maintenance or damage;
- persistent progression.

Those remain deterministic OpenCareer state.

## Runtime permission

AI requests are blocked unless `AppPreferences.AllowOptionalOnlineServices` is enabled.

Provider failures return a non-authoritative failed result instead of breaking the career loop.

## SDK

The integration uses the official OpenAI .NET package version 2.14.0 and the Responses API through `OpenAI.Responses.ResponsesClient`.

Requests set `StoredOutputEnabled = false`. OpenCareer does not use a live OpenAI request in CI.

## Distribution

Do not bundle a developer API key into the desktop executable or checked-in configuration. Desktop-distributed secrets are extractable.

For development, use a local environment variable.

A production distribution should use either:

- a user-supplied key stored through an appropriate local secret mechanism; or
- an OpenCareer-controlled backend or proxy with authentication, quota, rate limiting, and abuse controls.

## Verification

Run:

```text
dotnet test tests/OpenCareer.Tests/OpenCareer.Tests.csproj --configuration Release
dotnet build src/OpenCareer.App/OpenCareer.App.csproj --configuration Release -p:Platform=x64
```

Run SimLab regression separately. Do not send a live API request during automated tests.
