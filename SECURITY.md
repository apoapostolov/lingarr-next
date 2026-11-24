# Security policy

## Supported versions

| Version | Supported |
| --- | --- |
| Latest 1.x release | Yes |
| Older 1.x releases | No |

Lingarr Next keeps its own version line, independent of upstream Lingarr tags,
and fixes ship in a new release. Running from `main` means running unreleased
code that receives no support.

## Reporting a vulnerability

Use GitHub private vulnerability reporting. Do not open a public issue.

1. Open the **Security** tab of this repository.
2. Choose **Report a vulnerability**.
3. Describe the problem and how to reproduce it.

If that button is not visible, private reporting has not been enabled yet. Open
a short issue asking for a private channel, and keep the details to yourself
until one exists.

Please include:

- the Lingarr Next version, from the footer of the UI or from your image tag
- how it runs: Docker or source, SQLite, MySQL or PostgreSQL, `AUTH_ENABLED` on
  or off, behind a reverse proxy or not
- the surface: web UI, REST API, MCP server, importers, or a provider
  integration
- reproduction steps, and any log line with media paths, provider keys and Plex
  tokens removed
- whether an authenticated user is needed, and with which role

Do not attach API keys, Plex tokens, `.config.json`, database files, or paths
from your own library.

## What is in scope

- authentication and session handling when `AUTH_ENABLED` is on
- path traversal or arbitrary file read and write through library paths,
  subtitle paths, archives, or the plugin folder
- server-side request forgery through provider, webhook, or federation URLs
- disclosure of stored secrets through the UI, the API, logs, or error output
- injection in importers, subtitle parsing, or template rendering
- a dependency vulnerability with a reachable path in this codebase

## What is out of scope

- a deployment with authentication disabled and reachable from the internet
- anything that needs an already compromised host, browser, or provider account
- bugs in Sonarr, Radarr, Plex, or a translation provider. Report those upstream,
  and open an issue here only if Lingarr Next mishandles their response
- scanner output with no reproduction and no affected version
- missing hardening with no demonstrated impact
- self-XSS, or UI spoofing that needs the user to paste code into their own
  console

## How this repository is maintained

- Dependabot alerts and Dependabot security updates are enabled.
- CI and CodeQL run on every pull request, and a pull request merges only when
  they pass.
- Merging is squash-only, and the branch is deleted after the merge.

There is no bug bounty. Reports are handled by the maintainer.
