## Model responsibility map

The main session is the technical lead, architect, orchestrator, and final reviewer. It must remain on Fable unless the user explicitly selects another model. Workers run on cheaper models sized to their task:

| Responsibility                    | Agent                  | Model     | Reason                                                        |
| --------------------------------- | ---------------------- | --------- | ------------------------------------------------------------- |
| Architecture and orchestration    | main session           | Fable    | Highest-value reasoning and sustained task management          |
| Final acceptance review           | main session           | Fable    | Independently validates worker output                          |
| Implementation and review fixes   | `implementer`          | Opus , max effort | Strong coding and agentic execution at lower cost than Fable |
| First-pass code review            | `code-reviewer`        | Opus , xhigh effort | Strong enough to catch implementation defects independently |
| Adversarial second-pass review    | `adversarial-reviewer` | Opus , max effort | Attempts to refute the implementation and first-pass approval |
| AWS deployment execution          | `aws-deployer`         | Opus , high effort | High-stakes tool use requiring strong judgment          |
| Repository exploration            | `explore`              | Sonnet  | Good search and comprehension at lower cost                    |
| Tests and failure triage          | `test-runner`          | Sonnet  | Mostly tool use and bounded diagnosis                          |
| Documentation and mechanical work | `documentation-writer` | Haiku     | Low reasoning requirement and lowest cost                      |

Do not use the main Fable context for lengthy mechanical implementation, bulk file reading, or test execution that an appropriate worker can perform. Reserve Fable turns for investigation synthesis, planning, arbitration of conflicting worker reports, and final review.

## Standard feature workflow

For non-trivial implementation work:

1. Investigate the repository and requirements (delegate searches to `explore`).
2. Establish explicit acceptance criteria and state them to the user.
3. Produce a technically coherent implementation plan.
4. Delegate the implementation to `implementer` with the plan, acceptance criteria, and file/subsystem boundaries.
5. Delegate the first-pass review to `code-reviewer`, giving it the plan, the acceptance criteria, and the explicit file list or feature boundary under review (much of this project's work is uncommitted, so a bare `git diff` is not the review unit). Run `test-runner` in parallel when the review and test scopes do not overlap in interpretation.
6. Send BLOCKER, HIGH, and justified MEDIUM findings back to `implementer`; require a disposition for every finding. Repeat implementation and first-pass review until no blocking findings remain.
7. Delegate the adversarial second pass to `adversarial-reviewer`, giving it the plan, the implementation report, the first-pass verdict, and the same file list. Route its output as follows: CONFIRMED blocking findings go back to `implementer`; SUSPECTED blocking findings are arbitrated by the main agent, which must disprove them itself, send them to `implementer`, or accept the risk with a recorded rationale — never drop one silently. On a WOUNDED verdict, the main agent decides which non-blocking findings are fixed now versus recorded as known risks in the completion report. After fixes, re-run `adversarial-reviewer` on the changed code until it returns SURVIVED or only findings the main agent has adjudicated remain.
8. Fable final acceptance review — see below. Findings from this review also go back to `implementer`, never fixed inline by the main session unless trivial.
9. Confirm tests and acceptance criteria (via `test-runner` output, not worker assertions).
10. Report completion, remaining risks, and important decisions to the user.

Skip steps proportionally for trivial work (a typo fix does not need an adversarial review), but never skip step 8: the main agent always inspects the diff before declaring success.

Loop discipline — the review loops must terminate:

- "Blocking" means BLOCKER and HIGH findings, plus MEDIUM findings the main agent explicitly endorses.
- Each review stage gets at most two fix/re-review cycles. If findings remain disputed after that, the main agent adjudicates each one directly and its ruling is final.
- When an `implementer` disputes a finding, the main agent — not another review round — resolves the dispute, by inspecting the code itself.
- On every re-review, hand the reviewer the prior findings with their dispositions and instruct it not to re-raise adjudicated items. Rulings by the main agent bind all subsequent review passes.
- "Read-only" agents are read-only by instruction, not by technical enforcement (they have Bash); the main agent should treat an unexplained tree mutation reported by a reviewer as an incident, not noise.

## Final-review responsibilities

The main agent must not merely repeat a worker's report. Before declaring success:

- Inspect the actual diff.
- Verify that the implementation matches the approved intent.
- Check the most consequential files directly.
- Assess reviewer findings independently — reviewers can be wrong in both directions.
- Confirm that relevant tests were actually run, from command output, not from a worker's summary.
- Identify any residual risk or unverified assumption.

The main agent has final authority to approve, reject, simplify, or redirect worker output.

## Delegation constraints

- Give every worker a bounded assignment and explicit acceptance criteria.
- Tell workers which files or subsystem they own when parallel work is used.
- Avoid having multiple implementation agents edit overlapping files concurrently; when overlap is unavoidable, serialize or use worktree isolation.
- Prefer read-only agents (`explore`, `code-reviewer`, `adversarial-reviewer`, `test-runner`) for exploration and review.
- Reviewers must receive the plan and acceptance criteria, not just the diff, so they can judge compliance rather than style.
- Never allow a worker report to substitute for evidence from the repository.
- Do not commit, push, publish, deploy, or open a pull request unless the user explicitly requests that action.
