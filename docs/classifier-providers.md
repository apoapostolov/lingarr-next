# Subtitle classifiers

Settings → Translation → Advanced offers TypeSafe Jev and GPT-6 Luna Decisions. Jev stays selected on existing installs. The selector chooses one provider for both checks; each check has its own switch and starts off.

- **Skip sound cues and credits:** Before translation, classify each source line. Keep a line unchanged when the answer is `sound` or `credit` with confidence at least 0.85.
- **Drop a result that is not a translation:** After translation, ask whether the result renders the source into the target language. Keep the original line when the probability is below 0.35.

The saved switch keys remain `jev_skip_non_dialogue` and `jev_reject_untranslated` for compatibility. Migration 43 adds `classifier_provider=jev`. A missing selector also resolves to Jev, so a previous install retains its behavior. An unknown selector or a missing key turns both checks off. A refused, invalid, timed-out, or failed request leaves the normal translation path in place.

## Credentials and endpoint

Jev uses the encrypted `typesafe_api_key` setting and its existing `jev-latest` request. Luna uses the encrypted `openai_api_key` setting shared with OpenAI translation. You can enter that key in either settings panel. The server never sends the key to the other classifier.

Luna calls `POST https://api.openai.com/v1/decisions` with model `gpt-6-luna`. The request sends up to 16 subtitle items as JSON text in `input`. Each item has a named question: a `choice` with dialogue, sound, credit, and other options before translation, or a `predicate` after translation. Luna's `confidence` and `probability` feed the same thresholds used by Jev. The endpoint is in public beta and bills the OpenAI API account for input tokens. Codex login is not used for this endpoint.

OpenAI's [Decisions API guide](https://developers.openai.com/api/docs/guides/decisions) documents the model ID, request shape, response fields, authentication, and current pricing. The OpenRouter listing `openai/gpt-6-luna-decisions` is a different provider-specific model ID; Lingarr calls OpenAI directly.

## Validation

Run the focused server tests and client build after changing either adapter or the settings panel:

```bash
dotnet test Lingarr.Server.Tests/Lingarr.Server.Tests.csproj -c Release --filter 'FullyQualifiedName~ClassifierProviderTests|FullyQualifiedName~JevLineBenefitTests'
cd Lingarr.Client && npm ci && npm run build
```

A live provider smoke test needs an OpenAI API key and incurs API charges. Keep it separate from the unit suite and ask before spending credits.
