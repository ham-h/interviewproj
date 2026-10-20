# VoiceHire AI

A speaking AI technical interviewer for internal practice and fresher assessment. You talk to the interviewer and can interrupt it at any time. It adapts its questions to your answers. A separate evaluator then scores each answer against a versioned rubric and backs each score with evidence.

.NET 10 · Blazor Web App (Interactive Server) · ASP.NET Core Identity · EF Core 10 + SQL Server · OpenAI Realtime (WebRTC)

## Run it

Prerequisites: .NET 10 SDK and SQL Server LocalDB (or change `ConnectionStrings:DefaultConnection`).

```bash
dotnet user-secrets --project src/VoiceHire.Web set "OpenAI:ApiKey" "<your key>"
dotnet run --project src/VoiceHire.Web --launch-profile https
```

In Development, the database is migrated and seeded on startup. The seed adds an "Internal Pilot" organization and a Junior .NET Developer template with 16 questions and a 4-criterion rubric.

- Register an account. With `Pilot:AutoEnrollUsers` on, every signed-in user joins the pilot organization.
- Add reviewer and admin emails to `Pilot:AdminEmails`. Admins can view and adjust every report.
- Email confirmation is skipped in Development only.
- In Development, `appsettings.Development.json` makes `pilot.tester@voicehire.test` an admin. Add your own email there to manage question banks.

Check `OpenAI:RealtimeModel`, `OpenAI:TranscriptionModel` and `OpenAI:AssessmentModel` against the provider's current model list before the pilot.

## How a live interview works

```
Browser (voice.js)                   ASP.NET Core                            OpenAI Realtime
──────────────────                   ────────────                            ───────────────
mic + WebRTC offer ──POST SDP──▶ /api/interviews/{id}/realtime-session
                                     checks ownership, limits, rate limit
                                     POST /v1/realtime/calls (server key) ──▶ creates call
                  ◀──SDP answer──── returns answer + keeps call_id
audio ◀════════════════════════════════════════════════════════════════════▶ speech-to-speech
                                     RealtimeCallMonitor (sideband WS) ◀───▶ same call
                                       • persists transcripts per turn
                                       • answers get_next_question / end_interview
                                       • enforces the time limit
```

- **The provider key never reaches the browser.** The browser's SDP offer is forwarded by the server.
- **The server controls the interview.** The model has to call `get_next_question`. The `InterviewOrchestrator` then picks the question, adjusts difficulty with `DifficultyPolicy`, and enforces the question and time limits.
- **The transcript comes from the server's own connection to the call, not from the browser,** so a candidate cannot edit it. Browser captions are display-only.
- **Each answer is filed under the right question.** A candidate's speech is mapped to the open question when it is committed, so a transcription that arrives late still lands on the correct turn.
- **The provider handles interruptions** (`interrupt_response`). The UI marks interrupted interviewer lines.

## Scoring

`ReportGenerator` runs in a background worker after the interview ends.

1. `OpenAiAnswerAssessmentService` scores each question 1–5 per criterion. It uses a strict JSON schema and a separate model from the live interviewer.
2. `AssessmentValidator` rejects malformed scores. It also drops any evidence whose quote doesn't appear in what the candidate actually said, and flags that answer for human review.
3. `ScoreCalculator` computes the 0–100 score in C#: each score is normalised as `(score − 1) / 4` and then weighted. The model never produces the total.
4. Unanswered questions score 0 and are flagged for review, because a missing answer may be a speech-recognition failure.

Reviewers can adjust a score with `POST /api/interviews/{id}/report/review`. The original AI score is kept.

## Your own questions (PDF / Word upload)

Reviewers and admins open **Question bank** (`/manage/questions`). There they pick a template or create a new one, then upload a `.pdf`, `.docx` or `.txt` question list.

1. `DocumentTextExtractor` reads the text locally: PdfPig for PDFs, the Open XML inside a `.docx`. The file itself isn't stored. Scanned PDFs (images of text) and old `.doc` files are rejected with a clear message.
2. `OpenAiQuestionExtractor` finds the questions and suggests a category, difficulty and expected concepts. If the document has model answers, the concepts come from those; otherwise the AI suggests them, and the review screen says which.
3. `QuestionImportService` checks that each question appears word-for-word in the document. A question the model reworded or invented is flagged and left unticked. Questions already in the bank are flagged as duplicates.
4. The reviewer edits and selects the drafts, then saves them. Saved questions are stored with `Source = Imported` and the document name, and the template is published.

Interviews only ever use questions from the template's bank, so a template built from an uploaded document interviews only on those questions. The AI still adds natural follow-ups.

## Solution layout

| Project | Contents |
|---|---|
| `VoiceHire.Domain` | Entities, enums, `ScoreCalculator`, `DifficultyPolicy` |
| `VoiceHire.Application` | `InterviewOrchestrator`, `InterviewAccess`, `ReportGenerator`, `AssessmentValidator`, provider interfaces |
| `VoiceHire.Infrastructure` | `VoiceHireDbContext` (Identity + domain), migrations, seed, OpenAI realtime provider, call monitor, evaluator, report worker |
| `VoiceHire.Web` | Blazor pages (Home, InterviewRoom, Report), REST endpoints, `wwwroot/js/voice.js` |
| `VoiceHire.Tests` | Domain rules, validator, orchestrator and report flow against in-memory SQLite |

## API

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/interview-templates` | Templates in the caller's organizations |
| POST | `/api/interviews` | Create a session `{ templateId }` |
| GET | `/api/interviews/{id}` | Session state (candidate or org reviewer) |
| GET | `/api/interviews/{id}/turns` | Persisted transcript |
| POST | `/api/interviews/{id}/realtime-session` | `application/sdp` offer in, SDP answer out (rate limited) |
| POST | `/api/interviews/{id}/finish` | End the interview and queue evaluation (idempotent) |
| GET | `/api/interviews/{id}/report` | Report, or 202 while evaluating |
| POST | `/api/interviews/{id}/report/review` | Reviewer score adjustment and notes |

## Status

What has been verified so far:
- The solution builds with no warnings.
- All 25 tests pass.
- The migration applies to SQL Server LocalDB.
- Register, auto-enrol, list templates, create an interview, open the interview room, open the question bank page and create a template all work locally. API authorization (401/404) works too.

**Not yet verified:** a live voice call against OpenAI, the sideband event handling, the evaluator, and finding questions in an uploaded document (needs a key; the upload itself was not tried from the browser). They follow the current OpenAI docs but haven't run against the real API yet.

Next up:
- A first live call and latency measurement.
- Mark interviews abandoned when they stay in progress past their time limit.
- A reviewer dashboard.
- More advanced questions: the bank has only 4, so a strong candidate falls back to intermediate questions by question 7.
- Learning plans.
- Docker and CI.
