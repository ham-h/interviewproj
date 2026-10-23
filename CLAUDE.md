# VoiceHire AI: notes for Claude

A speaking AI technical interviewer. It is an internal team tool, used both by candidates for practice and by the team for fresher screening. Users and architecture are described in README.md; setting up on another PC is in SETUP-HOME.md.

## Stack and commands

.NET 10, Blazor Web App (Interactive Server), ASP.NET Core Identity, EF Core 10 + SQL Server LocalDB (`(localdb)\mssqllocaldb`, database `VoiceHire`), OpenAI Realtime over WebRTC, xUnit.

- Build: `dotnet build`. If Visual Studio is running the app, the output DLLs are locked; build with `-p:OutDir=<temp>` to check compilation.
- Test: `dotnet test` (tests use in-memory SQLite).
- Migration: `dotnet ef migrations add <Name> --project src/VoiceHire.Infrastructure --startup-project src/VoiceHire.Web --output-dir Persistence/Migrations`
- In Development, the app migrates and seeds the database on startup (`Program.cs`, `DbSeeder`).
- The OpenAI key is in user-secrets (`OpenAI:ApiKey`), never in appsettings.

## Architecture rules (keep these)

- **The browser never holds the provider key.** It posts its SDP offer to `POST /api/interviews/{id}/realtime-session`, and the server forwards it to `/v1/realtime/calls` (`OpenAiRealtimeProvider`).
- **The server observes every call itself.** `RealtimeCallMonitor` opens a sideband WebSocket using the `call_id`. It persists transcripts, answers the `get_next_question` and `end_interview` tool calls, and enforces the time limit. Browser captions are display-only and never persisted.
- **The server picks the questions.** `InterviewOrchestrator` chooses them from the template's bank, adapts difficulty (`DifficultyPolicy`), and enforces question and time limits. The prompt (`InterviewerInstructions`) must not be relied on for limits.
- **Scoring is separate from the live interviewer.** `ReportGenerator` → `IAnswerAssessmentService` returns 1–5 per criterion. `AssessmentValidator` drops evidence not quoted from the candidate. `ScoreCalculator` computes the 0–100 score in C#, normalising each score as (s−1)/4. Never take a total from the model.
- **Late transcripts stay with the right question.** A candidate's transcription can arrive after the next question was issued. The monitor maps `input_audio_buffer.committed` item ids to the turn that was open at the time.
- **Question import:** `QuestionImportService` reads PDF/DOCX/TXT locally (`DocumentTextExtractor`), and the AI extracts questions (`OpenAiQuestionExtractor`). Questions that don't appear word-for-word in the document are flagged and unticked. Nothing is saved without reviewer approval.
- **Access:** tenant membership comes from `OrganizationMembers`, never from client input. Reviewers and admins manage question banks and can view any report in their org. Candidates see only their own interviews.
- **EF gotcha:** entity keys are client-generated (`Guid.CreateVersion7()`). Add new entities explicitly with `db.X.Add(...)`; one attached only through a navigation property is treated as existing and causes an UPDATE that affects 0 rows.
- Fairness: never score accent, fluency or confidence. Unanswered turns score 0 and are flagged for human review.

## Status (2026-10-23)

- Built: interviews, scoring, reports, reviewer score adjustment, question import UI, and clear microphone error messages.
- Not yet verified against the live OpenAI API: realtime call, sideband events, evaluator, question extraction. Model names in appsettings need checking.
- Next: first live call and latency measurement; mark stale in-progress interviews as abandoned; reviewer dashboard; more advanced seed questions (only 4); org picker when creating a template; learning plans; Docker/CI.
