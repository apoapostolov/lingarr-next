# Provider timeouts

Machine-translation providers are held to two timeouts. Together they decide how long one
translated line may take before Lingarr gives up on it.

| Setting | Key | Default | Bounds |
| --- | --- | --- | --- |
| Attempt timeout | `request_attempt_timeout` | 120 seconds | one provider request |
| Request timeout | `request_timeout` | 15 minutes for Microsoft, 5 minutes for the other machine translators | the whole call, retries included |

Both accept a per-provider override, `request_attempt_timeout_<provider>` and
`request_timeout_<provider>`.

## What the request deadline covers

The request deadline is a budget for the entire call. It covers every attempt, the wait between
attempts, and, for Microsoft, each 1000-character chunk of a long line. A retry sequence cannot
outlive it.

`HttpClient.Timeout` is not that budget. It bounds a single HTTP request, so on its own it let a
provider that never answered spend fifteen minutes on one line: the job measured 901-second
stalls, which is the Microsoft client timeout plus overhead, not a provider limit.

## What happens when a timeout expires

- **Attempt timeout.** The attempt is abandoned, logged, and retried with backoff inside the
  remaining request budget. The interface reports
  `Translation failed. The provider exceeded the 120 second attempt timeout on all 4 attempts.`
  when every attempt runs out.
- **Request timeout.** No further attempt can fit, so the call ends with
  `Translation failed. The provider exceeded the 900 second request timeout.`
- **Caller cancellation.** A cancelled job or a disconnected client stays a cancellation. It is
  never reported as a provider timeout.

## Limit worth knowing

`GTranslate` 2.4.0 exposes `TranslateAsync(text, targetLanguage, sourceLanguage)` with no
`CancellationToken` overload. Abandoning an attempt therefore stops Lingarr waiting on it, but the
underlying HTTP request lives until `HttpClient.Timeout` ends it. The job is no longer held by it,
which is the property that matters for a one-slot translation queue.
