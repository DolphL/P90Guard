# P90Guard network protocol v1

A shared cheater database that servers connect to with an API key issued by the network owner. The reference network is run by FLX
(`https://flx-scp-project-90.ru/api/guard/v1`); anyone can host a compatible one.

- Every request: `Authorization: Bearer <key>`. JSON in and out, every response has `ok`.
- Errors: HTTP 4xx/5xx with `{"ok": false, "error": "..."}` (401 bad key, 403 key disabled, 400 bad data, 429 rate limit).
- Dates: ISO 8601 UTC. SteamID: 17-digit string.
- The plugin fails open: if the network is unreachable, the server plays normally; errors are logged at most once per 5 minutes.

| Request | Body / query | Response |
|---|---|---|
| `GET /ping` | - | `{"ok":true,"server":{"id":5,"name":"..."}}` |
| `GET /check?steam_id=...` | - | `{"ok":true,"servers":2,"reports":[{"id":1,"server":"...","reason":"...","created_at":"...","until":null,"evidence_count":3,"own":false,"verified":true}]}` - active reports only; `verified` = the author server is verified |
| `POST /reports` | `{"steam_id","nick","reason","duration_minutes","evidence":[...],"server_name","plugin"}` | `{"ok":true,"id":1}` - one active report per (server, SteamID): a repeat updates it |
| `POST /reports/revoke` | `{"steam_id","reason"}` | `{"ok":true,"revoked":1}` - only this server's own reports |

The author of a report is identified by the key, not by `server_name`. `check` never returns nicks or evidence text.
