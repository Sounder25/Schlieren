Below is the handoff I would give Grok. It assumes it will read the **Schlieren Guard Rev. 5 plan** and then execute from the repository/AWS environment. The plan's current core is execution-proof token risk checking, with the first prototype deliberately constrained to Ethereum ERC-20 → pinned state → Uniswap buy → approve → sell → causal explanation → Workbench.  

> ## Schlieren Guard — Execution Handoff Context
>
> Read the current Guard plan `.md` completely before changing code. Treat it as the design baseline, but apply the decisions/corrections below where they are more specific.
>
> ### Objective
>
> We are validating whether **Schlieren Guard** is worth becoming a product.
>
> Guard is a consumer-facing token/contract risk checker built on Schlieren's real EVM execution and journal. The first question is deliberately narrow:
>
> **“If I buy this ERC-20 right now, can I actually sell it again, what do I lose, and why?”**
>
> We are **not** building a generic token scanner, static scorecard, wallet extension, multichain product, liquidity-analysis platform, dashboard, or SaaS infrastructure yet.
>
> ### Competitive reality
>
> Honeypot.is, GoPlus, TokenSniffer and others already simulate transactions and perform token checks. Therefore:
>
> **“We execute the trade” is NOT sufficient differentiation.**
>
> Guard's prospective moat is the combination of:
>
> **pinned real chain state + stateful sequential execution + exact measured outcome + causal frame-level explanation + reproducible evidence + Workbench drill-down + later privileged-action counterfactuals.**
>
> The product claim should be closer to:
>
> **Guard reproduces the behavior against pinned on-chain state, identifies the execution path that caused it, and preserves the evidence so the user can see why the verdict was reached.**
>
> ### Architecture decision
>
> Create a dedicated:
>
> `Schlieren.Guard`
>
> project.
>
> Do **not** put Guard semantics into `Schlieren.Core`.
>
> Core remains the evaluator/state/journal substrate. Guard owns consumer scenario orchestration and verdicts.
>
> Conceptually:
>
> ```text
> Schlieren.Core
>     EVM
>     state
>     journal
>         │
>         ▼
> forked/pinned state + scenario overlay
>         │
>         ├──────── Guard
>         │          token-risk scenarios
>         │
>         └──────── Hunter
>                    differential-client testing
>
> Both may open evidence in Workbench.
> ```
>
> **Guard and Hunter are siblings. Guard must not depend on Hunter.**
>
> Do not create a generic scenario-execution framework merely because Hunter might use one someday. Build the minimum Guard abstractions first. Extract common machinery only when two real consumers require it.
>
> ### REVM/Hunter correction
>
> Do not propagate the old claim that Schlieren found a confirmed REVM Berlin SSTORE bug.
>
> That apparent divergence turned out to be **our REVM harness/configuration problem**, not a proven REVM implementation defect.
>
> This is an important engineering lesson:
>
> **observed divergence/failure is not causal attribution.**
>
> Guard should follow the same discipline. A failed sell does not automatically mean “honeypot.”
>
> ### State-source decision
>
> We do **not** want Guard fundamentally dependent on PublicNode, Alchemy, Infura, Amazon Managed Blockchain, or another hosted blockchain interpretation layer.
>
> The existing generic `ForkProvider` is useful. Reuse it.
>
> Its preferred production/prototype endpoint should be **our own Ethereum node**.
>
> AWS should provide infrastructure only:
>
> ```text
> AWS EC2
>   ├── Reth execution client
>   ├── Lighthouse consensus client
>   └── persistent EBS/NVMe-class storage
>          │
>          │ private/local Ethereum JSON-RPC
>          ▼
> Existing Schlieren ForkProvider
>          │
>          ▼
> Guard
> ```
>
> Do not integrate an AWS-specific blockchain SDK and do not make Amazon Managed Blockchain a dependency.
>
> The node stack should remain portable to local hardware, another cloud, or bare metal.
>
> AWS credentials/secrets already available in your environment must **never** be committed, printed into logs, embedded into config files tracked by Git, or included in generated documentation/evidence.
>
> ### Ethereum node scope
>
> For Prototype 0 we only need **current/recent canonical Ethereum state**, not arbitrary historical archive replay.
>
> Run our own Reth + Lighthouse stack on AWS.
>
> Archive/history support is later.
>
> Guard should preferably operate against a finalized/recent pinned block.
>
> ### Existing ForkProvider
>
> Do not replace it just because Guard needs extra behavior.
>
> Think:
>
> **existing fork provider + a few missing capabilities**, not “new blockchain provider.”
>
> Add only what Guard actually needs:
>
> 1. Pin the base chain state to one block number/hash.
> 2. Ensure all RPC-backed state reads are resolved against that same pinned state.
> 3. Cache account/code/storage reads for the scenario.
> 4. Create a mutable **local overlay** for scenario execution.
> 5. Preserve reproducibility metadata: chain ID, block number, block hash, fork, scenario version, etc.
>
> ### Critical state semantics
>
> BUY → APPROVE → SELL is **one stateful scenario**, not three independent calls against the same pristine fork.
>
> Required semantics:
>
> ```text
> Ethereum base state @ block N
>             │
>             ▼
>       ScenarioSession
>             │
>      mutable local overlay
>             │
>       execute BUY
>             │
>       commit locally
>             │
>      execute APPROVE
>             │
>       commit locally
>             │
>       execute SELL
>             │
>             ▼
>      verdict + journals
> ```
>
> The sell must see:
>
> * the tokens acquired by BUY,
> * updated buyer balances,
> * router allowance,
> * pool changes caused by BUY,
> * token storage mutations caused by BUY,
> * all intermediate state produced by APPROVE.
>
> The remote/pinned Ethereum state remains immutable. Only the local scenario overlay evolves.
>
> A useful minimal abstraction is approximately:
>
> ```text
> ScenarioSession
>     BaseBlock
>     BaseStateProvider
>     OverlayState
>     Transactions[]
>     Journals[]
>     FinalStateDelta
> ```
>
> Do not over-generalize it prematurely.
>
> ### Prototype DEX path
>
> Use **Uniswap V2 Router02**, not direct `Pair.swap()`, for Prototype 0.
>
> The goal is to reproduce what a real retail user would actually do.
>
> A malicious token may branch on router, pair, caller, transfer direction or execution path, so bypassing Router02 can produce misleading behavior.
>
> Support the relevant fee-on-transfer Router02 paths because weird transfer behavior is exactly part of the target class.
>
> Do **not** implement direct-pair-vs-router comparison yet. That can later become an adjudication/diagnostic scenario.
>
> ### Prototype scenario
>
> Keep the initial workflow bounded:
>
> ```text
> ERC-20 address
>      ↓
> identify supported Uniswap V2 pool/path
>      ↓
> pin Ethereum state @ block N
>      ↓
> create disposable funded buyer in local overlay
>      ↓
> Router02 BUY
>      ↓
> measure actual tokens received
>      ↓
> APPROVE Router02
>      ↓
> Router02 SELL
>      ↓
> measure actual ETH/WETH/USDC returned
>      ↓
> analyze journal
>      ↓
> plain-language result
>      ↓
> Show Execution → Workbench exact causal frame
> ```
>
> Do not build UI infrastructure beyond what is required to demonstrate this flow.
>
> ### Verdict discipline
>
> Do not classify:
>
> `BUY PASS + SELL REVERT = HONEYPOT`
>
> automatically.
>
> Sell failure can arise from:
>
> * blacklist behavior,
> * trading disabled,
> * same-block anti-bot,
> * cooldown,
> * maximum transaction/wallet limits,
> * insufficient allowance,
> * insufficient liquidity,
> * slippage,
> * router incompatibility,
> * timestamp/block-dependent behavior,
> * scenario construction errors,
> * other contextual restrictions.
>
> Guard needs to locate the **first causal failure** and classify conservatively.
>
> Eventually useful adjudication probes include:
>
> ```text
> immediate sell
> block+1 sell
> timestamp-advanced sell
> alternate trade amount
> alternate wallet
> ```
>
> But implement only what is necessary for the initial qualification corpus.
>
> Preferred result vocabulary should distinguish things such as:
>
> **SELL BLOCKED — wallet blacklist**
>
> **SELL DELAYED — same-block restriction**
>
> **SELL SUCCESSFUL — effective loss X%**
>
> **INCONCLUSIVE — liquidity/scenario limitation**
>
> rather than reducing everything to “honeypot yes/no.”
>
> ### Initial qualification cases
>
> The first implementation needs to prove four behaviors:
>
> ```text
> A. Normal token
>    BUY PASS
>    SELL PASS
>
> B. True honeypot
>    BUY PASS
>    SELL BLOCKED
>    causal frame correctly identified
>
> C. High-tax token
>    BUY PASS
>    SELL PASS
>    actual economic loss measured correctly
>
> D. Anti-bot/cooldown token
>    immediate SELL FAILS
>    delayed/context-correct SELL PASSES
>    Guard does NOT incorrectly label it a honeypot
> ```
>
> Case D is especially important because it proves Schlieren can **adjudicate** a failure rather than merely detect one.
>
> ### Current vs historical data
>
> Eventually maintain two corpora:
>
> ```text
> CURRENT BENCHMARK
> Currently tradable tokens.
> Compare Guard against current incumbent scanners.
>
> QUALIFICATION CORPUS
> Token + historical block + pool + scenario + known expected behavior.
> Reproducible permanent regression cases.
> ```
>
> Historical/archive capability is **not required to get Prototype 0 running**. Do not let that delay the current-state product experiment.
>
> ### Control Risk — later, but preserve architecture for it
>
> Guard ultimately has two separate questions:
>
> **Execution Risk:** What happens if I trade this now?
>
> **Control Risk:** Can a privileged actor change the rules later?
>
> Never merge these into one arbitrary risk score.
>
> A future strong capability is:
>
> ```text
> current state:
> sell succeeds
>
> locally execute privileged owner action:
> setTax(...)
>
> then repeat sell:
> economic outcome becomes catastrophic
> ```
>
> That is far stronger than simply detecting `modifiable_fee = true`.
>
> However, do **not** build this before the basic buy→approve→sell prototype works.
>
> Liquidity-rug analysis is also later because LP ownership/lockers/Uniswap V3 positions constitute a separate data domain.
>
> ### Market/go-no-go requirement
>
> We have already established that token-risk tools have real user demand. What is **not** established is whether Guard provides sufficient additional value over incumbent free scanners.
>
> Therefore the purpose of Prototype 0 is not merely:
>
> **“Can Schlieren simulate a token trade?”**
>
> It is:
>
> **“Can Schlieren correctly explain something existing scanners misclassify, cannot adjudicate, or cannot causally explain?”**
>
> After the engine works, benchmark roughly 25–50 normal/scam/problematic tokens against incumbent tools.
>
> If Guard only returns the same answer with a more detailed trace:
>
> **NO-GO / reconsider product.**
>
> If Guard finds cases where incumbents say “honeypot” or “sell failed,” while Schlieren can prove the contextual cause and correctly distinguish safe/unsafe behavior—or Guard proves an executable privileged capability competitors only flag heuristically:
>
> **GO.**
>
> ### What NOT to build yet
>
> Do not spend time on:
>
> * multichain support,
> * wallet extension,
> * billing,
> * user accounts,
> * production cloud scheduler,
> * large multi-tenant portal,
> * generic risk scores,
> * liquidity-locker ecosystem,
> * full Hunter integration,
> * arbitrary smart-contract vulnerability scanning,
> * generic fuzzing,
> * historical archive infrastructure unless needed for a specific qualification case.
>
> ### Immediate execution order
>
> 1. Read the Guard plan and repository architecture.
> 2. Inspect existing `ForkProvider`, `ForkingGlobalState`, state overlays, journal and Workbench fixture-loading paths before designing replacements.
> 3. Determine exactly what existing infrastructure already satisfies pinned-state/overlay requirements.
> 4. Provision self-managed Reth + Lighthouse on AWS using secrets already available in your environment, with RPC exposed only where needed/private—not publicly by default.
> 5. Verify Reth/Lighthouse sync and JSON-RPC from Schlieren.
> 6. Implement minimal `Schlieren.Guard`.
> 7. Implement pinned `ScenarioSession` semantics.
> 8. Implement one Router02 buy→approve→sell path.
> 9. Preserve journals/evidence for every step.
> 10. Produce a minimal causal verdict and Workbench-open path.
> 11. Run the four qualification behavior classes.
> 12. Stop and report before expanding scope.
>
> ### Engineering rule
>
> Before changing existing Core/Forking architecture, **prove the required capability does not already exist**.
>
> This project has repeatedly shown that apparent engine problems can actually be harness, comparator, adapter, or normalization problems. Diagnose the first true mismatch before “fixing” underlying engine behavior.
>
> ### Definition of success for this build
>
> The prototype is successful when Schlieren can take one supported Ethereum ERC-20, freeze real current chain state from our own node, execute a realistic Router02 BUY → APPROVE → SELL sequence entirely through Schlieren, preserve sequential state correctly, measure the actual result, identify the causal frame for failure/abnormal outcome, and open that exact evidence in Workbench.
>
> **Do not optimize or broaden the product until that is demonstrated.**

One additional point for the AWS side: **tell Grok to inventory your existing AWS resources and credits before provisioning anything expensive**. It should reuse an appropriate VPC/security-group/storage setup where sensible, estimate monthly burn before launching the node, and avoid destroying or modifying unrelated AWS resources. The goal is to put AWS to work for the prototype, not create an uncontrolled infrastructure bill.
