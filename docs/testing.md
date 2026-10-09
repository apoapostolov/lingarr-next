# Testing Lingarr Next

## Test pyramid

| Layer | Location | Runner | Purpose |
|-------|----------|--------|---------|
| **Unit / regression** | `Lingarr.Server.Tests/` | `dotnet test` | Fast, no network, no Docker required |
| **Migration** | `Lingarr.Migrations.Tests/` | `dotnet test` | Schema migration safety |
| **API smoke** | `tests/smoke/smoke_api.py` | Python 3 | Live container regression |

## Unit / regression suite

### Run all server tests

```bash
cd /path/to/lingarr
dotnet test Lingarr.Server.Tests/Lingarr.Server.Tests.csproj -c Release
```

Via Docker (no local SDK):

```bash
docker run --rm --network=host \
  -v "$PWD":/src -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test Lingarr.Server.Tests/Lingarr.Server.Tests.csproj -c Release
```

### Bedroom-focused regression tests (this fork)

| File | What it guards |
|------|----------------|
| `Services/Translation/TranslationChainTests.cs` | `service_type` parse: plain, legacy array, rich `{provider,model}`, duplicates, serialize, `SupportsModel` |
| `Services/Translation/ZaiServiceTests.cs` | Coding Plan endpoint constants + rewrite of general API URLs |
| `Services/Translation/ModelCatalogServiceTests.cs` | Model list cache hit, refresh bypass, stale on failure |
| `Services/Translation/OpenRouterModelOrderingTests.cs` | **`openrouter/free` always first**; inject if catalogue omits |
| `Services/Translation/LlmPricingCatalogTests.cs` | Known public per-token rates and unknown-model no-guess behaviour |
| `Services/Translation/TranslationFallbackChainTests.cs` | Subtitle line falls through providers on failure |
| `Services/TranslationPromptProfileServiceTests.cs` | Immutable prompt versions, AI-only resolution, exact usage history, and protected defaults |
| `Services/TranslationQualityServiceTests.cs` | Observe-only quality rules, penalties, caps, and scoring |
| `Services/Translation/ProofreadPromptsTests.cs` | AI revision prompt defaults and Mistral endpoint |
| `Services/Subtitle/SubtitleNamingTests.cs` | Sidecar rename matching, language aliases, episode isolation |
| `Services/Subtitle/EmbeddedSubtitleExtractorTests.cs` | English text-track selection and ffprobe JSON parse |
| `Services/DashboardActivityServiceTests.cs` | Bounded recent-work metrics and deterministic progress prose |
| `Services/ProviderHealthServiceTests.cs` | Provider status precedence, recovery, and safe event classification |
| `Services/ClassifierProviderTests.cs` | Selected classifier key, OpenAI Decisions request and answers, Jev fallback, and fail-open behavior |

### Upstream coverage already present

Media include flags, subtitle processors (SSA/ASS), language codes, Gemini service HTTP mocks, automated job, startup, migrations, etc. under `Lingarr.Server.Tests/Services/**`.

## API smoke (live)

Requires a running Lingarr Next instance (Bedroom default `http://127.0.0.1:9876`).

```bash
# Required free-path checks (microsoft translate + plugin shapes)
python3 tests/smoke/smoke_api.py

# Optional AI translate (uses openrouter/free — needs key configured)
LINGARR_SMOKE_AI=1 python3 tests/smoke/smoke_api.py

# Custom base URL
LINGARR_URL=http://192.168.1.10:9876 python3 tests/smoke/smoke_api.py
```

### Smoke checklist (automated)

1. `/api/version` healthy  
2. `/api/plugin` includes microsoft, deepseek, openrouter, zai, opencode-go, openai  
3. Set `service_type` → microsoft  
4. `POST /api/translate/line` en→es returns non-empty string  
5. Model catalogues load; **openrouter first option is `openrouter/free`**  
6. zai catalogue includes `glm-5.2`  
7. `POST /api/translate/content` accepts a body  

### Manual UI smoke (Services page)

1. Hard-refresh Settings → Translation → Setup.
2. Each row: number badge, full-width provider, stacked model, stacked API key.  
3. No shared credentials panel under the list.  
4. Delete only on fallbacks; primary cannot be deleted.  
5. OpenRouter model list: free first; spinner stops after load.  
6. Development pill uses theme accent (not amber).  
7. Save toast uses secondary/accent (not neon green).
8. Prompts tab manages separate System and Context libraries with drafts, publish,
   default selection, version history, and recommended examples.
9. Built-in AI rows expose System/Context selectors; Microsoft and other
   traditional providers do not.

### Stale-tab deployment smoke

1. Open Dashboard in a fresh tab without visiting Movies, TV Shows, or Path mapping.
2. Deploy a client build with different asset hashes.
3. In the still-open tab, open one of those lazy-loaded pages.
4. Lingarr Next should refresh once and arrive on the requested page without a blank
   view or repeated refresh.
5. A removed `/assets/<old-hash>.js` URL must return 404, while `/` returns
   `Cache-Control: no-cache, no-store, must-revalidate`.

## CI recommendations

```yaml
# minimal
- dotnet test Lingarr.Server.Tests -c Release --no-restore
# after image deploy to staging
- python3 tests/smoke/smoke_api.py
```

Do not enable `LINGARR_SMOKE_AI=1` in CI unless secrets and spend limits are intentional.

## Adding tests

1. Prefer pure unit tests for parse/normalize/order logic.  
2. Use Moq + `HttpMessageHandler` for provider HTTP.  
3. Keep tests free of real media titles/paths (placeholders only).  
4. Update this file when adding a new regression area.
