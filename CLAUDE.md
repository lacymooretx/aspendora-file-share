# claude.me — Project Execution Rules for Claude Code

You are Claude Code operating inside this repository.  
These instructions are **non-negotiable** and apply to every task unless a more specific project file explicitly overrides them.

---

## 1. Completion Discipline (No Partial Stops)
- **Continue working until the task is fully complete.**
- Do **not** stop after doing a partial chunk and asking “is this good enough?”
- Only pause for user input when:
  1) a required decision is ambiguous and blocks progress, or  
  2) you must confirm a high-stakes action (data deletion, irreversible changes, cost-incurring actions), or  
  3) credentials or access are missing and cannot be inferred.

If you pause, state clearly:
- what is blocked,
- what exact input you need,
- what you will do next immediately after receiving it.

---

## 2. Always Keep a Recovery Log
You must maintain a step-by-step execution log so work can resume after interruptions.

### Log location
Create and update:
- `./docs/claude-runlog.md`

### Logging requirements
After **every meaningful step**, append:
- **Timestamp**
- **Goal of the step**
- **What you did**
- **Why you did it**
- **Files changed** (with paths)
- **Commands run**
- **Result / output summary**
- **Next required steps**

If you plan multiple steps, write them as a checklist and mark each complete as you go.

### Crash recovery behavior
If you restart or re-enter a task:
1) Read `./docs/claude-runlog.md` first.  
2) Summarize current state.  
3) Continue from the first unchecked or incomplete step.  
4) Do not redo finished work unless necessary — explain why.

---

## 3. Plan → Execute → Verify Loop
For any task bigger than a tiny edit:

1) **Plan**  
   - Write a short plan (3–10 bullets).
   - Identify risks, assumptions, and dependencies.

2) **Execute**  
   - Follow the plan.
   - Update the runlog continuously.

3) **Verify**  
   - Run relevant tests, builds, or checks.
   - If verification fails, fix and re-verify.
   - Record outcomes in the runlog.

---

## 4. Secrets and Credentials Policy (Safe Handling)

**Never store real secret values in this repo.**  
That includes API keys, passwords, private tokens, certificates, or customer data.

### Local secrets source of truth
- Secret values must live **only** in a local, non-synced file:
  - `~/.secrets` (preferred)
- Claude Code should assume this file exists and may contain needed values.
- If a required secret is missing from `~/.secrets`, note it in the runlog and request the user to add it there.

### How to use local secrets
- Prefer environment variables exported from `~/.secrets`, e.g.:
  - user loads them with `source ~/.secrets` (shell) or equivalent.
- If the project needs a local file reference, use a **gitignored** project file:
  - `.env.local` (gitignored)
  - populated by the user from `~/.secrets`

### What to store in this project
Store **only metadata and instructions**:
- A list of required secrets and their purpose:
  - `./docs/secrets-required.md`
- Non-secret templates:
  - `.env.example`
  - `config/example.*`
- Provisioning docs:
  - where to get each secret,
  - who owns it,
  - how to rotate it,
  - how to validate it works.

### What NOT to store
- Actual secret values (even masked/partial ones)
- Logs, screenshots, or dumps containing secrets
- Hard-coded tokens in code
- Secrets copied from other folders/systems into this repo

### If a secret is discovered in code or docs
1) Stop and **remove/redact it immediately**.  
2) Add it to `secrets-required.md` as a required secret **without the value**.  
3) Note remediation steps in the runlog.  
4) Recommend rotation if exposure is possible.

---

## 5. Documentation Standard
When you change behavior, architecture, or workflows:

- Update or create docs in `./docs/`
- Include:
  - what changed,
  - why,
  - how to use it,
  - how to test it,
  - rollback notes if relevant.

Keep docs concise, factual, and reproducible.

---

## 6. External Docs Assimilation (~/code/stuff)

There is additional documentation outside this repo in:

- `~/code/stuff`

Treat this directory as valid reference material for this project.

**If you consult, quote, or rely on anything from `~/code/stuff`:**
1. **Assimilate it into this repo immediately** so the project becomes self-contained over time.
2. Put the assimilated info in the most appropriate local place:
   - Prefer updating existing docs (`README.md`, `./docs/*`) if it fits.
   - Otherwise create a new doc under `./docs/` with a clear, specific name.
3. Add a short **Source note** in the local doc:
   - “Source: ~/code/stuff/<path>”
   - “Imported: YYYY-MM-DD”
4. **Summarize and extract only what’s needed** for the current task.
   - Do not dump large irrelevant blocks.
   - If a large block is truly required, include it but still summarize.

**Goal:** any external knowledge this repo depends on should be copied in here as soon as it becomes relevant.

---

## 7. Large Task Parallelization (“Spin Up Agents”)

When a task is large, multi-part, or risks mistakes if done linearly, you MUST split it into parallel subtasks.

**How to do this:**
1. Start with a short plan that identifies 3–6 independent subtasks.
2. Treat each subtask as a “separate agent”:
   - Label them clearly (Agent A, Agent B, Agent C, …).
   - Give each a one-line mission.
   - Solve each subtask independently to a concrete artifact (patch, doc section, checklist, test result).
3. Integrate the artifacts into one coherent final result.
4. Update the runlog as usual — include the agent split and integration steps.

You do **not** need to ask permission to split work.  
Only ask if the split depends on a blocking decision per §1.

---

## 8. Quality Bar
Before declaring completion:
- All requested deliverables exist.
- Code is formatted, linted (if applicable), and tests pass.
- No TODOs remain that affect functionality.
- Runlog shows a clean, auditable trail.
- Secrets policy is followed.

---

## 9. Default Communication Style
- Be direct and practical.
- Prefer doing work over asking questions.
- If you must ask a question, ask **one clear, blocking question at a time** and propose a default.
